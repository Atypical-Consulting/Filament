# ADR 0002 — Bucket B: the framework-layer roadmap

**Status:** Accepted · **Date:** 2026-07-20 · **Scope:** Bucket B (framework features)

> Bucket B is the largest frontier: the spec §3 non-goals — the features that make components a
> *system* rather than a page. This ADR does two things. It records an **empirical audit** that found
> two of those features already work (now banked with tests), and it **sequences the genuine gaps**
> with a tractability estimate for each, so Bucket B has a concrete plan instead of a wishlist.

## The audit — measure, don't pre-defer

The repo's most-repeated lesson (recorded nine times): *a suspected gap is often a conservative
over-refusal, or a capability that already works and was simply never tested.* Before treating any
Bucket B item as unimplemented, each was **run through the generator**. Results:

| Capability | Probe | Verdict |
|-----------|-------|---------|
| **Multi-parameter fan-out** | child with 2 params, parent binds one static string + one reactive int | ✅ **already compiles** — `EmitComposition`'s attribute loop never limited the count |
| **Nested composition** (3 levels) | parent → Mid → Grand, a reactive param threaded through | ✅ **already compiles** — children inline recursively into one `mount()` |
| **EventCallback** (child→parent) | child raises `[Parameter] EventCallback OnClick` to a parent method | ❌ `FIL0001 [unresolved-name]` — genuine gap |
| **RenderFragment / ChildContent** | child renders `@ChildContent`, parent passes markup | ❌ `FIL0002 [unsupported-type]` — genuine gap |
| Routing / DI / inheritance / `@ref` / CascadingParameter / forms / generics / JsInterop | directive- and type-level | ❌ genuine non-goals (directive allowlist / type subset) |

**Two "framework" items were not gaps at all.** Multi-parameter fan-out and nested composition are
coverage-widenings of #88 (static-leaf) and #90 (single bound parameter) — the *same*
`EmitComposition` machinery, exercised with N parameters and N levels. They are now **banked** by
`CompositionFanoutTests` (behavior + byte snapshots) and **DECISIONS #129**. This is the honest first
Bucket B advance: two capabilities moved from *assumed-missing* to *pinned-and-verified*.

## The genuine gaps, sequenced

Ordered by value-per-effort. Each estimate assumes the full slice discipline (baseline + answer key +
oracle + tests + BENCH + DECISIONS).

### 1. EventCallback — child → parent (highest value, medium effort)

The missing half of composition: a child raising an event the parent handles. This is what turns
inlined leaves into interacting components.

- **Why it's blocked today:** a child carrying a handler is not a *leaf display*, so `IsLeafDisplay`
  refuses it, and `OnClick="@Bump"` doesn't resolve as a bound value.
- **The shape of the fix:** at the composition site, an `EventCallback` parameter binds the *parent's*
  method; inside the inlined child, `@onclick="OnClick"` lowers to a call to that parent method (which
  the existing handler machinery already translates). No child instance, no runtime primitive — it
  reuses the same inline-into-parent-scope model composition already uses.
- **Boundary to keep refused:** `EventCallback<T>` with an argument, and `async` callbacks, until
  measured.

### 2. RenderFragment / ChildContent — markup passed into a child (high value, higher effort)

`<Card><p>…</p></Card>` — the parent hands a fragment of markup to the child to place.

- **Why it's blocked:** `RenderFragment` is an out-of-subset *type*, and the child body `@ChildContent`
  has no source to inline.
- **The shape of the fix:** the child's `@ChildContent` slot is filled by inlining the parent-supplied
  markup subtree at that position — a structural inline, sibling to how the child's own markup inlines.
  The `RenderFragment` type is admitted only in the `[Parameter] RenderFragment ChildContent` position.

### 3. The directive-level non-goals (largest effort, reconsider only on a RADICAL commitment)

Routing (`@page`), DI (`@inject`), inheritance (`@inherits`), `@ref`, `CascadingParameter`, forms,
generics, JsInterop. Each is a spec §3 non-goal for a reason: they imply runtime services a static
module has no home for (a router, a DI container, a form/validation system). They are **not** a natural
extension of the compile-time model — they are new subsystems. They belong to the question *"is
RADICAL the architecture?"* (spec §8), not to widening the current subset, and should be scoped
individually if and when that question is answered yes.

## Decision

1. **Bank the two already-working capabilities** (multi-parameter fan-out, nested composition) — done,
   `CompositionFanoutTests` + DECISIONS #129.
2. **Sequence the genuine gaps** as above. EventCallback is the recommended next Bucket B slice: it is
   the highest-value gap, and its fix reuses the existing inline-into-parent model rather than adding a
   subsystem.
3. **Hold the directive-level non-goals** behind the RADICAL/PRUDENT decision (spec §8). They are not
   subset-widenings; committing to them is committing to build a framework, which the evidence does not
   yet justify.

## Note on measurement

The banked capabilities were verified by generator tests (shape + byte snapshot), not a *new* oracle
app, because they are the identical reactive-binding mechanism #90 already measured faithful against
Blazor (BENCH n°12): a bound parameter is the parent's translated expression wired as a live effect,
independent of how many parameters or levels participate. A distinct oracle witness would re-measure
the same mechanism. When EventCallback lands (a genuinely new mechanism), it gets its own baseline and
oracle run — the discipline every genuinely-new slice follows.
