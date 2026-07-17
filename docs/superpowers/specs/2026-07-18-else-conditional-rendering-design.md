# Design: `@else` / `@else if` — second `@if`-family increment

**Date:** 2026-07-18
**Status:** approved (design), pending implementation plan
**Context:** `@if` compiles (DECISIONS #81), lowered to a reused `list()` with a comment anchor and
**zero new runtime primitive**. `@else` / `@else if` are the deferred variants #81 refused with
`else-not-yet-implemented`. This increment moves them into the compiled subset. One construct family;
it does **not** move the §8 verdict.

## Goal

Compile multi-branch template conditionals — `@if` / `@else if …` / `@else` — with the same rigor as
every construct in the repo: canon alpha-equivalence to a Blazor-faithful hand-written answer key,
located diagnostics (mutation-tested) for what stays out, a DECISIONS entry, runtime byte-untouched.
`@else` completes the control-flow pair started by `@if`/`@foreach`; it is *in-subset-but-unimplemented*,
not a hard §3 non-goal. Nothing beyond this construct family is claimed.

## Decisions taken during brainstorming

1. **Sub-part = `@else` / `@else if`** (not multi-node body, nested control flow, root-level, or
   next-sibling anchoring). Highest real-world value, compiler-only, budget-safe.
2. **Full chain, N branches** (not `@else`-only). `if / else if… / else` → branch index `0..N`. Real
   templates use `else if`; the `list()` lowering is identical for two or N branches, so the marginal
   cost is walking the Roslyn else-chain and marking every condition's reads — worth doing once.
3. **Runtime approach = reuse `list()`, key = branch index.** Verified against `list()`'s contract
   (`list(parent, source, keyOf, create, anchor)` where `create(item)` receives the item and
   `keyOf(item)` sets the key): a multi-branch conditional is a keyed 1-item list whose single item's
   value **is** the active branch index. **Zero new runtime primitive; runtime stays 1943 B / 2048 B.**
   Same architectural move as #81.

## The lowering

`if (c0) { B0 } else if (c1) { B1 } else { B2 }` emits:

```js
const anchor = document.createComment('');
insert(container, anchor);
function branch0() { /* B0 */ return root0; }
function branch1() { /* B1 */ return root1; }
function branch2() { /* B2 */ return root2; }
list(container,
  () => c0 ? [0] : c1 ? [1] : [2],   // exactly one item; its value = the active branch index
  i => i,                             // key = branch index
  i => i === 0 ? branch0() : i === 1 ? branch1() : branch2(),
  anchor);
```

- **Source** is a ternary chain selecting the active branch index, wrapped in a one-element array.
- **A chain WITHOUT a final `@else`** returns `[]` when no condition matches — the plain-`@if` case
  (`() => cond ? [0] : []`) generalizes cleanly, no special path.
- **When any condition flips**, `source()` yields a different index → the key changes →
  `reconcile` unmounts the old branch's node and mounts the new branch's → a branch swap, using the
  exact machinery `@foreach` and `@if` already use.
- **The precise JS shape** (ternary chain vs array-index for `create`; formatting) is settled against
  the hand-written answer key during implementation via canon reconciliation, exactly as #81 settled
  `@if`'s single-use inlining and batch choices (DECISIONS #75's "the answer key DICTATES" pattern).

## Reactivity — every branch condition must lift

A field read **only** inside a branch condition must be marked as a template read, or it never lifts
to a signal and the conditional renders once instead of reacting. `@if` already does this for the
single condition via `MarkConditionReads` at step 2c (`CSharpFrontEnd.cs`), before `Body(...)` and
`TranslateSlots(...)` read `IsSignal`. This increment **extends `MarkConditionReads` to walk the
whole else-if chain**, marking `c0, c1, …` alike. A condition over only never-assigned state renders
once (fixed source), consistent with `@if`.

## Scope

### In this increment
- `@if (c0) { <e0> } else if (c1) { <e1> } … else { <en> }` with any number of `else if` clauses,
  with or without a trailing `@else`.
- Each branch body is **exactly one element** (the existing single-root rule, applied per branch,
  because `list()` returns one `Node` per item).
- Conditions are boolean expressions over `@code` state using operators already in the §5 subset,
  reusing the existing escape analysis.

### Explicitly out — each → a located diagnostic, no file written; deferred to later increments
- **A multi-node branch body** (`@else { <a/><b/> }`) → `unsupported-if-body` (per branch).
- **Nested control flow inside a branch** (`@if`/`@foreach` within a branch body) → refused.
- **`@if`/`@else` at the template root** → `template-code-at-root` (root control flow stays deferred,
  entangled with the open #77 cascade).
- **Next-sibling anchoring** — the comment anchor carries over as a disclosed +1-node divergence
  (category #20); removing it is a separate sub-part.

## Gate & the seeded-divergence discipline

- New sample: `samples/IfElse/IfElse.razor` (a component exercising `@if`/`@else if`/`@else`) and a
  **hand-written** `samples/IfElse/ifelse.js` answer key, mirroring the `samples/If/` pattern.
- **Per #81's reviewer note, the answer key is written FIRST from Blazor's own DOM contract**, read
  from a throwaway `IfElseRef.razor`'s `BuildRenderTree` (decision-64 method: build with
  `-p:EmitCompilerGeneratedFiles=true`, inspect the generated `*.g.cs`, delete), **before** looking
  at generator output — so a genuine divergence is seeded, not back-fitted.
- Blazor wraps branches in `OpenRegion`/`CloseRegion` (compiler bookkeeping, no DOM-visible
  consequence — see `samples/If/if.js`'s finding #2). The key reproduces only the **active branch's
  DOM**, not the regions.
- **Gate:** a fresh regeneration of `IfElse.razor` is `canon` **ALPHA-EQUIVALENT** to `ifelse.js`
  (`exit 0`).

## Diagnostics

- **Remove** `else-not-yet-implemented` — `@else`/`@else if` are now accepted.
- **Keep**, each mutation-tested (a backstop that is not tested is a claim, #61): multi-node branch
  body → `unsupported-if-body`; nested control flow in a branch → refused; `@if`/`@else` at root →
  `template-code-at-root`.

## Testing

Same shape as #81:
- **Canon gate** — `IfElse.razor` regen → alpha-equivalent to `ifelse.js`.
- **Approval snapshot** — `IfElse.approved.js` pins the generator's exact emission.
- **In-browser behavior** — driving the component, each branch shows iff its condition is the first
  true one; flipping conditions swaps branches; the no-match (no-`@else`) case shows nothing.
- **Per-branch reactivity** — a field read only in `c1` lifts to a signal (asserted from emitted JS).
- **Located diagnostics** — the three still-deferred variants each refuse with a located message and
  **write no file**; each guard mutation-tested (neutralize → confirm failure → restore).
- **Closed-runtime invariant** — `git diff --stat src/filament-runtime` empty; size gate 1943 B /
  2048 B (same assertion as #81's Task 4).
- **Suite** grows and stays green.

## DECISIONS #82

Records: the choice; the branch-index-as-key `list()` reuse (zero new primitive); the ternary-chain
source and no-final-`else` → `[]` generalization; the carried-over comment-anchor divergence; the
`MarkConditionReads` chain-walk ordering; and the still-deferred variants. **Honest ceiling
unchanged:** §5 subset widens by one construct family; RADICAL stays "not eliminated, not
established."
