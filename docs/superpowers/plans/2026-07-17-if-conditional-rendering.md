# `@if` Conditional Rendering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move template `@if` from refused (`[control-flow-not-yet-implemented]`) into the compiled subset, lowered onto the existing `list()` runtime primitive with zero runtime change.

**Architecture:** In Razor's IR, `@if` (like `@foreach`) arrives as raw C# spans that the generator already re-assembles and re-parses into a statement tree; an `@if` is an `IfStatementSyntax` in that tree (`CSharpFrontEnd.RegionOps`), currently refused. We replace the refusal with a builder that produces a new `IfOp`, and emit it as `list(parent, () => (cond) ? [0] : [], () => 0, bodyFn, commentAnchor)` — a keyed 0/1 list whose comment-node anchor keeps it correctly positioned among siblings. The condition's field reads are marked as template reads so the condition is reactive.

**Tech Stack:** C# 13 / .NET 10 (generator + xUnit tests), Roslyn (`Microsoft.CodeAnalysis.CSharp`), Razor Language 6.0.36, TypeScript runtime (Vitest), `node tools/canon.mjs` (esbuild-based alpha-equivalence).

## Global Constraints

- **Runtime is CLOSED and untouched by this feature.** No new export from `src/filament-runtime/`. Size gate stays **1,943 B / 2,048 B**. `git diff --stat src/filament-runtime` must be empty at the end.
- **No silently-wrong output.** Every construct outside this cut's subset raises a **located** diagnostic (`file(line,col): FIL000x: [reason]`) and writes **no file**. Codes: `FIL0001` out-of-subset C#, `FIL0003` out-of-subset Razor.
- **The answer key is the REFERENCE; the generator is JUDGED** (decisions #21/#51). Never edit an answer key to make the gate pass; only the owner corrects a key against the baseline (#64/#80).
- **DOM contract is pinned against Blazor's own generated `BuildRenderTree`**, not a reading of the rules (#64/#76). The comment anchor is a disclosed +1-node divergence (category of #20).
- **Scope of this cut:** plain `@if (cond) { <single-root-node body> }` nested inside an element. OUT (→ located diagnostic): `@else`/`@else if`, nested control flow, `@if` at template root, multiple-top-level-node body.
- **Commit after every green step.** TDD: failing test first.
- Build the generator with `dotnet build src/Filament.Generator/Filament.Generator.csproj -c Debug`. Run the suite with `dotnet test tests/Filament.Generator.Tests/Filament.Generator.Tests.csproj` (it rebuilds the generator DLL the tests shell out to). Expected baseline before this plan: **162 passed / 0 failed**.

---

### Task 1: Compile a plain reactive `@if` to a conditional `list()`

**Files:**
- Modify: `src/Filament.Generator/TemplatePlan.cs` (add `IfOp` after `ForEachOp`, ~line 98)
- Modify: `src/Filament.Generator/CSharpFrontEnd.cs` (refactor `MarkTemplateReads` ~1162; add `MarkConditionReads` call after line 419; replace the `IfStatementSyntax` refusal at ~475-485 with an `If` builder)
- Modify: `src/Filament.Generator/TemplateCompiler.cs` (add `_if` counter ~line 200; add `IfOp` arm to `EmitOps` ~838-851; add `EmitIf` after `EmitList` ~916)
- Modify: `tests/Filament.Generator.Tests/DiagnosticTests.cs` (remove the now-compiling `If.razor` from the refusal theories at ~75-84, ~403-441, ~448-474)
- Delete: `tests/Filament.Generator.Tests/Unsupported/If.razor` (plain `@if` now compiles)
- Create: `tests/Filament.Generator.Tests/IfTests.cs` (happy-path unit test + its `Compile` helper). The `Compile` helper calls `Directory.CreateDirectory(samples/If)` so the emitted runtime specifier resolves — no committed placeholder is needed; Task 2 populates the dir with real files.

**Interfaces:**
- Produces: `IfOp(string Cond, IntermediateNode Body) : TemplateOp` — `Cond` is the condition already translated to JS (e.g. `show.value`); `Body` is the single markup node of the `@if` body.
- Produces (CSharpFrontEnd, private): `IfOp? If(IfStatementSyntax ifs, IReadOnlyDictionary<string, IntermediateNode> markers)`, `void MarkReads(ExpressionSyntax e)`, `void MarkConditionReads(ClassDeclarationSyntax regionClass, IReadOnlyList<string> regionMethods)`.
- Produces (TemplateCompiler, private): `void EmitIf(IfOp op, string container)`; consumes existing `EmitNode`, `_create`, `_bindings`, `_used`, `Unique`.

- [ ] **Step 1: Write the failing happy-path test**

Create `tests/Filament.Generator.Tests/IfTests.cs`:

```csharp
using Xunit;

namespace Filament.Generator.Tests;

public class IfTests
{
    /// <summary>
    /// A plain @if nested in an element compiles to a conditional list(): a 0/1 source over the
    /// condition, a constant key, and a comment anchor. The condition field is lifted to a signal
    /// because a read in an @if condition counts as a template read (else the @if renders once).
    /// </summary>
    [Fact]
    public void PlainIf_CompilesToAConditionalList_AndLiftsTheConditionField()
    {
        var js = Compile(
            """
            <div id="wrap"><button id="t" @onclick="Toggle">t</button>@if (show)
            {
                <span id="msg">hi</span>
            }</div>

            @code {
                private bool show = true;
                void Toggle() => show = !show;
            }
            """);

        Assert.Contains("const show = signal(true);", js);          // lifted: read by @if condition
        Assert.Contains("document.createComment('')", js);          // the anchor node
        Assert.Contains("() => (show.value) ? [0] : []", js);       // reactive 0/1 source
        Assert.Contains("() => 0,", js);                            // constant key
        Assert.Contains("document.createElement('span')", js);      // the body subtree
        Assert.DoesNotContain("when(", js);                         // no new runtime primitive
        Assert.DoesNotContain("[control-flow-not-yet-implemented]", js);
    }

    /// <summary>Compile an inline .razor from samples/If so the runtime specifier resolves.</summary>
    static string Compile(string razor)
    {
        var dir = Path.Combine(RepoPaths.Root, "samples", "If");
        Directory.CreateDirectory(dir);
        var src = Path.Combine(dir, $".t-{Guid.NewGuid():N}.razor");
        var outPath = Path.Combine(dir, $".t-{Guid.NewGuid():N}.js");
        try
        {
            File.WriteAllText(src, razor);
            var (exit, _, stderr) = Run.Generator(src, outPath);
            Assert.True(exit == 0, $"the generator refused to emit:\n{stderr}");
            return File.ReadAllText(outPath);
        }
        finally
        {
            if (File.Exists(src)) File.Delete(src);
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/Filament.Generator.Tests/Filament.Generator.Tests.csproj --filter "FullyQualifiedName~IfTests.PlainIf_CompilesToAConditionalList"`
Expected: FAIL — the generator refuses with `[control-flow-not-yet-implemented]`, so `exit != 0` and the `Assert.True(exit == 0, ...)` in `Compile` throws.

- [ ] **Step 3: Add the `IfOp` record**

In `src/Filament.Generator/TemplatePlan.cs`, immediately after the `ForEachOp` record (~line 98), add:

```csharp
/// <summary>
/// `@if (cond) { &lt;body&gt; }` -> a conditional list() with a 0/1 source and a comment anchor.
/// </summary>
/// <param name="Cond">the condition, already translated to JS (e.g. "show.value")</param>
/// <param name="Body">the ONE markup node the @if body produces</param>
public sealed record IfOp(string Cond, IntermediateNode Body) : TemplateOp;
```

- [ ] **Step 4: Refactor `MarkReads` out of `MarkTemplateReads` and add `MarkConditionReads`**

In `src/Filament.Generator/CSharpFrontEnd.cs`, replace the body of `MarkTemplateReads` (~1162-1174) with a call to a new shared `MarkReads`, and add `MarkConditionReads`:

```csharp
void MarkTemplateReads(IReadOnlyList<IntermediateNode> slots)
{
    foreach (var node in slots) MarkReads(SlotSyntax(node));
}

/// <summary>Mark every field/prop READ inside one expression as read-by-template.</summary>
void MarkReads(ExpressionSyntax e)
{
    foreach (var id in e.DescendantNodesAndSelf())
    {
        if (id is not (IdentifierNameSyntax or MemberAccessExpressionSyntax)) continue;
        switch (_model.GetSymbolInfo(id).Symbol)
        {
            case IFieldSymbol fs when Field(fs) is { } f: f.ReadByTemplate = true; break;
            case IPropertySymbol ps when PropAnywhere(ps) is { } p: p.ReadByTemplate = true; break;
        }
    }
}

/// <summary>
/// A template @if condition reads state the way a slot does, so its reads must count as template
/// reads -- otherwise a bool read ONLY in `@if (show)` is never lifted and the conditional renders
/// once. MUST run with MarkTemplateReads (step 2c), BEFORE method bodies and slots are translated
/// (Body/TranslateSlots), so IsSignal is settled when Expr() runs on the condition.
/// </summary>
void MarkConditionReads(ClassDeclarationSyntax regionClass, IReadOnlyList<string> regionMethods)
{
    foreach (var method in regionMethods)
    foreach (var ifs in FindMethod(regionClass, method).Body!.DescendantNodes().OfType<IfStatementSyntax>())
        MarkReads(ifs.Condition);
}
```

Then, in `Compile`, right after line 419 (`MarkTemplateReads(slots);`), add:

```csharp
        MarkConditionReads(classes[1], regionMethods);
```

- [ ] **Step 5: Replace the `IfStatementSyntax` refusal with the `If` builder**

In `src/Filament.Generator/CSharpFrontEnd.cs`, replace the whole `if (s is IfStatementSyntax) { Refuse("control-flow-not-yet-implemented", ...); continue; }` block (~475-485) with:

```csharp
            if (s is IfStatementSyntax ifs)
            {
                if (If(ifs, markers) is { } op) ops.Add(op);
                continue;
            }
```

Then add the `If` builder next to `ForEach` (after `ForEach`, ~579):

```csharp
    /// <summary>
    /// `@if (cond) { &lt;element&gt; }` -> IfOp, lowered to a conditional list() by TemplateCompiler.
    ///
    /// First cut: plain @if only. @else, nested control flow, and a body that is not exactly one
    /// element are refused -- each is a separate mapping no answer key covers yet.
    /// </summary>
    IfOp? If(IfStatementSyntax ifs, IReadOnlyDictionary<string, IntermediateNode> markers)
    {
        if (ifs.Else is { } els)
        {
            Refuse("else-not-yet-implemented",
                "@else / @else if is not in this step's subset. The first cut compiles a plain @if to a " +
                "conditional list() with a single body; an alternative branch is a separate mapping (two " +
                "bodies, or a swap) that no answer key covers yet. Refusing to emit.",
                els.ElseKeyword.SpanStart);
            return null;
        }

        // The ORIGINAL statement nodes, never a SyntaxFactory copy (see ForEach).
        IEnumerable<StatementSyntax> body = ifs.Statement is BlockSyntax b ? b.Statements : [ifs.Statement];
        var ops = RegionOps(body, markers);
        var markup = ops.OfType<MarkupOp>().ToList();

        if (markup.Count != 1 || ops.Count != markup.Count)
        {
            Refuse("unsupported-if-body",
                $"a template @if body must be exactly ONE element and nothing else; this one produces " +
                $"{ops.Count} thing(s). @if lowers to a conditional list() whose create() returns ONE root " +
                "node, so a body with two roots, a stray text node, or nested control flow has no single " +
                "thing to insert and remove. Refusing to emit.",
                ifs.Statement.SpanStart);
            return null;
        }

        return new IfOp(Expr(ifs.Condition), markup[0].Node);
    }
```

- [ ] **Step 6: Emit the `IfOp` as a conditional `list()`**

In `src/Filament.Generator/TemplateCompiler.cs`, add a counter field next to `_el`/`_tx` (~line 200-216):

```csharp
    int _if;
```

Add an `IfOp` arm to the `EmitOps` switch (~840-850), after the `ForEachOp` case:

```csharp
                case IfOp iff:
                    EmitIf(iff, container);
                    break;
```

Add `EmitIf` immediately after `EmitList` (~916):

```csharp
    /// <summary>
    /// `@if (cond) { &lt;body&gt; }` -> a conditional list() with a 0/1 source and a comment anchor.
    ///
    /// The anchor is a comment node inserted at the @if's position among its siblings (in _create,
    /// so it lands in source order); list() inserts the body BEFORE it, so the conditional is
    /// positioned correctly no matter what follows. Zero new runtime primitive: document.createComment
    /// is a DOM builtin, and 3-arg insert / list(...anchor) already exist.
    /// </summary>
    void EmitIf(IfOp op, string container)
    {
        var anchor = $"_if{_if++}";
        _create.Add($"const {anchor} = document.createComment('');");
        _used.Add("insert");
        _create.Add($"insert({container}, {anchor});");

        var fn = Unique("ifBody");

        // Build the body subtree into a fresh create/binding pair, exactly as EmitList does.
        var outerCreate = _create;
        var outerBindings = _bindings;
        var outerKey = _consumedKey;
        _create = [];
        _bindings = [];
        _consumedKey = null;

        var root = EmitNode(op.Body, parent: null);
        var body = new List<string>();
        body.AddRange(_create);
        body.AddRange(_bindings);

        _create = outerCreate;
        _bindings = outerBindings;
        _consumedKey = outerKey;

        if (root is null) return; // the body was refused; nothing is emitted
        body.Add($"return {root};");

        _bindings.Add($"function {fn}() {{\n" + string.Join("\n", body.Select(l => "  " + l)) + "\n}");

        _used.Add("list");
        _bindings.Add($"list({container}, () => ({op.Cond}) ? [0] : [], () => 0, {fn}, {anchor});");
    }
```

- [ ] **Step 7: Run the happy-path test to verify it passes**

Run: `dotnet test tests/Filament.Generator.Tests/Filament.Generator.Tests.csproj --filter "FullyQualifiedName~IfTests.PlainIf_CompilesToAConditionalList"`
Expected: PASS.

If `const show = signal(true)` is NOT present (the field stayed a plain `let show = true`), the condition read was not counted before translation: confirm `MarkConditionReads` is called at line 419-420 (before `Body`/`TranslateSlots` at 433-434) and that `classes[1]`/`regionMethods` are the same values used at line 435-436.

- [ ] **Step 8: Handle the `If.razor` fallout in the diagnostic suite**

The plain `Unsupported/If.razor` now compiles, so it must leave the refusal theories. In `tests/Filament.Generator.Tests/DiagnosticTests.cs`:
- Remove the line `[InlineData("If.razor", 2, 2, "control-flow-not-yet-implemented")]` from `ControlFlow_OutsideTheSubset_IsRefused_AtItsExactLocation` (~75-84), leaving the `Foreach.razor` case.
- Remove `"If.razor"` from any fixture list in the quantified theories `EveryDiagnostic_CarriesAnExactLocation_AndOneOfTheSpecsCodes` (~403-441) and `ARefusalWritesNoFile` (~448-474). (Search the file for `If.razor` and delete each occurrence in a refusal list.)

Then delete the fixture:

```bash
git rm tests/Filament.Generator.Tests/Unsupported/If.razor
```

- [ ] **Step 9: Run the full suite**

Run: `dotnet test tests/Filament.Generator.Tests/Filament.Generator.Tests.csproj`
Expected: PASS, count = 162 (baseline) − removed If.razor theory cases + 1 new IfTests = **all green, 0 failed**. If any `Foreach.razor`/root/nested test broke, it means `RegionOps` recursion changed behavior for a case still meant to be refused — re-read Task 3 before "fixing" it (those refusals are intended).

- [ ] **Step 10: Commit**

```bash
git add src/Filament.Generator/TemplatePlan.cs src/Filament.Generator/CSharpFrontEnd.cs \
        src/Filament.Generator/TemplateCompiler.cs tests/Filament.Generator.Tests/IfTests.cs \
        tests/Filament.Generator.Tests/DiagnosticTests.cs
git rm tests/Filament.Generator.Tests/Unsupported/If.razor
git commit -m "feat(@if): compile plain @if to a conditional list() with a comment anchor"
```

---

### Task 2: The canon gate — Blazor-faithful answer key + alpha-equivalence

**Files:**
- Create (temporary): a throwaway `IfRef.razor` in an existing baseline project to read Blazor's generated `BuildRenderTree`; removed at the end of the task.
- Create: `samples/If/If.razor` (the gate input), `samples/If/if.js` (the hand-written answer key)
- Create: `tests/Filament.Generator.Tests/Snapshots/If.approved.js`
- Modify: `tests/Filament.Generator.Tests/RepoPaths.cs` (add `IfRazor`, `IfAnswerKey`)
- Modify: `tests/Filament.Generator.Tests/GateTests.cs` (add `Generate.IfToTemp()`) OR add to `IfTests.cs`
- Create/Modify: `tests/Filament.Generator.Tests/IfTests.cs` (gate + snapshot + contract tests)

**Interfaces:**
- Consumes: `Run.Node(RepoPaths.Canon, generated, answerKey)`, `Generate` / `Run.Generator` (Task 1's world).
- Produces: `RepoPaths.IfRazor` → `samples/If/If.razor`, `RepoPaths.IfAnswerKey` → `samples/If/if.js`.

- [ ] **Step 1: Establish Blazor's DOM contract for `@if`**

Add a throwaway component to an existing baseline project (it will not be referenced by the app, only compiled):

Create `baseline/Counter.Blazor/IfRef.razor`:

```razor
<div id="wrap"><button id="t" @onclick="Toggle">t</button>@if (show)
{
    <span id="msg">hi</span>
}</div>

@code {
    private bool show = true;
    void Toggle() => show = !show;
}
```

Build and read Blazor's generated BuildRenderTree:

```bash
dotnet build baseline/Counter.Blazor -c Debug
find baseline/Counter.Blazor/obj -name "IfRef_razor.g.cs" -o -name "IfRef.razor.g.cs"
```

Open the `.g.cs` and record, in a comment you will paste into `if.js`, exactly what Blazor emits around the `@if`: whether it `OpenRegion`/`CloseRegion`s, whether it `AddMarkupContent`s any whitespace text nodes between `</button>` and the conditional (and after it before `</div>`), and whether the conditional content is any node other than the `<span>`. This is the same read #76 did for the Rows whitespace nodes. **The answer key's DOM (below) must match this**, except the comment anchor, which is the disclosed divergence.

- [ ] **Step 2: Write the failing gate test**

Add `RepoPaths` entries in `tests/Filament.Generator.Tests/RepoPaths.cs` (next to `RowsRazor`/`RowsAnswerKey`):

```csharp
    public static string IfRazor => Path.Combine(Root, "samples", "If", "If.razor");
    public static string IfAnswerKey => Path.Combine(Root, "samples", "If", "if.js");
```

Add a generate helper in `tests/Filament.Generator.Tests/GateTests.cs` `Generate` class (next to `RowsToTemp`):

```csharp
    public static string IfToTemp() => ToTemp(RepoPaths.IfRazor, "If");
```

Add the gate test to `IfTests.cs`:

```csharp
    [Fact]
    public void Gate_GeneratedIf_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.IfToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.IfAnswerKey);
        Assert.True(exit == 0,
            "PHASE: @if gate FAILED. Generated module is NOT alpha-equivalent to samples/If/if.js.\n" +
            stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }
```

- [ ] **Step 3: Run it to verify it fails**

Run: `dotnet test tests/Filament.Generator.Tests/Filament.Generator.Tests.csproj --filter "FullyQualifiedName~Gate_GeneratedIf"`
Expected: FAIL — `samples/If/If.razor` and `samples/If/if.js` do not exist yet, so the generator or canon errors.

- [ ] **Step 4: Write the gate input `samples/If/If.razor`**

```razor
<div id="wrap"><button id="t" @onclick="Toggle">t</button>@if (show)
{
    <span id="msg">hi</span>
}</div>

@code {
    private bool show = true;
    void Toggle() => show = !show;
}
```

- [ ] **Step 5: Write the hand-written answer key `samples/If/if.js`**

Match the generator's lowering AND Blazor's DOM contract from Step 1. Adjust whitespace text nodes to whatever Blazor actually ships (record it in the header comment). Baseline shape:

```js
/**
 * If — hand-written Filament answer key for samples/If/If.razor.
 *
 * Blazor DOM contract (read from IfRef_razor.g.cs, decision-64 method): <div id="wrap"> holds
 * <button id="t"> then the conditional <span id="msg">. Whitespace between siblings: <RECORD FROM
 * STEP 1 — add createTextNode('\n    ') nodes here if and only if Blazor ships them>.
 *
 * The @if lowers to a conditional list(): a 0/1 source over the condition, a constant key, and a
 * COMMENT ANCHOR. The comment node is a DISCLOSED +1-node divergence from Blazor (category of
 * decision #20's <!--!--> markers): Blazor positions conditional content via its render tree, not
 * a DOM comment. Removing it needs next-sibling anchoring, deferred.
 */

import { signal, effect, batch, setText, listen, insert, list } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const show = signal(true);
  function toggle() { show.value = !show.value; }

  const main = document.createElement('div');
  main.id = 'wrap';

  const btn = document.createElement('button');
  btn.id = 't';
  insert(btn, document.createTextNode('t'));
  insert(main, btn);

  const anchor = document.createComment('');
  insert(main, anchor);

  function ifBody() {
    const span = document.createElement('span');
    span.id = 'msg';
    insert(span, document.createTextNode('hi'));
    return span;
  }
  list(main, () => (show.value) ? [0] : [], () => 0, ifBody, anchor);

  listen(btn, 'click', () => batch(toggle));

  insert(target, main);
}
```

> Note: `Toggle()` performs one write, so per decision #68's batch rule it may or may not need `batch`; keep it consistent with the generator's output. Run canon (next step) — if it diverges on `batch(toggle)` vs `toggle`, match the generator (it is JUDGED, the key is the REFERENCE for shape only where the owner has not ruled — here just mirror what the generator emits, since there is no prior key for @if).

- [ ] **Step 6: Run the generator by hand and reconcile with canon**

```bash
dotnet run --project src/Filament.Generator -- samples/If/If.razor samples/If/.gen-check.js
node tools/canon.mjs samples/If/.gen-check.js samples/If/if.js
rm -f samples/If/.gen-check.js
```
Expected: `VERDICT: ALPHA-EQUIVALENT`, exit 0. If not, read the first-divergence token and adjust `if.js` (whitespace nodes, `batch`, id-vs-setAttr) to match the generator — the generator is not edited.

- [ ] **Step 7: Run the gate test to verify it passes**

Run: `dotnet test tests/Filament.Generator.Tests/Filament.Generator.Tests.csproj --filter "FullyQualifiedName~Gate_GeneratedIf"`
Expected: PASS.

- [ ] **Step 8: Add the snapshot + DOM-contract tests**

Add to `IfTests.cs`:

```csharp
    [Fact]
    public void Snapshot_EmittedIfJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.IfToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "If.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    [Fact]
    public void EmittedIf_HonoursTheContract()
    {
        var js = File.ReadAllText(Generate.IfToTemp());
        Assert.Contains("document.createComment('')", js);
        Assert.Contains("() => (show.value) ? [0] : []", js);
        Assert.DoesNotContain("innerHTML", js);
        Assert.DoesNotContain("textContent", js);
        Assert.DoesNotContain("'@onclick'", js);   // descriptors resolved (decision 53)
    }
```

Run: `dotnet test ... --filter "FullyQualifiedName~IfTests"` — the snapshot test writes `If.approved.js` on first run and fails; re-run and it passes. Review `If.approved.js` before committing.

- [ ] **Step 9: Remove the throwaway Blazor reference**

```bash
git status --short baseline/Counter.Blazor   # confirm only IfRef.razor is new
rm baseline/Counter.Blazor/IfRef.razor
```
The baseline stays pristine; the finding is recorded in `if.js`'s header.

- [ ] **Step 10: Commit**

```bash
git add samples/If/If.razor samples/If/if.js tests/Filament.Generator.Tests/Snapshots/If.approved.js \
        tests/Filament.Generator.Tests/RepoPaths.cs tests/Filament.Generator.Tests/GateTests.cs \
        tests/Filament.Generator.Tests/IfTests.cs
git commit -m "test(@if): canon gate against a Blazor-faithful answer key + snapshot"
```

---

### Task 3: Out-of-subset diagnostics for the deferred variants

**Files:**
- Create: `tests/Filament.Generator.Tests/Unsupported/IfElse.razor`, `Unsupported/IfNested.razor`, `Unsupported/IfMultiBody.razor`, `Unsupported/IfAtRoot.razor`
- Modify: `tests/Filament.Generator.Tests/DiagnosticTests.cs` (add InlineData rows; add mutation-probe coverage note)

**Interfaces:**
- Consumes: the `Refused(fixture)` helper (`DiagnosticTests.cs:479-496`), `ControlFlow_OutsideTheSubset_IsRefused_AtItsExactLocation` theory pattern.

- [ ] **Step 1: Write the failing diagnostic theory rows**

Add to `DiagnosticTests.cs` `ControlFlow_OutsideTheSubset_IsRefused_AtItsExactLocation` (compute exact line/col from each fixture with `cat -n` after creating them in Step 2 — refine these numbers then):

```csharp
    [InlineData("IfElse.razor",     3, 1,  "else-not-yet-implemented")]
    [InlineData("IfNested.razor",   2, 2,  "unsupported-if-body")]
    [InlineData("IfMultiBody.razor", 2, 2, "unsupported-if-body")]
    [InlineData("IfAtRoot.razor",   1, 1,  "template-code-at-root")]
```

- [ ] **Step 2: Create the fixtures**

`Unsupported/IfElse.razor`:
```razor
<div id="w">@if (show)
{
    <span id="a">a</span>
}
else
{
    <span id="b">b</span>
}</div>

@code { private bool show = true; void T() => show = !show; }
```

`Unsupported/IfNested.razor`:
```razor
<div id="w">@if (show)
{
    @if (other) { <span id="a">a</span> }
}</div>

@code { private bool show = true, other = true; void T() { show = !show; other = !other; } }
```

`Unsupported/IfMultiBody.razor`:
```razor
<div id="w">@if (show)
{
    <span id="a">a</span><span id="b">b</span>
}</div>

@code { private bool show = true; void T() => show = !show; }
```

`Unsupported/IfAtRoot.razor`:
```razor
@if (show)
{
    <span id="a">a</span>
}

@code { private bool show = true; void T() => show = !show; }
```

- [ ] **Step 3: Verify exact locations and finalize InlineData**

```bash
for f in IfElse IfNested IfMultiBody IfAtRoot; do
  echo "== $f =="; dotnet run --project src/Filament.Generator -- \
    tests/Filament.Generator.Tests/Unsupported/$f.razor samples/If/x.js; done
rm -f samples/If/x.js
```
Read each `file(line,col): FIL000x: [reason]` line and correct the InlineData `line`/`col`/`code` to match exactly. (`IfAtRoot` is refused by the existing `template-code-at-root` guard and is `FIL0003`; the others are `FIL0001`. If a code differs from the InlineData's implicit `FIL0001`, add the code to the assertion — mirror how `ControlFlow_...` builds its expected string; if it hardcodes `FIL0001`, split `IfAtRoot` into the `FIL0003`-aware theory or its own `[Fact]`.)

- [ ] **Step 4: Run the theory to verify it passes**

Run: `dotnet test ... --filter "FullyQualifiedName~ControlFlow_OutsideTheSubset_IsRefused"`
Expected: PASS for all rows.

- [ ] **Step 5: Mutation-test the guards (temporary probes)**

Confirm each guard is load-bearing:
1. In `CSharpFrontEnd.If`, comment out the `ifs.Else` refusal `return null;` → run `--filter IfElse` → it must now FAIL (IfElse compiles). Restore.
2. Comment out the `markup.Count != 1` refusal `return null;` → run `--filter "IfNested|IfMultiBody"` → they must FAIL. Restore.

```bash
grep -rn "MUTATION PROBE" src/   # must return nothing after restoring
```

- [ ] **Step 6: Run the full suite**

Run: `dotnet test tests/Filament.Generator.Tests/Filament.Generator.Tests.csproj`
Expected: all green, 0 failed.

- [ ] **Step 7: Commit**

```bash
git add tests/Filament.Generator.Tests/Unsupported/IfElse.razor \
        tests/Filament.Generator.Tests/Unsupported/IfNested.razor \
        tests/Filament.Generator.Tests/Unsupported/IfMultiBody.razor \
        tests/Filament.Generator.Tests/Unsupported/IfAtRoot.razor \
        tests/Filament.Generator.Tests/DiagnosticTests.cs
git commit -m "test(@if): located diagnostics for @else, nested, multi-node, and root @if"
```

---

### Task 4: Runtime-invariant test (the budget claim)

**Files:**
- Modify: `tests/Filament.Generator.Tests/IfTests.cs` (add the closed-primitives test)

**Interfaces:**
- Consumes: `Generate.IfToTemp()`, the allowed-exports list pattern from `RowsTests.EmittedJs_OnlyCallsClosedRuntimePrimitives`.

- [ ] **Step 1: Write the failing invariant test**

Add to `IfTests.cs` (mirror `RowsTests.cs:349-367`):

```csharp
    [Fact]
    public void EmittedIf_OnlyCallsClosedRuntimePrimitives_NoNewExport()
    {
        var js = File.ReadAllText(Generate.IfToTemp());
        var import = js.Split('\n').First(l => l.StartsWith("import "));

        string[] allowed =
            ["signal", "computed", "effect", "batch", "untrack", "setText", "setAttr", "listen", "insert", "remove", "list"];
        foreach (var name in import[(import.IndexOf('{') + 1)..import.IndexOf('}')]
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.True(allowed.Contains(name),
                $"'{name}' is not a runtime export. @if must add NO new primitive (decision: reuse list()).");

        // document.createComment is a DOM builtin, not a runtime import.
        Assert.Contains("document.createComment(''", js);
        Assert.DoesNotContain("when", import);
    }
```

- [ ] **Step 2: Run it — expect PASS** (the feature added no import)

Run: `dotnet test ... --filter "FullyQualifiedName~EmittedIf_OnlyCallsClosedRuntimePrimitives"`
Expected: PASS. If a new name appears in the import, the lowering used something outside `list()` — revisit Task 1's `EmitIf`.

- [ ] **Step 3: Verify the runtime is byte-untouched**

Run: `git diff --stat src/filament-runtime`
Expected: **empty output**. If not, revert any runtime change — this feature must not touch it.

- [ ] **Step 4: Commit**

```bash
git add tests/Filament.Generator.Tests/IfTests.cs
git commit -m "test(@if): assert zero new runtime primitive (closed-runtime invariant)"
```

---

### Task 5: In-browser behavioral verification (reactivity + effect disposal)

**Files:**
- Create: `src/filament-runtime/test/if-behavior.test.ts` (DOM-env behavioral test of the answer key)

**Interfaces:**
- Consumes: the DOM-env + MutationObserver assertion pattern from `src/filament-runtime/test/c3-counter.test.ts`; the answer key `samples/If/if.js`.

- [ ] **Step 1: Read the existing behavioral test's setup**

Read `src/filament-runtime/test/c3-counter.test.ts` for: the DOM environment (`// @vitest-environment` pragma or config), how a module is mounted, and how a `MutationObserver` (or the runtime's stats) counts DOM writes. Mirror that setup exactly.

- [ ] **Step 2: Write the failing behavioral test**

Create `src/filament-runtime/test/if-behavior.test.ts` (adapt the environment pragma/import to match c3-counter.test.ts):

```ts
// @vitest-environment happy-dom   // <- match c3-counter.test.ts's environment
import { describe, it, expect } from 'vitest';
import { mount } from '../../../samples/If/if.js';

describe('@if answer key behavior', () => {
  it('inserts the body on true, removes it on false, and disposes its effects', async () => {
    const root = document.createElement('div');
    document.body.appendChild(root);
    mount(root);

    const wrap = root.querySelector('#wrap')!;
    const btn = wrap.querySelector('#t') as HTMLButtonElement;

    // Shown initially (show = true).
    expect(wrap.querySelector('#msg')).not.toBeNull();

    // Toggle off -> body removed; the comment anchor stays.
    btn.click();
    expect(wrap.querySelector('#msg')).toBeNull();
    expect([...wrap.childNodes].some(n => n.nodeType === Node.COMMENT_NODE)).toBe(true);

    // Toggle on -> body re-created fresh.
    btn.click();
    expect(wrap.querySelector('#msg')).not.toBeNull();
  });
});
```

If `samples/If/if.js` has no reactive text binding inside the body, this proves insert/remove. To also prove **effect disposal**, extend the answer-key body with a reactive `@expr` and assert (via the runtime's `__filament` stats or a MutationObserver on `characterData`) that mutating the underlying signal while hidden produces **no** DOM write, and that showing again reflects the latest value. Only add this if `if.js`'s body carries a reactive binding; otherwise state in the test comment that disposal is covered structurally by `list()`'s row scope (already unit-tested in `list`'s own suite) and insert/remove is what this test adds.

- [ ] **Step 3: Run it to verify it fails, then passes**

Run: `cd src/filament-runtime && npm test -- if-behavior`
Expected: FAIL if the import path or environment is wrong; fix to match c3-counter.test.ts, then PASS.

- [ ] **Step 4: Run the full runtime verify (size gate included)**

Run: `cd src/filament-runtime && npm run verify`
Expected: build + typecheck + tests green, **size gate 1,943 B / 2,048 B unchanged**.

- [ ] **Step 5: Commit**

```bash
git add src/filament-runtime/test/if-behavior.test.ts
git commit -m "test(@if): in-browser insert/remove + disposal behavior of the answer key"
```

---

### Task 6: Record the arbitrage (DECISIONS + README)

**Files:**
- Modify: `DECISIONS.md` (append a new numbered entry, #81, in the repo's style)
- Modify: `README.md` (scope statement + test counts)

**Interfaces:** none (documentation). This is not optional polish — the repo's core discipline is that every arbitrage gets a numbered DECISIONS entry (the diff IS the result, #21/#51).

- [ ] **Step 1: Append DECISIONS entry #81**

Append to `DECISIONS.md` (match the existing numbered, dense style; French to match the ledger). Record, at minimum: `@if` moves from refused into the subset; the lowering is `list()` reuse with a **comment anchor** (zero new runtime primitive, runtime byte-untouched); the comment anchor is a **disclosed +1-node divergence** from Blazor (category of #20), to be re-measured if `@if` ever enters a measured app; the condition's reads are marked as template reads so a bool read only in `@if` is lifted; the **deferred variants** (`@else`, nesting, root-level, multi-node body) each raise a located diagnostic, mutation-tested; and the **honest ceiling** — one construct does not move the §8 verdict, RADICAL stays "not eliminated, not established."

- [ ] **Step 2: Update README**

In `README.md`, update the top scope statement to note `@if` is now in the subset (with the comment-anchor divergence disclosed), and bump the generator-suite test count to the new total.

- [ ] **Step 3: Commit**

```bash
git add DECISIONS.md README.md
git commit -m "docs(@if): record decision #81 (list() reuse, comment-anchor divergence, deferred variants)"
```

---

## Final verification (run after all tasks)

- [ ] `dotnet test tests/Filament.Generator.Tests/Filament.Generator.Tests.csproj` → all green, 0 failed.
- [ ] `cd src/filament-runtime && npm run verify` → green, size gate 1,943 B.
- [ ] `git diff --stat src/filament-runtime` → empty (runtime untouched).
- [ ] `node tools/canon.mjs` on a fresh generate of `samples/If/If.razor` vs `samples/If/if.js` → ALPHA-EQUIVALENT.

## Notes for the implementer

- **Line numbers are hints as of writing** and will drift as you edit; anchor on the exact code shown, not the number.
- **The condition-reactivity ordering (Task 1 Step 4) is the subtle part.** `MarkConditionReads` MUST run at step 2c (right after `MarkTemplateReads`, line 419-420), before `Body(...)` (433) and `TranslateSlots(...)` (434) read `IsSignal`. Marking inside the `If` builder (line 435) is too late — method bodies would translate the condition field as a plain `let`.
- **`RegionOps` recurses,** so a nested `@if` in a body naturally builds a nested `IfOp`; the `markup.Count != 1 || ops.Count != markup.Count` check refuses it (the nested op is not a `MarkupOp`). That is intended — do not "fix" it into support.
- **Do not touch the runtime.** If you feel you need a new primitive, stop: the design chose `list()` reuse precisely to avoid that, and the invariant test (Task 4) will fail.
