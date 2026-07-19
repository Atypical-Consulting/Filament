# Reactive string attribute names (`title`/`href`/`aria-label`) — design

**Date:** 2026-07-19
**Status:** approved (design), pending spec review
**Kind:** MEASURED subset widening (BENCH entry, DECISIONS entry). NOT a DX/packaging slice.

## Goal

Widen the reactive-string-attribute allowlist beyond `class`: admit `title`, `href`, and `aria-label`
as reactive/dynamic string attributes, each compiling to the same composed `setAttr` emission `class`
already uses. Measured against Blazor for DOM-contract equivalence.

One sentence: add `title`, `href`, `aria-label` to `DynamicAttributes` (a one-line generator change,
because the harvest and emission are already name-agnostic) and measure that the string allowlist
generalizes to more names against Blazor.

## Why this is the next slice

The reactive-`class` slice (#94), the mixed-`class` slice (#96), and their machinery
(`ComposableValue` / `ComposeAttributeValue` / `CollectDynamicAttributes`) are all **name-agnostic** —
they operate on whatever `DynamicAttributes` contains (`TemplateCompiler.cs`). The set has held a single
name, `class`, since #94, with "other attribute names" disclosed as deferred at #94/#95/#96. This closes
that deferral for a representative batch.

**Blazor-validity verified up front** (the RZ9979 lesson): `dotnet build` of a probe with
`<a href="@url" title="@tip" aria-label="@label" data-testid="@tid">` **succeeds** — reactive string
attributes, including hyphenated `aria-label`/`data-*`, are valid Blazor. `--dump-ir` confirms the
generator parses each name intact as the `HtmlAttributeIntermediateNode.AttributeName` (hyphens
preserved).

## The change (generator only)

### 1. The allowlist grows by three names

```csharp
static readonly HashSet<string> DynamicAttributes =
    new(StringComparer.OrdinalIgnoreCase) { "class", "title", "href", "aria-label" };
```

That is the **entire** generator change. Each new name flows through the unchanged paths:

- **Harvest** — `CollectDynamicAttributes` already harvests any `DynamicAttributes` name whose value is
  `ComposableValue`, into `FreeSlots`.
- **Emission** — `EmitAttribute` already composes any `DynamicAttributes` name via `ComposeAttributeValue`
  (the prefix-aware fold), reactive or create-time. `setAttr(el, name, …)` takes any attribute-name
  string, so `setAttr(a, 'aria-label', label.value)` needs nothing special.
- **Refusal message** — already lists `DynamicAttributes.Order()`, so it auto-updates to name the four.

`title="@tip"` → `effect(() => setAttr(a, 'title', tip.value))`; `href="@url"` → `… 'href', url.value`;
`aria-label="@label"` → `… 'aria-label', label.value`. Mixed values (`title="pre @x"`) and create-time
(non-reactive) values compose exactly as for `class`, for free.

### 2. Why these three names

Representative coverage of the real patterns, all verified valid Blazor:

- **`title`** — a universal attribute (any element).
- **`href`** — an element-specific attribute (`<a>`).
- **`aria-label`** — a hyphenated/namespaced attribute (accessibility), proving hyphenated names flow
  through unchanged.

**Deliberately excluded:** `value` — kept out so `@bind`'s lowered `value=` stays on the
`dynamic-attribute` refusal path (`Bind` test unchanged). `data-*`, `style`, `alt`, `src`, `role`, and
every other name remain deferred, refused.

## Scope

**In:** `title`, `href`, `aria-label` carrying a reactive or create-time value — a single `@expr`, a mixed
literal+expression, exactly the value shapes `class` admits. Nothing widens the C# expression subset.

**Refused, deferred, loud + located (unchanged behaviour):**

- **Any string attribute not in the set** — `role`, `data-*`, `style`, `value`, … stay refused
  `dynamic-attribute`. (`value` keeps `@bind` refused.)
- **Boolean attributes** — `disabled` stays on the `BooleanAttributes` present/absent path; other boolean
  names stay refused (unchanged from #95).
- **Control flow in an attribute value** — still refused `unaccounted-attribute-value` (and it is invalid
  Blazor anyway, RZ9979).

## The boundary witness moves

`title` was the "still-refused non-`class` name" witness in two existing diagnostic tests. Since `title`
is now admitted, both move to **`role`** — a clean string attribute NOT in the set, so it stays refused
and keeps a genuine allowlist boundary under test:

- `Unsupported/DynamicTitle.razor` (`title="@caption"`) → **rename to `DynamicRole.razor`**,
  `role="@caption"`. `DiagnosticTests.DynamicNonClassAttribute_IsRefused_AtItsExactLocation` updates its
  `Refused("DynamicRole.razor")` target and location (column re-checked).
- `Unsupported/MixedNonAllowed.razor` (`title="pre @caption"`) → **`role="pre @caption"`** (filename kept;
  it is already generic). `MixedValueOnNonAllowedAttribute_IsRefused_AtItsExactLocation` location
  re-checked.

Both still assert `[dynamic-attribute]`, that the message names the allowlist (`class` still appears —
now alongside `title`/`href`/`aria-label`), and that the refused expression (`caption`) is echoed.

## Runtime

**Unchanged.** `setAttr` handles every attribute name; no `.ts` edit. The firewall on
`src/filament-runtime` stays clean — but this IS a measured generator widening, so emitted bytes change
and a BENCH entry is added.

## The measured app — `StringAttrs`

One `<a>` carrying all three reactive string attributes, driven by a toggle so each is measured changing.

`baseline/StringAttrs.Blazor/App.razor` (NO trailing `@using`):

```razor
@* Reactive string attribute names widening (BENCH n°16): title/href/aria-label as reactive values,
   the same composed emission as `class`. Blank line between siblings is a "\n\n" text node. *@

<a id="link" href="@url" title="@tip" aria-label="@label">Go</a>

<button id="toggle" @onclick="Toggle">Toggle</button>

@code {
    private string url = "/a";
    private string tip = "first";
    private string label = "one";

    private void Toggle()
    {
        url = "/b";
        tip = "second";
        label = "two";
    }
}
```

- `#link` emits three effects (document order: `href`, `title`, `aria-label`):
  `effect(() => setAttr(a, 'href', url.value))`, `… 'title', tip.value`, `… 'aria-label', label.value`.
- `url`/`tip`/`label` are reactive (read by the template, assigned in `Toggle`). `Toggle` writes three
  fields → the handler is `batch(...)`.
- Behaviour: initial `href="/a" title="first" aria-label="one"`; click → `href="/b" title="second"
  aria-label="two"`.

Companion files: `samples/StringAttrs/stringattrs.js` (answer key), `samples/filament-stringattrs-gen/main.js`
(host shim), `baseline/StringAttrs.Blazor/` (Blazor project modelled on `MixedAttr.Blazor`).

## The measurement — oracle + BENCH

Correctness-only (like divide/compose/reactiveattr/boolattr/mixedattr): no timing, no weight.

- `bench/harness/bench.mjs` `APPS`: add
  ```js
  stringattrs: { readySelector: '#toggle', observeSelector: '#link', scenarios: [] },
  ```
  and a `verifyContract` clause (`app === 'stringattrs'`): capture `#link`'s `href`/`title`/`aria-label`
  initial, click `#toggle`, capture after. Assert on BOTH builds, identically:
  - initial: `href === '/a'`, `title === 'first'`, `aria-label === 'one'`;
  - after:   `href === '/b'`, `title === 'second'`, `aria-label === 'two'`.

  Asserting all three (read with `getAttribute`) is the measurement: a name that did not track, or a
  hyphenated name mishandled, would show here.
- `bench/build-filament.sh`: add `filament-stringattrs-gen` case arms (mirroring `filament-mixedattr-gen`).
- Publish the Blazor baseline directly to `bench/publish/blazor-stringattrs` via `dotnet publish`.
- Record **BENCH n°16**: CORRECTION only. `HARNESS_VERSION` bump **disclosed** (`1.10.0 → 1.11.0`).

## Tests (TDD)

New `tests/Filament.Generator.Tests/StringAttrsTests.cs`, mirroring `MixedAttrTests`:

1. **Canon gate** — generated module alpha-equivalent to `samples/StringAttrs/stringattrs.js`.
2. **Behaviour** — emitted JS contains `effect(`, `setAttr(`, `'title'`, `'href'`, `'aria-label'`,
   `url.value`, `tip.value`, `label.value`; does **not** contain `[dynamic-attribute]`.
3. **Snapshot** — byte-exact against `Snapshots/StringAttrs.approved.js`.

`RepoPaths`: add `StringAttrsRazor` + `StringAttrsAnswerKey`. `Generate`: add `StringAttrsToTemp()`.

Regression / negative controls:

- `ReactiveAttrTests`, `BoolAttrTests`, `MixedAttrTests` — stay green **unchanged** (name-agnostic paths;
  `class` still `class`).
- `DiagnosticTests.Bind_IsRefused_AtItsExactLocation` — stays green **unchanged** (`value` still refused).
- `DiagnosticTests.NonAllowedBooleanAttribute_IsRefused_AtItsExactLocation` — unchanged.
- The two boundary tests move `title → role` (see §boundary), still proving a non-allow-listed dynamic
  string attribute refuses.

Full suite (subset + analyzer + generator + runtime) stays green.

## Non-goals / disclosure

- Only `title`, `href`, `aria-label`. `value` is deliberately out; `data-*`/`style`/other names deferred.
- Low-novelty: no new emission shape — the value is the measured proof that the string allowlist
  generalizes, and closing the "other names" deferral.
- No runtime change; no C# subset widening.

## Decision record

Append **DECISIONS #97** (French, house style): the reactive-string-attribute-names widening — three names
(`title`/`href`/`aria-label`) added to `DynamicAttributes`, a one-line change because the harvest/emission
are name-agnostic; each compiles to the same composed `setAttr` as `class`; `value` deliberately excluded
(keeps `@bind` refused); the boundary witness moved `title → role`; Blazor-validity verified up front (the
RZ9979 lesson); measured vs Blazor asserting all three attributes (BENCH n°16, `HARNESS_VERSION` bump
disclosed); low-novelty disclosed; `data-*`/`style`/`value`/other names deferred.
