# Mixed literal+expression `class` value Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Admit a mixed literal+expression value on `class` (`class="badge @statusClass rounded"`) by composing the ordered value parts into one concatenated string via a prefix-aware fold, and measure it against Blazor (BENCH n°15).

**Architecture:** A generator-only widening. A new shared predicate `ComposableValue(attr)` (every child is a literal/expression value part, ≥1 expression, no event handler) is consulted by both the harvest (harvest *each* expression part into `FreeSlots`) and the emission. A prefix-aware fold `ComposeAttributeValue(parts)` builds the concatenation. The fold **generalises** the existing `class` branch — the pure `@expr` case is its degenerate form and emits byte-identically, so the ReactiveAttr gate + snapshot are the safety net. `DynamicValue` (single-pure) is retained for the boolean `disabled` path; the `unaccounted` control-flow refusal is untouched.

**Tech Stack:** C# Roslyn-derived generator (`Filament.Generator`), Razor IR intermediate nodes, xUnit, `canon.mjs` alpha-equivalence gate, byte snapshots, the Playwright DOM-contract oracle, Blazor WASM baselines.

## Global Constraints

- **Measured slice, not DX.** Generator emitted bytes DO change; a BENCH entry IS added.
- **Runtime UNCHANGED.** No edit to `src/filament-runtime`. Verify `git diff --stat -- src/filament-runtime` is EMPTY at the end. Composition is JS string concatenation over the existing `setAttr`.
- **The pure `class="@x"` case must stay byte-identical.** The generalised fold replaces the old branch; the existing `ReactiveAttrTests` (gate + behaviour + snapshot) MUST stay green **unchanged** — that is the proof the refactor is safe. If any ReactiveAttr test changes, STOP: the fold is not faithful to the degenerate case.
- **Never reason, always measure.** The correctness claim is settled by the oracle running BOTH builds and observing identical DOM.
- **The answer key is the reference (decisions 21/51).** `samples/MixedAttr/mixedattr.js` is never edited to make a gate pass.
- **Single predicate, no drift (decision 53).** Harvest and emission share `ComposableValue`.
- **Control-flow-in-attribute stays refused.** `ComposableValue` returns null for a `CSharpCodeAttributeValueIntermediateNode`; the `unaccounted-attribute-value` path is untouched. The `AttributeCodeValue.razor` test stays green.
- **Trunk-based, no remote.** Commit directly to `main`.
- **French house style** for DECISIONS.md / BENCH.md, mirroring #95 / n°14.
- **HARNESS_VERSION bump disclosed:** `1.9.0 → 1.10.0`.

---

## File Structure

**Reference (Task 1):**
- Create `baseline/MixedAttr.Blazor/` — a normal Blazor WASM project, modelled on `baseline/ReactiveAttr.Blazor/`.
- Create `samples/MixedAttr/mixedattr.js` — the hand-written answer key.
- Create `samples/filament-mixedattr-gen/main.js` — the oracle host shim.
- Modify `tests/Filament.Generator.Tests/RepoPaths.cs` and `GateTests.cs`.
- Modify `.gitignore`.

**Feature (Task 2):**
- Modify `src/Filament.Generator/TemplateCompiler.cs` — `ComposableValue` predicate, `ComposeAttributeValue` fold, generalised `class` branch, widened `CollectDynamicAttributes`, refusal message.
- Create `tests/Filament.Generator.Tests/MixedAttrTests.cs`.
- Create `tests/Filament.Generator.Tests/Snapshots/MixedAttr.approved.js` (bootstrapped).

**Boundary (Task 3):**
- Create `tests/Filament.Generator.Tests/Unsupported/MixedNonAllowed.razor` — `title="pre @caption"`.
- Modify `tests/Filament.Generator.Tests/DiagnosticTests.cs`.

**Measurement (Task 4):**
- Modify `bench/harness/bench.mjs`, `bench/build-filament.sh`; append `BENCH.md`.

**Record (Task 5):**
- Append `DECISIONS.md`; update memory.

---

## Task 1: Reference — `MixedAttr.Blazor` app, answer key, wiring, host shim

**Files:**
- Create: `baseline/MixedAttr.Blazor/MixedAttr.Blazor.csproj`, `Program.cs`, `_Imports.razor`, `App.razor`, `wwwroot/index.html`, `wwwroot/css/app.css`
- Create: `samples/MixedAttr/mixedattr.js`, `samples/filament-mixedattr-gen/main.js`
- Modify: `tests/Filament.Generator.Tests/RepoPaths.cs`, `tests/Filament.Generator.Tests/GateTests.cs`, `.gitignore`

**Interfaces:**
- Produces: `RepoPaths.MixedAttrRazor`, `RepoPaths.MixedAttrAnswerKey`, `Generate.MixedAttrToTemp()`.

- [ ] **Step 1: Create the Blazor project files**

`baseline/MixedAttr.Blazor/MixedAttr.Blazor.csproj`:

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

`baseline/MixedAttr.Blazor/Program.cs`:

```csharp
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MixedAttr.Blazor;

// Same minimal host as Counter.Blazor/Divide.Blazor: no Router (one screen), no
// HeadOutlet (static title), no HttpClient (no HTTP calls).
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

await builder.Build().RunAsync();
```

`baseline/MixedAttr.Blazor/_Imports.razor`:

```razor
@* Only what the component actually uses: the Web namespace supplies @onclick. *@
@using Microsoft.AspNetCore.Components.Web
```

`baseline/MixedAttr.Blazor/App.razor` (NO trailing `@using` — it would refuse FIL0003):

```razor
@* Mixed literal+expression `class` widening (BENCH n°15). #status carries
   class="badge @statusClass rounded" -- a leading literal, a reactive expression, and a trailing
   literal -- so the whole class string is composed and updates with state. Blank lines between the
   three siblings are "\n\n" text nodes -- the shared DOM contract. *@

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

`baseline/MixedAttr.Blazor/wwwroot/index.html` (title `MixedAttr`):

```html
<!DOCTYPE html>
<html lang="en">

<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>MixedAttr</title>
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
cp baseline/ReactiveAttr.Blazor/wwwroot/css/app.css baseline/MixedAttr.Blazor/wwwroot/css/app.css
```

- [ ] **Step 3: Verify the Blazor app builds**

Run: `dotnet build baseline/MixedAttr.Blazor/MixedAttr.Blazor.csproj -v q`
Expected: build succeeds (0 errors).

- [ ] **Step 4: Write the answer key `samples/MixedAttr/mixedattr.js`**

This is `samples/ReactiveAttr/reactiveattr.js` with ONE change — the `class` binding composes
`'badge ' + statusClass.value + ' rounded'` instead of the bare `statusClass.value`:

```js
/**
 * MixedAttr — hand-written Filament app. Reference for the mixed literal+expression `class` widening
 * (BENCH n°15).
 *
 * ANSWER KEY (decisions 21/51): the generator's emission from baseline/MixedAttr.Blazor/App.razor is
 * snapshot- and alpha-equivalence-tested against this file. Every line is written the way a COMPILER
 * would emit it. Never edited to make a gate pass.
 *
 * The source, transcribed exactly (the blank lines between the three siblings are "\n\n" text nodes):
 *
 *     <h1 id="title">Counter</h1>
 *
 *     <p id="status" class="badge @statusClass rounded">Current count: <span id="counter-value">@currentCount</span></p>
 *
 *     <button id="increment" @onclick="Increment">Click me</button>
 *
 *     @code {
 *         private int currentCount = 0;
 *         private string statusClass = "zero";
 *         private void Increment() { currentCount++; statusClass = "counting"; }
 *     }
 *
 * THE POINT: `class="badge @statusClass rounded"` is a MIXED literal+expression value. Razor gives the
 * value as ordered parts, each with a Prefix; the compiler folds them into one concatenation:
 * `'badge '` (literal "badge" + the expression's leading " ") + `statusClass.value` + `' rounded'`
 * (the trailing literal's " " + "rounded"). `statusClass` is read by the template AND assigned outside
 * construction, so it lifts to a Signal and the whole class binding is a live
 * `effect(() => setAttr(p, 'class', 'badge ' + statusClass.value + ' rounded'))`. The pure `@expr`
 * case (BENCH n°13) is the degenerate fold; this adds the literal terms around the expression. Nothing
 * new was added to the runtime (setAttr + JS string concat).
 *
 * The class effect emits BEFORE the @currentCount text effect (the <p>'s attributes are walked before
 * its children). Both first-run against the DETACHED tree, so neither makes a MutationRecord; attach is
 * last. Increment writes twice, so the handler batches.
 */

import { signal, effect, batch, setText, setAttr, listen, insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  // -- @code: state -----------------------------------------------------------
  const currentCount = signal(0);
  const statusClass = signal('zero');

  // -- create(): the tree, built detached -------------------------------------

  // <h1 id="title">Counter</h1>
  const h1 = document.createElement('h1');
  h1.id = 'title';
  insert(h1, document.createTextNode('Counter'));

  // <p id="status" class="badge @statusClass rounded">Current count: <span id="counter-value">@currentCount</span></p>
  const p = document.createElement('p');
  p.id = 'status';
  insert(p, document.createTextNode('Current count: '));
  const span = document.createElement('span');
  span.id = 'counter-value';
  const t = document.createTextNode('');
  insert(span, t);
  insert(p, span);

  // <button id="increment" @onclick="Increment">Click me</button>
  const button = document.createElement('button');
  button.id = 'increment';
  insert(button, document.createTextNode('Click me'));

  // -- bindings ---------------------------------------------------------------
  // class first (the <p> attribute, composed), then the @currentCount text (the inner span).
  effect(() => setAttr(p, 'class', 'badge ' + statusClass.value + ' rounded'));
  effect(() => setText(t, currentCount.value));

  // -- events -----------------------------------------------------------------
  // Increment writes twice (currentCount and statusClass), so the handler batches.
  listen(button, 'click', () => batch(() => {
    currentCount.value++;
    statusClass.value = 'counting';
  }));

  // -- attach: last, so the effects' first run made no MutationRecord ----------
  insert(target, h1);
  insert(target, document.createTextNode('\n\n'));
  insert(target, p);
  insert(target, document.createTextNode('\n\n'));
  insert(target, button);
}
```

- [ ] **Step 5: Write the oracle host shim `samples/filament-mixedattr-gen/main.js`**

```js
/**
 * Entry point for the `filament-mixedattr-gen` label — the mixed-`class` app.
 *
 * It mounts the JS the generator emits from baseline/MixedAttr.Blazor/App.razor (a counter whose
 * #status element carries class="badge @statusClass rounded"). Like the reactiveattr label it is NOT
 * weighed or timed: it exists only so the DOM-contract oracle can click #increment and assert the
 * composed `class` string tracks state, against Blazor's own DOM.
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

In `.gitignore`, after the `samples/filament-boolattr-gen/App.g.js` line, add:

```
samples/filament-mixedattr-gen/App.g.js
```

- [ ] **Step 7: Add the RepoPaths properties**

In `tests/Filament.Generator.Tests/RepoPaths.cs`, after the `BoolAttrAnswerKey` property (before `Canon`), add:

```csharp
    /// <summary>Mixed literal+expression `class` value (a counter whose #status class is composed) — the file Blazor compiles.</summary>
    public static string MixedAttrRazor => Path.Combine(Root, "baseline", "MixedAttr.Blazor", "App.razor");

    /// <summary>The mixed-attribute SPEC (decisions 21/51). Never edited to make a gate pass.</summary>
    public static string MixedAttrAnswerKey => Path.Combine(Root, "samples", "MixedAttr", "mixedattr.js");
```

- [ ] **Step 8: Add the Generate helper**

In `tests/Filament.Generator.Tests/GateTests.cs`, after the `BoolAttrToTemp()` line, add:

```csharp
    public static string MixedAttrToTemp() => ToTemp(RepoPaths.MixedAttrRazor, "MixedAttr");
```

- [ ] **Step 9: Commit**

```bash
git add baseline/MixedAttr.Blazor samples/MixedAttr samples/filament-mixedattr-gen \
        tests/Filament.Generator.Tests/RepoPaths.cs tests/Filament.Generator.Tests/GateTests.cs .gitignore
git commit -m "test(mixed-attr): MixedAttr baseline app + answer key + test wiring"
```

---

## Task 2: Feature — compose a mixed `class` value via the prefix-aware fold

**Files:**
- Modify: `src/Filament.Generator/TemplateCompiler.cs` (`CollectDynamicAttributes` ~512, `EmitAttribute` class branch ~1270, refusal message ~1308; new `ComposableValue` + `ComposeAttributeValue` beside `DynamicValue` ~525)
- Create: `tests/Filament.Generator.Tests/MixedAttrTests.cs`
- Create: `tests/Filament.Generator.Tests/Snapshots/MixedAttr.approved.js` (bootstrapped)

**Interfaces:**
- Consumes: `Generate.MixedAttrToTemp()`, `RepoPaths.MixedAttrAnswerKey`, `RepoPaths.Canon`, `Run.Node`.
- Produces: `ComposableValue(attr) -> IReadOnlyList<IntermediateNode>?`, `ComposeAttributeValue(parts) -> (string js, bool reactive)`, and the emission `effect(() => setAttr(<el>, 'class', <folded terms>));` (or create-time).

- [ ] **Step 1: Write the failing tests `tests/Filament.Generator.Tests/MixedAttrTests.cs`**

```csharp
using Xunit;

namespace Filament.Generator.Tests;

public class MixedAttrTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the module emitted from baseline/MixedAttr.Blazor/App.razor is
    /// alpha-equivalent to the hand-written samples/MixedAttr/mixedattr.js. The spec is the reference; the
    /// generator is judged. mixedattr.js's Blazor-faithfulness is what the DOM-contract oracle measures
    /// (baseline/MixedAttr.Blazor vs filament-mixedattr-gen, BENCH n°15).
    /// </summary>
    [Fact]
    public void Gate_GeneratedMixedAttr_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.MixedAttrToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.MixedAttrAnswerKey);
        Assert.True(exit == 0,
            "mixed-attribute gate FAILED. Generated module is NOT alpha-equivalent to samples/MixedAttr/mixedattr.js.\n"
            + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>The `class` value is a live effect over setAttr whose argument is the COMPOSED string:
    /// the literal terms survive around the reactive expression, in order.</summary>
    [Fact]
    public void EmittedMixedAttr_ComposesLiteralsAroundExpression()
    {
        var js = File.ReadAllText(Generate.MixedAttrToTemp());
        Assert.Contains("effect(", js);
        Assert.Contains("setAttr(", js);
        Assert.Contains("'class'", js);
        Assert.Contains("'badge '", js);          // leading literal
        Assert.Contains("statusClass.value", js); // the reactive expression
        Assert.Contains("' rounded'", js);         // trailing literal
        Assert.DoesNotContain("[dynamic-attribute]", js);
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedMixedAttrJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.MixedAttrToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "MixedAttr.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
```

- [ ] **Step 2: Run the gate to verify it fails**

Run: `dotnet build src/Filament.Generator -v q && dotnet test tests/Filament.Generator.Tests --filter "FullyQualifiedName~MixedAttrTests" -v q`
Expected: FAIL — the generator refuses the mixed `class` value with FIL0003 `[dynamic-attribute]` (`DynamicValue` is null when a literal part is present), so `Generate.MixedAttrToTemp()` throws.

- [ ] **Step 3: Add `ComposableValue` + `ComposeAttributeValue` beside `DynamicValue`**

In `src/Filament.Generator/TemplateCompiler.cs`, immediately after the `DynamicValue` method (~line 532, right after its closing `}`), add:

```csharp
    /// <summary>
    /// The ordered value parts of an attribute that COMPOSES to a string, or null. Composable = every
    /// child is a literal (HtmlAttributeValue) or an expression (CSharpExpressionAttributeValue) part,
    /// there is at least one expression, and no expression part is an event handler. A control-flow value
    /// node (CSharpCodeAttributeValue -- `class="@if(c){…}"`) makes it null, so that value stays on the
    /// `unaccounted-attribute-value` refusal (distinct slice). The pure `@expr` case is the degenerate
    /// composable value (one expression, no literals). The ONE predicate the harvest
    /// (CollectDynamicAttributes) and the emission (EmitAttribute) both consult (decision 53).
    /// </summary>
    static IReadOnlyList<IntermediateNode>? ComposableValue(HtmlAttributeIntermediateNode attr)
    {
        var parts = attr.Children
            .Where(c => c is HtmlAttributeValueIntermediateNode or CSharpExpressionAttributeValueIntermediateNode)
            .ToList();
        if (parts.Count != attr.Children.Count) return null;   // a non-value node (control flow) -> not composable
        var exprs = parts.OfType<CSharpExpressionAttributeValueIntermediateNode>().ToList();
        if (exprs.Count == 0) return null;                     // no expression -> the static-literal path handles it
        foreach (var e in exprs)
        {
            var text = string.Concat(e.Children.OfType<IntermediateToken>().Select(t => t.Content));
            if (TryUnwrapEventCallback(text, out _)) return null; // an event handler keeps its listen() path
        }
        return parts;
    }

    /// <summary>
    /// Fold the ordered value parts into a single JS string expression, prefix-aware. Each part
    /// contributes its Prefix (the literal text before it) then its body: a literal part appends its
    /// content to a running buffer; an expression part flushes the buffer as a JS string term, then emits
    /// SlotJs (never a splice). Terms are joined with ` + `. `class="badge @x rounded"` folds to
    /// `'badge ' + x.value + ' rounded'`; the pure `class="@x"` folds to just `x.value` (byte-identical to
    /// the reactive-`class` slice). `reactive` is true iff ANY expression part is reactive.
    /// </summary>
    (string js, bool reactive) ComposeAttributeValue(IReadOnlyList<IntermediateNode> parts)
    {
        var terms = new List<string>();
        var buf = new System.Text.StringBuilder();
        var reactive = false;
        foreach (var part in parts)
        {
            if (part is HtmlAttributeValueIntermediateNode h)
            {
                buf.Append(h.Prefix);
                buf.Append(string.Concat(h.Children.OfType<IntermediateToken>().Select(t => t.Content)));
            }
            else if (part is CSharpExpressionAttributeValueIntermediateNode c)
            {
                buf.Append(c.Prefix);
                if (buf.Length > 0) { terms.Add(JsString(buf.ToString())); buf.Clear(); }
                terms.Add(_code.SlotJs(c));
                if (_code.SlotIsReactive(c)) reactive = true;
            }
        }
        if (buf.Length > 0) terms.Add(JsString(buf.ToString()));
        return (string.Join(" + ", terms), reactive);
    }
```

> **NOTE:** if `JsString` is a static method, keep `ComposeAttributeValue` instance (it uses `_code`); if the compiler warns `System.Text.StringBuilder` is already imported, drop the namespace qualifier. Match the file's existing `using`s (it already uses `IntermediateToken`, `HtmlAttributeValueIntermediateNode`, etc.).

- [ ] **Step 4: Generalise the `class` emission branch**

In `EmitAttribute`, replace the existing `DynamicAttributes` branch (the `if (DynamicAttributes.Contains(name) && DynamicValue(attr) is { } valueNode)` block and its comment) with the composed form:

```csharp
            // COMPOSED STRING ATTRIBUTE VALUE (the `class` slice: pure #94/n°13, mixed #96/n°15). An
            // allow-listed string attribute folds its ordered value parts (literals + expressions) into one
            // setAttr. The pure `@expr` case is the degenerate fold (one expression, no literals) and emits
            // byte-identically -- the ReactiveAttr gate proves it. Reactive iff any expression part is
            // reactive; the effect lands in _bindings (before attach), so its first setAttr writes into the
            // detached tree and makes no MutationRecord.
            if (DynamicAttributes.Contains(name) && ComposableValue(attr) is { } parts)
            {
                var (js, reactive) = ComposeAttributeValue(parts);
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

- [ ] **Step 5: Widen the harvest**

In `CollectDynamicAttributes` (~line 512), replace the single-`DynamicValue` harvest with the composable harvest for the string allowlist, keeping `DynamicValue` for the boolean allowlist:

```csharp
                if (DynamicAttributes.Contains(attr.AttributeName) && ComposableValue(attr) is { } parts)
                    foreach (var e in parts.OfType<CSharpExpressionAttributeValueIntermediateNode>())
                        plan.FreeSlots.Add(e);
                else if (BooleanAttributes.Contains(attr.AttributeName) && DynamicValue(attr) is { } expr)
                    plan.FreeSlots.Add(expr);
```

- [ ] **Step 6: Update the refusal message (mixed on `class` is now admitted)**

In the `Diag("dynamic-attribute", ...)` message (~line 1308), remove the now-false "or a mixed literal+expression value (`class="box @x"`)" clause. Replace the two message lines:

```csharp
                "neither a resolved event handler nor a static value. A dynamic value on an un-measured " +
                "attribute -- or a mixed literal+expression value (class=\"box @x\") -- has no measurement " +
                "covering it. Refusing to emit.",
```

with:

```csharp
                "neither a resolved event handler nor a static value. A dynamic value on an un-measured " +
                "attribute has no measurement covering it. Refusing to emit.",
```

- [ ] **Step 7: Rebuild, run the mixed gate + behaviour, then bootstrap the snapshot**

Run: `dotnet build src/Filament.Generator -v q && dotnet test tests/Filament.Generator.Tests --filter "FullyQualifiedName~MixedAttrTests" -v q`
Expected: `Gate_*` and `EmittedMixedAttr_*` PASS; `Snapshot_*` FAILS once with "wrote …MixedAttr.approved.js; review + re-run".

> **LIVE-ADJUST 1 (gate shape):** if `Gate_*` fails on structure, read the canon diff. The most likely divergence is the composed term spacing (`'badge ' + statusClass.value + ' rounded'`) — confirm the generator's actual fold output and, if the compiler's faithful emission differs from the answer key, correct `samples/MixedAttr/mixedattr.js` to match the real output (not to force green). Re-run.

- [ ] **Step 8: Review + approve the snapshot**

Read `tests/Filament.Generator.Tests/Snapshots/MixedAttr.approved.js`. Confirm by eye: the class binding is `effect(() => setAttr(<el>, 'class', 'badge ' + statusClass.value + ' rounded'));`, imports include `signal, effect, batch, setText, setAttr, listen, insert`, two `\n\n` attach nodes, batched handler. Then re-run:

Run: `dotnet test tests/Filament.Generator.Tests --filter "FullyQualifiedName~MixedAttrTests" -v q`
Expected: all 3 PASS.

- [ ] **Step 9: Prove the pure case is unchanged + full suites + firewall**

Run:
```bash
dotnet test tests/Filament.Generator.Tests --filter "FullyQualifiedName~ReactiveAttrTests|FullyQualifiedName~BoolAttrTests" -v q
dotnet test tests/Filament.Generator.Tests -v q
cd src/filament-runtime && npm test; cd -
git diff --stat -- src/filament-runtime
```
Expected: **ReactiveAttrTests all green UNCHANGED** (the degenerate fold emits the pure `class="@x"` case byte-identically — this is the safety-net proof), BoolAttrTests green, all generator tests green (including `AttributeCodeValue`/`unaccounted` control-flow refusal and `Bind`), runtime green, **`git diff --stat -- src/filament-runtime` EMPTY**.

> **LIVE-ADJUST 2 (ReactiveAttr snapshot):** if `ReactiveAttrTests.Snapshot_*` fails, the generalised fold is NOT byte-identical for the pure case — STOP and fix the fold so `class="@x"` emits exactly `setAttr(_el, 'class', statusClass.value)` (no stray `'' + ` term). Do NOT re-approve the ReactiveAttr snapshot; the refactor must be transparent to it.

- [ ] **Step 10: Commit**

```bash
git add src/Filament.Generator/TemplateCompiler.cs \
        tests/Filament.Generator.Tests/MixedAttrTests.cs \
        tests/Filament.Generator.Tests/Snapshots/MixedAttr.approved.js
git commit -m "feat(mixed-attr): compose mixed literal+expression class value via prefix-aware fold"
```

---

## Task 3: Boundary — a mixed value on a non-allow-listed name stays refused

**Files:**
- Create: `tests/Filament.Generator.Tests/Unsupported/MixedNonAllowed.razor`
- Modify: `tests/Filament.Generator.Tests/DiagnosticTests.cs`

**Interfaces:**
- Consumes: the `Refused("<fixture>.razor")` helper.

- [ ] **Step 1: Create the fixture `tests/Filament.Generator.Tests/Unsupported/MixedNonAllowed.razor`**

A mixed value on `title` (NOT in `DynamicAttributes`), so composition must NOT apply (NO trailing `@using`):

```razor
<p id="box" title="pre @caption">hello</p>

@code {
    private string caption = "x";

    private void Touch()
    {
        caption = "y";
    }
}
```

- [ ] **Step 2: Write the failing boundary test**

In `tests/Filament.Generator.Tests/DiagnosticTests.cs`, after `NonAllowedBooleanAttribute_IsRefused_AtItsExactLocation`, add:

```csharp
    /// <summary>
    /// Composition is gated by the ALLOWLIST, not by the value shape: a mixed literal+expression value on
    /// a non-allow-listed name (here `title`) still refuses `[dynamic-attribute]` at its exact location.
    /// Only `class` composes; every other name keeps the refusal.
    /// </summary>
    [Fact]
    public void MixedValueOnNonAllowedAttribute_IsRefused_AtItsExactLocation()
    {
        var d = Refused("MixedNonAllowed.razor");

        Assert.Contains("MixedNonAllowed.razor(1,13): FIL0003: [dynamic-attribute]", d);
        Assert.Contains("class", d);    // the message names the string allowlist
        Assert.Contains("caption", d);  // and echoes the refused expression
    }
```

> **LIVE-ADJUST 3 (column):** the `(1,13)` column is the predicted start of `title` in `<p id="box" title=…`. The pure `DynamicTitle` fixture landed at `(1,12)`. Run the test, read the ACTUAL `MixedNonAllowed.razor(1,NN)` from the failure, and set the assertion to the real value.

- [ ] **Step 3: Run the boundary + regression diagnostics**

Run: `dotnet test tests/Filament.Generator.Tests --filter "FullyQualifiedName~DiagnosticTests" -v q`
Expected: `MixedValueOnNonAllowedAttribute_*` PASS (after any column adjust); all other diagnostics (`Bind`, `DynamicNonClassAttribute`, `NonAllowedBooleanAttribute`, the `AttributeCodeValue`/`unaccounted` control-flow test) PASS unchanged.

- [ ] **Step 4: Commit**

```bash
git add tests/Filament.Generator.Tests/Unsupported/MixedNonAllowed.razor tests/Filament.Generator.Tests/DiagnosticTests.cs
git commit -m "test(mixed-attr): mixed value on a non-allowlisted name stays refused (allowlist boundary)"
```

---

## Task 4: Measurement — DOM-contract oracle + BENCH n°15

**Files:**
- Modify: `bench/harness/bench.mjs` (`HARNESS_VERSION`, `APPS`, `verifyContract`)
- Modify: `bench/build-filament.sh` (case arms mirroring `filament-reactiveattr-gen`)
- Append: `BENCH.md`

- [ ] **Step 1: Bump HARNESS_VERSION**

In `bench/harness/bench.mjs`, change `'1.9.0'` to `'1.10.0'` and prepend the note:

```js
export const HARNESS_VERSION = '1.10.0';   // 1.10.0: 'mixedattr' contract (mixed literal+expression class value). 1.9.0: 'boolattr' (boolean disabled present/absent). 1.8.0: 'reactiveattr' (reactive class attribute). 1.7.0: 'boundcompose' (bound-parameter composition). 1.6.0: rootforeach/rootif. 1.5.0: compose. 1.4.0: divide.
```

- [ ] **Step 2: Add the APPS entry**

In `bench/harness/bench.mjs`, after the `boolattr` APPS entry (before the closing `};`), add:

```js
  // Correctness-only: verifyContract clicks #increment and asserts a COMPOSED `class` value on #status
  // ("badge @statusClass rounded") tracks state -- the whole string, literals surviving around the
  // reactive token -- against Blazor's own rendered DOM. The measurement of the mixed-`class` widening
  // (BENCH n°15).
  mixedattr: {
    readySelector: '#increment',
    observeSelector: '#status',
    scenarios: [],
  },
```

- [ ] **Step 3: Add the verifyContract clause**

In `bench/harness/bench.mjs`, after the `boolattr` clause (before `if (app === 'divide')`), add:

```js
    if (app === 'mixedattr') {
      return ctx.page.evaluate(() => {
        const out = { problems: [], observed: {} };
        for (const sel of ['#increment', '#status', '#counter-value']) {
          if (!document.querySelector(sel)) out.problems.push(`missing required element: ${sel}`);
        }
        if (out.problems.length) return out;

        const cls = () => document.querySelector('#status').getAttribute('class');
        const txt = () => document.querySelector('#counter-value').textContent.trim();
        out.observed.initialClass = cls();
        out.observed.initialText = txt();
        // Blazor's own initial render: the composed class is "badge zero rounded", count "0". If either
        // already read the post-click value the assertions below would be vacuous.
        if (out.observed.initialClass !== 'badge zero rounded') {
          out.problems.push(`#status class initial is "${out.observed.initialClass}", expected "badge zero rounded"`);
          return out;
        }
        if (out.observed.initialText !== '0') {
          out.problems.push(`#counter-value initial is "${out.observed.initialText}", expected "0"`);
          return out;
        }
        document.querySelector('#increment').click();
        out.observed.afterClass = cls();
        out.observed.afterText = txt();
        // THE MEASUREMENT: the mixed `class` value re-composes on state change -- the literals survive
        // around the reactive token, in order and spacing, against Blazor's OWN rendered DOM. A dropped
        // literal or mis-ordered prefix would show as a class other than "badge counting rounded".
        if (out.observed.afterClass !== 'badge counting rounded') {
          out.problems.push(`#status class after #increment is "${out.observed.afterClass}", expected "badge counting rounded"`);
        }
        if (out.observed.afterText !== '1') {
          out.problems.push(`#counter-value after #increment is "${out.observed.afterText}", expected "1"`);
        }
        return out;
      });
    }
```

- [ ] **Step 4: Add the build-filament.sh case arms**

In `bench/build-filament.sh`, add a `filament-mixedattr-gen` arm beside each existing `filament-boolattr-gen` arm (eight arms):

1. `ALL_LABELS`: add `filament-mixedattr-gen`.
2. `project_for`: `filament-mixedattr-gen) echo "samples/filament-mixedattr-gen" ;;`
3. `mode_for`: add `filament-mixedattr-gen` to the `production` alternation.
4. `razor_for`: `filament-mixedattr-gen) echo "$REPO_ROOT/baseline/MixedAttr.Blazor/App.razor" ;;`
5. `generated_js_for`: `filament-mixedattr-gen) echo "App.g.js" ;;`
6. `title_for`: `filament-mixedattr-gen) echo "MixedAttr" ;;`
7. `blazor_label_for`: `filament-mixedattr-gen) echo "blazor-mixedattr" ;;`
8. `css_for`: `filament-mixedattr-gen) echo "$REPO_ROOT/baseline/MixedAttr.Blazor/wwwroot/css/app.css" ;;`

- [ ] **Step 5: Build the Filament app + publish the Blazor baseline**

Run:
```bash
bash bench/build-filament.sh filament-mixedattr-gen
dotnet publish baseline/MixedAttr.Blazor/MixedAttr.Blazor.csproj -c Release -o bench/publish/blazor-mixedattr
```
Expected: `samples/filament-mixedattr-gen/App.g.js` emitted; the Blazor publish lands under `bench/publish/blazor-mixedattr/` (static root `bench/publish/blazor-mixedattr/wwwroot`).

- [ ] **Step 6: Run the oracle against BOTH builds**

Run:
```bash
node bench/harness/bench.mjs --dir bench/publish/filament-mixedattr-gen --app mixedattr --label filament-mixedattr-gen --contract-only --headless
node bench/harness/bench.mjs --dir bench/publish/blazor-mixedattr/wwwroot --app mixedattr --label blazor-mixedattr --contract-only --headless
```
Expected: BOTH report `DOM contract OK` with `{"initialClass":"badge zero rounded","initialText":"0","afterClass":"badge counting rounded","afterText":"1"}`. If either reports problems, STOP and investigate (do not adjust the answer key to hide a real divergence).

- [ ] **Step 7: Append BENCH n°15**

Append to `BENCH.md` an `Entrée n°15` (French, mirroring n°14): the mixed-`class` widening; `baseline/MixedAttr.Blazor` vs `filament-mixedattr-gen`; BOTH render `#status` class `badge zero rounded` → `badge counting rounded` and `#counter-value` `0 → 1` identically; correctness-only; the composition is a prefix-aware fold, the pure `@expr` case being the degenerate form (runtime UNCHANGED); `HARNESS_VERSION 1.9.0→1.10.0` disclosed. No prior figure invalidated.

- [ ] **Step 8: Commit**

```bash
git add bench/harness/bench.mjs bench/build-filament.sh BENCH.md
git commit -m "bench(mixed-attr): DOM-contract oracle + BENCH n°15 (mixed literal+expression class)"
```

---

## Task 5: Record — DECISIONS #96 + memory, then finish

**Files:**
- Append: `DECISIONS.md`
- Update memory: `mixed-class-attribute-widened.md` + `MEMORY.md` index (outside the repo — not committed)

- [ ] **Step 1: Append DECISIONS #96**

Append to `DECISIONS.md` a `#96` entry (French, house style, mirroring #95): the mixed-`class` widening enters §5. Key points: the `dynamic-attribute` refusal narrowed to a prefix-aware **composition fold** for the `class` allowlist; Razor gives the value as ordered parts each with a `Prefix`, folded into one `setAttr` concatenation (`'badge ' + statusClass.value + ' rounded'`); the pure `@expr` case is the DEGENERATE fold and emits byte-identically (the ReactiveAttr gate + snapshot are the proof — the branch was GENERALISED, not duplicated); ONE shared `ComposableValue` predicate for harvest (harvest each expression part) + emission (decision 53), `DynamicValue` retained for the boolean `disabled` path; the `unaccounted-attribute-value` control-flow refusal is UNTOUCHED (`ComposableValue` returns null for a `CSharpCodeAttributeValue`, distinct slice); reactive iff any expression part is reactive; runtime UNCHANGED (JS string concat over the existing `setAttr`); measured vs Blazor asserting the WHOLE `class` string (BENCH n°15, `HARNESS_VERSION` bump disclosed); N-part general, one-expression measured; control-flow / string-`disabled` / other-name sub-slices deferred. Test count updated.

- [ ] **Step 2: Commit DECISIONS**

```bash
git add DECISIONS.md
git commit -m "docs(mixed-attr): DECISIONS #96 — mixed literal+expression class value (measured widening)"
```

- [ ] **Step 3: Update memory**

Create `~/.claude/projects/-Users-phmatray-Repositories-dotnet-Filament/memory/mixed-class-attribute-widened.md` (type: project) capturing: mixed literal+expression `class` entered the measured subset (#96 / BENCH n°15); generator-only prefix-aware fold; pure `@expr` is the degenerate fold (ReactiveAttr gate proves the generalisation transparent); one shared `ComposableValue` predicate (harvest all expr parts + emission); `DynamicValue` kept for boolean; control-flow-in-attr still refused via `unaccounted`; measured vs Blazor on the whole class string; runtime UNCHANGED; deferred remainder (control-flow-in-attr, string-`disabled`, other names). Add a one-line index pointer to `MEMORY.md`. Link `[[reactive-class-attribute-widened]]` and `[[boolean-disabled-attribute-widened]]`. (Outside the repo — not committed.)

- [ ] **Step 4: Final verification + finish**

Run:
```bash
dotnet test Filament.sln -v q
cd src/filament-runtime && npm test; cd -
git diff --stat -- src/filament-runtime   # must be empty
git log --oneline -6
```
Expected: all .NET tests green (subset + analyzer + generator, with the new MixedAttr gate/behaviour/snapshot + boundary, and ReactiveAttr/BoolAttr unchanged), runtime green, runtime firewall clean.

Then invoke **superpowers:finishing-a-development-branch**. Environment is a normal repo on `main` with no remote: report the slice landed on `main`.

---

## Self-Review

**Spec coverage** (checked against `docs/superpowers/specs/2026-07-19-mixed-attribute-value-design.md`):
- §1 prefix-aware fold → Task 2 Step 3 (`ComposeAttributeValue`). ✓
- §2 generalised emission → Task 2 Step 4. ✓
- §3 shared `ComposableValue` + widened harvest + untouched `unaccounted` → Task 2 Steps 3, 5; Global Constraints. ✓
- §Scope refusal (mixed on non-`class`, control flow) → Task 2 Step 6 message; Task 3 boundary; Task 2 Step 9 regressions. ✓
- §Runtime unchanged → Global Constraints + Task 2 Step 9 (`git diff` empty). ✓
- §Measured app MixedAttr → Task 1 (app + answer key). ✓
- §Measurement oracle + BENCH n°15 (whole class string) → Task 4. ✓
- §Tests gate/behaviour/snapshot + ReactiveAttr/BoolAttr/control-flow regressions + boundary → Task 2 Steps 1, 9; Task 3. ✓
- §Decision record #96 → Task 5. ✓

**Placeholder scan:** no TBD/TODO; every code step shows actual code; three genuine environment-reads are flagged LIVE-ADJUST (gate shape, ReactiveAttr-snapshot safety net, boundary column).

**Type/name consistency:** `MixedAttrRazor`/`MixedAttrAnswerKey`/`MixedAttrToTemp()` consistent across Tasks 1–2; `ComposableValue` (returns `IReadOnlyList<IntermediateNode>?`) and `ComposeAttributeValue` (returns `(string js, bool reactive)`) used consistently in harvest + emission; `filament-mixedattr-gen`/`blazor-mixedattr`/`mixedattr` labels consistent across Tasks 1, 4. The emitted `'badge ' + statusClass.value + ' rounded'` matches the behaviour test's three `Assert.Contains` and the answer key.
