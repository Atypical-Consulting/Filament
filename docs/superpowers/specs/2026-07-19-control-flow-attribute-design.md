# Control-flow-in-attribute: narrow `@if`/`@else` → ternary — design

**Date:** 2026-07-19
**Status:** approved (design), pending spec review
**Kind:** MEASURED subset widening (BENCH entry, DECISIONS entry). NOT a DX/packaging slice.

## Goal

Admit a **`@if` code block as the sole value of `class`** — `class="@if (on) { <text>active</text> } else
{ <text>idle</text> }"` (and the no-`else` form) — by recognizing the narrow shape and **rewriting it
into the equivalent ternary expression**, which the compiler already compiles. Only `class`, only the
sole-value case, only literal `<text>` branches; every other control-flow-in-attribute shape keeps its
current `unaccounted-attribute-value` refusal.

One sentence: recognize a narrow `@if`/`@else` `CSharpCodeAttributeValueIntermediateNode` on `class`,
rewrite it into a `CSharpExpressionAttributeValueIntermediateNode` carrying `cond ? "A" : "B"`, and let
the already-shipped composable path do the rest — measured against Blazor.

## Why this is the next slice

The mixed-`class` slice (#96, BENCH n°15) left control-flow-in-attribute refused via the
`unaccounted-attribute-value` check (`EmitAttribute`, for a `CSharpCodeAttributeValueIntermediateNode`).
That is the deferral this closes — **for the narrow, tractable shape only.**

The pivotal fact: the **ternary already compiles.** `class="@(on ? "active" : "idle")"` is a single
`CSharpExpressionAttributeValueIntermediateNode` and today emits
`effect(() => setAttr(_el, 'class', on.value ? 'active' : 'idle'))` — verified. A narrow `@if`-block is
**semantically identical** to that ternary. So this slice is a **source-to-source rewrite**: turn the
`@if` code node into the ternary expression node, and every existing mechanism (harvest, `SlotJs`,
`ComposeAttributeValue`, reactivity) works unchanged.

**Honest caveat, disclosed:** this adds a *spelling*, not new expressiveness — the author could already
write the `@(…)` ternary. Its value is completing the attribute-value grammar and closing the
`unaccounted` deferral for this shape, measured against Blazor.

## The IR (verified with `--dump-ir`)

`class="@if (on) { <text>active</text> } else { <text>idle</text> }"` lowers to one
`CSharpCodeAttributeValueIntermediateNode` whose children **alternate** CS tokens and `HtmlContent`
literals:

```
CSharpCodeAttributeValueIntermediateNode
  [CS] "if (on) { "
  HtmlContentIntermediateNode  [HTML] "active"
  [CS] " } else { "
  HtmlContentIntermediateNode  [HTML] "idle"
  [CS] " }"
```

The no-`else` form is the three-child prefix: `[CS] "if (on) { "`, `HtmlContent "active"`, `[CS] " }"`.

The front end translates a slot from its token **`Content`**, not by re-slicing source
(`CSharpFrontEnd.RawText`, line 2115: `string.Concat(…FindDescendantNodes<IntermediateToken>()…Content)`).
So a synthesized expression node whose token `Content` is a C# ternary compiles exactly like a
hand-written one.

## The change (generator only)

### 1. Recognizer — `IfBlockTernary`

A pure function on a `CSharpCodeAttributeValueIntermediateNode` returning the synthesized C# ternary
source, or null if the node is not the narrow shape:

```csharp
// The equivalent ternary source for a narrow @if / @if-else code value, or null. Narrow =
//   [CS "if (COND) {"], [HtmlContent LITERAL A], [CS "}"]                       -> `COND ? "A" : ""`
//   [CS "if (COND) {"], [HtmlContent A], [CS "} else {"], [HtmlContent B], [CS "}"] -> `COND ? "A" : "B"`
// where each HtmlContent is PURE literal text (only HTML tokens, no element children). Anything else
// (else-if, foreach, switch, an expression in a branch, nested markup) returns null and stays refused.
static string? IfBlockTernary(CSharpCodeAttributeValueIntermediateNode code)
```

- The opening CS token must match `^\s*if\s*\(\s*(.+?)\s*\)\s*\{\s*$` → `COND`.
- A middle CS token (else form) must match `^\s*\}\s*else\s*\{\s*$`; the final CS token `^\s*\}\s*$`.
- Each branch is a single `HtmlContentIntermediateNode` whose descendants are only `IntermediateToken`s
  (pure literal). Its text becomes a C#-escaped string literal.
- Result: `$"{cond} ? {CSharpString(a)} : {CSharpString(b)}"` — `b` is `""` for the no-`else` form (see
  §Measured decision).

### 2. Rewrite — synthesize the ternary expression node

A pre-pass over the IR, run before harvest. For a `class` attribute whose **sole** value child is a
`CSharpCodeAttributeValueIntermediateNode` with `IfBlockTernary(code) is { } ternary`, replace that
child in place with a synthesized expression node:

```csharp
var synth = new CSharpExpressionAttributeValueIntermediateNode { Source = code.Source };  // Prefix defaults ""
synth.Children.Add(new IntermediateToken { Kind = TokenKind.CSharp, Content = ternary, Source = code.Source });
var i = attr.Children.IndexOf(code);
attr.Children.RemoveAt(i);
attr.Children.Insert(i, synth);
```

Nothing else changes: after the rewrite the attribute's sole child is a normal
`CSharpExpressionAttributeValueIntermediateNode`, so `ComposableValue` returns `[synth]`,
`CollectDynamicAttributes` harvests it into `FreeSlots`, the front end compiles `cond ? "a" : "b"` (and
lifts `cond` to `cond.value` when reactive), and `ComposeAttributeValue` emits the single term. The
result is byte-for-byte what the hand-written `@(…)` ternary produces.

**Placement.** The pre-pass runs in `PrepareComponent`, immediately before `CollectDynamicAttributes`
(which harvests), so the synthesized node is present to be harvested and later found (same object) by
`EmitAttribute`. It is gated to `DynamicAttributes` names (`class`) and to the **sole-value** case, so
it never fires for a non-`class` attribute or for control flow mixed with literal siblings.

### 3. Why sole-value only

Restricting to the sole-value case keeps the ternary a **single fold term** (`setAttr(el, 'class',
cond.value ? 'a' : 'b')`, no `+`), which sidesteps operator-precedence entirely — a ternary spliced
between `+` operators (`'badge ' + cond ? 'a' : '' + ' end'`) would mis-parse. Control flow mixed with
literal siblings is therefore a deferred sub-slice, not part of this one.

## Scope

**In:** a narrow `@if` / `@if…else` code block with **literal `<text>` branches**, as the **sole** value
of `class`. The condition is any expression the front end already compiles (lifted to a signal read when
reactive). Nothing here widens the C# expression subset beyond what the equivalent ternary already uses.

**Refused, deferred, loud + located (unchanged behaviour):**

- **Control flow mixed with literal siblings** — `class="box @if (c) { <text>active</text> }"` (the
  existing `AttributeCodeValue.razor` fixture) is not sole-value, so the rewrite does not fire; it stays
  refused `unaccounted-attribute-value`.
- **Non-literal branches** — `<text>@count</text>` (an expression in a branch). The recognizer requires
  pure-literal `HtmlContent`; anything else returns null → stays refused.
- **`else if` chains, `@foreach`, `@switch`, nested markup in a branch** — recognizer returns null →
  refused.
- **Control flow on a non-`class` attribute** — the rewrite is gated to `DynamicAttributes`; a code value
  on any other name stays refused `unaccounted-attribute-value`.
- **The boolean `disabled` path is untouched** (a `@if` block on `disabled` is not sole-value-ternary
  material and stays refused).

## Runtime

**Unchanged.** The rewrite produces a ternary that compiles to `setAttr(el, 'class', cond ? 'a' : 'b')`
over the existing `setAttr`. No `.ts` edit. The firewall on `src/filament-runtime` stays clean — but this
IS a measured generator widening, so emitted bytes change and a BENCH entry is added.

## The measured app — `IfAttr`

A toggle driving **both** the `@if/@else` form and the no-`else` form, so the false rendering of each is
measured.

`baseline/IfAttr.Blazor/App.razor` (NO trailing `@using`):

```razor
@* Control-flow-in-attribute widening (BENCH n°16): a narrow @if/@else as the sole `class` value,
   compiled to the equivalent ternary. Blank lines between siblings are "\n\n" text nodes. *@

<p id="withelse" class="@if (on) { <text>active</text> } else { <text>idle</text> }">With else</p>

<p id="noelse" class="@if (on) { <text>active</text> }">No else</p>

<button id="toggle" @onclick="Toggle">Toggle</button>

@code {
    private bool on = true;

    private void Toggle()
    {
        on = !on;
    }
}
```

- `#withelse` emits `effect(() => setAttr(p1, 'class', on.value ? 'active' : 'idle'))`.
- `#noelse` emits `effect(() => setAttr(p2, 'class', on.value ? 'active' : ''))` (see the measured
  decision below).
- `on` is reactive (read by both class values, assigned in `Toggle`). `Toggle` writes once → no `batch`.
- Behaviour: `on=true` → both `class="active"`; click → `#withelse` `class="idle"`, `#noelse`
  `class=""` (the measured unknown).

Companion files: `samples/IfAttr/ifattr.js` (answer key), `samples/filament-ifattr-gen/main.js` (host
shim), `baseline/IfAttr.Blazor/` (Blazor project modelled on `MixedAttr.Blazor`).

## The measured decision — no-`else` false rendering

The `@if/@else` form is unambiguous (both branches literal). The **no-`else` false branch** is the one
measured unknown: does Blazor render `class=""` (empty attribute present) or **omit** the attribute?

- **Hypothesis (primary):** Blazor renders `class=""` — the attribute is declared in markup and the code
  block contributes empty content. So `IfBlockTernary` emits `cond ? "A" : ""`, and Filament's
  `on.value ? 'active' : ''` → `setAttr(el, 'class', '')` → `class=""` matches.
- **Fallback (if the oracle shows Blazor OMITS the attribute):** synthesize `cond ? "A" : null` instead;
  the front end compiles `null` → `null`, and `setAttr(el, 'class', null)` → `removeAttribute` (setAttr's
  existing null path). The gate/snapshot/answer key are then written to that emission.

The oracle run against the real Blazor build **decides** this — it is measured, not reasoned.

## The measurement — oracle + BENCH

Correctness-only (like divide/compose/reactiveattr/boolattr/mixedattr): no timing, no weight.

- `bench/harness/bench.mjs` `APPS`: add
  ```js
  ifattr: { readySelector: '#toggle', observeSelector: '#withelse', scenarios: [] },
  ```
  and a `verifyContract` clause (`app === 'ifattr'`): capture `#withelse` and `#noelse` class initial,
  click `#toggle`, capture after. Assert on BOTH builds, identically:
  - initial: `#withelse` class `=== 'active'` and `#noelse` class `=== 'active'`;
  - after:   `#withelse` class `=== 'idle'` and `#noelse` class `=== ''` (or the measured false value).
- `bench/build-filament.sh`: add `filament-ifattr-gen` case arms (mirroring `filament-mixedattr-gen`).
- Publish the Blazor baseline directly to `bench/publish/blazor-ifattr` via `dotnet publish`.
- Record **BENCH n°16**: CORRECTION only. `HARNESS_VERSION` bump **disclosed** (`1.10.0 → 1.11.0`).

## Tests (TDD)

New `tests/Filament.Generator.Tests/IfAttrTests.cs`, mirroring `MixedAttrTests`:

1. **Canon gate** — generated module alpha-equivalent to `samples/IfAttr/ifattr.js`.
2. **Behaviour** — emitted JS contains `effect(`, `setAttr(`, `'class'`, `on.value ? 'active' : 'idle'`
   and `on.value ? 'active' : ''`; does **not** contain `[unaccounted-attribute-value]` or `[dynamic-attribute]`.
3. **Snapshot** — byte-exact against `Snapshots/IfAttr.approved.js`.

`RepoPaths`: add `IfAttrRazor` + `IfAttrAnswerKey`. `Generate`: add `IfAttrToTemp()`.

Regression / negative controls:

- `ReactiveAttrTests`, `BoolAttrTests`, `MixedAttrTests` — all stay green **unchanged** (the rewrite only
  fires for a sole-value `@if` code node; existing shapes are untouched).
- The `AttributeCodeValue.razor` `unaccounted-attribute-value` test — must stay green **unchanged** (`class="box
  @if (true) { <text>active</text> }"` is not sole-value → not rewritten → still refused).
- Add a fixture proving **control flow on a non-`class` attribute** — `title="@if (c) { <text>a</text> }"` —
  stays refused `unaccounted-attribute-value` at its exact location, so the rewrite is gated by the allowlist.

Full suite (subset + analyzer + generator + runtime) stays green.

## Non-goals / disclosure

- Only `class`, only sole-value, only literal branches, only `@if`/`@if…else`.
- Ternary-equivalent: a spelling, not new expressiveness (disclosed).
- No runtime change; no C# subset widening beyond the equivalent ternary.
- Deferred: control flow mixed with literal siblings, non-literal branches, `else if`/`@foreach`/`@switch`,
  non-`class` attributes.

## Decision record

Append **DECISIONS #97** (French, house style): the control-flow-in-attribute widening (narrow) — a
sole-value `@if`/`@else` on `class` rewritten to the equivalent ternary expression node (source-to-source),
compiled by the already-shipped composable path (harvest + `SlotJs` + `ComposeAttributeValue`, byte-identical
to the hand-written `@(…)` ternary); the recognizer's narrow shape and why sole-value (precedence);
`IfBlockTernary` the single recognizer; the `unaccounted-attribute-value` refusal untouched for every
non-narrow shape (mixed siblings, non-literal branches, else-if/foreach/switch, non-`class`); the measured
no-`else` false-rendering decision (empty `""` vs absent `null`, decided by the oracle); measured vs Blazor
(BENCH n°16, `HARNESS_VERSION` bump disclosed); ternary-equivalence disclosed; remaining sub-slices deferred.
