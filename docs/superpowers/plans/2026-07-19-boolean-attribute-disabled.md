# Boolean attribute (present/absent `disabled`) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Admit `disabled="@expr"` on a plain element into the compiled subset as a boolean present/absent binding — `effect(() => setAttr(el, 'disabled', <expr> ? '' : null))` — and measure it against Blazor for DOM-contract equivalence (BENCH n°14).

**Architecture:** A generator-only widening mirroring the reactive-`class` slice (#94/BENCH n°13). A new name allowlist `BooleanAttributes = { disabled }` sits beside `DynamicAttributes = { class }` (the two are disjoint). `CollectDynamicAttributes` is widened to harvest values for names in *either* allowlist; `EmitAttribute` gains a second middle case that wraps the compiled expression in a `? '' : null` present/absent ternary over the existing `setAttr`. The runtime does **not** change — `setAttr`'s `v == null → removeAttribute` branch is the present/absent primitive.

**Tech Stack:** C# Roslyn source-derived generator (`Filament.Generator`), Razor IR intermediate nodes, xUnit, the `canon.mjs` alpha-equivalence gate, byte snapshots, the Playwright DOM-contract oracle (`bench/harness/bench.mjs`), Blazor WASM baselines.

## Global Constraints

- **Measured slice, not DX.** Generator emitted bytes DO change; a BENCH entry IS added. (Opposite of the DX-slice firewall.)
- **Runtime UNCHANGED.** No edit to `src/filament-runtime`. Verify `git diff --stat -- src/filament-runtime` is EMPTY at the end. The present/absent primitive is `setAttr`'s existing `v == null → removeAttribute`.
- **Never reason, always measure.** The correctness claim is settled by the oracle running BOTH the Filament build and a real Blazor build and observing identical DOM — not by argument.
- **The answer key is the reference (decisions 21/51).** `samples/BoolAttr/boolattr.js` is written the way a compiler would emit it and is NEVER edited to make a gate pass. If the gate fails, the generator is wrong (or the answer key mis-transcribes the source) — investigate, don't tweak the key to be green.
- **Single predicate, no drift (decision 53).** Harvest and emission share the one `DynamicValue` predicate. Do not add a second notion of "which attributes are dynamic."
- **Trunk-based, no remote.** Commit directly to `main` (the established convention for every prior slice #87–#94; no feature branch, no push).
- **French house style** for DECISIONS.md / BENCH.md entries, mirroring #94 / n°13.
- **HARNESS_VERSION bump disclosed** whenever `bench.mjs` changes (this slice: `1.8.0 → 1.9.0`).

---

## File Structure

**Reference (Task 1):**
- Create `baseline/BoolAttr.Blazor/` — a normal Blazor WASM project (the file Blazor compiles), modelled on `baseline/ReactiveAttr.Blazor/`.
- Create `samples/BoolAttr/boolattr.js` — the hand-written answer key.
- Create `samples/filament-boolattr-gen/main.js` — the oracle host shim (App.g.js is gitignored, re-emitted per build).
- Modify `tests/Filament.Generator.Tests/RepoPaths.cs` and `GateTests.cs` (add path properties + `Generate.BoolAttrToTemp()`).
- Modify `.gitignore` (ignore `samples/filament-boolattr-gen/App.g.js`).

**Feature (Task 2):**
- Modify `src/Filament.Generator/TemplateCompiler.cs` — `BooleanAttributes` allowlist, widen `CollectDynamicAttributes`, new `EmitAttribute` boolean branch, refusal message names both allowlists.
- Create `tests/Filament.Generator.Tests/BoolAttrTests.cs` — gate + behaviour + snapshot.
- Create `tests/Filament.Generator.Tests/Snapshots/BoolAttr.approved.js` — bootstrapped by the snapshot test.

**Boundary (Task 3):**
- Create `tests/Filament.Generator.Tests/Unsupported/BooleanNotAllowed.razor` — a non-allow-listed boolean attribute (`hidden="@isHidden"`).
- Modify `tests/Filament.Generator.Tests/DiagnosticTests.cs` — assert the refusal; confirm `DynamicNonClassAttribute` and `Bind` stay green.

**Measurement (Task 4):**
- Modify `bench/harness/bench.mjs` — `HARNESS_VERSION`, `APPS.boolattr`, `verifyContract` clause.
- Modify `bench/build-filament.sh` — `filament-boolattr-gen` case arms.
- Append `BENCH.md` — Entrée n°14.

**Record (Task 5):**
- Append `DECISIONS.md` — #95.
- Update memory (`boolean-disabled-attribute-widened.md` + MEMORY.md index).

---

## Task 1: Reference — `BoolAttr.Blazor` app, answer key, wiring, host shim

**Files:**
- Create: `baseline/BoolAttr.Blazor/BoolAttr.Blazor.csproj`, `Program.cs`, `_Imports.razor`, `App.razor`, `wwwroot/index.html`, `wwwroot/css/app.css`
- Create: `samples/BoolAttr/boolattr.js`
- Create: `samples/filament-boolattr-gen/main.js`
- Modify: `tests/Filament.Generator.Tests/RepoPaths.cs`, `tests/Filament.Generator.Tests/GateTests.cs`
- Modify: `.gitignore`

**Interfaces:**
- Produces: `RepoPaths.BoolAttrRazor` (→ `baseline/BoolAttr.Blazor/App.razor`), `RepoPaths.BoolAttrAnswerKey` (→ `samples/BoolAttr/boolattr.js`), `Generate.BoolAttrToTemp()` (→ emits from the razor to a temp path, used by Task 2's tests).

- [ ] **Step 1: Create the Blazor project files**

`baseline/BoolAttr.Blazor/BoolAttr.Blazor.csproj` (identical size-affecting PropertyGroup to ReactiveAttr.Blazor):

```xml
<Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <OverrideHtmlAssetPlaceholders>true</OverrideHtmlAssetPlaceholders>

    <!-- Identical size-affecting PropertyGroup to Counter.Blazor/Divide.Blazor by
         design, so the baselines stay comparable. InvariantGlobalization keeps the
         rendered values stable under any locale. -->
    <InvariantGlobalization>true</InvariantGlobalization>
    <PublishTrimmed>true</PublishTrimmed>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly" Version="10.0.9" />
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly.DevServer" Version="10.0.9" PrivateAssets="all" />
  </ItemGroup>

</Project>
```

`baseline/BoolAttr.Blazor/Program.cs`:

```csharp
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using BoolAttr.Blazor;

// Same minimal host as Counter.Blazor/Divide.Blazor: no Router (one screen), no
// HeadOutlet (static title), no HttpClient (no HTTP calls).
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

await builder.Build().RunAsync();
```

`baseline/BoolAttr.Blazor/_Imports.razor`:

```razor
@* Only what the component actually uses: the Web namespace supplies @onclick. *@
@using Microsoft.AspNetCore.Components.Web
```

`baseline/BoolAttr.Blazor/App.razor` (NO trailing `@using` — the generator does not read `_Imports.razor`, and a trailing `@using` refuses as FIL0003; Blazor gets the Web namespace from `_Imports.razor`):

```razor
@* Boolean-`disabled` widening (BENCH n°14). #target's `disabled` attribute is present when
   `locked` is true and absent when false -- Blazor's boolean-attribute contract. `locked`
   starts true (rendered disabled), the toggle flips it. Blank line between the buttons is a
   "\n\n" text node -- the shared DOM contract. *@

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

`baseline/BoolAttr.Blazor/wwwroot/index.html` (title `BoolAttr`):

```html
<!DOCTYPE html>
<html lang="en">

<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>BoolAttr</title>
    <base href="/" />
    <link rel="preload" id="webassembly" />
    <link rel="stylesheet" href="css/app.css" />
    <!-- Empty data: URI suppresses the browser's automatic /favicon.ico request,
         which would otherwise 404 and add noise to the benchmark network trace. -->
    <link rel="icon" href="data:," />
    <script type="importmap"></script>
</head>

<body>
    <div id="app">Loading...</div>

    <div id="blazor-error-ui">
        An unhandled error has occurred.
        <a href="." class="reload">Reload</a>
        <span class="dismiss">🗙</span>
    </div>
    <script src="_framework/blazor.webassembly#[.{fingerprint}].js"></script>
</body>

</html>
```

- [ ] **Step 2: Copy the shared css**

Run:
```bash
cp baseline/ReactiveAttr.Blazor/wwwroot/css/app.css baseline/BoolAttr.Blazor/wwwroot/css/app.css
```

- [ ] **Step 3: Verify the Blazor app builds**

Run: `dotnet build baseline/BoolAttr.Blazor/BoolAttr.Blazor.csproj -v q`
Expected: build succeeds (0 errors). This proves the razor is valid Blazor before we ever compare against it.

- [ ] **Step 4: Write the answer key `samples/BoolAttr/boolattr.js`**

```js
/**
 * BoolAttr — hand-written Filament app. Reference for the boolean-`disabled` widening (BENCH n°14).
 *
 * ANSWER KEY (decisions 21/51): the generator's emission from baseline/BoolAttr.Blazor/App.razor is
 * snapshot- and alpha-equivalence-tested against this file. Every line is written the way a COMPILER
 * would emit it. Never edited to make a gate pass.
 *
 * The source, transcribed exactly (the blank line between the two buttons is a "\n\n" text node):
 *
 *     <button id="target" disabled="@locked">Target</button>
 *
 *     <button id="toggle" @onclick="Toggle">Toggle</button>
 *
 *     @code {
 *         private bool locked = true;
 *         private void Toggle() { locked = !locked; }
 *     }
 *
 * THE POINT: `disabled="@locked"` is a BOOLEAN attribute. `locked` is read by the template (the
 * disabled attribute) AND assigned outside construction (in Toggle), so it lifts to a Signal and the
 * disabled binding is `effect(() => setAttr(target, 'disabled', locked.value ? '' : null))` — the SAME
 * reactive rule as a string attribute (BENCH n°13), with the value mapped through a present/absent
 * ternary: true -> '' -> setAttribute (present, <button disabled="">); false -> null -> removeAttribute
 * (absent). setAttr's null->remove branch already ships; nothing new was added to the runtime.
 *
 * `locked` starts TRUE, so the binding's first run (against the DETACHED tree, before attach) writes
 * setAttribute -> #target disabled present, making no MutationRecord. The click flips locked to false
 * -> the effect re-runs -> removeAttribute -> #target disabled absent (the path a string attribute
 * never takes). Toggle writes once, so the handler is a plain assignment (no batch).
 */

import { signal, effect, setAttr, listen, insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  // -- @code: state -----------------------------------------------------------
  const locked = signal(true);

  // -- create(): the tree, built detached -------------------------------------

  // <button id="target" disabled="@locked">Target</button>  (disabled is a binding, below)
  const targetButton = document.createElement('button');
  targetButton.id = 'target';
  insert(targetButton, document.createTextNode('Target'));

  // <button id="toggle" @onclick="Toggle">Toggle</button>
  const toggleButton = document.createElement('button');
  toggleButton.id = 'toggle';
  insert(toggleButton, document.createTextNode('Toggle'));

  // -- bindings ---------------------------------------------------------------
  // disabled: boolean present/absent via setAttr's null->remove. true -> '' (present), false -> null (absent).
  effect(() => setAttr(targetButton, 'disabled', locked.value ? '' : null));

  // -- events -----------------------------------------------------------------
  // Toggle writes once (locked), so the handler is a plain assignment -- no batch.
  listen(toggleButton, 'click', () => locked.value = !locked.value);

  // -- attach: last, so the effect's first run made no MutationRecord ----------
  insert(target, targetButton);
  insert(target, document.createTextNode('\n\n'));
  insert(target, toggleButton);
}
```

- [ ] **Step 5: Write the oracle host shim `samples/filament-boolattr-gen/main.js`**

```js
/**
 * Entry point for the `filament-boolattr-gen` label — the boolean-`disabled` app.
 *
 * It mounts the JS the generator emits from baseline/BoolAttr.Blazor/App.razor (two buttons; #target
 * carries disabled="@locked"). Like the compose/divide/boundcompose/reactiveattr labels it is NOT
 * weighed or timed: it exists only so the DOM-contract oracle can click #toggle and assert the boolean
 * `disabled` attribute goes present->absent in lockstep with Blazor's own DOM.
 *
 * App.g.js IS NOT COMMITTED AND IS NOT EDITABLE: build-filament.sh deletes and re-emits it from
 * App.razor on every build, so the app the oracle loads is always this run's generator output.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
```

- [ ] **Step 6: Ignore the generated App.g.js**

In `.gitignore`, beside the existing `samples/filament-reactiveattr-gen/App.g.js` line (line ~41), add:

```
samples/filament-boolattr-gen/App.g.js
```

- [ ] **Step 7: Add the RepoPaths properties**

In `tests/Filament.Generator.Tests/RepoPaths.cs`, after the `ReactiveAttrAnswerKey` property (before `Canon`), add:

```csharp
    /// <summary>Boolean `disabled` attribute (a toggle whose #target disabled tracks state) — the file Blazor compiles.</summary>
    public static string BoolAttrRazor => Path.Combine(Root, "baseline", "BoolAttr.Blazor", "App.razor");

    /// <summary>The boolean-attribute SPEC (decisions 21/51). Never edited to make a gate pass.</summary>
    public static string BoolAttrAnswerKey => Path.Combine(Root, "samples", "BoolAttr", "boolattr.js");
```

- [ ] **Step 8: Add the Generate helper**

In `tests/Filament.Generator.Tests/GateTests.cs`, after the `ReactiveAttrToTemp()` line (~273), add:

```csharp
    public static string BoolAttrToTemp() => ToTemp(RepoPaths.BoolAttrRazor, "BoolAttr");
```

- [ ] **Step 9: Commit**

```bash
git add baseline/BoolAttr.Blazor samples/BoolAttr samples/filament-boolattr-gen \
        tests/Filament.Generator.Tests/RepoPaths.cs tests/Filament.Generator.Tests/GateTests.cs .gitignore
git commit -m "test(bool-attr): BoolAttr baseline app + answer key + test wiring"
```

---

## Task 2: Feature — compile boolean `disabled` via the present/absent ternary

**Files:**
- Modify: `src/Filament.Generator/TemplateCompiler.cs` (allowlist ~174, `CollectDynamicAttributes` ~508, `EmitAttribute` ~1257 + refusal ~1273)
- Create: `tests/Filament.Generator.Tests/BoolAttrTests.cs`
- Create: `tests/Filament.Generator.Tests/Snapshots/BoolAttr.approved.js` (bootstrapped by Step 6)

**Interfaces:**
- Consumes: `Generate.BoolAttrToTemp()`, `RepoPaths.BoolAttrAnswerKey`, `RepoPaths.Canon`, `Run.Node` (all from Task 1 + existing infra).
- Produces: the emission `effect(() => setAttr(<el>, 'disabled', <expr>.value ? '' : null));` for a reactive `disabled`, `setAttr(<el>, 'disabled', <expr> ? '' : null);` for a non-reactive one.

- [ ] **Step 1: Write the failing tests `tests/Filament.Generator.Tests/BoolAttrTests.cs`**

```csharp
using Xunit;

namespace Filament.Generator.Tests;

public class BoolAttrTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the module emitted from baseline/BoolAttr.Blazor/App.razor is
    /// alpha-equivalent to the hand-written samples/BoolAttr/boolattr.js. The spec is the reference; the
    /// generator is judged. boolattr.js's Blazor-faithfulness is what the DOM-contract oracle measures
    /// (baseline/BoolAttr.Blazor vs filament-boolattr-gen, BENCH n°14).
    /// </summary>
    [Fact]
    public void Gate_GeneratedBoolAttr_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.BoolAttrToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.BoolAttrAnswerKey);
        Assert.True(exit == 0,
            "boolean-attribute gate FAILED. Generated module is NOT alpha-equivalent to samples/BoolAttr/boolattr.js.\n"
            + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>The `disabled` attribute is a live effect that maps the bool to present/absent via
    /// setAttr's null->remove -- never the naive `setAttr(el,'disabled',true)` that yields disabled="true".</summary>
    [Fact]
    public void EmittedBoolAttr_BindsDisabledPresentAbsent()
    {
        var js = File.ReadAllText(Generate.BoolAttrToTemp());
        Assert.Contains("effect(", js);
        Assert.Contains("setAttr(", js);
        Assert.Contains("'disabled'", js);
        Assert.Contains("? '' : null", js);          // the present/absent ternary
        Assert.DoesNotContain("[dynamic-attribute]", js);
        Assert.DoesNotContain("'disabled', true", js); // the naive-boolean trap
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedBoolAttrJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.BoolAttrToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "BoolAttr.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
```

- [ ] **Step 2: Run the gate to verify it fails**

Run: `dotnet build src/Filament.Generator -v q && dotnet test tests/Filament.Generator.Tests --filter "FullyQualifiedName~BoolAttrTests" -v q`
Expected: FAIL — the generator currently refuses `disabled="@locked"` with FIL0003 `[dynamic-attribute]`, so `Generate.BoolAttrToTemp()` throws "the generator refused to emit" (`disabled` is in no allowlist).

- [ ] **Step 3: Add the boolean-attribute allowlist**

In `src/Filament.Generator/TemplateCompiler.cs`, immediately after the `DynamicAttributes` declaration (~line 174), add:

```csharp
    /// <summary>
    /// Attribute names whose value MAY be a compiled BOOLEAN expression, rendered present/absent (a second
    /// allowlist beside DynamicAttributes, disjoint from it). `disabled` is the MEASURED one (BENCH n°14):
    /// `disabled="@b"` compiles to `effect(() => setAttr(el, 'disabled', b.value ? '' : null))` -- the
    /// present/absent contract via setAttr's own null->remove branch (true -> '' -> setAttribute, false ->
    /// null -> removeAttribute), NOT the naive `setAttr(el,'disabled',true)` that yields disabled="true".
    /// Name-based because the generator does no type inference: `disabled` is COMMITTED to boolean
    /// present/absent (a string-typed `disabled` is a deferred, distinct case). Widening this set is a NEW
    /// measured slice each time.
    /// </summary>
    static readonly HashSet<string> BooleanAttributes = new(StringComparer.OrdinalIgnoreCase) { "disabled" };
```

- [ ] **Step 4: Widen the harvest to the boolean allowlist**

In `CollectDynamicAttributes` (~line 512), change the name guard so an allow-listed name in EITHER set is harvested:

```csharp
                if ((DynamicAttributes.Contains(attr.AttributeName) || BooleanAttributes.Contains(attr.AttributeName))
                    && DynamicValue(attr) is { } expr)
                    plan.FreeSlots.Add(expr);
```

- [ ] **Step 5: Add the boolean emission branch + update the refusal message**

In `EmitAttribute`, immediately AFTER the existing `class`/`DynamicAttributes` branch's closing `}` (~line 1271) and BEFORE the `Diag("dynamic-attribute", ...)` call, insert:

```csharp
            // BOOLEAN / PRESENT-ABSENT ATTRIBUTE VALUE (the `disabled` slice, BENCH n°14). Same shape as
            // the `class` branch above (disjoint allowlist), but the compiled expression is wrapped in a
            // present/absent ternary: true -> '' -> setAttribute (present, <button disabled="">), false ->
            // null -> removeAttribute (absent, setAttr's own null->remove). Not the naive setAttr of the
            // bool, which would render disabled="true". The effect lands in _bindings (before attach), so
            // its first write goes into the detached tree and makes no MutationRecord.
            if (BooleanAttributes.Contains(name) && DynamicValue(attr) is { } boolNode)
            {
                var js = _code.SlotJs(boolNode);
                _used.Add("setAttr");
                if (_code.SlotIsReactive(boolNode))
                {
                    _used.Add("effect");
                    _bindings.Add($"effect(() => setAttr({v}, {JsString(name)}, {js} ? '' : null));");
                }
                else
                {
                    _create.Add($"setAttr({v}, {JsString(name)}, {js} ? '' : null);");
                }
                return;
            }
```

Then update the `Diag("dynamic-attribute", ...)` message (~line 1273) to name BOTH allowlists. Replace the message argument with:

```csharp
                $"attribute '{name}' on <{el.TagName}> carries the C# expression \"{Trunc(expr)}\". This " +
                "compiler compiles a dynamic value only for ALLOW-LISTED attributes (reactive string: " +
                $"{string.Join(", ", DynamicAttributes.Order())}; boolean present/absent: " +
                $"{string.Join(", ", BooleanAttributes.Order())}); '{name}' is not one of them, and this is " +
                "neither a resolved event handler nor a static value. A dynamic value on an un-measured " +
                "attribute -- or a mixed literal+expression value (class=\"box @x\") -- has no measurement " +
                "covering it. Refusing to emit.",
```

- [ ] **Step 6: Rebuild, run the gate + behaviour, then bootstrap the snapshot**

Run: `dotnet build src/Filament.Generator -v q && dotnet test tests/Filament.Generator.Tests --filter "FullyQualifiedName~BoolAttrTests" -v q`
Expected: `Gate_*` and `EmittedBoolAttr_*` PASS; `Snapshot_*` FAILS once with "wrote …BoolAttr.approved.js; review + re-run" (first-run bootstrap).

> **LIVE-ADJUST 1 (gate shape):** if `Gate_*` fails on structure (not on the generator refusing), read the canon diff. The likely culprits are the single-write handler form (`() => locked.value = !locked.value` vs a block body) or the two-button variable naming. The answer key is the reference for *intent*, but if the generator's faithful emission differs structurally, correct `samples/BoolAttr/boolattr.js` to match the generator's actual shape — NOT to force green, but because the compiler's real output is the truth the oracle will run. Re-run.

- [ ] **Step 7: Review + approve the snapshot**

Read `tests/Filament.Generator.Tests/Snapshots/BoolAttr.approved.js`. Confirm by eye: `const locked = signal(true);`, the `disabled` binding is `effect(() => setAttr(<el>, 'disabled', <expr>.value ? '' : null));`, imports are `signal, effect, setAttr, listen, insert` (no `batch`, no `setText`, no `computed`), two `\n\n`-free … i.e. exactly one `\n\n` attach node between the buttons. Then re-run:

Run: `dotnet test tests/Filament.Generator.Tests --filter "FullyQualifiedName~BoolAttrTests" -v q`
Expected: all 3 PASS.

- [ ] **Step 8: Run the full generator + runtime suites (no regressions)**

Run:
```bash
dotnet test tests/Filament.Generator.Tests -v q
cd src/filament-runtime && npm test; cd -
git diff --stat -- src/filament-runtime
```
Expected: all generator tests green (including the existing `Bind` and `DynamicNonClassAttribute` diagnostics — the refusal message still contains "class" and "caption"); runtime tests green; **`git diff --stat -- src/filament-runtime` is EMPTY** (firewall clean).

- [ ] **Step 9: Commit**

```bash
git add src/Filament.Generator/TemplateCompiler.cs \
        tests/Filament.Generator.Tests/BoolAttrTests.cs \
        tests/Filament.Generator.Tests/Snapshots/BoolAttr.approved.js
git commit -m "feat(bool-attr): compile boolean disabled=\"@expr\" present/absent via setAttr (allowlisted)"
```

---

## Task 3: Boundary — a non-allow-listed boolean attribute stays refused

**Files:**
- Create: `tests/Filament.Generator.Tests/Unsupported/BooleanNotAllowed.razor`
- Modify: `tests/Filament.Generator.Tests/DiagnosticTests.cs`

**Interfaces:**
- Consumes: the existing `Refused("<fixture>.razor")` helper (drives the generator on `Unsupported/<fixture>.razor` and returns the diagnostic text).

- [ ] **Step 1: Create the fixture `tests/Filament.Generator.Tests/Unsupported/BooleanNotAllowed.razor`**

A boolean attribute (`hidden`) that is NOT in `BooleanAttributes`, so it must stay on the refusal path (NO trailing `@using`):

```razor
<p id="box" hidden="@isHidden">hello</p>

@code {
    private bool isHidden = true;

    private void Touch()
    {
        isHidden = !isHidden;
    }
}
```

- [ ] **Step 2: Write the failing boundary test**

In `tests/Filament.Generator.Tests/DiagnosticTests.cs`, after `DynamicNonClassAttribute_IsRefused_AtItsExactLocation` (~line 155), add:

```csharp
    /// <summary>
    /// The boolean allowlist is a MEASURED boundary, not folklore: a boolean attribute that is NOT
    /// `disabled` (here `hidden`) still refuses `[dynamic-attribute]` at its exact location, and the
    /// message now names BOTH allowlists (reactive string: class; boolean present/absent: disabled).
    /// </summary>
    [Fact]
    public void NonAllowedBooleanAttribute_IsRefused_AtItsExactLocation()
    {
        var d = Refused("BooleanNotAllowed.razor");

        Assert.Contains("BooleanNotAllowed.razor(1,13): FIL0003: [dynamic-attribute]", d);
        Assert.Contains("disabled", d);  // the message names the boolean allowlist
        Assert.Contains("class", d);     // and still names the string allowlist
        Assert.Contains("isHidden", d);  // and echoes the refused expression
    }
```

> **LIVE-ADJUST 2 (column):** the `(1,13)` column is the predicted start of `hidden` in `<p id="box" hidden=…`. Razor may report a slightly different column (the reactive-`class` slice's `title` boundary landed at `(1,12)`, one left of the letter). Run the test, read the ACTUAL `BooleanNotAllowed.razor(1,NN)` from the failure message, and set the assertion to the real value.

- [ ] **Step 3: Run the boundary + regression diagnostics**

Run: `dotnet test tests/Filament.Generator.Tests --filter "FullyQualifiedName~DiagnosticTests" -v q`
Expected: `NonAllowedBooleanAttribute_*` PASS (after any column adjust); `DynamicNonClassAttribute_*` and `Bind_IsRefused_AtItsExactLocation` PASS unchanged (the new message still contains "class" and echoes the expression, so `@bind` still refuses `[dynamic-attribute]` naming `BindConverter`).

- [ ] **Step 4: Commit**

```bash
git add tests/Filament.Generator.Tests/Unsupported/BooleanNotAllowed.razor tests/Filament.Generator.Tests/DiagnosticTests.cs
git commit -m "test(bool-attr): non-allowlisted boolean attribute stays refused (boolean-allowlist boundary)"
```

---

## Task 4: Measurement — DOM-contract oracle + BENCH n°14

**Files:**
- Modify: `bench/harness/bench.mjs` (`HARNESS_VERSION` ~72, `APPS` ~345, `verifyContract` ~1570)
- Modify: `bench/build-filament.sh` (case arms mirroring `filament-reactiveattr-gen`)
- Append: `BENCH.md`

**Interfaces:**
- Consumes: the generator build (Task 2), `baseline/BoolAttr.Blazor` (Task 1), `samples/filament-boolattr-gen/main.js` (Task 1).

- [ ] **Step 1: Bump HARNESS_VERSION**

In `bench/harness/bench.mjs` line 72, change `'1.8.0'` to `'1.9.0'` and prepend the note:

```js
export const HARNESS_VERSION = '1.9.0';   // 1.9.0: 'boolattr' contract (boolean disabled present/absent). 1.8.0: 'reactiveattr' (reactive class attribute). 1.7.0: 'boundcompose' (bound-parameter composition). 1.6.0: rootforeach/rootif. 1.5.0: compose. 1.4.0: divide.
```

- [ ] **Step 2: Add the APPS entry**

In `bench/harness/bench.mjs`, after the `reactiveattr` APPS entry (~line 345, before the closing `};`), add:

```js
  // Correctness-only: verifyContract clicks #toggle and asserts a BOOLEAN `disabled` attribute on
  // #target goes present->absent (hasAttribute AND the .disabled IDL property), against Blazor's own
  // rendered DOM. The measurement of the boolean-`disabled` widening (BENCH n°14).
  boolattr: {
    readySelector: '#toggle',
    observeSelector: '#target',
    scenarios: [],
  },
```

- [ ] **Step 3: Add the verifyContract clause**

In `bench/harness/bench.mjs`, after the `reactiveattr` clause's closing (the `}` at ~line 1570, before `if (app === 'divide')`), add:

```js
    if (app === 'boolattr') {
      return ctx.page.evaluate(() => {
        const out = { problems: [], observed: {} };
        for (const sel of ['#toggle', '#target']) {
          if (!document.querySelector(sel)) out.problems.push(`missing required element: ${sel}`);
        }
        if (out.problems.length) return out;

        const target = () => document.querySelector('#target');
        out.observed.initialHasAttr = target().hasAttribute('disabled');
        out.observed.initialProp = target().disabled;
        // Blazor's own initial render: locked=true -> #target disabled present. If it already read the
        // post-click (absent) state the assertions below would be vacuous.
        if (out.observed.initialHasAttr !== true || out.observed.initialProp !== true) {
          out.problems.push(`#target initial disabled is {hasAttr:${out.observed.initialHasAttr}, prop:${out.observed.initialProp}}, expected both true`);
          return out;
        }
        document.querySelector('#toggle').click();
        out.observed.afterHasAttr = target().hasAttribute('disabled');
        out.observed.afterProp = target().disabled;
        // THE MEASUREMENT: a boolean binding REMOVES the attribute when false (removeAttribute) -- the
        // path a string attribute never takes -- against Blazor's OWN rendered DOM. A still-present
        // attribute here means the false value was written as a string (disabled="false"/"") instead of
        // removing it.
        if (out.observed.afterHasAttr !== false) {
          out.problems.push(`#target disabled after #toggle still present (hasAttribute), expected absent`);
        }
        if (out.observed.afterProp !== false) {
          out.problems.push(`#target .disabled after #toggle is ${out.observed.afterProp}, expected false`);
        }
        return out;
      });
    }
```

- [ ] **Step 4: Add the build-filament.sh case arms**

In `bench/build-filament.sh`, add a `filament-boolattr-gen` arm beside each existing `filament-reactiveattr-gen` arm:

1. `ALL_LABELS` (~line 181): add `filament-boolattr-gen` to the list.
2. `project_for` (~line 199): `filament-boolattr-gen) echo "samples/filament-boolattr-gen" ;;`
3. `mode_for` (~line 209): add `filament-boolattr-gen` to the `production` alternation.
4. `razor_for` (~line 236): `filament-boolattr-gen) echo "$REPO_ROOT/baseline/BoolAttr.Blazor/App.razor" ;;`
5. `generated_js_for` (~line 256): `filament-boolattr-gen) echo "App.g.js" ;;`
6. `title_for` (~line 272): `filament-boolattr-gen) echo "BoolAttr" ;;`
7. `blazor_label_for` (~line 291): `filament-boolattr-gen) echo "blazor-boolattr" ;;`
8. `css_for` (~line 318): `filament-boolattr-gen) echo "$REPO_ROOT/baseline/BoolAttr.Blazor/wwwroot/css/app.css" ;;`

- [ ] **Step 5: Build the Filament app + publish the Blazor baseline**

Run:
```bash
bash bench/build-filament.sh filament-boolattr-gen
dotnet publish baseline/BoolAttr.Blazor/BoolAttr.Blazor.csproj -c Release -o bench/publish/blazor-boolattr
```
Expected: `samples/filament-boolattr-gen/App.g.js` emitted; the Blazor publish lands under `bench/publish/blazor-boolattr` (correctness-only apps are not in `publish-baseline.sh` — the divide/compose/boundcompose/reactiveattr precedent).

- [ ] **Step 6: Run the oracle against BOTH builds**

Run (adjust the exact invocation to match how `filament-reactiveattr-gen` was driven — see the reactive-attr slice; both need `--label`):
```bash
node bench/harness/bench.mjs --app boolattr --label filament-boolattr-gen --contract-only
node bench/harness/bench.mjs --app boolattr --label blazor-boolattr --contract-only
```
Expected: BOTH report the contract satisfied — initial `{hasAttr:true, prop:true}`, after `{hasAttr:false, prop:false}` — with an empty `problems` array. If either reports problems, STOP and investigate (do not adjust the answer key to hide a real divergence).

> **LIVE-ADJUST 3 (oracle invocation):** the exact `bench.mjs` flags/URL wiring for a `-gen` label may differ from the sketch above. Mirror precisely how the reactive-attr slice invoked the oracle for `filament-reactiveattr-gen` / `blazor-reactiveattr`; if a static server or publish path is required, follow that precedent.

- [ ] **Step 7: Append BENCH n°14**

Append to `BENCH.md` an `Entrée n°14` (French, mirroring n°13): the boolean-`disabled` widening; `baseline/BoolAttr.Blazor` vs `filament-boolattr-gen`; BOTH render `#target` disabled present→absent identically (`hasAttribute` AND `.disabled`); correctness-only (C1/C3/C4 not claimed); `HARNESS_VERSION 1.8.0→1.9.0` disclosed (new branch + APPS entry); runtime UNCHANGED (the emission reuses `setAttr`'s null→remove). No prior weight/speed figure invalidated.

- [ ] **Step 8: Commit**

```bash
git add bench/harness/bench.mjs bench/build-filament.sh BENCH.md
git commit -m "bench(bool-attr): DOM-contract oracle + BENCH n°14 (boolean disabled present/absent)"
```

---

## Task 5: Record — DECISIONS #95 + memory, then finish

**Files:**
- Append: `DECISIONS.md`
- Update memory: `boolean-disabled-attribute-widened.md` + `MEMORY.md` index (outside the repo — not committed)

- [ ] **Step 1: Append DECISIONS #95**

Append to `DECISIONS.md` a `#95` entry (French, house style, mirroring #94): the boolean-`disabled` widening enters §5. Key points: the `dynamic-attribute` refusal narrowed to a present/absent emission for a second, disjoint `BooleanAttributes = { disabled }` allowlist; runtime UNCHANGED (`setAttr`'s `v == null → removeAttribute` is the primitive, a `? '' : null` ternary maps the bool — NOT the naive `setAttr` of the bool, which would render `disabled="true"`); name-based because there is no type inference (so `disabled` is committed to present/absent and a string-typed `disabled` stays deferred); `CollectDynamicAttributes` widened to harvest for either allowlist, the single `DynamicValue` predicate unchanged (decision 53); measured vs Blazor via the oracle asserting both `hasAttribute` and the `.disabled` IDL property (BENCH n°14, `HARNESS_VERSION` bump disclosed); the mount exercises both primitives in one scenario (setAttribute at mount with `locked=true`, removeAttribute at the click); test count updated; other-boolean-name / string-`disabled` / mixed sub-slices deferred.

- [ ] **Step 2: Commit DECISIONS**

```bash
git add DECISIONS.md
git commit -m "docs(bool-attr): DECISIONS #95 — boolean disabled attribute (measured widening)"
```

- [ ] **Step 3: Update memory**

Create `~/.claude/projects/-Users-phmatray-Repositories-dotnet-Filament/memory/boolean-disabled-attribute-widened.md` (type: project) capturing: boolean `disabled` entered the measured subset (#95 / BENCH n°14); generator-only, present/absent via `setAttr` null→remove ternary; second disjoint `BooleanAttributes` allowlist beside `class`, name-based (no type inference); measured vs Blazor on both `hasAttribute` and `.disabled`; runtime UNCHANGED; the deferred remainder (other boolean names, string-`disabled`, mixed literal+expr). Add a one-line index pointer to `MEMORY.md`. Link `[[reactive-class-attribute-widened]]`. (These are outside the repo — not committed.)

- [ ] **Step 4: Final verification + finish**

Run:
```bash
dotnet test tests/Filament.Generator.Tests -v q
cd src/filament-runtime && npm test; cd -
git diff --stat -- src/filament-runtime   # must be empty
git log --oneline -6
```
Expected: all .NET generator tests green (with the new BoolAttr gate/behaviour/snapshot + the boundary test), runtime green, runtime firewall clean.

Then invoke **superpowers:finishing-a-development-branch**. Environment is a normal repo on `main` with no remote (the trunk-based convention): report the slice landed on `main` — the merge/PR options do not apply.

---

## Self-Review

**Spec coverage** (checked against `docs/superpowers/specs/2026-07-19-boolean-attribute-disabled-design.md`):
- §"The change" emission + ternary → Task 2 Steps 3–5. ✓
- §"harvest" widening → Task 2 Step 4. ✓
- §"Boolean-attribute-name allowlist" (disjoint) → Task 2 Step 3. ✓
- §"Scope" name-based boundary / refusal message names both allowlists → Task 2 Step 5. ✓
- §"Runtime unchanged" → Global Constraints + Task 2 Step 8 + Task 5 Step 4 (`git diff` empty). ✓
- §"Measured app BoolAttr" → Task 1 (app + answer key). ✓
- §"Measurement oracle + BENCH n°14" (both hasAttribute + .disabled) → Task 4. ✓
- §"Tests" gate/behaviour/snapshot + boundary + `Bind`/`DynamicTitle` regressions → Task 2 Step 1, Task 3. ✓
- §"Decision record #95" → Task 5. ✓

**Placeholder scan:** no TBD/TODO; every code step shows the actual code; three ambiguities are explicitly flagged as LIVE-ADJUST (gate shape, boundary column, oracle invocation) with the exact procedure to resolve each — these are genuine environment reads, not deferred work.

**Type/name consistency:** `BoolAttrRazor` / `BoolAttrAnswerKey` / `BoolAttrToTemp()` used consistently across Tasks 1–2; `BooleanAttributes` (allowlist), `boolNode` (local), `filament-boolattr-gen` (label), `blazor-boolattr` (publish), `boolattr` (APPS/oracle app) consistent across Tasks 2–5. Emission string `{js} ? '' : null` matches the behaviour test's `Assert.Contains("? '' : null")` and the answer key's `locked.value ? '' : null`.
