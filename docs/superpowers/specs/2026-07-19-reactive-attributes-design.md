# Reactive attributes (string-valued `class`) — design

**Date:** 2026-07-19
**Status:** approved (design), pending spec review
**Kind:** MEASURED subset widening (BENCH entry, DECISIONS entry). NOT a DX/packaging slice.

## Goal

Admit a **reactive string attribute** into the compiled subset: `class="@expr"` on a plain
element compiles to a live binding that updates the attribute as the state it reads changes —
exactly as `@expr` in text content already does. The measured attribute is **`class`** and only
`class`; every other dynamic attribute stays refused, loud and located.

One sentence: move the single `dynamic-attribute` refusal for `class="@expr"` onto an emission path
that mirrors `EmitBinding`, and measure it against Blazor for DOM-contract equivalence.

## Why this is the next slice

`setAttr(el, name, v)` already exists in the runtime and is already listed in `RuntimeExports`; the
static-attribute path already emits `setAttr(td, 'class', 'col-md-1')` (rows.js). The **only** thing
missing is the reactive/dynamic *value* path, which today is a flat refusal
(`TemplateCompiler.EmitAttribute`, reason `dynamic-attribute`). Text content already proves the
pattern (`EmitBinding`): `SlotJs` gives the C#-compiled expression, `SlotIsReactive` decides
effect-vs-create-time. This slice reuses that machinery for attribute values. Because the primitive
already ships, **the runtime does not change** — this is a generator-only widening.

## The change (generator only)

### 1. Emission — `TemplateCompiler.EmitAttribute`

The C# branch currently does: unwrap-event-callback → else refuse `dynamic-attribute`. Insert a
middle case, gated on an **attribute-name allowlist** (see §Scope), that mirrors `EmitBinding`:

- **reactive** (`_code.SlotIsReactive(exprNode)` is true):
  ```js
  effect(() => setAttr(el, 'class', <SlotJs>));
  ```
  emitted into `_bindings`, `_used.Add("effect"); _used.Add("setAttr");`
- **non-reactive** (`SlotIsReactive` is false): a create-time write, no effect, no subscription:
  ```js
  setAttr(el, 'class', <SlotJs>);
  ```
  emitted into `_create`, `_used.Add("setAttr");`

`<SlotJs>` is `_code.SlotJs(exprNode)` — the front end's translation of the author's C#, **never a
verbatim splice** (the invariant the whole compiler defends). This is the same reactive/non-reactive
split `EmitBinding` makes for text; `class` binding is that rule with the write target being an
attribute instead of a text node.

Ordering is preserved: the reactive `setAttr` lands in `_bindings`, which is emitted **before**
`_attach`. So its first run writes into the still-detached tree and produces no `MutationRecord` —
the attach-last / C3 invariant is untouched.

### 2. Plumbing — harvest the value expression into `plan.FreeSlots`

`SlotJs`/`SlotIsReactive` only answer for nodes the front end compiled, i.e. nodes harvested into
`plan.FreeSlots` (or region markup slots). The `Collect` walk filters out
`HtmlAttributeIntermediateNode`, so an attribute expression on a **plain element** is never
harvested — only `CollectComponentBindings` harvests them, and only for component elements.

Extend collection to harvest the value expression of an **allow-listed dynamic attribute on a plain
element** into `plan.FreeSlots`, with two guards so nothing else is drawn in:

- **allow-listed name only** — the same `class`-only allowlist §Scope defines. `value`, `disabled`,
  `style`, `@bind`-lowered `value`, etc. are not harvested and stay on the refusal path.
- **not an event handler** — discriminated with the existing `TryUnwrapEventCallback` on the
  expression text. Event attributes (`@onclick`) carry `EventCallback.Factory.Create<…>(this, …)`;
  they must keep their `listen()` path and never become slots (naming a method cannot make a field
  reactive — the existing invariant).

This is the identical harvest `CollectComponentBindings` already performs, applied to plain elements
under a name guard. `EmitAttribute` then reads `SlotJs`/`SlotIsReactive` back off the same node — the
same node identity the front end saw, so no drift.

### 3. Attribute-name allowlist

A new `static readonly HashSet<string> DynamicAttributes = new(OrdinalIgnoreCase) { "class" };`,
sitting beside `PropertyAttributes` and modelled on it and on `AllowedDirectives`. It is the single
source of "which attribute names may carry a compiled dynamic value." Both the harvest (§2) and the
emission (§1) consult it; an attribute not in it is refused exactly as today.

## Scope

**In:** a single pure `@expr` value on `class` — `class="@statusClass"`, `class="@currentCount"` —
both reactive and (for symmetry/correctness) non-reactive. Nothing here widens the **C# expression
subset**: the demo's `@code` uses only already-admitted constructs (string/int fields, literal
reassignment, `++`).

**Refused, deferred, loud + located (unchanged behaviour):**

- **Any dynamic attribute other than `class`** — `value`, `disabled`, `style`, … Kept on the current
  `dynamic-attribute` refusal, whose message is updated to name the allowlist (`class` is admitted;
  this name is not yet measured). The message continues to echo `Trunc(expr)`, so `@bind`'s lowered
  `value="BindConverter.FormatValue(…)"` still refuses as `[dynamic-attribute]` naming `BindConverter`
  — `DiagnosticTests.Bind_IsRefused_AtItsExactLocation` passes unchanged.
- **Boolean / present-absent attributes** (`disabled="@b"`) — a genuinely different semantic (Blazor
  renders the attribute present with no value when true and omits it when false; naive
  `setAttr(el,'disabled',true)` yields `disabled="true"`). Explicitly a follow-on, not this slice.
- **Mixed literal + expression** (`class="box @extra"`) and **control flow in an attribute value**
  (`class="@if(c){…}"`, a `CSharpCodeAttributeValueIntermediateNode`) — the emission path admits only
  a single `CSharpExpressionAttributeValueIntermediateNode` with **no** `HtmlAttributeValueIntermediateNode`
  sibling. Concatenation stays refused (`dynamic-attribute`), control flow stays refused
  (`unaccounted-attribute-value`).
- **Dynamic attributes on component elements** — untouched; still handled by `EmitComposition`.

## Runtime

**Unchanged.** `setAttr` already exists (`src/filament-runtime/src/dom.ts`) and is exported and in
`RuntimeExports`. No `.ts` edit, no new primitive. (The firewall on `src/filament-runtime` therefore
stays clean for this slice — but note this IS a measured generator widening, so the generator's
emitted bytes DO change and a BENCH entry IS added; that is the opposite of the DX-slice firewall.)

## The measured app — `ReactiveAttr`

A counter-shaped app carrying **both** an established reactive text binding and the new reactive
`class` binding, driven by the same click, so the measurement isolates exactly one new variable: the
reactive read moved into an attribute.

`baseline/ReactiveAttr.Blazor/App.razor` (shared DOM contract — blank lines between siblings are
`"\n\n"` text nodes, per counter.js):

```razor
<h1 id="title">Counter</h1>

<p id="status" class="@statusClass">Current count: <span id="counter-value">@currentCount</span></p>

<button id="increment" @onclick="Increment">Click me</button>

@code {
    private int currentCount = 0;
    private string statusClass = "zero";

    private void Increment()
    {
        currentCount++;
        statusClass = "counting";
    }
}
@using Microsoft.AspNetCore.Components.Web
```

- `statusClass` is read by the template (the `class` attribute) and assigned outside construction →
  lifted to a signal → the `class` binding is a live `effect(() => setAttr(p, 'class', statusClass.value))`.
- `currentCount` is the familiar reactive text binding.
- `Increment` performs two writes → `MayWriteMoreThanOnce` → the handler is wrapped in `batch(...)`,
  coalescing the text-signal and attribute-signal writes into one flush. The slice thus also exercises
  batch spanning a text and an attribute binding.
- Behaviour: first click, count `0→1` (text) and class `zero→counting`; subsequent clicks keep
  incrementing the count while the class stays `counting`.

Companion files:

- `samples/ReactiveAttr/reactiveattr.js` — the hand-written **answer key** (Blazor-faithful),
  transcribing the source exactly (whitespace nodes included). Never edited to make a gate pass
  (decisions 21/51).
- `samples/filament-reactiveattr-gen/` — the generator's output (built by the bench script), for the
  oracle.
- `baseline/ReactiveAttr.Blazor/` — a normal Blazor project (Program.cs, wwwroot, css) modelled on
  `Divide.Blazor`, so the oracle can publish and drive the real Blazor build.

## The measurement — oracle + BENCH

Correctness-only (like divide/compose/rootforeach/rootif): no timing, no weight.

- `bench/harness/bench.mjs` `APPS`: add
  ```js
  reactiveattr: { readySelector: '#increment', observeSelector: '#status', scenarios: [] },
  ```
  and a `verifyContract` clause: click `#increment`, assert `#status`'s `class` goes `zero → counting`
  and `#counter-value` text goes `0 → 1`, **identically** on the Blazor baseline and the Filament
  build.
- `bench/build-filament.sh`: add `filament-reactiveattr-gen` cases (razor path →
  `baseline/ReactiveAttr.Blazor/App.razor`, output `ReactiveAttr.g.js`, source name `ReactiveAttr`,
  css path).
- `bench/publish-baseline.sh`: add a `blazor-reactiveattr` mapping to `baseline/ReactiveAttr.Blazor`
  if a Blazor-side publish is needed for the run.
- Record **BENCH n°13**: CORRECTION only (C1/C3/C4 not claimed — trivial app, owner's standing
  decision for these correctness slices). `HARNESS_VERSION` bump **disclosed** (bench.mjs changed:
  new branch + `APPS` entry). No prior weight/speed figure is invalidated.

## Tests (TDD)

New `tests/Filament.Generator.Tests/ReactiveAttrTests.cs`, mirroring `BoundComposeTests`:

1. **Canon gate** — generated module is alpha-equivalent to `samples/ReactiveAttr/reactiveattr.js`
   (`Run.Node(RepoPaths.Canon, generated, answerKey)`). The spec is the reference; the generator is
   judged.
2. **Behaviour** — the emitted JS contains `effect(` and `setAttr(` and `statusClass.value`, and does
   **not** contain `[dynamic-attribute]`.
3. **Snapshot** — byte-exact against `Snapshots/ReactiveAttr.approved.js` (the wall against silent
   generator regressions the name-blind canon gate cannot see).

`RepoPaths`: add `ReactiveAttrRazor` (→ `baseline/ReactiveAttr.Blazor/App.razor`) and
`ReactiveAttrAnswerKey`. `Generate`: add `ReactiveAttrToTemp()`.

Regression / negative controls:

- `DiagnosticTests.Bind_IsRefused_AtItsExactLocation` — must stay green **unchanged** (proves `@bind`
  still refuses `[dynamic-attribute]` naming `BindConverter`, because `value` is not allow-listed).
- Add a fixture under the existing `tests/Filament.Generator.Tests/Unsupported/` set (driven by
  `DiagnosticTests.Refused(...)`, as `Bind.razor` is) proving a **non-allow-listed** dynamic attribute
  — `title="@caption"` — still refuses `[dynamic-attribute]` at its exact location with the updated
  message naming the `class` allowlist, so the allowlist is a measured boundary, not folklore.

Full suite (subset + analyzer + generator + runtime) stays green.

## Non-goals / disclosure

- Only `class`. Other string attributes are *correct* under `setAttr` but not *measured*; admitting
  them without a measurement is the exact "ship an emission path no measurement covers" the current
  refusal exists to prevent.
- Boolean/present-absent attributes: deferred (distinct semantic).
- Mixed literal+expression and attribute control flow: deferred.
- No C# subset widening: the demo uses only already-admitted C#.
- Non-reactive dynamic `class` (a never-reassigned field) is handled for symmetry (create-time
  `setAttr`) but is not the headline; the measured claim is the reactive transition.

## Decision record

Append **DECISIONS #94** (French, house style): the reactive-`class` widening — the `dynamic-attribute`
refusal narrowed to an emission for the `class` allowlist, mirroring `EmitBinding`; runtime unchanged
(`setAttr` already shipped); the attribute-name allowlist as the measured boundary and why `@bind`
stays refused; measured vs Blazor via the oracle (BENCH n°13, `HARNESS_VERSION` bump disclosed);
boolean / mixed / other-attribute sub-slices deferred.
