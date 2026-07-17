# Design: `@if` conditional rendering — first RADICAL increment

**Date:** 2026-07-17
**Status:** approved (design), pending implementation plan
**Context:** Phase 3 §6 gate is closed (DECISIONS #80). The owner chose to push RADICAL
toward "established" by deepening one §3-adjacent construct end-to-end. This is that increment.

## Goal

Move `@if` conditional rendering from **refused** (`[control-flow-not-yet-implemented]`) into
the compiled subset, with the same rigor as every other construct in the repo. `@if` is the
natural partner to `@foreach`, which already compiles; it is *in-subset-but-unimplemented*, not
a hard §3 non-goal. One construct does **not** move the §8 verdict — RADICAL stays "not
eliminated, not established." Nothing more is claimed.

## Decisions taken during brainstorming

1. **First increment = deepen one construct end-to-end** (not a 3rd app, not a structural
   Core/Analyzer refactor). Smallest, most rigorous, keeps the runtime budget safe.
2. **The construct = `@if`** — completes template control flow; high real-world frequency;
   architecture-relevant (reactive conditional DOM); plausibly zero new runtime primitive.
3. **Runtime approach = reuse `list()`** — verified feasible against `list()`'s implementation:
   it reconciles **by key, not array identity**, and its `source` is a reactive `() => T[]`. A
   conditional is a keyed 0/1 list. **Zero new runtime primitive; runtime stays byte-for-byte at
   1,943 B against the 2,048 B budget.** Alternatives rejected: a `when()` primitive (eats most of
   the 105 B headroom on a closed runtime), and a compiler-only inline insert/remove effect
   (re-implements disposal-scoping and reconcile that `list()` already gets right — the exact bugs
   `list()` exists to prevent).

## Scope

### In the first cut
- `@if (cond) { <body> }` whose body is a **single root node** (one element, or one text node),
  **nested inside an element** (as `@foreach` sits inside `<tbody>` in Rows). `list()` returns one
  `Node` per item, so a single-root body is what maps cleanly.
- `cond` is a boolean expression over `@code` state using only operators already in the §5 subset.
  It reuses the existing escape analysis: a field the condition reads that is also assigned is
  already lifted to a signal, so the condition is reactive for free. A condition over only
  never-assigned (constant) state renders once and never re-evaluates — `list()` handles that too
  (fixed 0/1 source).
- The body may contain static markup and reactive bindings (`@message`). Its effects are adopted
  by the `list()` row scope and disposed when the condition goes false (no leak).

### Explicitly out — each → located `FIL0003`, no file written; deferred to later increments
- `@else` / `@else if`
- nested `@if` (inside `@if`, or an `@if` inside `@foreach`, or a `@foreach` inside `@if`)
- `@if` at the template **root** (entangled with the open #77 root-control-flow cascade — kept
  separate deliberately)
- a **multiple-top-level-node body** (`list()` is one-node-per-item; requiring a single root
  node sidesteps this cleanly and matches the reuse-`list()` decision)

## The lowering

In Razor's IR, `@if` and `@foreach` have the **same shape**: raw C# (`if (…) { … }`) with the
body markup as sibling spans, because Razor emits no control-flow node — it re-parses the text with
Roslyn at runtime (#54). The generator already **re-assembles and re-parses** those spans for
`@foreach` (#72); `@if` reuses that machinery.

Emitted JS:

```js
// @if (cond) { <body> }
const _if0 = document.createComment('');
insert(parent, _if0);
list(parent, () => (cond) ? [0] : [], () => 0, () => { /* build body subtree, return its root */ }, _if0);
```

- **source** `() => (cond) ? [0] : []` — reactive; reading the condition's signals inside it
  subscribes the list effect, so the conditional re-evaluates when the condition changes. A condition
  that reads no signal runs once (constant → rendered once), which is also correct.
- **keyOf** `() => 0` — a constant; at most one item.
- **create** builds the body subtree and returns its single root node, inside the row's disposal
  scope (so body effects die with the subtree).
- **anchor = a comment node** at the `@if`'s position among its siblings. The body is inserted
  *before* it, so the conditional is correctly positioned no matter what follows it — which the
  generator's append-only emission (`insert(parent, node)`, `list(..., null)`) cannot do today
  (#73's sibling-anchor computation does not exist). The runtime already supports this via 3-arg
  `insert(parent, node, anchor)` and `list(..., anchor)` — **no new runtime primitive**.

Semantics match Blazor: on a condition flip the subtree is destroyed and recreated (Blazor does the
same absent a `@key`), and body effects are disposed on removal.

## Compiler work

- Recognize the `@if` pattern in the re-assembled/re-parsed IR (an `if` statement header, body
  markup spans, closing brace).
- Add a conditional node to `TemplatePlan` / `TemplateCompiler` that emits the `list()` lowering
  above, emitting a comment anchor at the `@if`'s position and passing it to `list()`.
- Route the deferred variants (see Scope-out) to located diagnostics.
- **Runtime: untouched.**

## DOM contract

The hand-written answer key is pinned against **Blazor's own generated `BuildRenderTree`** for the
same `@if` (verifying any marker or whitespace text nodes Blazor ships around a conditional), exactly
as #64/#76 did for Counter/Rows — never assumed from a reading of the rules.

The **comment anchor is a known, disclosed divergence**: Blazor's browser renderer positions
conditional content via its render tree, not a DOM comment, so it likely ships no marker node at the
`@if` site. Filament's lowering ships one comment node there. This is the same *category* as decision
#20's residual (Blazor's own `<!--!-->` markers) — a non-rendered node that does not affect layout —
and it is **disclosed here, not hidden**, and re-measured if/when this construct enters a measured
app. A later increment can remove it by anchoring on the next static sibling instead (needs emit-order
work), but that is out of this cut. If Blazor turns out to ship marker/whitespace nodes the naive
lowering omits, the answer key follows Blazor and that divergence is disclosed the same way.

## How it's proven (rigor proportional to one construct)

This increment does **not** build a new C1/C4-measured app — that is the deferred "3rd app"
increment. It proves `@if` to the repo's "verified, not assumed" bar:

- **Gate** — a hand-written answer-key fixture (`.razor` + hand-written `.js`), `canon`
  **alpha-equivalent**. The core gate, same as Counter/Rows.
- **Snapshot** — emitted bytes pinned (§10 silent-regression wall).
- **Out-of-subset suite** — each deferred variant (`@else`, nesting, root-level, multi-node body)
  → located `FIL0003`, no file written, **mutation-tested** (neutralize the guard → the case goes
  red), against negative controls that must still compile clean (#61/#77 discipline).
- **Behavioral, in-browser** — `MutationObserver` confirms the subtree is added/removed on
  condition flip, **and** a reactive binding inside the body stops updating once hidden (proves
  effect disposal — no leak).
- **Runtime invariant** — a test asserts `@if` adds **no new runtime export** and the size gate
  stays 1,943 B / 2,048.

## YAGNI / non-goals for this increment

No `@else`/`@else if`; no nesting; no root-level `@if`; no multi-node body; no new runtime
primitive; no new measured app; no §8 re-verdict. Each is a deliberate deferral, and the
still-unsupported ones fail loudly with a located diagnostic rather than emitting wrong JS.

## Honest ceiling

`@if` graduates from non-goal to subset, measured against Blazor's actual DOM and gated by
alpha-equivalence. RADICAL is not established by it; the §5 subset is still narrow and the §3
non-goals (async, LINQ, generics, DI, routing, forms, `EventCallback`, `RenderFragment`, cascading
params) remain intact. This is one rigorous step, and it is described as exactly that.
