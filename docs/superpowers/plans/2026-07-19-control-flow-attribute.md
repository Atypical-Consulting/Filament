# Control-flow-in-attribute: narrow `@if`/`@else` → ternary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Admit a narrow `@if`/`@else` code block as the sole value of `class` (`class="@if (on) { <text>active</text> } else { <text>idle</text> }"`) by rewriting it into the equivalent ternary expression node, and measure it against Blazor (BENCH n°16).

**Architecture:** A generator-only, source-to-source rewrite. A recognizer `IfBlockTernary` matches the narrow shape and returns the equivalent C# ternary source; a pre-pass `RewriteAttributeIfBlocks` (run before harvest, gated to `class` and the sole-value case) replaces the `CSharpCodeAttributeValueIntermediateNode` with a synthesized `CSharpExpressionAttributeValueIntermediateNode` carrying `cond ? "A" : "B"`. Everything downstream (harvest, `SlotJs`, `ComposeAttributeValue`, reactivity) is unchanged — the emission is byte-identical to a hand-written `@(…)` ternary.

**Tech Stack:** C# Roslyn-derived generator (`Filament.Generator`), Razor IR intermediate nodes (`CSharpCodeAttributeValueIntermediateNode`, `CSharpExpressionAttributeValueIntermediateNode`, `IntermediateToken`, `HtmlContentIntermediateNode`), xUnit, `canon.mjs`, byte snapshots, the Playwright DOM-contract oracle, Blazor WASM baselines.

## Global Constraints

- **Measured slice, not DX.** Generator emitted bytes DO change; a BENCH entry IS added.
- **Runtime UNCHANGED.** No edit to `src/filament-runtime`. Verify `git diff --stat -- src/filament-runtime` is EMPTY at the end.
- **The IR-construction API is verified.** A probe confirmed `new CSharpExpressionAttributeValueIntermediateNode { Source = code.Source }`, `synth.Children.Add(new IntermediateToken { Kind = TokenKind.CSharp, Content = ternary, Source = code.Source })`, and `attr.Children.RemoveAt`/`Insert` all compile. Use exactly that surface.
- **Reuse the composable path; touch it minimally.** The rewrite produces a normal expression node; do NOT special-case the fold. `ReactiveAttrTests`, `BoolAttrTests`, `MixedAttrTests` MUST stay green unchanged.
- **The `unaccounted-attribute-value` refusal stays intact** for every non-narrow shape (mixed siblings, non-literal branch, else-if/foreach/switch, non-`class` name). `AttributeCodeValue.razor` MUST stay refused unchanged.
- **Never reason, always measure.** The no-`else` false rendering (`class=""` vs absent) is decided by the Blazor oracle run, not argued.
- **The answer key is the reference (decisions 21/51).** `samples/IfAttr/ifattr.js` is never edited to make a gate pass.
- **Trunk-based, no remote.** Commit directly to `main`.
- **French house style** for DECISIONS.md / BENCH.md, mirroring #96 / n°15.
- **HARNESS_VERSION bump disclosed:** `1.10.0 → 1.11.0`.

---

## File Structure

**Reference (Task 1):** `baseline/IfAttr.Blazor/` (Blazor project modelled on `MixedAttr.Blazor`), `samples/IfAttr/ifattr.js` (answer key), `samples/filament-ifattr-gen/main.js` (host shim), `RepoPaths.cs` + `GateTests.cs` + `.gitignore`.

**Feature (Task 2):** `src/Filament.Generator/TemplateCompiler.cs` (`IfBlockTernary` + `BranchLiteral` + `CSharpString` recognizer, `RewriteAttributeIfBlocks` pre-pass, wired into `PrepareComponent`); `tests/Filament.Generator.Tests/IfAttrTests.cs`; `Snapshots/IfAttr.approved.js`.

**Boundary (Task 3):** `tests/Filament.Generator.Tests/Unsupported/IfNonClass.razor`; `DiagnosticTests.cs`.

**Measurement (Task 4):** `bench/harness/bench.mjs`, `bench/build-filament.sh`, `BENCH.md`.

**Record (Task 5):** `DECISIONS.md`; memory.

---

## Task 1: Reference — `IfAttr.Blazor` app, answer key, wiring, host shim

**Files:**
- Create: `baseline/IfAttr.Blazor/IfAttr.Blazor.csproj`, `Program.cs`, `_Imports.razor`, `App.razor`, `wwwroot/index.html`, `wwwroot/css/app.css`
- Create: `samples/IfAttr/ifattr.js`, `samples/filament-ifattr-gen/main.js`
- Modify: `tests/Filament.Generator.Tests/RepoPaths.cs`, `GateTests.cs`, `.gitignore`

**Interfaces:**
- Produces: `RepoPaths.IfAttrRazor`, `RepoPaths.IfAttrAnswerKey`, `Generate.IfAttrToTemp()`.

- [ ] **Step 1: Create the Blazor project files**

`baseline/IfAttr.Blazor/IfAttr.Blazor.csproj` (identical size-affecting PropertyGroup to the other baselines):

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

`baseline/IfAttr.Blazor/Program.cs`:

```csharp
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using IfAttr.Blazor;

// Same minimal host as Counter.Blazor/Divide.Blazor: no Router (one screen), no
// HeadOutlet (static title), no HttpClient (no HTTP calls).
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

await builder.Build().RunAsync();
```

`baseline/IfAttr.Blazor/_Imports.razor`:

```razor
@* Only what the component actually uses: the Web namespace supplies @onclick. *@
@using Microsoft.AspNetCore.Components.Web
```

`baseline/IfAttr.Blazor/App.razor` (NO trailing `@using`):

```razor
@* Control-flow-in-attribute widening (BENCH n°16): a narrow @if/@else as the sole `class` value,
   compiled to the equivalent ternary. #withelse toggles active/idle; #noelse is the no-else form
   (false -> empty class, the measured branch). Blank lines between siblings are "\n\n" text nodes. *@

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

`baseline/IfAttr.Blazor/wwwroot/index.html` (title `IfAttr`):

```html
<!DOCTYPE html>
<html lang="en">

<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>IfAttr</title>
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
cp baseline/MixedAttr.Blazor/wwwroot/css/app.css baseline/IfAttr.Blazor/wwwroot/css/app.css
```

- [ ] **Step 3: Verify the Blazor app builds**

Run: `dotnet build baseline/IfAttr.Blazor/IfAttr.Blazor.csproj -v q`
Expected: build succeeds (0 errors).

- [ ] **Step 4: Write the answer key `samples/IfAttr/ifattr.js`**

The no-`else` `#noelse` binding uses the `''` hypothesis (see the measured decision in Task 4). If Task 4's
oracle refutes it, update this file and the snapshot then.

```js
/**
 * IfAttr — hand-written Filament app. Reference for the control-flow-in-attribute widening (BENCH n°16).
 *
 * ANSWER KEY (decisions 21/51): the generator's emission from baseline/IfAttr.Blazor/App.razor is
 * snapshot- and alpha-equivalence-tested against this file. Every line is written the way a COMPILER
 * would emit it. Never edited to make a gate pass.
 *
 * The source, transcribed exactly (the blank lines between the three siblings are "\n\n" text nodes):
 *
 *     <p id="withelse" class="@if (on) { <text>active</text> } else { <text>idle</text> }">With else</p>
 *
 *     <p id="noelse" class="@if (on) { <text>active</text> }">No else</p>
 *
 *     <button id="toggle" @onclick="Toggle">Toggle</button>
 *
 *     @code { private bool on = true; private void Toggle() { on = !on; } }
 *
 * THE POINT: a narrow @if / @if-else as the SOLE value of `class` is the same as the ternary
 * `@(on ? "active" : "idle")`, which already compiles. The compiler recognizes the @if block and
 * rewrites it to that ternary, so the binding is `effect(() => setAttr(p, 'class', on.value ? 'active' :
 * 'idle'))` -- byte-identical to the hand-written ternary. The no-else form's false branch is the empty
 * string (class=""). Nothing new was added to the runtime.
 *
 * `on` is read by both class values AND assigned outside construction (in Toggle), so it lifts to a
 * Signal. Toggle writes once, so the handler is a plain assignment (no batch).
 */

import { signal, effect, setAttr, listen, insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  // -- @code: state -----------------------------------------------------------
  const on = signal(true);

  // -- create(): the tree, built detached -------------------------------------

  // <p id="withelse" class="@if (on) { <text>active</text> } else { <text>idle</text> }">With else</p>
  const p1 = document.createElement('p');
  p1.id = 'withelse';
  insert(p1, document.createTextNode('With else'));

  // <p id="noelse" class="@if (on) { <text>active</text> }">No else</p>
  const p2 = document.createElement('p');
  p2.id = 'noelse';
  insert(p2, document.createTextNode('No else'));

  // <button id="toggle" @onclick="Toggle">Toggle</button>
  const button = document.createElement('button');
  button.id = 'toggle';
  insert(button, document.createTextNode('Toggle'));

  // -- bindings ---------------------------------------------------------------
  // Each @if-block class value compiles to the equivalent ternary over setAttr.
  effect(() => setAttr(p1, 'class', on.value ? 'active' : 'idle'));
  effect(() => setAttr(p2, 'class', on.value ? 'active' : ''));

  // -- events -----------------------------------------------------------------
  // Toggle writes once (on), so the handler is a plain assignment -- no batch.
  listen(button, 'click', () => {
    on.value = !on.value;
  });

  // -- attach: last, so the effects' first run made no MutationRecord ----------
  insert(target, p1);
  insert(target, document.createTextNode('\n\n'));
  insert(target, p2);
  insert(target, document.createTextNode('\n\n'));
  insert(target, button);
}
```

- [ ] **Step 5: Write the oracle host shim `samples/filament-ifattr-gen/main.js`**

```js
/**
 * Entry point for the `filament-ifattr-gen` label — the control-flow-in-attribute app.
 *
 * It mounts the JS the generator emits from baseline/IfAttr.Blazor/App.razor (two <p>s whose `class`
 * is a narrow @if/@else block). Like the reactiveattr/mixedattr labels it is NOT weighed or timed: it
 * exists only so the DOM-contract oracle can click #toggle and assert the composed `class` tracks
 * state, against Blazor's own DOM.
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
samples/filament-ifattr-gen/App.g.js
```

- [ ] **Step 7: Add the RepoPaths properties**

In `tests/Filament.Generator.Tests/RepoPaths.cs`, after the `MixedAttrAnswerKey` property (before `Canon`), add:

```csharp
    /// <summary>Control-flow-in-attribute (a narrow @if/@else as the sole `class` value) — the file Blazor compiles.</summary>
    public static string IfAttrRazor => Path.Combine(Root, "baseline", "IfAttr.Blazor", "App.razor");

    /// <summary>The control-flow-attribute SPEC (decisions 21/51). Never edited to make a gate pass.</summary>
    public static string IfAttrAnswerKey => Path.Combine(Root, "samples", "IfAttr", "ifattr.js");
```

- [ ] **Step 8: Add the Generate helper**

In `tests/Filament.Generator.Tests/GateTests.cs`, after the `MixedAttrToTemp()` line, add:

```csharp
    public static string IfAttrToTemp() => ToTemp(RepoPaths.IfAttrRazor, "IfAttr");
```

- [ ] **Step 9: Commit**

```bash
git add baseline/IfAttr.Blazor samples/IfAttr samples/filament-ifattr-gen \
        tests/Filament.Generator.Tests/RepoPaths.cs tests/Filament.Generator.Tests/GateTests.cs .gitignore
git commit -m "test(if-attr): IfAttr baseline app + answer key + test wiring"
```

---

## Task 2: Feature — recognize + rewrite a narrow `@if` class value to a ternary

**Files:**
- Modify: `src/Filament.Generator/TemplateCompiler.cs` (recognizer + `RewriteAttributeIfBlocks` beside `ComposableValue` ~532; wire into `PrepareComponent` ~281)
- Create: `tests/Filament.Generator.Tests/IfAttrTests.cs`
- Create: `tests/Filament.Generator.Tests/Snapshots/IfAttr.approved.js` (bootstrapped)

**Interfaces:**
- Consumes: `Generate.IfAttrToTemp()`, `RepoPaths.IfAttrAnswerKey`, `RepoPaths.Canon`, `Run.Node`; the existing `ComposableValue` / `CollectDynamicAttributes` / `ComposeAttributeValue` (unchanged).
- Produces: `IfBlockTernary(code) -> string?`, `RewriteAttributeIfBlocks(node)` (void, mutates the IR in place).

- [ ] **Step 1: Write the failing tests `tests/Filament.Generator.Tests/IfAttrTests.cs`**

```csharp
using Xunit;

namespace Filament.Generator.Tests;

public class IfAttrTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the module emitted from baseline/IfAttr.Blazor/App.razor is
    /// alpha-equivalent to the hand-written samples/IfAttr/ifattr.js. A narrow @if/@else class value is
    /// rewritten to the equivalent ternary; the emission is what the DOM-contract oracle measures
    /// (baseline/IfAttr.Blazor vs filament-ifattr-gen, BENCH n°16).
    /// </summary>
    [Fact]
    public void Gate_GeneratedIfAttr_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.IfAttrToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.IfAttrAnswerKey);
        Assert.True(exit == 0,
            "control-flow-attribute gate FAILED. Generated module is NOT alpha-equivalent to samples/IfAttr/ifattr.js.\n"
            + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>A narrow @if/@else class value compiles to the equivalent ternary over setAttr, NOT a
    /// refusal.</summary>
    [Fact]
    public void EmittedIfAttr_CompilesIfBlockToTernary()
    {
        var js = File.ReadAllText(Generate.IfAttrToTemp());
        Assert.Contains("effect(", js);
        Assert.Contains("setAttr(", js);
        Assert.Contains("'class'", js);
        Assert.Contains("on.value ? 'active' : 'idle'", js);   // @if/@else
        Assert.Contains("on.value ? 'active' : ''", js);        // no-else (see the measured decision)
        Assert.DoesNotContain("[unaccounted-attribute-value]", js);
        Assert.DoesNotContain("[dynamic-attribute]", js);
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedIfAttrJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.IfAttrToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "IfAttr.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
```

- [ ] **Step 2: Run the gate to verify it fails**

Run: `dotnet build src/Filament.Generator -v q && dotnet test tests/Filament.Generator.Tests --filter "FullyQualifiedName~IfAttrTests" -v q`
Expected: FAIL — the generator refuses the `@if` class value with `[unaccounted-attribute-value]`, so `Generate.IfAttrToTemp()` throws.

- [ ] **Step 3: Add the recognizer beside `ComposableValue`**

In `src/Filament.Generator/TemplateCompiler.cs`, immediately after the `ComposeAttributeValue` method (~line 603, after its closing `}`), add the recognizer and its helpers. (`Regex` is already used in this file — `BareIdentifier` — so `System.Text.RegularExpressions` is imported.)

```csharp
    static readonly Regex IfOpen = new(@"^\s*if\s*\(\s*(.+?)\s*\)\s*\{\s*$", RegexOptions.Compiled);
    static readonly Regex ElseMid = new(@"^\s*\}\s*else\s*\{\s*$", RegexOptions.Compiled);
    static readonly Regex IfClose = new(@"^\s*\}\s*$", RegexOptions.Compiled);

    /// <summary>
    /// The equivalent C# ternary source for a NARROW @if / @if-else code value, or null. Narrow shape
    /// (verified with --dump-ir): the code node's direct children alternate CS token and HtmlContent --
    ///   [CS "if (COND) {"], [HtmlContent A], [CS "}"]                                  -> `COND ? "A" : ""`
    ///   [CS "if (COND) {"], [HtmlContent A], [CS "} else {"], [HtmlContent B], [CS "}"] -> `COND ? "A" : "B"`
    /// where each branch is a single HtmlContent of PURE literal tokens (an element, an expression, a
    /// nested block, else-if/foreach/switch -> null, stays refused). COND is passed through verbatim; the
    /// front end compiles it (and lifts it to a signal read when reactive) exactly as for a hand-written
    /// @(COND ? "A" : "B"). The no-else false branch is "" (empty class); see the BENCH n°16 measured
    /// decision.
    /// </summary>
    static string? IfBlockTernary(CSharpCodeAttributeValueIntermediateNode code)
    {
        var kids = code.Children;
        if (kids.Count != 3 && kids.Count != 5) return null;
        if (kids[0] is not IntermediateToken open || open.IsHtml) return null;
        var m = IfOpen.Match(open.Content);
        if (!m.Success) return null;
        var cond = m.Groups[1].Value;
        if (BranchLiteral(kids[1]) is not { } a) return null;

        if (kids.Count == 3)
        {
            if (kids[2] is not IntermediateToken c3 || c3.IsHtml || !IfClose.IsMatch(c3.Content)) return null;
            return $"{cond} ? {CSharpString(a)} : \"\"";
        }

        if (kids[2] is not IntermediateToken mid || mid.IsHtml || !ElseMid.IsMatch(mid.Content)) return null;
        if (BranchLiteral(kids[3]) is not { } b) return null;
        if (kids[4] is not IntermediateToken c5 || c5.IsHtml || !IfClose.IsMatch(c5.Content)) return null;
        return $"{cond} ? {CSharpString(a)} : {CSharpString(b)}";
    }

    /// <summary>The literal text of a branch that is a single HtmlContent of PURE literal tokens, or null.</summary>
    static string? BranchLiteral(IntermediateNode n) =>
        n is HtmlContentIntermediateNode h && h.Children.All(c => c is IntermediateToken t && t.IsHtml)
            ? string.Concat(h.Children.OfType<IntermediateToken>().Select(t => t.Content))
            : null;

    /// <summary>A C# string literal for a branch's literal text (quotes/backslashes escaped).</summary>
    static string CSharpString(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
```

- [ ] **Step 4: Add the `RewriteAttributeIfBlocks` pre-pass**

Immediately after the recognizer (still after `ComposeAttributeValue`), add:

```csharp
    /// <summary>
    /// Rewrite a narrow @if/@else code VALUE (the sole value of a `class` attribute) into the equivalent
    /// ternary expression node, so the composable path compiles it exactly like a hand-written
    /// @(cond ? "a" : "b"). Source-to-source: recognize (IfBlockTernary) then swap the child in place. The
    /// front end reads a slot from its token Content (CSharpFrontEnd.RawText), so a synthesized expression
    /// token IS compilable. Gated to DynamicAttributes names and the SOLE-value case -- a lone ternary
    /// term, no `+`, no precedence hazard. Every non-narrow shape (mixed siblings, non-literal branch,
    /// else-if/foreach/switch, non-`class` name) is left untouched and stays refused
    /// (unaccounted-attribute-value). Runs before CollectDynamicAttributes so the synthesized node is
    /// harvested and later found (same object) by EmitAttribute.
    /// </summary>
    static void RewriteAttributeIfBlocks(IntermediateNode node)
    {
        if (node is MarkupElementIntermediateNode el && !LooksLikeComponent(el.TagName))
            foreach (var attr in el.Children.OfType<HtmlAttributeIntermediateNode>())
                if (DynamicAttributes.Contains(attr.AttributeName)
                    && attr.Children.Count == 1
                    && attr.Children[0] is CSharpCodeAttributeValueIntermediateNode code
                    && IfBlockTernary(code) is { } ternary)
                {
                    var synth = new CSharpExpressionAttributeValueIntermediateNode { Source = code.Source };
                    synth.Children.Add(new IntermediateToken { Kind = TokenKind.CSharp, Content = ternary, Source = code.Source });
                    attr.Children.RemoveAt(0);
                    attr.Children.Insert(0, synth);
                }
        foreach (var child in node.Children) RewriteAttributeIfBlocks(child);
    }
```

> **NOTE:** `LooksLikeComponent` is an instance method used by `CollectDynamicAttributes`; if the compiler rejects the `static` on `RewriteAttributeIfBlocks` because of it, drop `static` (make it an instance method — `CollectDynamicAttributes` is instance too). The recognizer helpers stay `static`.

- [ ] **Step 5: Wire the pre-pass into `PrepareComponent`**

In `PrepareComponent`, immediately before `CollectDynamicAttributes(method, plan);` (~line 282), add:

```csharp
        RewriteAttributeIfBlocks(method);
        CollectDynamicAttributes(method, plan);
```

- [ ] **Step 6: Rebuild, run the gate + behaviour, then bootstrap the snapshot**

Run: `dotnet build src/Filament.Generator -v q && dotnet test tests/Filament.Generator.Tests --filter "FullyQualifiedName~IfAttrTests" -v q`
Expected: `Gate_*` and `EmittedIfAttr_*` PASS; `Snapshot_*` FAILS once with "wrote …IfAttr.approved.js; review + re-run".

> **LIVE-ADJUST 1 (gate shape):** if `Gate_*` fails, read the canon diff. Likely culprits: the synthesized ternary emitting differently than `on.value ? 'active' : 'idle'` (e.g. stray parens, or a non-reactive create-time write if `on`'s reactivity was not detected), or the single-write handler shape. Confirm the generator's actual emission; if the compiler's faithful output differs from the answer key, correct `samples/IfAttr/ifattr.js` to match (not to force green). Re-run.

- [ ] **Step 7: Review + approve the snapshot**

Read `tests/Filament.Generator.Tests/Snapshots/IfAttr.approved.js`. Confirm by eye: `const on = signal(true);`, two class bindings `effect(() => setAttr(<el>, 'class', on.value ? 'active' : 'idle'));` and `… on.value ? 'active' : '');`, imports `signal, effect, setAttr, listen, insert` (no `batch`/`setText`), two `\n\n` attach nodes. Then re-run:

Run: `dotnet test tests/Filament.Generator.Tests --filter "FullyQualifiedName~IfAttrTests" -v q`
Expected: all 3 PASS.

- [ ] **Step 8: Prove the existing shapes are unchanged + full suites + firewall**

Run:
```bash
dotnet test tests/Filament.Generator.Tests --filter "FullyQualifiedName~ReactiveAttrTests|FullyQualifiedName~BoolAttrTests|FullyQualifiedName~MixedAttrTests" -v q
dotnet test tests/Filament.Generator.Tests -v q
cd src/filament-runtime && npm test; cd -
git diff --stat -- src/filament-runtime
```
Expected: ReactiveAttr/BoolAttr/MixedAttr all green UNCHANGED (the rewrite only fires for a sole-value `@if` code node); all generator tests green (including `AttributeCodeValue` — still refused, not sole-value); runtime green; **`git diff --stat -- src/filament-runtime` EMPTY**.

- [ ] **Step 9: Commit**

```bash
git add src/Filament.Generator/TemplateCompiler.cs \
        tests/Filament.Generator.Tests/IfAttrTests.cs \
        tests/Filament.Generator.Tests/Snapshots/IfAttr.approved.js
git commit -m "feat(if-attr): compile a narrow @if/@else class value to the equivalent ternary"
```

---

## Task 3: Boundary — control flow on a non-`class` attribute stays refused

**Files:**
- Create: `tests/Filament.Generator.Tests/Unsupported/IfNonClass.razor`
- Modify: `tests/Filament.Generator.Tests/DiagnosticTests.cs`

**Interfaces:**
- Consumes: the `Refused("<fixture>.razor")` helper.

- [ ] **Step 1: Create the fixture `tests/Filament.Generator.Tests/Unsupported/IfNonClass.razor`**

A narrow `@if` on `title` (NOT in `DynamicAttributes`), so the rewrite must NOT fire (NO trailing `@using`):

```razor
<p id="box" title="@if (on) { <text>active</text> }">hello</p>

@code {
    private bool on = true;

    private void Touch()
    {
        on = !on;
    }
}
```

- [ ] **Step 2: Write the failing boundary test**

In `tests/Filament.Generator.Tests/DiagnosticTests.cs`, after `MixedValueOnNonAllowedAttribute_IsRefused_AtItsExactLocation`, add:

```csharp
    /// <summary>
    /// The @if-block rewrite is gated to the `class` allowlist: a narrow @if on a non-allow-listed name
    /// (here `title`) is NOT rewritten and stays refused `[unaccounted-attribute-value]` at its exact
    /// location, the same way control flow mixed with literal siblings (AttributeCodeValue) does.
    /// </summary>
    [Fact]
    public void IfBlockOnNonClassAttribute_IsRefused_AtItsExactLocation()
    {
        var d = Refused("IfNonClass.razor");

        Assert.Contains("IfNonClass.razor(1,20): FIL0003: [unaccounted-attribute-value]", d);
        Assert.Contains("CSharpCodeAttributeValueIntermediateNode", d);
    }
```

> **LIVE-ADJUST 2 (column):** the `(1,20)` column is the predicted start of the code value in `<p id="box" title="@if…`. Run the test, read the ACTUAL `IfNonClass.razor(1,NN)` from the failure, and set the assertion to the real value.

- [ ] **Step 3: Run the boundary + regression diagnostics**

Run: `dotnet test tests/Filament.Generator.Tests --filter "FullyQualifiedName~DiagnosticTests" -v q`
Expected: `IfBlockOnNonClassAttribute_*` PASS (after any column adjust); all other diagnostics (`AttributeCodeValue`, `Bind`, `DynamicNonClassAttribute`, `NonAllowedBooleanAttribute`, `MixedValueOnNonAllowedAttribute`) PASS unchanged.

- [ ] **Step 4: Commit**

```bash
git add tests/Filament.Generator.Tests/Unsupported/IfNonClass.razor tests/Filament.Generator.Tests/DiagnosticTests.cs
git commit -m "test(if-attr): @if on a non-class attribute stays refused (allowlist boundary)"
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
export const HARNESS_VERSION = '1.11.0';   // 1.11.0: 'ifattr' contract (narrow @if/@else class value). 1.10.0: 'mixedattr' (mixed literal+expression class value). 1.9.0: 'boolattr' (boolean disabled present/absent). 1.8.0: 'reactiveattr' (reactive class attribute). 1.7.0: 'boundcompose' (bound-parameter composition). 1.6.0: rootforeach/rootif. 1.5.0: compose. 1.4.0: divide.
```

- [ ] **Step 2: Add the APPS entry**

In `bench/harness/bench.mjs`, after the `mixedattr` APPS entry (before the closing `};`), add:

```js
  // Correctness-only: verifyContract clicks #toggle and asserts two narrow @if/@else class values
  // (#withelse active/idle, #noelse active/"") track state, against Blazor's own rendered DOM. The
  // measurement of the control-flow-in-attribute widening (BENCH n°16).
  ifattr: {
    readySelector: '#toggle',
    observeSelector: '#withelse',
    scenarios: [],
  },
```

- [ ] **Step 3: Add the verifyContract clause**

In `bench/harness/bench.mjs`, after the `mixedattr` clause (before `if (app === 'divide')`), add:

```js
    if (app === 'ifattr') {
      return ctx.page.evaluate(() => {
        const out = { problems: [], observed: {} };
        for (const sel of ['#toggle', '#withelse', '#noelse']) {
          if (!document.querySelector(sel)) out.problems.push(`missing required element: ${sel}`);
        }
        if (out.problems.length) return out;

        const cls = (sel) => document.querySelector(sel).getAttribute('class');
        out.observed.initialWithElse = cls('#withelse');
        out.observed.initialNoElse = cls('#noelse');
        // Blazor's own initial render (on=true): both "active". If either already read the post-click
        // value the assertions below would be vacuous.
        if (out.observed.initialWithElse !== 'active' || out.observed.initialNoElse !== 'active') {
          out.problems.push(`initial class is {withelse:"${out.observed.initialWithElse}", noelse:"${out.observed.initialNoElse}"}, expected both "active"`);
          return out;
        }
        document.querySelector('#toggle').click();
        out.observed.afterWithElse = cls('#withelse');
        out.observed.afterNoElse = cls('#noelse');
        // THE MEASUREMENT: a narrow @if/@else class value re-composes on state change, against Blazor's
        // OWN rendered DOM. #withelse -> "idle" (both branches literal, unambiguous). #noelse false is
        // the MEASURED branch: the hypothesis is class="" (empty). If Blazor OMITS the attribute here,
        // getAttribute returns null -> this run fails, and the emission switches to null (LIVE-ADJUST 3).
        if (out.observed.afterWithElse !== 'idle') {
          out.problems.push(`#withelse class after #toggle is "${out.observed.afterWithElse}", expected "idle"`);
        }
        if (out.observed.afterNoElse !== '') {
          out.problems.push(`#noelse class after #toggle is ${JSON.stringify(out.observed.afterNoElse)}, expected "" (empty)`);
        }
        return out;
      });
    }
```

- [ ] **Step 4: Add the build-filament.sh case arms**

In `bench/build-filament.sh`, add a `filament-ifattr-gen` arm beside each existing `filament-mixedattr-gen` arm (eight arms): `ALL_LABELS`; `project_for` (`samples/filament-ifattr-gen`); `mode_for` (`production` alternation); `razor_for` (`$REPO_ROOT/baseline/IfAttr.Blazor/App.razor`); `generated_js_for` (`App.g.js`); `title_for` (`IfAttr`); `blazor_label_for` (`blazor-ifattr`); `css_for` (`$REPO_ROOT/baseline/IfAttr.Blazor/wwwroot/css/app.css`).

- [ ] **Step 5: Build the Filament app + publish the Blazor baseline**

Run:
```bash
bash bench/build-filament.sh filament-ifattr-gen
dotnet publish baseline/IfAttr.Blazor/IfAttr.Blazor.csproj -c Release -o bench/publish/blazor-ifattr
```
Expected: `samples/filament-ifattr-gen/App.g.js` emitted; the Blazor publish lands under `bench/publish/blazor-ifattr/wwwroot`.

- [ ] **Step 6: Run the oracle against BOTH builds — this DECIDES the no-else branch**

Run:
```bash
node bench/harness/bench.mjs --dir bench/publish/filament-ifattr-gen --app ifattr --label filament-ifattr-gen --contract-only --headless
node bench/harness/bench.mjs --dir bench/publish/blazor-ifattr/wwwroot --app ifattr --label blazor-ifattr --contract-only --headless
```
Expected: BOTH report `DOM contract OK` with `{"initialWithElse":"active","initialNoElse":"active","afterWithElse":"idle","afterNoElse":""}`.

> **LIVE-ADJUST 3 (the measured no-else decision):** if the **blazor-ifattr** run fails on `#noelse` — `afterNoElse` is `null` (attribute omitted) rather than `""` — then Blazor omits the attribute on the false no-else branch. Remediate:
> 1. In `IfBlockTernary`, change the no-else return from `$"{cond} ? {CSharpString(a)} : \"\""` to `$"{cond} ? {CSharpString(a)} : null"`.
> 2. Rebuild the generator; re-generate (`bash bench/build-filament.sh filament-ifattr-gen`).
> 3. Update `samples/IfAttr/ifattr.js` `#noelse` binding to `on.value ? 'active' : null` and re-approve `Snapshots/IfAttr.approved.js` (delete + re-run the snapshot test), and update `EmittedIfAttr_CompilesIfBlockToTernary`'s `on.value ? 'active' : ''` assertion to `... : null`.
> 4. In this `verifyContract` clause, change the `#noelse` after-assertion to expect `null` (attribute absent).
> 5. Re-run both oracle runs; both must now report OK. (Fold the resulting edits into the Task 2 + Task 4 commits, or a small follow-up commit.)

- [ ] **Step 7: Append BENCH n°16**

Append to `BENCH.md` an `Entrée n°16` (French, mirroring n°15): the control-flow-in-attribute widening (narrow @if/@else on `class`, rewritten to the equivalent ternary); `baseline/IfAttr.Blazor` vs `filament-ifattr-gen`; BOTH render `#withelse` `active → idle` and `#noelse` `active → ""` (state the MEASURED no-else result) identically; correctness-only; ternary-equivalent (disclosed); runtime UNCHANGED; `HARNESS_VERSION 1.10.0→1.11.0` disclosed. No prior figure invalidated.

- [ ] **Step 8: Commit**

```bash
git add bench/harness/bench.mjs bench/build-filament.sh BENCH.md
git commit -m "bench(if-attr): DOM-contract oracle + BENCH n°16 (narrow @if/@else class value)"
```

---

## Task 5: Record — DECISIONS #97 + memory, then finish

**Files:**
- Append: `DECISIONS.md`
- Update memory: `if-block-attribute-widened.md` + `MEMORY.md` index (outside the repo — not committed)

- [ ] **Step 1: Append DECISIONS #97**

Append to `DECISIONS.md` a `#97` entry (French, house style, mirroring #96): the control-flow-in-attribute widening (narrow) enters §5. Key points: a sole-value `@if`/`@else` on `class` is **rewritten source-to-source** to the equivalent ternary expression node (`cond ? "A" : "B"`), compiled by the already-shipped composable path — byte-identical to the hand-written `@(…)` ternary (which already compiled); the front end reads a slot from its token Content (`RawText`), so a synthesized token IS compilable; `IfBlockTernary` the single recognizer of the narrow shape (verified via `--dump-ir`); **sole-value only** (a lone ternary term → no `+` → no precedence hazard), so control flow mixed with literal siblings stays deferred; the `unaccounted-attribute-value` refusal is UNTOUCHED for every non-narrow shape (mixed siblings — `AttributeCodeValue` unchanged, non-literal branch, else-if/foreach/switch, non-`class` name — `IfNonClass` at its column); the MEASURED no-else false-rendering decision (empty `""` vs absent `null`, decided by the oracle — state the outcome); runtime UNCHANGED; measured vs Blazor (BENCH n°16, `HARNESS_VERSION` bump disclosed); ternary-equivalence disclosed (a spelling, not new power); remaining sub-slices deferred. Test count updated (state the exact number after running the full suite).

- [ ] **Step 2: Commit DECISIONS**

```bash
git add DECISIONS.md
git commit -m "docs(if-attr): DECISIONS #97 — narrow @if/@else class value (measured widening)"
```

- [ ] **Step 3: Update memory**

Create `~/.claude/projects/-Users-phmatray-Repositories-dotnet-Filament/memory/if-block-attribute-widened.md` (type: project) capturing: narrow `@if`/`@else` as sole `class` value entered the measured subset (#97 / BENCH n°16); generator-only source-to-source rewrite to the equivalent ternary (the `@(…)` ternary already compiled); `IfBlockTernary` recognizer, sole-value-only for precedence; `unaccounted` refusal untouched for non-narrow shapes; the measured no-else decision (record the outcome, `""` vs `null`); ternary-equivalent (a spelling, disclosed); runtime UNCHANGED; deferred remainder (mixed siblings, non-literal branches, else-if/foreach/switch, non-`class`). Add a one-line index pointer to `MEMORY.md`. Link `[[mixed-class-attribute-widened]]`. (Outside the repo — not committed.)

- [ ] **Step 4: Final verification + finish**

Run:
```bash
dotnet test Filament.sln -v q
cd src/filament-runtime && npm test; cd -
git diff --stat -- src/filament-runtime   # must be empty
git log --oneline -7
```
Expected: all .NET tests green (with the new IfAttr gate/behaviour/snapshot + boundary, and ReactiveAttr/BoolAttr/MixedAttr/AttributeCodeValue unchanged), runtime green, runtime firewall clean.

Then invoke **superpowers:finishing-a-development-branch**. Environment is a normal repo on `main` with no remote: report the slice landed on `main`.

---

## Self-Review

**Spec coverage** (checked against `docs/superpowers/specs/2026-07-19-control-flow-attribute-design.md`):
- §The IR + §change 1 recognizer → Task 2 Step 3 (`IfBlockTernary`/`BranchLiteral`/`CSharpString`). ✓
- §change 2 rewrite + placement → Task 2 Steps 4–5. ✓
- §change 3 sole-value / precedence → Task 2 Step 4 gate (`Children.Count == 1`). ✓
- §Scope refusals (mixed siblings, non-literal, else-if/foreach/switch, non-`class`) → Task 2 Step 8 (`AttributeCodeValue`), Task 3 (`IfNonClass`). ✓
- §Runtime unchanged → Global Constraints + Task 2 Step 8 (`git diff` empty). ✓
- §Measured app IfAttr → Task 1. ✓
- §Measured decision (no-else `''` vs `null`) → Task 1 Step 4 note + Task 4 Step 6 LIVE-ADJUST 3. ✓
- §Measurement oracle + BENCH n°16 → Task 4. ✓
- §Tests gate/behaviour/snapshot + ReactiveAttr/BoolAttr/MixedAttr/AttributeCodeValue regressions + boundary → Task 2 Steps 1, 8; Task 3. ✓
- §Decision record #97 → Task 5. ✓

**Placeholder scan:** no TBD/TODO; every code step shows actual code; three genuine environment-reads flagged LIVE-ADJUST (gate shape, boundary column, the measured no-else decision) with exact remediation.

**Type/name consistency:** `IfAttrRazor`/`IfAttrAnswerKey`/`IfAttrToTemp()` consistent across Tasks 1–2; `IfBlockTernary` (returns `string?`), `BranchLiteral`, `CSharpString`, `RewriteAttributeIfBlocks` (void) consistent; `filament-ifattr-gen`/`blazor-ifattr`/`ifattr` labels consistent across Tasks 1, 4. The emitted `on.value ? 'active' : 'idle'` / `on.value ? 'active' : ''` matches the behaviour test, the answer key, and the oracle assertions.
