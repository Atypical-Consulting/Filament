# Boolean attribute (present/absent `disabled`) — design

**Date:** 2026-07-19
**Status:** approved (design), pending spec review
**Kind:** MEASURED subset widening (BENCH entry, DECISIONS entry). NOT a DX/packaging slice.

## Goal

Admit a **boolean attribute** into the compiled subset: `disabled="@expr"` on a plain element
compiles to a live binding that renders the attribute **present when the state it reads is true and
absent when false** — Blazor's boolean-attribute contract — updating as that state changes. The
measured attribute is **`disabled`** and only `disabled`; every other boolean/dynamic attribute stays
refused, loud and located.

One sentence: move the `dynamic-attribute` refusal for `disabled="@expr"` onto an emission path that
maps the boolean to present/absent via the runtime's existing `setAttr` null→remove primitive, and
measure it against Blazor for DOM-contract equivalence.

## Why this is the next slice

The reactive-`class` slice (#94, BENCH n°13) admitted a reactive **string** attribute. `disabled` was
explicitly deferred there because a boolean attribute is a *different semantic*: Blazor renders
`<button disabled>` when the value is true and omits the attribute entirely when false — never
`disabled="true"` or `disabled="false"`. The prior spec named the trap directly: "naive
`setAttr(el,'disabled',true)` yields `disabled=\"true\"`."

The primitive to express present/absent, however, **already ships**. `setAttr` is:

```ts
// src/filament-runtime/src/dom.ts
export function setAttr(el: Element, name: string, v: unknown): void {
  if (v == null) el.removeAttribute(name);      // "null/undefined removes it —
  else el.setAttribute(name, v as string);      //  that is how a compiler expresses 'absent'."
}
```

The runtime author built that null→remove branch for exactly this. So the boolean contract is a
**generator-only** emission: map the C# boolean to `('' | null)` and hand it to `setAttr`. `true → ''
→ setAttribute` (present, empty value, matching `<button disabled>`); `false → null →
removeAttribute` (absent). No new runtime op, no `RuntimeExports` change — the firewall on
`src/filament-runtime` stays clean.

## The change (generator only)

### 1. Emission — `TemplateCompiler.EmitAttribute`

The C# branch currently does: unwrap-event-callback → the `class` allowlist middle case → else refuse
`dynamic-attribute`. Add a **second middle case**, gated on a **boolean-attribute-name allowlist**
(see §Scope), that mirrors the `class` case but wraps the compiled expression in a boolean→present/absent
ternary:

- **reactive** (`_code.SlotIsReactive(exprNode)` is true):
  ```js
  effect(() => setAttr(el, 'disabled', <SlotJs> ? '' : null));
  ```
  emitted into `_bindings`, `_used.Add("effect"); _used.Add("setAttr");`
- **non-reactive** (`SlotIsReactive` is false): a create-time write, no effect, no subscription:
  ```js
  setAttr(el, 'disabled', <SlotJs> ? '' : null);
  ```
  emitted into `_create`, `_used.Add("setAttr");`

`<SlotJs>` is `_code.SlotJs(exprNode)` — the front end's translation of the author's C#, **never a
verbatim splice** (the invariant the whole compiler defends). The only difference from the `class`
case is the emitted value expression: `<SlotJs>` becomes `(<SlotJs> ? '' : null)`. This is the
"different emission (present/absent, not setAttr of 'true')" the generator's own comment (line 170)
already names — supplied by a ternary over the existing op, not by a new op.

Ordering is preserved exactly as for `class`: the reactive `setAttr` lands in `_bindings`, emitted
**before** `_attach`. Its first run writes into the still-detached tree and produces no
`MutationRecord` — the attach-last / C3 invariant is untouched. (For the measured app, that first run
has `locked=true`, so it exercises `setAttribute('disabled','')` into the detached tree; the later
click exercises `removeAttribute` — both primitives, one scenario.)

### 2. Plumbing — harvest the value expression into `plan.FreeSlots`

`SlotJs`/`SlotIsReactive` only answer for nodes harvested into `plan.FreeSlots`. The `class` slice
added `CollectDynamicAttributes`, which harvests the value expression of an allow-listed dynamic
attribute on a plain element (with the "single pure `@expr`, not an event handler" guards via the
shared `DynamicValue` predicate).

Widen `CollectDynamicAttributes` to also harvest when the attribute name is in the **boolean**
allowlist — i.e. harvest when the name is in `DynamicAttributes` **or** `BooleanAttributes`. The
harvest logic, the `DynamicValue` predicate (one pure `CSharpExpressionAttributeValueIntermediateNode`,
no `HtmlAttributeValueIntermediateNode` sibling, not an `EventCallback`), and the FreeSlots identity
are **unchanged** — only the name guard grows. `EmitAttribute` reads `SlotJs`/`SlotIsReactive` back off
the same node the front end saw, so no drift (decision 53).

### 3. Boolean-attribute-name allowlist

A new `static readonly HashSet<string> BooleanAttributes = new(OrdinalIgnoreCase) { "disabled" };`,
sitting beside `DynamicAttributes` and modelled on it. It is the single source of "which attribute
names carry a compiled **boolean** (present/absent) value." Both the harvest (§2) and the emission
(§1) consult it. The two allowlists are **disjoint** (`class` vs `disabled`), so the two `EmitAttribute`
middle cases never contend and their order is immaterial; a name in neither is refused exactly as
today.

## Scope

**In:** a single pure `@expr` value on `disabled` — `disabled="@locked"` — both reactive and (for
symmetry/correctness) non-reactive. Nothing here widens the **C# expression subset**: the demo's
`@code` uses only already-admitted constructs (a `bool` field, `!` negation in a `void` method).

**Identification is name-based, and that is the documented boundary.** The generator performs no C#
type inference, so `disabled` is *assumed* boolean present/absent. Disclosed divergence: in Blazor a
**string**-typed `disabled` (`disabled="@someString"`) renders the attribute present with that string
as its value, whereas this slice always emits present/absent. The measured app binds a `bool`, so it
matches Blazor exactly — the same "the allowlist name commits to one semantic" discipline as
`PropertyAttributes` (which commits `id` to a property write). A string-typed `disabled` is a
deferred, distinct case.

**Refused, deferred, loud + located (unchanged behaviour):**

- **Any boolean attribute other than `disabled`** — `checked`, `readonly`, `hidden`, `required`, …
  Kept on the current `dynamic-attribute` refusal, whose message is updated to name **both** allowlists
  (`class` = reactive string; `disabled` = boolean present/absent). Every other name is not one of them.
- **Any dynamic string attribute other than `class`** — unchanged from #94; `title`, `value`, `style`,
  … still refuse. The message continues to echo `Trunc(expr)`, so `@bind`'s lowered
  `value="BindConverter.FormatValue(…)"` still refuses as `[dynamic-attribute]` naming `BindConverter`
  — `DiagnosticTests.Bind_IsRefused_AtItsExactLocation` passes unchanged.
- **String-typed `disabled`** — indistinguishable from a bool at the IR level without type inference;
  the name-based allowlist commits `disabled` to present/absent. Deferred, disclosed.
- **Mixed literal + expression** and **control flow in an attribute value** — the emission path admits
  only a single `CSharpExpressionAttributeValueIntermediateNode` with **no**
  `HtmlAttributeValueIntermediateNode` sibling (the `DynamicValue` guard). Unchanged.
- **Dynamic attributes on component elements** — untouched; still handled by `EmitComposition`.

## Runtime

**Unchanged.** `setAttr` already exists (`src/filament-runtime/src/dom.ts`), is exported, and is in
`RuntimeExports`; its `v == null → removeAttribute` branch is the present/absent primitive. No `.ts`
edit, no new export, no `RuntimeExports` reorder. The firewall on `src/filament-runtime` stays clean
(runtime tests remain byte-identical) — but note this IS a measured generator widening, so the
generator's emitted bytes DO change and a BENCH entry IS added; that is the opposite of the DX-slice
firewall.

## The measured app — `BoolAttr`

A minimal two-button app that isolates exactly one new variable — a boolean value bound to
`disabled` — and drives a full round trip through **both** primitives (`setAttribute` at mount,
`removeAttribute` at click) in a single scenario.

`baseline/BoolAttr.Blazor/App.razor` (shared DOM contract — blank lines between siblings are `"\n\n"`
text nodes):

```razor
<button id="target" disabled="@locked">Target</button>

<button id="toggle" @onclick="Toggle">Toggle</button>

@code {
    private bool locked = true;

    private void Toggle()
    {
        locked = !locked;
    }
}
```

- `locked` is read by the template (the `disabled` attribute) and assigned outside construction
  (`Toggle`) → lifted to a signal → the `disabled` binding is a live
  `effect(() => setAttr(target, 'disabled', locked.value ? '' : null))`.
- `locked` starts **`true`**, so the initial render (binding effect, run before attach) writes
  `setAttr(target,'disabled','')` → `setAttribute` → `#target` disabled **present**, into the detached
  tree (no MutationRecord, C3-clean).
- Clicking `#toggle` flips `locked` to `false` → the effect re-runs → `setAttr(target,'disabled',null)`
  → **`removeAttribute`** → `#target` disabled **absent**. This is the genuinely novel path
  (`class` never removes).
- `Toggle` performs one write, so the handler is a plain assignment (no `batch`); the slice does not
  need batch to make its point (batch across a text+attribute binding is already covered by #13).

Companion files:

- `samples/BoolAttr/boolattr.js` — the hand-written **answer key** (Blazor-faithful), transcribing the
  source exactly (whitespace nodes included). Never edited to make a gate pass (decisions 21/51).
- `samples/filament-boolattr-gen/` — the generator's output (built by the bench script), for the
  oracle.
- `baseline/BoolAttr.Blazor/` — a normal Blazor project (Program.cs, wwwroot, css) modelled on
  `ReactiveAttr.Blazor`, so the oracle can publish and drive the real Blazor build.

## The measurement — oracle + BENCH

Correctness-only (like divide/compose/rootforeach/rootif/reactiveattr): no timing, no weight.

- `bench/harness/bench.mjs` `APPS`: add
  ```js
  boolattr: { readySelector: '#toggle', observeSelector: '#target', scenarios: [] },
  ```
  and a `verifyContract` clause (`app === 'boolattr'`): capture `#target` **initial**, click `#toggle`,
  capture **after**. Assert on BOTH the Blazor baseline and the Filament build, identically:
  - initial: `#target.hasAttribute('disabled') === true` **and** `#target.disabled === true`;
  - after:   `#target.hasAttribute('disabled') === false` **and** `#target.disabled === false`.

  Both `hasAttribute` (the DOM-serialization contract) and the `.disabled` IDL property are asserted so
  the measurement pins what Blazor *actually* does, not what we assume; if Blazor diverged (e.g. kept a
  different value string) the oracle would catch it.
- `bench/build-filament.sh`: add `filament-boolattr-gen` cases (razor path →
  `baseline/BoolAttr.Blazor/App.razor`, output `BoolAttr.g.js`, source name `BoolAttr`, title
  `BoolAttr`, blazor label `blazor-boolattr`, css path — mirroring the `filament-reactiveattr-gen`
  arms).
- Publish the Blazor baseline directly to `bench/publish/blazor-boolattr` via `dotnet publish` (matching
  the divide/compose/boundcompose/reactiveattr precedent — correctness-only apps are not in
  `publish-baseline.sh`).
- Record **BENCH n°14**: CORRECTION only (C1/C3/C4 not claimed — trivial app, owner's standing decision
  for these correctness slices). `HARNESS_VERSION` bump **disclosed** (bench.mjs changed: new branch +
  `APPS` entry). No prior weight/speed figure is invalidated.

## Tests (TDD)

New `tests/Filament.Generator.Tests/BoolAttrTests.cs`, mirroring `ReactiveAttrTests`:

1. **Canon gate** — generated module is alpha-equivalent to `samples/BoolAttr/boolattr.js`
   (`Run.Node(RepoPaths.Canon, generated, answerKey)`). The spec is the reference; the generator is
   judged.
2. **Behaviour** — the emitted JS contains `effect(` and `setAttr(` and `locked.value ? '' : null`, and
   does **not** contain `[dynamic-attribute]` and does **not** contain `disabled="true"`/`'disabled', true`
   (the naive-boolean trap).
3. **Snapshot** — byte-exact against `Snapshots/BoolAttr.approved.js` (the wall against silent generator
   regressions the name-blind canon gate cannot see).

`RepoPaths`: add `BoolAttrRazor` (→ `baseline/BoolAttr.Blazor/App.razor`) and `BoolAttrAnswerKey`.
`Generate`: add `BoolAttrToTemp()`.

Regression / negative controls:

- `DiagnosticTests.Bind_IsRefused_AtItsExactLocation` — must stay green **unchanged** (proves `@bind`
  still refuses `[dynamic-attribute]` naming `BindConverter`, because `value` is in neither allowlist).
- `DiagnosticTests.DynamicNonClassAttribute_IsRefused_AtItsExactLocation` (the `title="@caption"`
  `DynamicTitle` fixture) — stays green; its assertion on the message is updated to tolerate the message
  now naming both allowlists (it must still name `class`, and now may also name `disabled`).
- Add a fixture under `tests/Filament.Generator.Tests/Unsupported/` proving a **non-allow-listed boolean
  attribute** — `readonly="@ro"` (or `checked="@on"`) — still refuses `[dynamic-attribute]` at its exact
  location with the updated message, so the boolean allowlist is a measured boundary, not folklore.

Full suite (subset + analyzer + generator + runtime) stays green.

## Non-goals / disclosure

- Only `disabled`. Other boolean attributes are *expressible* under the same ternary but not *measured*;
  admitting them without a measurement is the exact "ship an emission path no measurement covers" the
  current refusal exists to prevent.
- String-typed `disabled`: deferred (name-based allowlist commits `disabled` to present/absent; no type
  inference).
- Mixed literal+expression and attribute control flow: deferred (unchanged from #94).
- No C# subset widening: the demo uses only already-admitted C# (a `bool` field, `!`, a `void` method).
- Non-reactive `disabled` (a never-reassigned bool field) is handled for symmetry (create-time
  `setAttr(...?'':null)`) but is not the headline; the measured claim is the reactive present→absent
  transition.

## Decision record

Append **DECISIONS #95** (French, house style): the boolean-`disabled` widening — the `dynamic-attribute`
refusal narrowed to a present/absent emission for the `disabled` allowlist; runtime unchanged
(`setAttr`'s null→remove is the primitive, a ternary maps the bool to `''`/`null`); the
boolean-attribute-name allowlist as a second measured boundary beside `class`, name-based because there
is no type inference (and why a string-typed `disabled` stays deferred); measured vs Blazor via the
oracle asserting both `hasAttribute` and the `.disabled` property (BENCH n°14, `HARNESS_VERSION` bump
disclosed); other-boolean-name / string-disabled / mixed sub-slices deferred.
