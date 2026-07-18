# Root-Level Control Flow (§5 widening, #77's third and last false positive) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Admit `@foreach` and `@if` at the template ROOT (not wrapped in an element) into the compiled §5 subset, mapping a root-level control-flow region onto `mount()`'s `target`, and MEASURE both against Blazor via the existing DOM-contract oracle.

**Architecture:** The refusal at `TemplateCompiler.cs:264` (`template-code-at-root`) exists only because `Collect()` keys regions by a *containing element*, and root C# has none. The fix is one structural change: when the method's children contain template C#, treat the METHOD ITSELF as the region container (`Collect(method, plan)`), then emit its ops with `container = "target"` instead of walking children individually. `list(parent, …)` (`list.ts:238`) already accepts any `Node` as `parent`, and `RegionOps` (`CSharpFrontEnd.cs:495`) already refuses any non-`@foreach`/`@if` statement with `unsupported-template-statement` — so the re-parse is its own guard and the mapping can be fully general. This is a **Razor-mapping widening (FIL0003), generator-only**: the analyzer works on C# syntax (FIL0001/FIL0002) and never sees templates, so there is NO `ConstructSubset`/analyzer seam here (unlike #87/#88). No new runtime primitive.

**Tech Stack:** C# Roslyn source generator (`Filament.Generator`), Blazor WASM baselines (net10.0), the Filament TS runtime, the Playwright/CDP DOM-contract oracle (`bench/harness/bench.mjs`), xUnit tests, the `tools/canon.mjs` alpha-equivalence checker.

## Global Constraints

- **Measure, don't reason (decisions 29/30).** No construct enters §5 on an argument alone; it enters because a Blazor baseline and the generated app render the SAME DOM through the ONE trusted oracle. This widening ships TWO measured apps (root `@foreach`, root `@if`).
- **Answer keys are never edited to make a gate pass (decisions 21/51).** But TRANSCRIBING the generator's faithful output INTO a new answer key is exactly the answer key's purpose. New keys (`rootforeach.js`, `rootif.js`) are transcribed from the generator's real emission, then frozen.
- **The 181/183/185 in-element gates are the parity net.** Counter/Rows/If/IfElse/Divide/Compose alpha-equivalence + snapshots must stay byte-identical after the core change — the root path must not perturb the in-element path.
- **Reverting a probe edit uses Edit, never `git checkout`** (checkout drops uncommitted neighbours).
- **`bench.mjs` edits bump `HARNESS_VERSION`** (decisions 31/43/59). This widening: `1.5.0 → 1.6.0`.
- **Commit only when a task's deliverable is green.** Trunk-based; no remote configured — commits ARE the deliverable. French house style for `DECISIONS.md` / `BENCH.md`.
- **Refuse loud and located.** Anything outside the measured slice (root code that is not `@foreach`/`@if`) must produce a located FIL0003, never a silent or garbage emission.

---

### Task 1: Generator — admit root control-flow regions, attach to `target`

**Files:**
- Modify: `src/Filament.Generator/TemplateCompiler.cs` (the root-code refusal at ~264; the top-level emit walk at ~296-300; remove `_refusedRootCode` field ~215 and its `EmitNode` case ~716)
- Modify: `tests/Filament.Generator.Tests/DiagnosticTests.cs` (reconcile `IfAtRoot_IsRefused_...` ~104; remove `IfAtRoot.razor` from `ARefusalWritesNoFile` theory ~487)
- Create: `tests/Filament.Generator.Tests/RootControlFlowTests.cs`
- Create fixture: `tests/Filament.Generator.Tests/Unsupported/RootCodeBlock.razor` (a bare `@{ }` at root — still refused)

**Interfaces:**
- Consumes: `CSharpFrontEnd.OpsFor(IntermediateNode container)` (returns `_ops[container]`; container may be the method node), `EmitOps(IReadOnlyList<TemplateOp>, string container)`, `Collect(IntermediateNode, TemplatePlan)`.
- Produces: root `@foreach` → `list(target, …, fn, null)`; root `@if` → comment anchor `insert(target, _ifN)` + `list(target, …, anchor)`. Non-control-flow root code → `unsupported-template-statement` (FIL0003). These emission shapes are what Tasks 2/3 gate against.

- [ ] **Step 1: Write the failing tests**

Create `tests/Filament.Generator.Tests/RootControlFlowTests.cs`:

```csharp
using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// #77's THIRD and last disclosed false positive: @foreach/@if at the template ROOT.
/// The mapping (decided by the old refusal itself) is "a root region attaches to mount()'s target".
/// These prove the emission shape; RootForeachTests/RootIfTests measure it against Blazor.
/// </summary>
public class RootControlFlowTests
{
    // A root @foreach compiles to list() whose PARENT is target (not a created element).
    [Fact]
    public void RootForeach_AttachesTheListToTarget()
    {
        var js = File.ReadAllText(Generate.ToTempFixture("RootForeachInline.razor"));
        Assert.Contains("list(target,", js);
        Assert.DoesNotContain("[template-code-at-root]", js);
    }

    // A root @if compiles to a comment anchor inserted INTO target and a conditional list(target, ...).
    [Fact]
    public void RootIf_AnchorsAndListsAgainstTarget()
    {
        var js = File.ReadAllText(Generate.ToTempFixture("RootIfInline.razor"));
        Assert.Contains("insert(target,", js);       // the anchor lands in target
        Assert.Contains("list(target,", js);          // the conditional attaches to target
        Assert.DoesNotContain("[template-code-at-root]", js);
    }

    // Root C# that is NOT control flow is STILL refused -- now by RegionOps' shared re-parse,
    // with a more specific message than the old blanket root-code guard.
    [Fact]
    public void RootBareCodeBlock_IsStillRefused_ButAsUnsupportedStatement()
    {
        var (exit, _, stderr) = Run.Generator(
            Path.Combine(RepoPaths.Unsupported, "RootCodeBlock.razor"),
            Path.Combine(Path.GetTempPath(), $"filament-gen-{Guid.NewGuid():N}.js"));
        Assert.NotEqual(0, exit);
        Assert.Contains("[unsupported-template-statement]", stderr);
        Assert.DoesNotContain("[template-code-at-root]", stderr);   // the old guard is gone
    }
}
```

Add two inline fixtures under `Unsupported/` (used here only as generator inputs; they COMPILE now, so they are not "unsupported" — but the dir is the test-fixture dir). Create `tests/Filament.Generator.Tests/Unsupported/RootForeachInline.razor`:

```razor
@foreach (Item item in items)
{
    <li @key="item.Id">@item.Label</li>
}

@code {
    record Item { public int Id { get; set; } public string Label { get; set; } = ""; }
    List<Item> items = new List<Item> { new Item { Id = 1, Label = "a" } };
}
```

Create `tests/Filament.Generator.Tests/Unsupported/RootIfInline.razor`:

```razor
@if (show)
{
    <span id="cond">visible</span>
}

@code { private bool show = true; }
```

Create `tests/Filament.Generator.Tests/Unsupported/RootCodeBlock.razor` (bare code block, must stay refused):

```razor
@{ int x = 5; }
<p id="p">@x</p>
```

Add the helper `Generate.ToTempFixture` to the `Generate` class in `tests/Filament.Generator.Tests/GateTests.cs` (next to `ToTemp`):

```csharp
    /// <summary>Emit a fixture from the Unsupported dir (some now COMPILE) to a temp file.</summary>
    public static string ToTempFixture(string fixture)
    {
        var outside = Path.Combine(Path.GetTempPath(), $"filament-gen-{Guid.NewGuid():N}.js");
        var (exit, _, stderr) = Run.Generator(Path.Combine(RepoPaths.Unsupported, fixture), outside);
        Assert.True(exit == 0, $"the generator refused to emit:\n{stderr}");
        return outside;
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Filament.Generator.Tests --filter RootControlFlowTests`
Expected: `RootForeach_AttachesTheListToTarget` and `RootIf_AnchorsAndListsAgainstTarget` FAIL (the fixtures are refused today with `[template-code-at-root]`); `RootBareCodeBlock_...` FAILS (today it refuses with `[template-code-at-root]`, not `[unsupported-template-statement]`).

- [ ] **Step 3: Make the root region form and emit to target**

In `TemplateCompiler.cs`, `PrepareComponent`, REPLACE the per-child collect + root-code refusal. Currently:

```csharp
        // --- gate 3: ALL the C#, in ONE compilation, BEFORE the walk (decision 54) ----
        var plan = new TemplatePlan();
        foreach (var child in method.Children) Collect(child, plan);

        // Root-level template C# is refused WITH A LOCATION (not a FIL-WIRING crash); its mapping
        // attaches to mount()'s target and no answer key covers it. See DiagnosticTests.
        foreach (var rootCode in method.Children.OfType<CSharpCodeIntermediateNode>())
        {
            _refusedRootCode.Add(rootCode);
            Diag("template-code-at-root",
                $"template C# ({Trunc(RawText(rootCode))}) sits at the component's ROOT rather than inside an " +
                ...
                rootCode.Source);
        }
```

Replace the whole block with:

```csharp
        // --- gate 3: ALL the C#, in ONE compilation, BEFORE the walk (decision 54) ----
        var plan = new TemplatePlan();

        // ROOT-LEVEL CONTROL FLOW (decision 89, #77's third false positive). Collect() keys a
        // region by its CONTAINING element; root @foreach/@if have none. So when the root holds
        // template C#, the METHOD ITSELF is the region container -- its ops emit against target,
        // the mount point, exactly as an in-element region emits against its created element.
        // RegionOps refuses any statement that is not @foreach/@if (unsupported-template-statement),
        // so the re-parse is its own guard: no root construct is admitted that it does not map.
        if (method.Children.Any(c => c is CSharpCodeIntermediateNode))
            Collect(method, plan);
        else
            foreach (var child in method.Children) Collect(child, plan);
```

In `Compile()`, the top-level emit walk currently:

```csharp
        // --- the template -----------------------------------------------------
        foreach (var child in method.Children)
        {
            var v = EmitNode(child, parent: null);
            if (v is not null) _attach.Add($"insert(target, {v});");
        }
```

Replace with:

```csharp
        // --- the template -----------------------------------------------------
        // A root region (decision 89) emits as ONE unit against target: EmitOps lays down its
        // markup/list/anchor ops in source order, the same shape an in-element region emits.
        if (_regions.Contains(method))
        {
            EmitOps(_code.OpsFor(method), "target");
        }
        else
        {
            foreach (var child in method.Children)
            {
                var v = EmitNode(child, parent: null);
                if (v is not null) _attach.Add($"insert(target, {v});");
            }
        }
```

Remove the now-dead `_refusedRootCode` field (`readonly HashSet<IntermediateNode> _refusedRootCode = [];`, ~line 215) and its `EmitNode` case:

```csharp
            case CSharpCodeIntermediateNode code when _refusedRootCode.Contains(code):
                return null;
```

(Read the exact surrounding lines first; if a general `CSharpCodeIntermediateNode` case remains needed as a FIL-WIRING guard for un-regioned root code, keep it but drop the `_refusedRootCode` predicate — a root code node reached in the walk when NOT part of a region is now genuine tool drift.)

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Filament.Generator.Tests --filter RootControlFlowTests`
Expected: all three PASS.

- [ ] **Step 5: Reconcile the two now-stale refusal tests**

`IfAtRoot.razor` (root `@if` with valid `@code`) COMPILES now. In `DiagnosticTests.cs`, replace `IfAtRoot_IsRefused_ByTheRootCodeGuard_AtItsExactLocation` with a positive assertion:

```csharp
    /// <summary>
    /// @if AT THE TEMPLATE ROOT now COMPILES (decision 89, #77's third false positive closed): a
    /// root region maps onto mount()'s target. This fixture used to be refused [template-code-at-root];
    /// the guard is gone, replaced by the container=target mapping. Kept as a live regression witness.
    /// </summary>
    [Fact]
    public void IfAtRoot_NowCompiles_ToAConditionalAgainstTarget()
    {
        var outPath = Path.Combine(Path.GetTempPath(), $"filament-gen-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Compile(Path.Combine(RepoPaths.Unsupported, "IfAtRoot.razor"), outPath);
            Assert.True(exit == 0, $"root @if should compile now:\n{stderr}");
            var js = File.ReadAllText(outPath);
            Assert.Contains("list(target,", js);
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }
```

Remove `[InlineData("IfAtRoot.razor")]` from the `ARefusalWritesNoFile` theory (~line 487) and its comment reference. Verify `Foreach.razor` (in-element, refused for undeclared `items`) is UNTOUCHED — it is not a root fixture.

- [ ] **Step 6: Run the whole generator suite; confirm the in-element gates are byte-identical**

Run: `dotnet test tests/Filament.Generator.Tests`
Expected: PASS, including the 185 pre-existing gates/snapshots (Counter/Rows/If/IfElse/Divide/Compose alpha-equivalence + `.approved.js` snapshots). If any in-element snapshot reddens, the root change perturbed the in-element path — STOP and diagnose; the root branch must be reached ONLY when the method holds root C#.

- [ ] **Step 7: Commit**

```bash
git add src/Filament.Generator/TemplateCompiler.cs tests/Filament.Generator.Tests/RootControlFlowTests.cs tests/Filament.Generator.Tests/DiagnosticTests.cs tests/Filament.Generator.Tests/GateTests.cs tests/Filament.Generator.Tests/Unsupported/RootForeachInline.razor tests/Filament.Generator.Tests/Unsupported/RootIfInline.razor tests/Filament.Generator.Tests/Unsupported/RootCodeBlock.razor
git commit -m "feat(@else): root-level @foreach/@if map onto mount() target (#77 fp3)"
```

---

### Task 2: Root-`@foreach` measured app + alpha-equivalence gate

**Files:**
- Create: `baseline/RootForeach.Blazor/{App.razor, RootForeach.Blazor.csproj, Program.cs, _Imports.razor, wwwroot/index.html, wwwroot/css/app.css}`
- Create: `samples/RootForeach/rootforeach.js` (answer key — TRANSCRIBED from generator output)
- Create: `tests/Filament.Generator.Tests/Snapshots/RootForeach.approved.js`
- Modify: `tests/Filament.Generator.Tests/RepoPaths.cs`, `tests/Filament.Generator.Tests/GateTests.cs` (`Generate`)
- Create: `tests/Filament.Generator.Tests/RootForeachTests.cs`

**Interfaces:**
- Consumes: `Generate.ToTemp(razor, sampleDir)`, `RepoPaths.Canon`, `Run.Node`.
- Produces: `RepoPaths.RootForeachRazor`, `RepoPaths.RootForeachAnswerKey`, `Generate.RootForeachToTemp()`.

- [ ] **Step 1: Author the Blazor baseline app**

Create `baseline/RootForeach.Blazor/App.razor` (root `@foreach`, no wrapping element; a static keyed list — the initial composed DOM IS the measurement, like Compose):

```razor
@* Root component: its ENTIRE body is a root-level @foreach with no wrapping element,
   so the list reconciles directly into #app (mount()'s target). This is #77's third
   false positive: the mapping "a root region attaches to target" (decision 89).

   Static list, three keyed rows -- no interaction. The initial rendered DOM (three
   <li> in document order) is the shared contract the oracle asserts against Blazor. *@

@foreach (Item item in items)
{
    <li @key="item.Id" class="item">@item.Label</li>
}

@code {
    record Item { public int Id { get; set; } public string Label { get; set; } = ""; }

    List<Item> items = new List<Item>
    {
        new Item { Id = 1, Label = "alpha" },
        new Item { Id = 2, Label = "beta" },
        new Item { Id = 3, Label = "gamma" },
    };
}
```

Create `baseline/RootForeach.Blazor/RootForeach.Blazor.csproj` (clone of `Divide.Blazor.csproj`, name swapped):

```xml
<Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <OverrideHtmlAssetPlaceholders>true</OverrideHtmlAssetPlaceholders>

    <!-- Identical size-affecting PropertyGroup to Counter.Blazor by design (see its csproj). -->
    <InvariantGlobalization>true</InvariantGlobalization>
    <PublishTrimmed>true</PublishTrimmed>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly" Version="10.0.9" />
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly.DevServer" Version="10.0.9" PrivateAssets="all" />
  </ItemGroup>

</Project>
```

Create `baseline/RootForeach.Blazor/Program.cs`:

```csharp
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using RootForeach.Blazor;

// Same minimal host as Counter.Blazor: one screen, no Router/HeadOutlet/HttpClient.
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

await builder.Build().RunAsync();
```

Create `baseline/RootForeach.Blazor/_Imports.razor`:

```razor
@* The Web namespace is not strictly needed (no events) but kept for parity with the other baselines. *@
@using Microsoft.AspNetCore.Components.Web
```

Create `baseline/RootForeach.Blazor/wwwroot/index.html` (clone of Divide's, `<title>` swapped to `RootForeach`) and `baseline/RootForeach.Blazor/wwwroot/css/app.css` (byte-identical to `baseline/Divide.Blazor/wwwroot/css/app.css`).

```bash
mkdir -p baseline/RootForeach.Blazor/wwwroot/css
cp baseline/Divide.Blazor/wwwroot/css/app.css baseline/RootForeach.Blazor/wwwroot/css/app.css
```

Then create `wwwroot/index.html` from Divide's with `<title>Divide</title>` → `<title>RootForeach</title>`.

- [ ] **Step 2: Verify the app compiles under the generator, and transcribe the answer key**

Run the generator against the app to see the real emission:

```bash
dotnet run --project src/Filament.Generator.Cli -- baseline/RootForeach.Blazor/App.razor /tmp/rootforeach.g.js 2>&1; cat /tmp/rootforeach.g.js
```

(Use whatever CLI invocation `Run.Generator` uses — inspect `tests/.../Run.cs` if the project path differs.) Confirm exit 0 and that the module contains `list(target, …)`. If it REFUSES, fix `App.razor` to stay in subset (the refusal is located) — do NOT weaken the generator to accept it.

Create `samples/RootForeach/rootforeach.js` by TRANSCRIBING that emitted module VERBATIM (decisions 21/51 — the transcription IS the key's purpose), wrapped with the same header-comment discipline as `samples/If/if.js`: a docstring stating the Blazor DOM contract (three `<li>` rendered into the mount point, no wrapper), then the `import … from '../../src/filament-runtime/src/index.ts';` line and the `export function mount(target) { … }` body. Keep the import path relative to `samples/RootForeach/`.

- [ ] **Step 3: Wire RepoPaths + Generate**

In `RepoPaths.cs`, after the Compose entries:

```csharp
    /// <summary>Root-level @foreach — the file Blazor compiles (no Filament stand-in; no drift, like Rows).</summary>
    public static string RootForeachRazor => Path.Combine(Root, "baseline", "RootForeach.Blazor", "App.razor");

    /// <summary>The root-@foreach SPEC (decisions 21/51). Never edited to make a gate pass.</summary>
    public static string RootForeachAnswerKey => Path.Combine(Root, "samples", "RootForeach", "rootforeach.js");
```

In `GateTests.cs`, `Generate` class:

```csharp
    public static string RootForeachToTemp() => ToTemp(RepoPaths.RootForeachRazor, "RootForeach");
```

- [ ] **Step 4: Write the gate test**

Create `tests/Filament.Generator.Tests/RootForeachTests.cs` (mirror `DivideTests.cs`):

```csharp
using Xunit;

namespace Filament.Generator.Tests;

public class RootForeachTests
{
    /// <summary>THE GATE (spec 6 / decisions 21/51): the module emitted from
    /// baseline/RootForeach.Blazor/App.razor is alpha-equivalent to samples/RootForeach/rootforeach.js.
    /// The key's Blazor-faithfulness is what the DOM-contract oracle measures (BENCH n°11).</summary>
    [Fact]
    public void Gate_GeneratedRootForeach_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.RootForeachToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.RootForeachAnswerKey);
        Assert.True(exit == 0,
            "root-@foreach gate FAILED. Generated module is NOT alpha-equivalent to samples/RootForeach/rootforeach.js.\n"
            + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>The root list attaches to target, not to a created wrapper element.</summary>
    [Fact]
    public void EmittedRootForeach_ListsAgainstTarget()
    {
        var js = File.ReadAllText(Generate.RootForeachToTemp());
        Assert.Contains("list(target,", js);
        Assert.DoesNotContain("[template-code-at-root]", js);
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions.</summary>
    [Fact]
    public void Snapshot_EmittedRootForeachJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.RootForeachToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "RootForeach.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
```

- [ ] **Step 5: Run the gate; let the snapshot self-seed; then re-run green**

Run: `dotnet test tests/Filament.Generator.Tests --filter RootForeachTests`
Expected: `Snapshot_...` fails on first run (writes `RootForeach.approved.js`), then re-run. `Gate_...` PASSES (proves rootforeach.js is a faithful transcription). If the gate FAILS, the answer key was mis-transcribed — fix the KEY to match the generator, never the reverse.

Run again: `dotnet test tests/Filament.Generator.Tests --filter RootForeachTests` → all PASS.

- [ ] **Step 6: Commit**

```bash
git add baseline/RootForeach.Blazor samples/RootForeach tests/Filament.Generator.Tests/RepoPaths.cs tests/Filament.Generator.Tests/GateTests.cs tests/Filament.Generator.Tests/RootForeachTests.cs tests/Filament.Generator.Tests/Snapshots/RootForeach.approved.js
git commit -m "test(@else): root-@foreach Blazor baseline + alpha-equivalence gate"
```

---

### Task 3: Root-`@if` measured app + alpha-equivalence gate

**Files:** mirror Task 2 for `RootIf`:
- Create: `baseline/RootIf.Blazor/{App.razor, RootIf.Blazor.csproj, Program.cs, _Imports.razor, wwwroot/index.html, wwwroot/css/app.css}`
- Create: `samples/RootIf/rootif.js`
- Create: `tests/Filament.Generator.Tests/Snapshots/RootIf.approved.js`
- Modify: `RepoPaths.cs`, `GateTests.cs`
- Create: `tests/Filament.Generator.Tests/RootIfTests.cs`

**Interfaces:** Produces `RepoPaths.RootIfRazor`, `RepoPaths.RootIfAnswerKey`, `Generate.RootIfToTemp()`.

- [ ] **Step 1: Author the Blazor baseline app**

Create `baseline/RootIf.Blazor/App.razor` (a root `@if` with a sibling toggle button — exercises BOTH branches, mount AND unmount, directly into `#app`; `@onclick` on a root markup sibling is admitted, `RefuseNestedCode` only rejects nested C#):

```razor
@* Root component: a toggle button and a root-level @if, no wrapping element. The
   conditional's mount/unmount happens directly against #app (mount()'s target) --
   #77's third false positive (decision 89). The button sibling is emitted in source
   order before the conditional's comment anchor.

   No newline/indent between the button and @if, matching If.razor's contract: Razor
   only turns SOURCE whitespace between siblings into text nodes, and there is none. *@

<button id="toggle" @onclick="Toggle">toggle</button>@if (show)
{
    <span id="cond">visible</span>
}

@code {
    private bool show = true;
    void Toggle() { show = !show; }
}
```

Create the csproj (clone, name `RootIf.Blazor`), `Program.cs` (`using RootIf.Blazor;`), `_Imports.razor` (Web namespace — needed here, there is an `@onclick`), `wwwroot/index.html` (`<title>RootIf</title>`), and `wwwroot/css/app.css`:

```bash
mkdir -p baseline/RootIf.Blazor/wwwroot/css
cp baseline/Divide.Blazor/wwwroot/css/app.css baseline/RootIf.Blazor/wwwroot/css/app.css
```

- [ ] **Step 2: Compile + transcribe the answer key**

Run the generator on `baseline/RootIf.Blazor/App.razor`; confirm exit 0 and the module contains `insert(target, …)` (anchor) + `list(target, …)` (conditional) + a `listen(…, 'click', …)` for the toggle. Transcribe VERBATIM into `samples/RootIf/rootif.js`, headered like `samples/If/if.js` (state the Blazor DOM contract: a `<button id="toggle">`, a comment anchor, and `<span id="cond">visible</span>` present iff `show`; note the +1 comment-node divergence from Blazor exactly as `if.js` does).

- [ ] **Step 3: Wire RepoPaths + Generate**

In `RepoPaths.cs`:

```csharp
    /// <summary>Root-level @if (with a sibling toggle) — the file Blazor compiles.</summary>
    public static string RootIfRazor => Path.Combine(Root, "baseline", "RootIf.Blazor", "App.razor");

    /// <summary>The root-@if SPEC (decisions 21/51). Never edited to make a gate pass.</summary>
    public static string RootIfAnswerKey => Path.Combine(Root, "samples", "RootIf", "rootif.js");
```

In `GateTests.cs`, `Generate`:

```csharp
    public static string RootIfToTemp() => ToTemp(RepoPaths.RootIfRazor, "RootIf");
```

- [ ] **Step 4: Write the gate test**

Create `tests/Filament.Generator.Tests/RootIfTests.cs` (mirror `RootForeachTests`, with `RootIf`/`rootif.js`/`RootIf.approved.js`; the emission assertion checks both `insert(target,` and `list(target,`).

- [ ] **Step 5: Run, self-seed snapshot, re-run green**

Run: `dotnet test tests/Filament.Generator.Tests --filter RootIfTests` (twice — first seeds the snapshot). Expected: all PASS; gate confirms rootif.js is a faithful transcription.

- [ ] **Step 6: Full generator suite green**

Run: `dotnet test tests/Filament.Generator.Tests`
Expected: PASS (now includes RootForeach + RootIf gates; the 185 in-element gates still byte-identical).

- [ ] **Step 7: Commit**

```bash
git add baseline/RootIf.Blazor samples/RootIf tests/Filament.Generator.Tests/RepoPaths.cs tests/Filament.Generator.Tests/GateTests.cs tests/Filament.Generator.Tests/RootIfTests.cs tests/Filament.Generator.Tests/Snapshots/RootIf.approved.js
git commit -m "test(@else): root-@if Blazor baseline (toggle) + alpha-equivalence gate"
```

---

### Task 4: Measurement wiring — oracle contracts + build labels for both apps

**Files:**
- Modify: `bench/harness/bench.mjs` (`HARNESS_VERSION`, `APPS`, `verifyContract`)
- Modify: `bench/build-filament.sh` (`ALL_LABELS` + all 7 dispatchers)
- Create: `samples/filament-rootforeach-gen/main.js`, `samples/filament-rootif-gen/main.js`
- Modify: `.gitignore`

**Interfaces:** Produces two new bench labels (`filament-rootforeach-gen`, `filament-rootif-gen`) and two Blazor labels (`blazor-rootforeach`, `blazor-rootif`) resolvable by `build-filament.sh` and drivable by `bench.mjs --app rootforeach|rootif --contract-only`.

- [ ] **Step 1: Bump HARNESS_VERSION**

In `bench/harness/bench.mjs` (~line 72):

```javascript
export const HARNESS_VERSION = '1.6.0';   // 1.6.0: 'rootforeach'/'rootif' contracts (root control flow). 1.5.0: 'compose'. 1.4.0: 'divide'.
```

- [ ] **Step 2: Add the two APPS entries**

In the `APPS` object (~after the `compose` entry, ~line 316):

```javascript
  // Correctness-only: verifyContract asserts a root @foreach reconciles three <li> INTO #app
  // (mount target), no wrapper. Root control flow, decision 89.
  rootforeach: {
    readySelector: '#app li',
    observeSelector: '#app',
    scenarios: [],
  },
  // Correctness-only: verifyContract drives #toggle and asserts a root @if mounts/unmounts
  // #cond directly on #app. Root control flow, decision 89.
  rootif: {
    readySelector: '#toggle',
    observeSelector: '#app',
    scenarios: [],
  },
```

- [ ] **Step 3: Add the two verifyContract branches**

In `verifyContract` (after the `compose` branch, ~line 1550):

```javascript
    if (app === 'rootforeach') {
      return ctx.page.evaluate(() => {
        const out = { problems: [], observed: {} };
        const texts = [...document.querySelectorAll('#app li')].map(e => e.textContent.trim());
        out.observed.items = texts;
        // Root @foreach reconciles three keyed rows directly into #app (no wrapper element).
        // A generator that attached the list to a created element instead of target, or dropped
        // a row, renders a different set -- caught here against Blazor's own rendered list.
        const expected = ['alpha', 'beta', 'gamma'];
        if (texts.length !== expected.length || texts.some((t, i) => t !== expected[i]))
          out.problems.push(`#app li texts are ${JSON.stringify(texts)}, expected ${JSON.stringify(expected)}`);
        return out;
      });
    }

    if (app === 'rootif') {
      return ctx.page.evaluate(() => {
        const out = { problems: [], observed: {} };
        if (!document.querySelector('#toggle')) { out.problems.push('missing required element: #toggle'); return out; }
        const present = () => !!document.querySelector('#cond');
        out.observed.initial = present();
        // show starts true: the branch renders directly into #app.
        if (!present()) { out.problems.push('#cond absent initially (show=true), expected present'); return out; }
        document.querySelector('#toggle').click();
        out.observed.afterToggle = present();
        // The root @if unmounts its branch from #app. Unconditional markup would still be here.
        if (present()) { out.problems.push('#cond still present after toggle (show=false), expected absent'); return out; }
        document.querySelector('#toggle').click();
        out.observed.afterSecondToggle = present();
        if (!present()) out.problems.push('#cond absent after second toggle (show=true), expected present');
        return out;
      });
    }
```

- [ ] **Step 4: Add build labels + dispatchers**

In `bench/build-filament.sh`: add `filament-rootforeach-gen` and `filament-rootif-gen` to `ALL_LABELS` (~line 176, after `filament-compose-gen`), and add a case to EACH of the 7 dispatchers (`project_for`, `mode_for`, `razor_for`, `generated_js_for`, `title_for`, `blazor_label_for`, `css_for`), following the `filament-divide-gen`/`filament-compose-gen` pattern exactly:

```sh
# ALL_LABELS
  filament-rootforeach-gen
  filament-rootif-gen

# project_for
    filament-rootforeach-gen)                         echo "samples/filament-rootforeach-gen" ;;
    filament-rootif-gen)                              echo "samples/filament-rootif-gen" ;;

# mode_for  (add both to the production alternation)
    ...|filament-divide-gen|filament-compose-gen|filament-rootforeach-gen|filament-rootif-gen) echo "production" ;;

# razor_for
    filament-rootforeach-gen)                         echo "$REPO_ROOT/baseline/RootForeach.Blazor/App.razor" ;;
    filament-rootif-gen)                              echo "$REPO_ROOT/baseline/RootIf.Blazor/App.razor" ;;

# generated_js_for   (App.g.js: the razor file is App.razor for both)
    filament-rootforeach-gen)                         echo "App.g.js" ;;
    filament-rootif-gen)                              echo "App.g.js" ;;

# title_for
    filament-rootforeach-gen)                         echo "RootForeach" ;;
    filament-rootif-gen)                              echo "RootIf" ;;

# blazor_label_for
    filament-rootforeach-gen)                         echo "blazor-rootforeach" ;;
    filament-rootif-gen)                              echo "blazor-rootif" ;;

# css_for
    filament-rootforeach-gen)                         echo "$REPO_ROOT/baseline/RootForeach.Blazor/wwwroot/css/app.css" ;;
    filament-rootif-gen)                              echo "$REPO_ROOT/baseline/RootIf.Blazor/wwwroot/css/app.css" ;;
```

- [ ] **Step 5: Create the -gen entry points**

Create `samples/filament-rootforeach-gen/main.js` (clone of `samples/filament-compose-gen/main.js`, importing `./App.g.js`, docstring naming the rootforeach contract — three `<li>` into `#app`). Create `samples/filament-rootif-gen/main.js` (same, naming the rootif contract — `#cond` toggled on `#app`).

- [ ] **Step 6: gitignore the generated modules**

In `.gitignore`, after the `samples/filament-compose-gen/App.g.js` line:

```
samples/filament-rootforeach-gen/App.g.js
samples/filament-rootif-gen/App.g.js
```

- [ ] **Step 7: Sanity-build both -gen labels (no browser yet)**

Run: `bash bench/build-filament.sh filament-rootforeach-gen filament-rootif-gen` (or the repo's invocation). Expected: both emit `App.g.js` from their App.razor with exit 0 and the CSS copy check passes. If a dispatcher case is missed, the build errors on that label — fix and re-run.

- [ ] **Step 8: Commit**

```bash
git add bench/harness/bench.mjs bench/build-filament.sh samples/filament-rootforeach-gen/main.js samples/filament-rootif-gen/main.js .gitignore
git commit -m "test(@else): DOM-contract oracle + build labels for root control flow (HARNESS 1.6.0)"
```

---

### Task 5: Run the two measurements against Blazor, record BENCH n°11

**Files:**
- Modify: `BENCH.md` (append entry n°11)
- (Regenerated, gitignored: `bench/publish/`, `App.g.js`)

**Interfaces:** none produced; this task produces measured EVIDENCE (`bench/results/*` JSON if the oracle writes it, plus the BENCH entry).

- [ ] **Step 1: Publish the two Blazor baselines**

Run the repo's baseline publish (e.g. `bash bench/publish-baseline.sh blazor-rootforeach blazor-rootif`, or the script's actual name/args — inspect `bench/` first). Expected: `bench/publish/blazor-rootforeach/` and `bench/publish/blazor-rootif/` produced with a WASM bundle each. This step needs a working `dotnet publish` + WASM toolchain (verified available in-session for #87/#88).

- [ ] **Step 2: Build the two filament -gen labels**

Already sanity-built in Task 4 Step 7; rebuild if stale: `bash bench/build-filament.sh filament-rootforeach-gen filament-rootif-gen`.

- [ ] **Step 3: Run the oracle, --contract-only, for both pairs**

For each of the four apps (blazor-rootforeach, filament-rootforeach-gen, blazor-rootif, filament-rootif-gen), serve it and run `node bench/harness/bench.mjs --app rootforeach|rootif --contract-only --url <served-url>` (match the exact invocation #87/#88 used — inspect a prior run command in `bench/` or `BENCH.md` entry n°9/n°10's "Commande pour rejouer"). Expected: `[bench] --contract-only: contract met` for ALL FOUR. A Filament app that fails the contract means the generated mapping diverges from Blazor — STOP, do not paper over it.

- [ ] **Step 4: Record the measurement**

Append `BENCH.md` entry n°11 (French house style, matching n°9/n°10 structure: Environnement / Protocole / Commande pour rejouer / Résultat / Ce que cette entrée N'établit PAS). State: both root-`@foreach` apps render `#app > li` = `[alpha, beta, gamma]`; both root-`@if` apps mount/unmount `#cond` on `#app` across the toggle; `HARNESS_VERSION` 1.5.0 → 1.6.0 disclosed (new `rootforeach`/`rootif` branches). Note this closes #77's THIRD and last false positive. End with the append-only marker: `*Fin de l'entrée n°11. Ne pas modifier — ajouter une entrée n°12 pour toute rectification.*`

- [ ] **Step 5: Commit**

```bash
git add BENCH.md bench/results 2>/dev/null; git add BENCH.md
git commit -m "test(@else): measure root @foreach/@if vs Blazor via the oracle (BENCH n°11)"
```

---

### Task 6: DECISIONS #89, memory, final verification

**Files:**
- Modify: `DECISIONS.md` (append #89)
- Modify: `~/.claude/projects/-Users-phmatray-Repositories-dotnet-Filament/memory/double-division-widened-subset.md` + `MEMORY.md`

- [ ] **Step 1: Write DECISIONS #89**

Append `DECISIONS.md` section `## 89.` (French house style, mirroring #87/#88). Cover: (a) the mapping — a root region's container IS `target`, realized by `Collect(method)` + `EmitOps(OpsFor(method), "target")`; (b) it is GENERAL (any root control flow), because `RegionOps` refuses non-`@foreach`/`@if` statements (`unsupported-template-statement`) — the re-parse is its own guard, no artificial "pure control flow" gate; (c) this is a **FIL0003 Razor-mapping widening, generator-only** — no `ConstructSubset`/analyzer seam (the analyzer never sees templates), unlike #87/#88; (d) the old `template-code-at-root` self-limiting refusal is GONE, replaced by the mapping, and its still-refused residue now carries the more specific `unsupported-template-statement`; (e) MEASURED twice (BENCH n°11): root list into target, root conditional mount/unmount on target; (f) #77's THIRD and last false positive is now CLOSED — all three closed or deferred-with-reason; RADICAL still "ni éliminée ni établie", subset width +1 notch.

- [ ] **Step 2: Update memory**

Update `memory/double-division-widened-subset.md`: add a `## #89 — root-level control flow` section; note all three #77 false positives are now closed. Update the `MEMORY.md` index line's hook accordingly. Follow the memory-file rules (frontmatter preserved, `[[links]]` to related memories).

- [ ] **Step 3: Final verification — all suites, runtime byte-unchanged, clean tree**

```bash
dotnet test tests/Filament.Generator.Tests
dotnet test tests/Filament.Subset.Tests 2>/dev/null || true
dotnet test tests/Filament.Analyzer.Tests 2>/dev/null || true   # expect UNCHANGED count — no analyzer change
git diff --stat -- src/filament-runtime                          # expect EMPTY (no runtime change)
git status --porcelain                                           # expect clean (only gitignored build output)
```

Expected: generator suite green with the two new gates + RootControlFlowTests; subset/analyzer counts UNCHANGED (this widening touched neither); runtime diff EMPTY; working tree clean.

- [ ] **Step 4: Commit**

```bash
git add DECISIONS.md
git commit -m "docs(@else): record decision #89 (root control flow closes #77's last false positive)"
```

---

## Self-Review

**Spec coverage:** Core mapping (Task 1) → both constructs measured (Tasks 2/3 gates + Task 5 DOM oracle) → wiring (Task 4) → journal (Tasks 5/6). The user's chosen slice (both `@foreach` AND `@if`, two measurements) is covered by two apps + two verifyContract branches + BENCH n°11.

**Placeholder scan:** Answer-key BYTES (rootforeach.js/rootif.js) and snapshot bytes are intentionally produced by transcribing the generator's real output (decisions 21/51) — the METHOD is concrete (Task 2 Step 2, Task 3 Step 2), only the exact bytes await the generator, exactly as divide.js/compose.js were made. The `Run.Generator`/publish/serve invocations reference "inspect the repo's actual script" because those exact commands live in `bench/` scripts and prior BENCH entries; the plan names what to look for.

**Type/name consistency:** `Generate.RootForeachToTemp`/`RootIfToTemp`, `RepoPaths.RootForeach*`/`RootIf*`, labels `filament-rootforeach-gen`/`filament-rootif-gen`, `blazor-rootforeach`/`blazor-rootif`, `App.g.js` for both, apps `rootforeach`/`rootif`, selectors `#app li`/`#toggle`/`#cond` — used consistently across Tasks 2–5.
