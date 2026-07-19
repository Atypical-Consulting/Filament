# Mixed literal+expression `class` value — design

**Date:** 2026-07-19
**Status:** approved (design), pending spec review
**Kind:** MEASURED subset widening (BENCH entry, DECISIONS entry). NOT a DX/packaging slice.

## Goal

Admit a **mixed literal+expression value** on the allow-listed string attribute `class`:
`class="badge @statusClass rounded"` compiles to a live binding whose value is the **composition** of
the literal and expression parts, updating as the state it reads changes. Only `class` (the string
allowlist); every other attribute — and every other value shape — keeps its current refusal, loud and
located.

One sentence: move the `dynamic-attribute` refusal for a mixed `class` value onto a prefix-aware
composition path that folds the ordered value parts into a single concatenated string, and measure it
against Blazor for DOM-contract equivalence.

## Why this is the next slice

The reactive-`class` slice (#94, BENCH n°13) admitted a **single pure** `@expr` value on `class`. The
boolean-`disabled` slice (#95, BENCH n°14) admitted a boolean present/absent value. Both explicitly
deferred the **mixed** value (`class="box @x"`) — a value that interleaves literal text and
expressions. That value is by far the most common real-world `class` shape (`"btn @variant"`,
`"badge @statusClass rounded"`), and it is currently refused: `EmitAttribute`'s `DynamicValue`
predicate returns null when a literal part is present (`html.Count != 0`), so a mixed value falls
through to the `dynamic-attribute` refusal.

The primitive to express the composition already ships. `setAttr` writes a string; JS string
concatenation builds it. Razor already hands the value as an **ordered list of parts, each carrying a
`Prefix`** (the text/whitespace before it) — verified with `--dump-ir`:

```
class="box @statusClass"
  HtmlAttributeValueIntermediateNode  prefix=""   [HTML] "box"
  CSharpExpressionAttributeValueIntermediateNode  prefix=" "  [CS] "statusClass"
```

So the composition is a **generator-only** fold over those parts. No runtime edit, no new primitive.

## The change (generator only)

### 1. The prefix-aware fold — the composition rule

Walk the value parts in document order (`attr.Children`, all of which are html-value or cs-value nodes
by the time this runs — see §3). Maintain a literal buffer; for each part append its `Prefix` first,
then:

- **html part** (`HtmlAttributeValueIntermediateNode`): append its literal content to the buffer.
- **cs part** (`CSharpExpressionAttributeValueIntermediateNode`): flush the buffer as a JS string
  literal term (if non-empty), then emit `_code.SlotJs(csNode)` as a term.

Flush any trailing buffer at the end. Join the terms with ` + `. Worked examples (verified against the
IR):

| source | terms | emitted value expr |
|---|---|---|
| `class="@x"` (pure) | `[x.value]` | `x.value` |
| `class="box @x"` | `['box ', x.value]` | `'box ' + x.value` |
| `class="@x tail"` | `[x.value, ' tail']` | `x.value + ' tail'` |
| `class="badge @x rounded"` | `['badge ', x.value, ' rounded']` | `'badge ' + x.value + ' rounded'` |

**The pure case is the degenerate fold** (one cs part, empty prefix, no literals → the single term
`x.value`) and emits **byte-identically** to today's `class` branch. This is why the fold **replaces**
(generalises) the existing `DynamicAttributes` branch rather than adding a parallel one: one code path
for every `class` value. The existing `ReactiveAttr` gate + byte snapshot are the safety net — they
must stay green unchanged, proving the generalisation changes nothing for the pure case.

### 2. Emission — `TemplateCompiler.EmitAttribute`

Replace the current `DynamicAttributes.Contains(name) && DynamicValue(attr) is { } valueNode` branch
with:

```csharp
if (DynamicAttributes.Contains(name) && ComposableValue(attr) is { } parts)
{
    var (js, reactive) = ComposeAttributeValue(parts);   // fold (§1) + "any cs part reactive?"
    _used.Add("setAttr");
    if (reactive)
    {
        _used.Add("effect");
        _bindings.Add($"effect(() => setAttr({v}, {JsString(name)}, {js}));");
    }
    else
    {
        _create.Add($"setAttr({v}, {JsString(name)}, {js});");
    }
    return;
}
```

`reactive` is true iff **any** cs part is reactive (`parts.OfType<cs>().Any(_code.SlotIsReactive)`).
The effect lands in `_bindings` (before `_attach`), so its first `setAttr` writes into the detached
tree and makes no `MutationRecord` — the attach-last / C3 invariant is untouched. The boolean
`disabled` branch (single pure expr → present/absent ternary) is **unchanged** and still uses
`DynamicValue`.

### 3. Plumbing — one shared predicate `ComposableValue`

`SlotJs`/`SlotIsReactive` only answer for nodes harvested into `plan.FreeSlots`. The pure slice
harvested the single `DynamicValue` node; the mixed value has **several** cs parts, each needing a
slot. Add a single predicate both harvest and emission consult (decision 53 — no drift):

```csharp
// The ordered value parts of an attribute that composes to a string: every child is a literal
// (HtmlAttributeValue) or an expression (CSharpExpressionAttributeValue) part, there is at least one
// expression, and no expression part is an event handler. Null otherwise (a control-flow value node
// -- CSharpCodeAttributeValue -- makes it null and it stays refused via the unaccounted check).
static IReadOnlyList<IntermediateNode>? ComposableValue(HtmlAttributeIntermediateNode attr)
```

`CollectDynamicAttributes` is widened: for a `DynamicAttributes` name whose `ComposableValue` is
non-null, harvest **each** cs part into `FreeSlots`; the `BooleanAttributes` path still harvests the
single `DynamicValue`. The event-handler guard is preserved (a cs part whose text unwraps as an
`EventCallback` makes `ComposableValue` null — a `class` value never contains one, but the guard keeps
the invariant that naming a method cannot become a slot).

The **`unaccounted` check is untouched**: a `CSharpCodeAttributeValueIntermediateNode` (control flow in
an attribute value, `class="@if(c){…}"`) still refuses `unaccounted-attribute-value` — it is a
different deferred slice, and `ComposableValue` returns null for it (that node is neither an html- nor
a cs-value part), so it never reaches the fold.

## Scope

**In:** a mixed literal+expression value on `class` — `class="badge @statusClass rounded"`,
`class="btn @x"`, `class="@x tail"`, and the general N-part interleaving — reactive or (for symmetry)
create-time. The pure `@expr` case continues to compile, now via the same fold. Nothing here widens
the **C# expression subset**: the demo's `@code` uses only already-admitted constructs.

**Refused, deferred, loud + located (unchanged behaviour):**

- **Mixed value on any name other than `class`** — a mixed value on `title`, `value`, `disabled`, …
  stays refused. For a non-allow-listed name it is `dynamic-attribute`; for `disabled` (a boolean
  present/absent attribute) a mixed value is nonsensical and refuses too (the boolean branch uses
  `DynamicValue`, which is null for a mixed value).
- **Control flow in an attribute value** (`class="@if(c){…}"`, a `CSharpCodeAttributeValueIntermediateNode`)
  — still refused `unaccounted-attribute-value` via the untouched structural check. Distinct slice.
- **Any dynamic value on a non-allow-listed name** — unchanged; still `dynamic-attribute`, so `@bind`'s
  lowered `value=` still refuses naming `BindConverter` (`Bind` test unchanged).
- **String-typed `disabled`, other boolean names, other attribute names** — unchanged deferrals from
  #94/#95.

## Runtime

**Unchanged.** `setAttr` already exists and is exported; the composition is JS string concatenation in
generated app code. No `.ts` edit, no new export. The firewall on `src/filament-runtime` stays clean
(runtime tests byte-identical) — but this IS a measured generator widening, so the emitted bytes change
and a BENCH entry is added.

## The measured app — `MixedAttr`

The `ReactiveAttr` counter with the `class` value changed from a pure `@expr` to a mixed
literal+expression, so the measurement isolates exactly one new variable: the composition.

`baseline/MixedAttr.Blazor/App.razor` (shared DOM contract — blank lines between siblings are `"\n\n"`
text nodes):

```razor
<h1 id="title">Counter</h1>

<p id="status" class="badge @statusClass rounded">Current count: <span id="counter-value">@currentCount</span></p>

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
```

- `class="badge @statusClass rounded"` — a leading literal (`badge `), one reactive expression
  (`statusClass`), and a trailing literal (` rounded`). This exercises **both** flush branches of the
  fold: the mid-flush at the expression and the final-flush of the trailing literal. Emits
  `effect(() => setAttr(p, 'class', 'badge ' + statusClass.value + ' rounded'))`.
- `statusClass` and `currentCount` are reactive (read by the template, assigned in `Increment`).
  `Increment` writes twice → the handler is `batch(...)`.
- Behaviour: first click, class `"badge zero rounded" → "badge counting rounded"` and count `0 → 1`.

Companion files:

- `samples/MixedAttr/mixedattr.js` — the hand-written **answer key** (Blazor-faithful), transcribing
  the source exactly. Never edited to make a gate pass (decisions 21/51).
- `samples/filament-mixedattr-gen/main.js` — the generator's output host shim (App.g.js gitignored).
- `baseline/MixedAttr.Blazor/` — a normal Blazor project modelled on `ReactiveAttr.Blazor`.

## The measurement — oracle + BENCH

Correctness-only (like divide/compose/reactiveattr/boolattr): no timing, no weight.

- `bench/harness/bench.mjs` `APPS`: add
  ```js
  mixedattr: { readySelector: '#increment', observeSelector: '#status', scenarios: [] },
  ```
  and a `verifyContract` clause (`app === 'mixedattr'`): capture initial, click `#increment`, capture
  after. Assert on BOTH the Blazor baseline and the Filament build, identically:
  - initial: `#status` class `=== 'badge zero rounded'` and `#counter-value` text `=== '0'`;
  - after:   `#status` class `=== 'badge counting rounded'` and `#counter-value` text `=== '1'`.

  Asserting the **whole** class string is the measurement: it proves the literals survive around the
  reactive token in the exact order and spacing Blazor renders — a fold that dropped a literal or
  mis-ordered a prefix would show here.
- `bench/build-filament.sh`: add `filament-mixedattr-gen` case arms (mirroring
  `filament-reactiveattr-gen`).
- Publish the Blazor baseline directly to `bench/publish/blazor-mixedattr` via `dotnet publish`
  (correctness-only apps are not in `publish-baseline.sh`).
- Record **BENCH n°15**: CORRECTION only (C1/C3/C4 not claimed). `HARNESS_VERSION` bump **disclosed**
  (bench.mjs changed: new branch + `APPS` entry).

## Tests (TDD)

New `tests/Filament.Generator.Tests/MixedAttrTests.cs`, mirroring `ReactiveAttrTests`:

1. **Canon gate** — generated module is alpha-equivalent to `samples/MixedAttr/mixedattr.js`.
2. **Behaviour** — the emitted JS contains `effect(`, `setAttr(`, `'badge '`, `statusClass.value`,
   `' rounded'`, and does **not** contain `[dynamic-attribute]`.
3. **Snapshot** — byte-exact against `Snapshots/MixedAttr.approved.js`.

`RepoPaths`: add `MixedAttrRazor` + `MixedAttrAnswerKey`. `Generate`: add `MixedAttrToTemp()`.

Regression / negative controls:

- `ReactiveAttrTests` (all three) — must stay green **unchanged** (the generalised fold emits the pure
  `class="@x"` case byte-identically; this is the proof the refactor is safe).
- `BoolAttrTests` — unchanged (the boolean branch is untouched).
- `DiagnosticTests.Bind_IsRefused_AtItsExactLocation`,
  `DynamicNonClassAttribute_IsRefused_AtItsExactLocation`,
  `NonAllowedBooleanAttribute_IsRefused_AtItsExactLocation` — unchanged (mixed on a non-`class` name
  still refuses; the message no longer needs the mixed example, but still names the allowlists and
  echoes the expression).
- The `AttributeCodeValue.razor` / `unaccounted-attribute-value` control-flow test — must stay green
  **unchanged** (control flow in an attribute is still refused; `ComposableValue` returns null for it).
- Add a fixture proving a **mixed value on a non-allow-listed name** — `title="pre @caption"` — still
  refuses `dynamic-attribute` at its exact location, so composition is gated by the allowlist, not by
  the value shape.

Full suite (subset + analyzer + generator + runtime) stays green.

## Non-goals / disclosure

- Only `class`. Mixed values on other string attributes are *expressible* under the same fold but not
  *measured*.
- The general N-part composition is admitted; the measured app uses one expression with leading and
  trailing literals (exercising both flush branches). Multi-expression is the identical fold, disclosed
  but not separately measured.
- Control-flow-in-attribute, string-typed `disabled`, other names: deferred, unchanged.
- No C# subset widening; no runtime change.

## Decision record

Append **DECISIONS #96** (French, house style): the mixed-`class` widening — the `dynamic-attribute`
refusal narrowed to a prefix-aware composition fold for the `class` allowlist; the pure `@expr` case is
the degenerate fold (byte-identical, ReactiveAttr gate is the proof); one shared `ComposableValue`
predicate for harvest + emission (decision 53), `DynamicValue` retained for the boolean path; the
`unaccounted` control-flow refusal untouched (distinct slice); measured vs Blazor asserting the whole
`class` string (BENCH n°15, `HARNESS_VERSION` bump disclosed); N-part general, one-expression measured;
control-flow / string-disabled / other-name sub-slices deferred.
