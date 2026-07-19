# Multi-node `@if` body — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Admit a single-branch `@if` (no `@else`) with a multi-node body into the compiled subset, lowered to a conditional `list()` with one item per body node, measured identical to Blazor.

**Architecture:** Generalize the plain-`@if` lowering from one-node-per-branch to one-list-item-per-body-node. `IfBranch.Body` becomes a node list; `BranchBody` gains an `allowMulti` flag lifted only for a branch-less `@if`; `EmitIf` keeps #81/#82 byte-identical and adds the multi-node path. No runtime change.

**Tech Stack:** C# generator (`Filament.Generator`), Razor/Roslyn front end, TS runtime (unchanged), xUnit, `tools/canon.mjs`, Playwright oracle (`bench/`), Blazor WASM baseline.

## Global Constraints

- **Runtime firewall:** `git diff --stat src/filament-runtime` MUST stay empty for every commit. This is generator-only.
- **Byte-preservation:** the single-node `@if` (#81) and multi-branch `@if/@else` (#82) emissions stay byte-identical — `IfTests`, `IfElseTests`, `RootIfTests`, `RootForeachTests` snapshots unchanged.
- **Boundary intact:** `Unsupported/IfElseMultiBody.razor` and `Unsupported/IfNested.razor` stay refused `[unsupported-if-body]` at their exact locations `(6,1)` and `(2,1)`. `Foreach.razor` stays refused `[unsupported-foreach]`.
- **Measured, never reasoned:** the answer key's DOM contract is read from Blazor's own `BuildRenderTree`; the oracle asserts both builds identically.
- **French house style** for `DECISIONS.md` (append #98) and `BENCH.md` (append n°17). `HARNESS_VERSION` bump disclosed.
- Commit directly to `main` (trunk-based, no remote).

---

### Task 1: Baseline Blazor app + Blazor-validity gate

**Files:**
- Create: `baseline/IfMultiBody.Blazor/IfMultiBody.Blazor.csproj`
- Create: `baseline/IfMultiBody.Blazor/Program.cs`
- Create: `baseline/IfMultiBody.Blazor/_Imports.razor`
- Create: `baseline/IfMultiBody.Blazor/App.razor`
- Create: `baseline/IfMultiBody.Blazor/wwwroot/index.html`
- Create: `baseline/IfMultiBody.Blazor/wwwroot/css/app.css` (copy of RootIf's)

**Interfaces:**
- Produces: `baseline/IfMultiBody.Blazor/App.razor` — the single compiled source for the canon gate, snapshot, and oracle (`RepoPaths.IfMultiBodyRazor` points here in Task 2).

- [ ] **Step 1: Create the csproj** (mirror `RootIf.Blazor.csproj`, namespace changed)

`baseline/IfMultiBody.Blazor/IfMultiBody.Blazor.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <OverrideHtmlAssetPlaceholders>true</OverrideHtmlAssetPlaceholders>

    <!-- Identical size-affecting PropertyGroup to Counter.Blazor by design, so the
         baselines stay comparable. See Counter.Blazor.csproj for the full rationale. -->
    <InvariantGlobalization>true</InvariantGlobalization>
    <PublishTrimmed>true</PublishTrimmed>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly" Version="10.0.9" />
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly.DevServer" Version="10.0.9" PrivateAssets="all" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create Program.cs**

`baseline/IfMultiBody.Blazor/Program.cs`:
```csharp
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using IfMultiBody.Blazor;

// Same minimal host as Counter.Blazor: one screen, no Router/HeadOutlet/HttpClient.
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

await builder.Build().RunAsync();
```

- [ ] **Step 3: Create _Imports.razor**

`baseline/IfMultiBody.Blazor/_Imports.razor`:
```razor
@* The Web namespace supplies @onclick. *@
@using Microsoft.AspNetCore.Components.Web
```

- [ ] **Step 4: Create App.razor** (the measured source)

`baseline/IfMultiBody.Blazor/App.razor`:
```razor
@* Multi-node @if body (BENCH n°17): a single-branch @if whose body is TWO adjacent <span>
   elements, mounted/unmounted together as direct children of #w -- no wrapper. One list()
   item per body node (keys [0,1]); the comment anchor is the disclosed +1 node (decision 81).
   A toggle drives `show` so both spans are measured appearing and disappearing together, IN ORDER.

   No whitespace between </span> and <span>, nor between the button and @if, matching If.razor's
   contract: Razor turns SOURCE whitespace between siblings into text nodes, and there is none here. *@

<div id="w"><button id="toggle" @onclick="Toggle">toggle</button>@if (show)
{
    <span id="a">a</span><span id="b">b</span>
}</div>

@code {
    private bool show = true;
    private void Toggle() { show = !show; }
}
```

- [ ] **Step 5: Create index.html** (title changed)

`baseline/IfMultiBody.Blazor/wwwroot/index.html`:
```html
<!DOCTYPE html>
<html lang="en">

<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>IfMultiBody</title>
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

- [ ] **Step 6: Copy the css**

Run: `cp baseline/RootIf.Blazor/wwwroot/css/app.css baseline/IfMultiBody.Blazor/wwwroot/css/app.css`

- [ ] **Step 7: Build the baseline — the RZ9979 gate**

Run: `dotnet build baseline/IfMultiBody.Blazor -c Debug`
Expected: **Build succeeded**, 0 errors. (If this fails, STOP — the source is not valid Blazor and the slice is non-viable, exactly the RZ9979 trap.)

- [ ] **Step 8: Read Blazor's render tree to pin the DOM contract**

Run:
```bash
dotnet build baseline/IfMultiBody.Blazor -c Debug \
  -p:EmitCompilerGeneratedFiles=true \
  -p:CompilerGeneratedFilesOutputPath=generated
find baseline/IfMultiBody.Blazor/generated -name 'App*.g.cs' -exec cat {} \;
```
Expected: inside `if (show)`, a single `__builder.AddMarkupContent(N, "<span id=\"a\">a</span><span id=\"b\">b</span>")` — both spans, adjacent, no wrapper, no interleaved text node. Note this in the answer-key header (Task 2). Then clean the probe: `rm -rf baseline/IfMultiBody.Blazor/generated`.

- [ ] **Step 9: Ignore build artifacts and commit**

```bash
# bin/ and obj/ are already covered by the repo .gitignore (verify none staged).
git add baseline/IfMultiBody.Blazor/IfMultiBody.Blazor.csproj baseline/IfMultiBody.Blazor/Program.cs \
        baseline/IfMultiBody.Blazor/_Imports.razor baseline/IfMultiBody.Blazor/App.razor \
        baseline/IfMultiBody.Blazor/wwwroot/index.html baseline/IfMultiBody.Blazor/wwwroot/css/app.css
git status --short   # confirm no bin/obj staged
git commit -m "test(if-multibody): Blazor baseline app (multi-node @if body) — validity gate green"
```

---

### Task 2: Generator change + generator tests + witness flip

**Files:**
- Modify: `src/Filament.Generator/TemplatePlan.cs:111` (`IfBranch.Body` → node list)
- Modify: `src/Filament.Generator/CSharpFrontEnd.cs:633-678` (`If` + `BranchBody`)
- Modify: `src/Filament.Generator/TemplateCompiler.cs:1137-1167` (`EmitIf`)
- Create: `samples/IfMultiBody/ifmulti.js` (answer key)
- Modify: `tests/Filament.Generator.Tests/RepoPaths.cs` (add two paths)
- Modify: `tests/Filament.Generator.Tests/GateTests.cs` (add `IfMultiBodyToTemp`)
- Create: `tests/Filament.Generator.Tests/IfMultiBodyTests.cs`
- Create: `tests/Filament.Generator.Tests/Snapshots/IfMultiBody.approved.js` (bootstrapped)
- Modify: `tests/Filament.Generator.Tests/DiagnosticTests.cs` (witness flip)

**Interfaces:**
- Consumes: `RepoPaths.IfMultiBodyRazor` = `baseline/IfMultiBody.Blazor/App.razor` (Task 1).
- Produces: `IfBranch(string? Cond, IReadOnlyList<IntermediateNode> Body)`; `Generate.IfMultiBodyToTemp()` → path to emitted module.

- [ ] **Step 1: Write the answer key**

`samples/IfMultiBody/ifmulti.js`:
```js
/**
 * IfMultiBody — hand-written Filament answer key for baseline/IfMultiBody.Blazor/App.razor.
 *
 * Blazor DOM contract (read from the baseline's own App.razor.g.cs, decision-64 method — built
 * with `dotnet build baseline/IfMultiBody.Blazor -p:EmitCompilerGeneratedFiles=true
 * -p:CompilerGeneratedFilesOutputPath=generated`, then deleted):
 *
 *   __builder.OpenElement(0, "div"); __builder.AddAttribute(1, "id", "w");
 *   __builder.OpenElement(2, "button"); id="toggle"; onclick=Toggle; content "toggle"; CloseElement();
 *   if (show) {
 *       __builder.AddMarkupContent(6, "<span id=\"a\">a</span><span id=\"b\">b</span>");
 *   }
 *   __builder.CloseElement();   // </div>
 *
 * Two findings pinned by that read:
 *   1. The two spans are ONE opaque AddMarkupContent blob — inert static markup, adjacent, no
 *      wrapper, no interleaved text node (the source has no whitespace between </span> and <span>).
 *      Rendered, that is two <span> elements, direct children of #w, in order a then b.
 *   2. NO whitespace text nodes between the button and @if, nor between the spans.
 *
 * The @if lowers to a conditional list() with ONE ITEM PER BODY NODE: a source over the
 * condition yielding [0,1] (both nodes) or [] (neither), keyed by identity, dispatched by
 * `(i) => i === 0 ? ifBody0_0() : ifBody0_1()`. Both spans mount/unmount TOGETHER, in order.
 * The comment anchor is the DISCLOSED +1-node divergence from Blazor (decision 81/20).
 *
 * `Toggle` performs one write (`show = !show`) -> no batch() (decision 68); single-use -> inlined.
 */

import { signal, listen, insert, list } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const show = signal(true);

  const main = document.createElement('div');
  main.id = 'w';

  const btn = document.createElement('button');
  btn.id = 'toggle';
  insert(btn, document.createTextNode('toggle'));
  insert(main, btn);

  const anchor = document.createComment('');
  insert(main, anchor);

  function ifBody0_0() {
    const span = document.createElement('span');
    span.id = 'a';
    insert(span, document.createTextNode('a'));
    return span;
  }
  function ifBody0_1() {
    const span = document.createElement('span');
    span.id = 'b';
    insert(span, document.createTextNode('b'));
    return span;
  }
  list(main, () => (show.value) ? [0, 1] : [], (i) => i, (i) => i === 0 ? ifBody0_0() : ifBody0_1(), anchor);

  listen(btn, 'click', () => {
    show.value = !show.value;
  });

  insert(target, main);
}
```

- [ ] **Step 2: Add RepoPaths entries**

In `tests/Filament.Generator.Tests/RepoPaths.cs`, after the `RootIfAnswerKey` block (around line 52):
```csharp
    public static string IfMultiBodyRazor => Path.Combine(Root, "baseline", "IfMultiBody.Blazor", "App.razor");
    public static string IfMultiBodyAnswerKey => Path.Combine(Root, "samples", "IfMultiBody", "ifmulti.js");
```

- [ ] **Step 3: Add the Generate helper**

In `tests/Filament.Generator.Tests/GateTests.cs`, beside `RootIfToTemp` (line 269):
```csharp
    public static string IfMultiBodyToTemp() => ToTemp(RepoPaths.IfMultiBodyRazor, "IfMultiBody");
```

- [ ] **Step 4: Write the generator tests (RED)**

`tests/Filament.Generator.Tests/IfMultiBodyTests.cs`:
```csharp
using Xunit;

namespace Filament.Generator.Tests;

public class IfMultiBodyTests
{
    /// <summary>
    /// THE GATE (decision 51): canon(minify(generated)) === canon(minify(key)).
    /// samples/IfMultiBody/ifmulti.js is the Blazor-faithful reference; the generator is judged.
    /// </summary>
    [Fact]
    public void Gate_GeneratedIfMultiBody_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.IfMultiBodyToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.IfMultiBodyAnswerKey);
        Assert.True(exit == 0,
            "PHASE: @if multi-node body gate FAILED. Generated module is NOT alpha-equivalent to " +
            "samples/IfMultiBody/ifmulti.js.\n" + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>Snapshot: the only guard against silent generator regression the canon gate is blind to.</summary>
    [Fact]
    public void Snapshot_EmittedIfMultiBodyJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.IfMultiBodyToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "IfMultiBody.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>
    /// The contract: a comment anchor, a reactive [0,1] source over the condition, identity key,
    /// and TWO span subtrees. This is the multi-node lowering, pinned.
    /// </summary>
    [Fact]
    public void EmittedIfMultiBody_HonoursTheContract()
    {
        var js = File.ReadAllText(Generate.IfMultiBodyToTemp());
        Assert.Contains("document.createComment('')", js);
        Assert.Contains("() => (show.value) ? [0, 1] : []", js);   // one item per body node
        Assert.Contains("(i) => i", js);                            // identity key
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(js, "document\\.createElement\\('span'\\)").Count);
        Assert.DoesNotContain("innerHTML", js);
        Assert.DoesNotContain("[unsupported-if-body]", js);
    }

    /// <summary>Closed-runtime invariant: multi-node @if adds NO new runtime primitive (reuses list()).</summary>
    [Fact]
    public void EmittedIfMultiBody_OnlyCallsClosedRuntimePrimitives_NoNewExport()
    {
        var js = File.ReadAllText(Generate.IfMultiBodyToTemp());
        var import = js.Split('\n').First(l => l.StartsWith("import "));
        string[] allowed =
            ["signal", "computed", "effect", "batch", "untrack", "setText", "setAttr", "listen", "insert", "remove", "list"];
        foreach (var name in import[(import.IndexOf('{') + 1)..import.IndexOf('}')]
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.True(allowed.Contains(name),
                $"'{name}' is not a runtime export. Multi-node @if must add NO new primitive (reuse list()).");
        Assert.Contains("document.createComment(''", js);
    }
}
```

- [ ] **Step 5: Run the tests — confirm RED**

Run: `dotnet test tests/Filament.Generator.Tests --filter FullyQualifiedName~IfMultiBodyTests`
Expected: FAIL — the gate/contract tests fail because the generator still refuses the multi-node body (`Generate.IfMultiBodyToTemp` sees a non-zero exit / no output). This confirms the guard is real.

- [ ] **Step 6: Change the plan node type**

In `src/Filament.Generator/TemplatePlan.cs` line 111:
```csharp
public sealed record IfBranch(string? Cond, IReadOnlyList<IntermediateNode> Body);
```

- [ ] **Step 7: Rewrite `If` and `BranchBody`**

In `src/Filament.Generator/CSharpFrontEnd.cs`, replace the `If(...)` method (lines 633-653) and `BranchBody(...)` (lines 660-678) with:
```csharp
    IfOp? If(IfStatementSyntax ifs, IReadOnlyDictionary<string, IntermediateNode> markers)
    {
        var singleBranch = ifs.Else is null;         // plain @if, no else -> multi-node body allowed
        var branches = new List<IfBranch>();
        var cur = ifs;
        while (true)
        {
            if (BranchBody(cur.Statement, markers, allowMulti: singleBranch) is not { } body) return null;
            branches.Add(new IfBranch(Expr(cur.Condition), body));

            if (cur.Else is not { } els) break;               // if / else-if chain ended, no @else
            if (els.Statement is IfStatementSyntax nested)    // "else if (...)"
            {
                cur = nested;
                continue;
            }
            if (BranchBody(els.Statement, markers, allowMulti: false) is not { } elseBody) return null;
            branches.Add(new IfBranch(null, elseBody));
            break;
        }
        return new IfOp(branches);
    }

    /// <summary>
    /// One @if/@else branch body -> the markup nodes it produces, or null (a located refusal already
    /// emitted). `allowMulti` (a branch-less @if only) lifts the "exactly ONE" cap to "one or more".
    /// Non-markup ops (nested control flow, stray text) stay refused regardless: ops.Count must equal
    /// markup.Count.
    /// </summary>
    IReadOnlyList<IntermediateNode>? BranchBody(
        StatementSyntax stmt, IReadOnlyDictionary<string, IntermediateNode> markers, bool allowMulti)
    {
        // The ORIGINAL statement nodes, never a SyntaxFactory copy (see ForEach).
        IEnumerable<StatementSyntax> body = stmt is BlockSyntax b ? b.Statements : [stmt];
        var ops = RegionOps(body, markers);
        var markup = ops.OfType<MarkupOp>().ToList();

        var tooMany = allowMulti ? markup.Count < 1 : markup.Count != 1;
        if (tooMany || ops.Count != markup.Count)
        {
            Refuse("unsupported-if-body",
                $"a template @if / @else branch body must be {(allowMulti ? "one or more elements" : "exactly ONE element")} " +
                $"and nothing else; this one produces {ops.Count} thing(s). @if lowers to a conditional list() " +
                "whose create() returns one root node per item, so a body with a stray text node or nested " +
                "control flow has no single thing to insert and remove. Refusing to emit.",
                stmt.SpanStart);
            return null;
        }
        return markup.Select(m => m.Node).ToList();
    }
```

- [ ] **Step 8: Rewrite `EmitIf`**

In `src/Filament.Generator/TemplateCompiler.cs`, replace the body of `EmitIf` after the anchor setup (the `if (op.Branches.Count == 1) { … }` block and the multi-branch tail, lines 1148-1166) with:
```csharp
        if (op.Branches.Count == 1)
        {
            var body = op.Branches[0].Body;
            if (body.Count == 1)
            {
                // EXACT #81 emission — byte-identical, so the @if gate + snapshot still hold.
                var fn = Unique("ifBody");
                if (!EmitBranchFn(body[0], fn)) return;
                _bindings.Add($"list({container}, () => ({op.Branches[0].Cond}) ? [0] : [], () => 0, {fn}, {anchor});");
                return;
            }

            // MULTI-NODE body: one list item per body node. The single item's VALUE is the node index;
            // the key IS that index, so the whole group mounts/unmounts on the condition, in order.
            var bodyFns = new List<string>();
            for (var i = 0; i < body.Count; i++)
            {
                var fn = Unique($"ifBody{id}_{i}");
                if (!EmitBranchFn(body[i], fn)) return;
                bodyFns.Add(fn);
            }
            var keys = string.Join(", ", Enumerable.Range(0, bodyFns.Count));
            _bindings.Add($"list({container}, () => ({op.Branches[0].Cond}) ? [{keys}] : [], (i) => i, {IfCreate(bodyFns)}, {anchor});");
            return;
        }

        // MULTI-BRANCH @if/@else if/@else (#82): each branch is exactly one node (allowMulti was false).
        var fns = new List<string>();
        for (var i = 0; i < op.Branches.Count; i++)
        {
            var fn = Unique($"ifBody{id}_{i}");
            if (!EmitBranchFn(op.Branches[i].Body[0], fn)) return;
            fns.Add(fn);
        }
        _bindings.Add($"list({container}, {IfSource(op.Branches)}, (i) => i, {IfCreate(fns)}, {anchor});");
```
(If the file lacks `using System.Linq;`, it is already present — `EmitNode`/`EmitOps` use LINQ.)

- [ ] **Step 9: Build the generator**

Run: `dotnet build src/Filament.Generator`
Expected: build succeeds, 0 errors.

- [ ] **Step 10: Run the IfMultiBody tests (gate/contract/closed-runtime GREEN, snapshot bootstraps)**

Run: `dotnet test tests/Filament.Generator.Tests --filter FullyQualifiedName~IfMultiBodyTests`
Expected: gate + contract + closed-runtime PASS; the snapshot test FAILS once with "wrote …IfMultiBody.approved.js; review + re-run".

- [ ] **Step 11: Review + re-run the snapshot**

Inspect `tests/Filament.Generator.Tests/Snapshots/IfMultiBody.approved.js`: it must show the two `ifBody0_0`/`ifBody0_1` functions, `() => (show.value) ? [0, 1] : []`, `(i) => i`, `(i) => i === 0 ? ifBody0_0() : ifBody0_1()`, the comment anchor, and the batch-free inlined click handler. Then:
Run: `dotnet test tests/Filament.Generator.Tests --filter FullyQualifiedName~IfMultiBodyTests`
Expected: all 4 PASS.

- [ ] **Step 12: Flip the witness in DiagnosticTests**

In `tests/Filament.Generator.Tests/DiagnosticTests.cs`:
1. In `ControlFlow_OutsideTheSubset_IsRefused_AtItsExactLocation` `[Theory]`, **delete** the `[InlineData]` line for `IfMultiBody.razor` (`FIL0001 [unsupported-if-body] @ (2,1)`). Keep `Foreach`, `IfNested`, `IfElseMultiBody`.
2. In `ARefusalWritesNoFile` `[InlineData]` block, **delete** the `IfMultiBody.razor` line. Keep `IfNested`, `IfElseMultiBody`.
3. Add a fact beside `IfAtRoot_NowCompiles_ToAConditionalAgainstTarget` (line ~97):
```csharp
    /// <summary>
    /// A single-branch @if with a MULTI-NODE body now compiles: a conditional list() with one item
    /// per body node ([0, 1]) and an identity key. The refusal that was [unsupported-if-body] is
    /// closed for the branch-less case (slice: multi-node @if body).
    /// </summary>
    [Fact]
    public void IfMultiBody_NowCompiles_ToAMultiNodeConditionalList()
    {
        var js = File.ReadAllText(Generate.ToTempFixture("IfMultiBody.razor"));
        Assert.Contains("list(", js);
        Assert.Contains("[0, 1]", js);
        Assert.Contains("(i) => i", js);
        Assert.DoesNotContain("[unsupported-if-body]", js);
    }
```
(`Generate.ToTempFixture` emits from `Unsupported/`; the minimal witness `Unsupported/IfMultiBody.razor` has no button but the same two-span body, so it compiles to the same `[0, 1]` list.)

- [ ] **Step 13: Full .NET suite + firewall**

Run:
```bash
dotnet test Filament.sln
git diff --stat src/filament-runtime   # MUST be empty
```
Expected: all .NET tests green (generator gains 4 `IfMultiBodyTests` + 1 `NowCompiles` fact; the two deleted refusal `InlineData` cases are superseded by that fact; report the exact new count). `IfTests`/`IfElseTests`/`RootIfTests`/`RootForeachTests` unchanged. `IfElseMultiBody`/`IfNested`/`Foreach` still refused. Firewall diff empty.

- [ ] **Step 14: Runtime verify (unchanged, still green)**

Run: `cd src/filament-runtime && npm run verify`
Expected: green (214 tests), size gate unchanged. `cd` back to repo root.

- [ ] **Step 15: Commit**

```bash
git add src/Filament.Generator/TemplatePlan.cs src/Filament.Generator/CSharpFrontEnd.cs \
        src/Filament.Generator/TemplateCompiler.cs samples/IfMultiBody/ifmulti.js \
        tests/Filament.Generator.Tests/RepoPaths.cs tests/Filament.Generator.Tests/GateTests.cs \
        tests/Filament.Generator.Tests/IfMultiBodyTests.cs \
        tests/Filament.Generator.Tests/Snapshots/IfMultiBody.approved.js \
        tests/Filament.Generator.Tests/DiagnosticTests.cs
git commit -m "feat(if-multibody): admit multi-node body for a single-branch @if (one list item per node)"
```

---

### Task 3: Playwright oracle + BENCH n°17

**Files:**
- Create: `samples/filament-ifmulti-gen/main.js` (host shim)
- Modify: `.gitignore` (add `samples/filament-ifmulti-gen/App.g.js`)
- Modify: `bench/harness/bench.mjs` (`APPS` entry, `verifyContract` clause, `HARNESS_VERSION`)
- Modify: `bench/build-filament.sh` (app list + 6 case arms)
- Modify: `BENCH.md` (append n°17)

**Interfaces:**
- Consumes: `baseline/IfMultiBody.Blazor/App.razor` (Task 1), the generator (Task 2).

- [ ] **Step 1: Host shim**

`samples/filament-ifmulti-gen/main.js`:
```js
/**
 * Entry point for the `filament-ifmulti-gen` label — the multi-node @if body correctness app.
 *
 * It mounts the JS the generator emits from baseline/IfMultiBody.Blazor/App.razor (a single-branch
 * @if whose body is two adjacent <span>s). Like divide/rootif it is NOT weighed or timed: it exists
 * only so the DOM-contract oracle can drive #toggle and assert BOTH spans mount/unmount together, in
 * order, as direct children of #w — the conditional DOM Blazor produces at runtime, produced here by
 * the same list(container, ..., anchor) mapping with one item per body node (decision 81/89).
 *
 * App.g.js IS NOT COMMITTED AND IS NOT EDITABLE: build-filament.sh deletes and re-emits it from
 * App.razor on every build, so the app the oracle loads is always this run's generator output.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
```

- [ ] **Step 2: gitignore the generated module**

In `.gitignore`, after the `samples/filament-stringattrs-gen/App.g.js` line:
```
samples/filament-ifmulti-gen/App.g.js
```

- [ ] **Step 3: Register the bench app**

In `bench/harness/bench.mjs` `APPS`, after the `stringattrs` block:
```js
  // Correctness-only: verifyContract clicks #toggle and asserts a MULTI-NODE @if body mounts/unmounts
  // BOTH #w spans together, in order (a,b), against Blazor's own rendered DOM. The measurement of the
  // multi-node @if body widening (BENCH n°17).
  ifmulti: {
    readySelector: '#toggle',
    observeSelector: '#w',
    scenarios: [],
  },
```

- [ ] **Step 4: Add the verifyContract clause**

In `bench/harness/bench.mjs`, beside the other correctness clauses (e.g. after the `rootif` clause):
```js
    if (app === 'ifmulti') {
      return ctx.page.evaluate(() => {
        const out = { problems: [], observed: {} };
        if (!document.querySelector('#toggle')) { out.problems.push('missing required element: #toggle'); return out; }
        const ids = () => Array.from(document.querySelectorAll('#w > span')).map(s => s.id).join(',');
        out.observed.initial = ids();
        if (ids() !== 'a,b') { out.problems.push(`#w spans initially "${ids()}", expected "a,b"`); return out; }
        document.querySelector('#toggle').click();
        out.observed.afterToggle = ids();
        // THE MEASUREMENT: a multi-node @if body unmounts BOTH spans together when the condition goes false.
        if (ids() !== '') { out.problems.push(`#w spans still "${ids()}" after toggle, expected "" (both removed together)`); return out; }
        document.querySelector('#toggle').click();
        out.observed.afterSecond = ids();
        // ...and remounts BOTH, in order, when it goes true again.
        if (ids() !== 'a,b') out.problems.push(`#w spans "${ids()}" after second toggle, expected "a,b" (both restored, in order)`);
        return out;
      });
    }
```

- [ ] **Step 5: Bump HARNESS_VERSION (disclosed)**

In `bench/harness/bench.mjs` line 72:
```js
export const HARNESS_VERSION = '1.12.0';   // 1.12.0: 'ifmulti' contract (multi-node @if body). 1.11.0: 'stringattrs' contract (reactive title/href/aria-label). 1.10.0: 'mixedattr' (mixed literal+expression class value). 1.9.0: 'boolattr' (boolean disabled present/absent). 1.8.0: 'reactiveattr' (reactive class attribute). 1.7.0: 'boundcompose' (bound-parameter composition). 1.6.0: rootforeach/rootif. 1.5.0: compose. 1.4.0: divide.
```

- [ ] **Step 6: Wire build-filament.sh (6 arms + app list)**

In `bench/build-filament.sh`:
1. App list (after `filament-stringattrs-gen`, ~line 183): add a line `  filament-ifmulti-gen`.
2. APPBASE `case` (after the `filament-stringattrs-gen` arm): `    filament-ifmulti-gen)                            echo "samples/filament-ifmulti-gen" ;;`
3. `mode_for` production list (line ~215): append `|filament-ifmulti-gen` before the `)` .
4. source `case`: `    filament-ifmulti-gen)                            echo "$REPO_ROOT/baseline/IfMultiBody.Blazor/App.razor" ;;`
5. gen-out `case`: `    filament-ifmulti-gen)                            echo "App.g.js" ;;`
6. sample-dir `case`: `    filament-ifmulti-gen)                            echo "IfMultiBody" ;;`
7. publish `case`: `    filament-ifmulti-gen)                            echo "blazor-ifmulti" ;;`
8. css `case`: `    filament-ifmulti-gen)                            echo "$REPO_ROOT/baseline/IfMultiBody.Blazor/wwwroot/css/app.css" ;;`

- [ ] **Step 7: Build both artifacts**

Run:
```bash
bash bench/build-filament.sh filament-ifmulti-gen
dotnet publish baseline/IfMultiBody.Blazor -c Release -o bench/publish/blazor-ifmulti
```
Expected: the generator emits `samples/filament-ifmulti-gen/App.g.js`; the Blazor publish writes `bench/publish/blazor-ifmulti/wwwroot`.

- [ ] **Step 8: Run the oracle on BOTH builds**

Run (mirror the command recorded for stringattrs; both must return no problems and identical observed):
```bash
node bench/harness/bench.mjs --app ifmulti --contract-only --target filament-ifmulti-gen
node bench/harness/bench.mjs --app ifmulti --contract-only --target blazor-ifmulti
```
Expected: both `problems: []`, both `observed: { initial: "a,b", afterToggle: "", afterSecond: "a,b" }`. Capture the exact JSON for BENCH n°17.

- [ ] **Step 9: Append BENCH n°17**

Add `## Entrée n°17 — 2026-07-19 — Phase 4 : le corps MULTI-NŒUD d'un `@if` (branche unique) mesuré contre Blazor (CORRECTION)` to `BENCH.md`, French house style, mirroring n°16's sections (Ce qui est mesuré / Environnement / Protocole / Commande pour rejouer / Résultat / Réserves). Record: correctness-only, `HARNESS_VERSION 1.11.0 → 1.12.0` disclosed, both builds `initial "a,b" → afterToggle "" → afterSecond "a,b"`, runtime unchanged, Blazor baseline `bench/publish/blazor-ifmulti`.

- [ ] **Step 10: Commit**

```bash
git add samples/filament-ifmulti-gen/main.js .gitignore bench/harness/bench.mjs \
        bench/build-filament.sh BENCH.md
git commit -m "bench(if-multibody): DOM-contract oracle + BENCH n°17 (both spans mount/unmount together, in order)"
```

---

### Task 4: DECISIONS #98 + finish

**Files:**
- Modify: `DECISIONS.md` (append #98)

- [ ] **Step 1: Append DECISIONS #98**

Add `## 98. Le corps MULTI-NŒUD d'un `@if` (branche unique) entre dans le §5 — l'abaissement plain-`@if` généralisé de « un nœud par branche » à « un item de liste par nœud du corps »` to `DECISIONS.md`, French house style. Cover: the one-list-item-per-node lowering (`() => cond ? [0,1] : []`, identity key, `IfCreate` dispatch); #81/#82 emissions byte-identical (new path only for `Body.Count > 1` on a branch-less `@if`); `IfElseMultiBody`/`IfNested` still refused (`allowMulti` gated on `ifs.Else is null`), witnesses intact + mutation-tested; the `IfMultiBody` witness flipped refused→compiles; runtime UNCHANGED (firewall); Blazor-validity verified up front (RZ9979 lesson); measured by canon gate against a `BuildRenderTree`-derived answer key + byte snapshot + Playwright oracle (BENCH n°17, `HARNESS_VERSION` bump disclosed); the exact final test counts; text-in-body / `@else`-multi-node / nested control flow deferred.

- [ ] **Step 2: Final full verification**

Run:
```bash
dotnet test Filament.sln
cd src/filament-runtime && npm run verify && cd ../..
git diff --stat src/filament-runtime   # empty
```
Expected: all .NET green (record exact count), runtime 214 green, firewall empty.

- [ ] **Step 3: Commit**

```bash
git add DECISIONS.md
git commit -m "docs(if-multibody): DECISIONS #98 — multi-node @if body (measured widening)"
```

- [ ] **Step 4: Update memory + finish**

Write `~/.claude/projects/-Users-phmatray-Repositories-dotnet-Filament/memory/multinode-if-body-widened.md` (project memory, links `[[demo-shipped-if-family-next]]`, `[[double-division-widened-subset]]`) + a MEMORY.md index line. Then invoke **superpowers:finishing-a-development-branch**: environment is a normal repo on `main`, no remote — report the slice landed on `main`.

## Self-Review

- **Spec coverage:** generator change (Task 2), baseline + validity gate (Task 1), witness flip (Task 2 Step 12), canon+snapshot+oracle (Tasks 2/3), BENCH n°17 + DECISIONS #98 (Tasks 3/4), byte-preservation + firewall + boundary-witness checks (Task 2 Steps 13-14). All covered.
- **Placeholders:** none — every code block is concrete; the only prose-described artifacts (BENCH n°17, DECISIONS #98) are French narrative documents whose required content is enumerated.
- **Type consistency:** `IfBranch.Body : IReadOnlyList<IntermediateNode>` used consistently in `TemplatePlan`, `BranchBody` (returns the list), `If` (constructs), `EmitIf` (`body.Count`, `body[i]`, `Body[0]`). `Generate.IfMultiBodyToTemp` / `RepoPaths.IfMultiBodyRazor` / `RepoPaths.IfMultiBodyAnswerKey` names match across Tasks 2's steps.
