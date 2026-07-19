# Reactive string attribute names (`title`/`href`/`aria-label`) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `title`, `href`, `aria-label` to `DynamicAttributes` so they compile to the same composed `setAttr` emission as `class`, and measure it against Blazor (BENCH n°16).

**Architecture:** A generator-only widening. The harvest (`CollectDynamicAttributes`) and emission (`ComposableValue`/`ComposeAttributeValue`) are already name-agnostic, so the generator change is a one-line allowlist edit. Because `title` was the "still-refused" witness in two diagnostic tests, the boundary witness is relocated to `role` *before* the widening, keeping every commit green.

**Tech Stack:** C# Roslyn-derived generator (`Filament.Generator`), xUnit, `canon.mjs`, byte snapshots, the Playwright DOM-contract oracle, Blazor WASM baselines.

## Global Constraints

- **Measured slice, not DX.** Generator emitted bytes DO change; a BENCH entry IS added.
- **Runtime UNCHANGED.** No edit to `src/filament-runtime`. Verify `git diff --stat -- src/filament-runtime` is EMPTY at the end.
- **Blazor-validity already verified.** `dotnet build` of a probe with `href`/`title`/`aria-label`/`data-*` reactive attributes succeeded; the generator parses each name intact. (The RZ9979 lesson — do not skip a baseline build.)
- **`value` stays out of the allowlist** — it keeps `@bind`'s lowered `value=` on the `dynamic-attribute` refusal path (`Bind` test unchanged).
- **Reuse the name-agnostic paths; touch them minimally.** `ReactiveAttrTests`, `BoolAttrTests`, `MixedAttrTests`, `Bind` MUST stay green unchanged.
- **The answer key is the reference (decisions 21/51).** `samples/StringAttrs/stringattrs.js` is never edited to make a gate pass.
- **Trunk-based, no remote.** Commit directly to `main`.
- **French house style** for DECISIONS.md / BENCH.md, mirroring #96 / n°15.
- **HARNESS_VERSION bump disclosed:** `1.10.0 → 1.11.0`.

---

## File Structure

**Reference (Task 1):** `baseline/StringAttrs.Blazor/` (Blazor project modelled on `MixedAttr.Blazor`), `samples/StringAttrs/stringattrs.js` (answer key), `samples/filament-stringattrs-gen/main.js` (host shim), `RepoPaths.cs` + `GateTests.cs` + `.gitignore`.

**Boundary move (Task 2):** rename `Unsupported/DynamicTitle.razor` → `DynamicRole.razor` (`title→role`); change `Unsupported/MixedNonAllowed.razor` (`title→role`); update the two `DiagnosticTests` methods.

**Feature (Task 3):** `src/Filament.Generator/TemplateCompiler.cs` (the one-line allowlist edit); `tests/Filament.Generator.Tests/StringAttrsTests.cs`; `Snapshots/StringAttrs.approved.js`.

**Measurement (Task 4):** `bench/harness/bench.mjs`, `bench/build-filament.sh`, `BENCH.md`.

**Record (Task 5):** `DECISIONS.md`; memory.

---

## Task 1: Reference — `StringAttrs.Blazor` app, answer key, wiring, host shim

**Files:**
- Create: `baseline/StringAttrs.Blazor/StringAttrs.Blazor.csproj`, `Program.cs`, `_Imports.razor`, `App.razor`, `wwwroot/index.html`, `wwwroot/css/app.css`
- Create: `samples/StringAttrs/stringattrs.js`, `samples/filament-stringattrs-gen/main.js`
- Modify: `tests/Filament.Generator.Tests/RepoPaths.cs`, `GateTests.cs`, `.gitignore`

**Interfaces:**
- Produces: `RepoPaths.StringAttrsRazor`, `RepoPaths.StringAttrsAnswerKey`, `Generate.StringAttrsToTemp()`.

- [ ] **Step 1: Create the Blazor project files**

`baseline/StringAttrs.Blazor/StringAttrs.Blazor.csproj`:

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

`baseline/StringAttrs.Blazor/Program.cs`:

```csharp
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using StringAttrs.Blazor;

// Same minimal host as Counter.Blazor/Divide.Blazor: no Router (one screen), no
// HeadOutlet (static title), no HttpClient (no HTTP calls).
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

await builder.Build().RunAsync();
```

`baseline/StringAttrs.Blazor/_Imports.razor`:

```razor
@* Only what the component actually uses: the Web namespace supplies @onclick. *@
@using Microsoft.AspNetCore.Components.Web
```

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

`baseline/StringAttrs.Blazor/wwwroot/index.html` (title `StringAttrs`):

```html
<!DOCTYPE html>
<html lang="en">

<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>StringAttrs</title>
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
cp baseline/MixedAttr.Blazor/wwwroot/css/app.css baseline/StringAttrs.Blazor/wwwroot/css/app.css
```

- [ ] **Step 3: Verify the Blazor app builds**

Run: `dotnet build baseline/StringAttrs.Blazor/StringAttrs.Blazor.csproj -v q`
Expected: build succeeds (0 errors). (Re-confirms Blazor-validity for this exact app — the RZ9979 lesson.)

- [ ] **Step 4: Write the answer key `samples/StringAttrs/stringattrs.js`**

```js
/**
 * StringAttrs — hand-written Filament app. Reference for the reactive-string-attribute-names widening
 * (BENCH n°16).
 *
 * ANSWER KEY (decisions 21/51): the generator's emission from baseline/StringAttrs.Blazor/App.razor is
 * snapshot- and alpha-equivalence-tested against this file. Every line is written the way a COMPILER
 * would emit it. Never edited to make a gate pass.
 *
 * The source, transcribed exactly (the blank line between the two siblings is a "\n\n" text node):
 *
 *     <a id="link" href="@url" title="@tip" aria-label="@label">Go</a>
 *
 *     <button id="toggle" @onclick="Toggle">Toggle</button>
 *
 *     @code { private string url = "/a", tip = "first", label = "one";
 *             private void Toggle() { url = "/b"; tip = "second"; label = "two"; } }
 *
 * THE POINT: title/href/aria-label are REACTIVE string attributes -- the SAME composed emission as
 * `class` (BENCH n°13/n°15), just more allow-listed names. Each is `effect(() => setAttr(a, name,
 * x.value))`. The three effects emit in document order (href, title, aria-label). setAttr already ships
 * and takes any attribute name (hyphens included); nothing new was added to the runtime.
 *
 * url/tip/label are read by the template AND assigned outside construction (in Toggle), so each lifts to
 * a Signal. Toggle writes three fields, so the handler batches: one flush, three signals.
 */

import { signal, effect, batch, setAttr, listen, insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  // -- @code: state -----------------------------------------------------------
  const url = signal('/a');
  const tip = signal('first');
  const label = signal('one');

  // -- create(): the tree, built detached -------------------------------------

  // <a id="link" href="@url" title="@tip" aria-label="@label">Go</a>  (the three attrs are bindings, below)
  const a = document.createElement('a');
  a.id = 'link';
  insert(a, document.createTextNode('Go'));

  // <button id="toggle" @onclick="Toggle">Toggle</button>
  const button = document.createElement('button');
  button.id = 'toggle';
  insert(button, document.createTextNode('Toggle'));

  // -- bindings ---------------------------------------------------------------
  // document order: href, title, aria-label.
  effect(() => setAttr(a, 'href', url.value));
  effect(() => setAttr(a, 'title', tip.value));
  effect(() => setAttr(a, 'aria-label', label.value));

  // -- events -----------------------------------------------------------------
  // Toggle writes three fields, so the handler batches: one flush, three signals.
  listen(button, 'click', () => batch(() => {
    url.value = '/b';
    tip.value = 'second';
    label.value = 'two';
  }));

  // -- attach: last, so the effects' first run made no MutationRecord ----------
  insert(target, a);
  insert(target, document.createTextNode('\n\n'));
  insert(target, button);
}
```

- [ ] **Step 5: Write the oracle host shim `samples/filament-stringattrs-gen/main.js`**

```js
/**
 * Entry point for the `filament-stringattrs-gen` label — the reactive string attribute names app.
 *
 * It mounts the JS the generator emits from baseline/StringAttrs.Blazor/App.razor (an <a> whose
 * href/title/aria-label are reactive). Like the reactiveattr/mixedattr labels it is NOT weighed or
 * timed: it exists only so the DOM-contract oracle can click #toggle and assert the three attributes
 * track state, against Blazor's own DOM.
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

In `.gitignore`, after `samples/filament-mixedattr-gen/App.g.js`, add:

```
samples/filament-stringattrs-gen/App.g.js
```

- [ ] **Step 7: Add the RepoPaths properties**

In `tests/Filament.Generator.Tests/RepoPaths.cs`, after the `MixedAttrAnswerKey` property (before `Canon`), add:

```csharp
    /// <summary>Reactive string attribute names (title/href/aria-label on an <a>) — the file Blazor compiles.</summary>
    public static string StringAttrsRazor => Path.Combine(Root, "baseline", "StringAttrs.Blazor", "App.razor");

    /// <summary>The string-attribute-names SPEC (decisions 21/51). Never edited to make a gate pass.</summary>
    public static string StringAttrsAnswerKey => Path.Combine(Root, "samples", "StringAttrs", "stringattrs.js");
```

- [ ] **Step 8: Add the Generate helper**

In `tests/Filament.Generator.Tests/GateTests.cs`, after the `MixedAttrToTemp()` line, add:

```csharp
    public static string StringAttrsToTemp() => ToTemp(RepoPaths.StringAttrsRazor, "StringAttrs");
```

- [ ] **Step 9: Commit**

```bash
git add baseline/StringAttrs.Blazor samples/StringAttrs samples/filament-stringattrs-gen \
        tests/Filament.Generator.Tests/RepoPaths.cs tests/Filament.Generator.Tests/GateTests.cs .gitignore
git commit -m "test(string-attrs): StringAttrs baseline app + answer key + test wiring"
```

---

## Task 2: Boundary move — relocate the refused-attribute witness `title → role`

Done BEFORE the widening so that when `title` becomes admitted (Task 3), the boundary tests already use
`role` (a name NOT in the allowlist) and stay green.

**Files:**
- Rename: `tests/Filament.Generator.Tests/Unsupported/DynamicTitle.razor` → `DynamicRole.razor`
- Modify: `tests/Filament.Generator.Tests/Unsupported/MixedNonAllowed.razor`
- Modify: `tests/Filament.Generator.Tests/DiagnosticTests.cs`

- [ ] **Step 1: Rename + rewrite the pure-value fixture**

Run:
```bash
git mv tests/Filament.Generator.Tests/Unsupported/DynamicTitle.razor tests/Filament.Generator.Tests/Unsupported/DynamicRole.razor
```
Then set `tests/Filament.Generator.Tests/Unsupported/DynamicRole.razor` to (attribute `title → role`):

```razor
<p id="box" role="@caption">hello</p>

@code {
    private string caption = "hi";

    private void Touch()
    {
        caption = "changed";
    }
}
```

- [ ] **Step 2: Rewrite the mixed-value fixture**

Set `tests/Filament.Generator.Tests/Unsupported/MixedNonAllowed.razor` to (attribute `title → role`):

```razor
<p id="box" role="pre @caption">hello</p>

@code {
    private string caption = "x";

    private void Touch()
    {
        caption = "y";
    }
}
```

- [ ] **Step 3: Update the two diagnostic tests**

In `tests/Filament.Generator.Tests/DiagnosticTests.cs`, in `DynamicNonClassAttribute_IsRefused_AtItsExactLocation`, change the fixture + location:

```csharp
        var d = Refused("DynamicRole.razor");

        Assert.Contains("DynamicRole.razor(1,12): FIL0003: [dynamic-attribute]", d);
        Assert.Contains("class", d);   // the message names the allowlist
        Assert.Contains("caption", d); // and echoes the refused expression
```

In `MixedValueOnNonAllowedAttribute_IsRefused_AtItsExactLocation`, change the location fixture name:

```csharp
        Assert.Contains("MixedNonAllowed.razor(1,12): FIL0003: [dynamic-attribute]", d);
```

> **LIVE-ADJUST 1 (columns):** `role` and `title` both start after `<p id="box" ` so the reported column
> should stay `(1,12)`. If either test fails on the column, run it, read the actual `…razor(1,NN)` from the
> failure, and set the assertion to the real value.

- [ ] **Step 4: Run the diagnostics (still all refused, still green)**

Run: `dotnet build src/Filament.Generator -v q && dotnet test tests/Filament.Generator.Tests --filter "FullyQualifiedName~DiagnosticTests" -v q`
Expected: all diagnostics green — `role` is still refused `[dynamic-attribute]` (not in the allowlist), exactly as `title` was.

- [ ] **Step 5: Commit**

```bash
git add tests/Filament.Generator.Tests/Unsupported/DynamicRole.razor tests/Filament.Generator.Tests/Unsupported/DynamicTitle.razor tests/Filament.Generator.Tests/Unsupported/MixedNonAllowed.razor tests/Filament.Generator.Tests/DiagnosticTests.cs
git commit -m "test(string-attrs): move the refused-attribute witness title->role (title admitted next)"
```

---

## Task 3: Feature — widen `DynamicAttributes` by three names

**Files:**
- Modify: `src/Filament.Generator/TemplateCompiler.cs` (line 174, the allowlist literal)
- Create: `tests/Filament.Generator.Tests/StringAttrsTests.cs`
- Create: `tests/Filament.Generator.Tests/Snapshots/StringAttrs.approved.js` (bootstrapped)

**Interfaces:**
- Consumes: `Generate.StringAttrsToTemp()`, `RepoPaths.StringAttrsAnswerKey`, `RepoPaths.Canon`, `Run.Node`; the existing name-agnostic `ComposableValue`/`CollectDynamicAttributes`/`ComposeAttributeValue` (unchanged).

- [ ] **Step 1: Write the failing tests `tests/Filament.Generator.Tests/StringAttrsTests.cs`**

```csharp
using Xunit;

namespace Filament.Generator.Tests;

public class StringAttrsTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the module emitted from baseline/StringAttrs.Blazor/App.razor
    /// is alpha-equivalent to the hand-written samples/StringAttrs/stringattrs.js. title/href/aria-label
    /// compile to the same composed setAttr as class; the emission is what the DOM-contract oracle
    /// measures (baseline/StringAttrs.Blazor vs filament-stringattrs-gen, BENCH n°16).
    /// </summary>
    [Fact]
    public void Gate_GeneratedStringAttrs_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.StringAttrsToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.StringAttrsAnswerKey);
        Assert.True(exit == 0,
            "string-attribute-names gate FAILED. Generated module is NOT alpha-equivalent to samples/StringAttrs/stringattrs.js.\n"
            + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>Each of title/href/aria-label is a live effect over setAttr on the lifted signal.</summary>
    [Fact]
    public void EmittedStringAttrs_BindsEachNameWithSetAttrEffect()
    {
        var js = File.ReadAllText(Generate.StringAttrsToTemp());
        Assert.Contains("effect(", js);
        Assert.Contains("setAttr(", js);
        Assert.Contains("'href'", js);
        Assert.Contains("'title'", js);
        Assert.Contains("'aria-label'", js);
        Assert.Contains("url.value", js);
        Assert.Contains("tip.value", js);
        Assert.Contains("label.value", js);
        Assert.DoesNotContain("[dynamic-attribute]", js);
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedStringAttrsJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.StringAttrsToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "StringAttrs.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
```

- [ ] **Step 2: Run the gate to verify it fails**

Run: `dotnet build src/Filament.Generator -v q && dotnet test tests/Filament.Generator.Tests --filter "FullyQualifiedName~StringAttrsTests" -v q`
Expected: FAIL — `title`/`href`/`aria-label` are not yet allow-listed, so the generator refuses them `[dynamic-attribute]` and `Generate.StringAttrsToTemp()` throws.

- [ ] **Step 3: Widen the allowlist (the one-line change)**

In `src/Filament.Generator/TemplateCompiler.cs` line 174, change:

```csharp
    static readonly HashSet<string> DynamicAttributes = new(StringComparer.OrdinalIgnoreCase) { "class" };
```

to:

```csharp
    static readonly HashSet<string> DynamicAttributes = new(StringComparer.OrdinalIgnoreCase) { "class", "title", "href", "aria-label" };
```

- [ ] **Step 4: Rebuild, run the gate + behaviour, then bootstrap the snapshot**

Run: `dotnet build src/Filament.Generator -v q && dotnet test tests/Filament.Generator.Tests --filter "FullyQualifiedName~StringAttrsTests" -v q`
Expected: `Gate_*` and `EmittedStringAttrs_*` PASS; `Snapshot_*` FAILS once with "wrote …StringAttrs.approved.js; review + re-run".

> **LIVE-ADJUST 2 (gate shape):** if `Gate_*` fails, read the canon diff. Likely culprits: the three effects' order (should be document order `href`, `title`, `aria-label`) or the batched handler shape. Confirm the generator's actual emission; if the compiler's faithful output differs from the answer key, correct `samples/StringAttrs/stringattrs.js` to match (not to force green). Re-run.

- [ ] **Step 5: Review + approve the snapshot**

Read `tests/Filament.Generator.Tests/Snapshots/StringAttrs.approved.js`. Confirm by eye: `const url = signal('/a');` etc., three bindings `effect(() => setAttr(<el>, 'href', url.value));` / `… 'title', tip.value` / `… 'aria-label', label.value`, imports `signal, effect, batch, setAttr, listen, insert`, batched handler, one `\n\n` attach node. Then re-run:

Run: `dotnet test tests/Filament.Generator.Tests --filter "FullyQualifiedName~StringAttrsTests" -v q`
Expected: all 3 PASS.

- [ ] **Step 6: Full suites + firewall (no regressions)**

Run:
```bash
dotnet test tests/Filament.Generator.Tests -v q
cd src/filament-runtime && npm test; cd -
git diff --stat -- src/filament-runtime
```
Expected: all generator tests green (ReactiveAttr/BoolAttr/MixedAttr unchanged; `Bind` still refused — `value` not allow-listed; the `role` boundary tests still refused), runtime green, **`git diff --stat -- src/filament-runtime` EMPTY**.

- [ ] **Step 7: Commit**

```bash
git add src/Filament.Generator/TemplateCompiler.cs \
        tests/Filament.Generator.Tests/StringAttrsTests.cs \
        tests/Filament.Generator.Tests/Snapshots/StringAttrs.approved.js
git commit -m "feat(string-attrs): admit title/href/aria-label as reactive string attributes"
```

---

## Task 4: Measurement — DOM-contract oracle + BENCH n°16

**Files:**
- Modify: `bench/harness/bench.mjs` (`HARNESS_VERSION`, `APPS`, `verifyContract`)
- Modify: `bench/build-filament.sh` (case arms mirroring `filament-mixedattr-gen`)
- Append: `BENCH.md`

- [ ] **Step 1: Bump HARNESS_VERSION**

In `bench/harness/bench.mjs`, change `'1.10.0'` to `'1.11.0'` and prepend the note:

```js
export const HARNESS_VERSION = '1.11.0';   // 1.11.0: 'stringattrs' contract (reactive title/href/aria-label). 1.10.0: 'mixedattr' (mixed literal+expression class value). 1.9.0: 'boolattr' (boolean disabled present/absent). 1.8.0: 'reactiveattr' (reactive class attribute). 1.7.0: 'boundcompose' (bound-parameter composition). 1.6.0: rootforeach/rootif. 1.5.0: compose. 1.4.0: divide.
```

- [ ] **Step 2: Add the APPS entry**

In `bench/harness/bench.mjs`, after the `mixedattr` APPS entry (before the closing `};`), add:

```js
  // Correctness-only: verifyContract clicks #toggle and asserts three reactive string attributes on
  // #link (href/title/aria-label) track state, against Blazor's own rendered DOM. The measurement of the
  // reactive-string-attribute-names widening (BENCH n°16).
  stringattrs: {
    readySelector: '#toggle',
    observeSelector: '#link',
    scenarios: [],
  },
```

- [ ] **Step 3: Add the verifyContract clause**

In `bench/harness/bench.mjs`, after the `mixedattr` clause (before `if (app === 'divide')`), add:

```js
    if (app === 'stringattrs') {
      return ctx.page.evaluate(() => {
        const out = { problems: [], observed: {} };
        for (const sel of ['#toggle', '#link']) {
          if (!document.querySelector(sel)) out.problems.push(`missing required element: ${sel}`);
        }
        if (out.problems.length) return out;

        const attr = (name) => document.querySelector('#link').getAttribute(name);
        const snap = () => ({ href: attr('href'), title: attr('title'), aria: attr('aria-label') });
        out.observed.initial = snap();
        // Blazor's own initial render. If it already read the post-click values the assertions below
        // would be vacuous.
        if (out.observed.initial.href !== '/a' || out.observed.initial.title !== 'first' || out.observed.initial.aria !== 'one') {
          out.problems.push(`#link initial is ${JSON.stringify(out.observed.initial)}, expected {href:"/a",title:"first",aria:"one"}`);
          return out;
        }
        document.querySelector('#toggle').click();
        out.observed.after = snap();
        // THE MEASUREMENT: three reactive string attributes (including the hyphenated aria-label) update
        // in lockstep, against Blazor's OWN rendered DOM. A stale value means the attribute did not track.
        if (out.observed.after.href !== '/b') out.problems.push(`#link href after #toggle is "${out.observed.after.href}", expected "/b"`);
        if (out.observed.after.title !== 'second') out.problems.push(`#link title after #toggle is "${out.observed.after.title}", expected "second"`);
        if (out.observed.after.aria !== 'two') out.problems.push(`#link aria-label after #toggle is "${out.observed.after.aria}", expected "two"`);
        return out;
      });
    }
```

- [ ] **Step 4: Add the build-filament.sh case arms**

In `bench/build-filament.sh`, add a `filament-stringattrs-gen` arm beside each existing `filament-mixedattr-gen` arm (eight arms): `ALL_LABELS`; `project_for` (`samples/filament-stringattrs-gen`); `mode_for` (`production` alternation); `razor_for` (`$REPO_ROOT/baseline/StringAttrs.Blazor/App.razor`); `generated_js_for` (`App.g.js`); `title_for` (`StringAttrs`); `blazor_label_for` (`blazor-stringattrs`); `css_for` (`$REPO_ROOT/baseline/StringAttrs.Blazor/wwwroot/css/app.css`).

- [ ] **Step 5: Build the Filament app + publish the Blazor baseline**

Run:
```bash
bash bench/build-filament.sh filament-stringattrs-gen
dotnet publish baseline/StringAttrs.Blazor/StringAttrs.Blazor.csproj -c Release -o bench/publish/blazor-stringattrs
```
Expected: `samples/filament-stringattrs-gen/App.g.js` emitted; the Blazor publish lands under `bench/publish/blazor-stringattrs/wwwroot`.

- [ ] **Step 6: Run the oracle against BOTH builds**

Run:
```bash
node bench/harness/bench.mjs --dir bench/publish/filament-stringattrs-gen --app stringattrs --label filament-stringattrs-gen --contract-only --headless
node bench/harness/bench.mjs --dir bench/publish/blazor-stringattrs/wwwroot --app stringattrs --label blazor-stringattrs --contract-only --headless
```
Expected: BOTH report `DOM contract OK` with `initial {href:"/a",title:"first",aria:"one"}` and after `{href:"/b",title:"second",aria:"two"}`. If either reports problems, STOP and investigate (do not adjust the answer key to hide a real divergence).

- [ ] **Step 7: Append BENCH n°16**

Append to `BENCH.md` an `Entrée n°16` (French, mirroring n°15): the reactive-string-attribute-names widening (title/href/aria-label added to the string allowlist, same composed emission as `class`); `baseline/StringAttrs.Blazor` vs `filament-stringattrs-gen`; BOTH render `#link` href/title/aria-label `/a·first·one → /b·second·two` identically; correctness-only; low-novelty (no new emission shape, disclosed); runtime UNCHANGED; `HARNESS_VERSION 1.10.0→1.11.0` disclosed. No prior figure invalidated.

- [ ] **Step 8: Commit**

```bash
git add bench/harness/bench.mjs bench/build-filament.sh BENCH.md
git commit -m "bench(string-attrs): DOM-contract oracle + BENCH n°16 (reactive title/href/aria-label)"
```

---

## Task 5: Record — DECISIONS #97 + memory, then finish

**Files:**
- Append: `DECISIONS.md`
- Update memory: `string-attribute-names-widened.md` + `MEMORY.md` index (outside the repo — not committed)

- [ ] **Step 1: Append DECISIONS #97**

Append to `DECISIONS.md` a `#97` entry (French, house style, mirroring #96): the reactive-string-attribute-names widening enters §5. Key points: three names (`title`/`href`/`aria-label`) added to `DynamicAttributes` — a **one-line change** because the harvest (`CollectDynamicAttributes`) and emission (`ComposableValue`/`ComposeAttributeValue`) are already **name-agnostic**; each compiles to the same composed `setAttr` as `class` (hyphenated `aria-label` included — `setAttr` takes any name); `value` **deliberately excluded** so `@bind` stays refused (`Bind` unchanged); the boundary witness **moved `title → role`** (a name outside the set, still refused — `DynamicRole`/`MixedNonAllowed`); **Blazor-validity verified up front** (`dotnet build` — the RZ9979 lesson from the withdrawn control-flow-in-attribute slice); measured vs Blazor asserting all three attributes (BENCH n°16, `HARNESS_VERSION` bump disclosed); **low-novelty disclosed** (no new emission shape — the value is measured coverage that the string allowlist generalizes); `data-*`/`style`/`value`/other names deferred. Test count updated (state the exact number after running the full suite).

- [ ] **Step 2: Commit DECISIONS**

```bash
git add DECISIONS.md
git commit -m "docs(string-attrs): DECISIONS #97 — reactive string attribute names (measured widening)"
```

- [ ] **Step 3: Update memory**

Create `~/.claude/projects/-Users-phmatray-Repositories-dotnet-Filament/memory/string-attribute-names-widened.md` (type: project) capturing: title/href/aria-label entered the measured subset (#97 / BENCH n°16); one-line allowlist change (harvest/emission name-agnostic); same composed `setAttr` as class; `value` excluded (keeps `@bind` refused); boundary witness moved title→role; Blazor-validity verified up front (RZ9979 lesson); low-novelty (measured coverage); runtime UNCHANGED; deferred remainder (data-*/style/value/other names). Add a one-line index pointer to `MEMORY.md`. Link `[[mixed-class-attribute-widened]]` and `[[control-flow-in-attribute-not-viable]]`. (Outside the repo — not committed.)

- [ ] **Step 4: Final verification + finish**

Run:
```bash
dotnet test Filament.sln -v q
cd src/filament-runtime && npm test; cd -
git diff --stat -- src/filament-runtime   # must be empty
git log --oneline -8
```
Expected: all .NET tests green (with the new StringAttrs gate/behaviour/snapshot + the moved `role` boundary, and ReactiveAttr/BoolAttr/MixedAttr/Bind unchanged), runtime green, runtime firewall clean.

Then invoke **superpowers:finishing-a-development-branch**. Environment is a normal repo on `main` with no remote: report the slice landed on `main`.

---

## Self-Review

**Spec coverage** (checked against `docs/superpowers/specs/2026-07-19-reactive-string-attribute-names-design.md`):
- §change 1 one-line allowlist → Task 3 Step 3. ✓
- §change 2 name set (title/href/aria-label) → Task 3 Step 3; §`value` excluded → Global Constraints + Task 3 Step 6 (`Bind`). ✓
- §boundary witness moves title→role → Task 2. ✓
- §Runtime unchanged → Global Constraints + Task 3 Step 6 (`git diff` empty). ✓
- §Measured app StringAttrs → Task 1. ✓
- §Measurement oracle + BENCH n°16 (all three attrs) → Task 4. ✓
- §Tests gate/behaviour/snapshot + ReactiveAttr/BoolAttr/MixedAttr/Bind regressions + role boundary → Task 2, Task 3 Steps 1, 6. ✓
- §Decision record #97 → Task 5. ✓

**Placeholder scan:** no TBD/TODO; every code step shows actual code; two genuine environment-reads flagged LIVE-ADJUST (boundary columns, gate shape).

**Type/name consistency:** `StringAttrsRazor`/`StringAttrsAnswerKey`/`StringAttrsToTemp()` consistent across Tasks 1–3; `filament-stringattrs-gen`/`blazor-stringattrs`/`stringattrs` labels consistent across Tasks 1, 4; the emitted `setAttr(a, 'href', url.value)` / `'title', tip.value` / `'aria-label', label.value` matches the behaviour test, the answer key, and the oracle assertions; `title → role` applied to both `DynamicRole.razor` and `MixedNonAllowed.razor` and both tests.
