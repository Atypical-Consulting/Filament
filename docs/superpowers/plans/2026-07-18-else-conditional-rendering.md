# `@else` / `@else if` Conditional Rendering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `@else` / `@else if` from refused (`else-not-yet-implemented`) into the compiled subset — multi-branch conditional rendering — lowered onto the existing `list()` primitive with zero runtime change.

**Architecture:** Generalize the `@if` lowering from one branch to N. `IfOp` becomes a list of `IfBranch(Cond?, Body)` (the trailing `@else` is a branch with a null condition). The front-end `If()` walks the Roslyn else-chain; `EmitIf` emits one body function per branch and a keyed `list()` whose single item's value **is** the active branch index — flipping any condition changes the key, so `reconcile` swaps the branch. A **plain `@if` (one branch) keeps its exact #81 emission byte-for-byte** so the existing gate/snapshot hold. Zero new runtime primitive.

**Tech Stack:** C# 13 / .NET 10 (generator + xUnit), Roslyn (`Microsoft.CodeAnalysis.CSharp`), Razor Language 6.0.36, TypeScript runtime (Vitest), `node tools/canon.mjs` (esbuild-based alpha-equivalence).

## Global Constraints

- **Runtime SOURCE is CLOSED and untouched.** No new export from `src/filament-runtime/src/`. Size gate stays **1,943 B / 2,048 B**. `git diff --stat src/filament-runtime/src` must be empty at the end (a new test under `src/filament-runtime/test/` in Task 5 is fine — not shipped runtime code).
- **No silently-wrong output.** Every construct outside this cut's subset raises a **located** diagnostic (`file(line,col): FIL000x: [reason]`) and writes **no file**. `FIL0001` = out-of-subset C#, `FIL0003` = out-of-subset Razor.
- **The answer key is the REFERENCE; the generator is JUDGED** (decisions #21/#51). Never edit an answer key to make the gate pass; only the owner corrects a key against the baseline (#64/#80). There is no prior `@else` key, so canon reconciliation settles undetermined shape choices — mirror the generator where the owner has not ruled (the #81/#75 pattern).
- **DOM contract is pinned against Blazor's own generated `BuildRenderTree`** (#64/#76), read BEFORE looking at generator output (#81 reviewer note: seed a genuine divergence). The comment anchor is a disclosed +1-node divergence (category of #20).
- **Plain `@if` stays byte-for-byte unchanged.** The one-branch path in `EmitIf` must reproduce #81's exact emission so `samples/If/if.js`, `If.approved.js`, and `IfTests` are untouched.
- **Scope of this cut:** `@if (c0){<e0>} else if (c1){<e1>} … else {<en>}`, any number of branches, each body **exactly one element**, nested inside an element. OUT (→ located diagnostic): a multi-node branch body, nested control flow inside a branch, `@if`/`@else` at the template root.
- **Commit after every green step.** TDD: failing test first.
- Build: `dotnet build src/Filament.Generator/Filament.Generator.csproj -c Debug`. Suite: `dotnet test tests/Filament.Generator.Tests/Filament.Generator.Tests.csproj` (rebuilds the generator DLL the tests shell out to). **Baseline before this plan: 172 passed / 0 failed.**

---

### Task 1: Generalize `@if` to N branches (`IfBranch`, chain-walking `If()`, N-branch `EmitIf`)

**Files:**
- Modify: `src/Filament.Generator/TemplatePlan.cs` (replace `IfOp` at ~105; add `IfBranch`)
- Modify: `src/Filament.Generator/CSharpFrontEnd.cs` (rewrite `If` at ~582-611; add `BranchBody`)
- Modify: `src/Filament.Generator/TemplateCompiler.cs` (rewrite `EmitIf` at ~930-963; add `EmitBranchFn`, `IfSource`, `IfCreate`)
- Create: `tests/Filament.Generator.Tests/IfElseTests.cs` (happy-path unit test + `Compile` helper)
- Modify: `tests/Filament.Generator.Tests/DiagnosticTests.cs` (remove the now-compiling `IfElse.razor` refusal rows)
- Delete: `tests/Filament.Generator.Tests/Unsupported/IfElse.razor` (a plain two-branch if/else now compiles)

**Interfaces:**
- Produces: `IfOp(IReadOnlyList<IfBranch> Branches) : TemplateOp`; `IfBranch(string? Cond, IntermediateNode Body)` — `Cond` is the branch condition already translated to JS (e.g. `n.value === 0`), null for the trailing `@else`; `Body` is that branch's single markup node.
- Produces (CSharpFrontEnd, private): `IfOp? If(IfStatementSyntax, IReadOnlyDictionary<string, IntermediateNode>)`, `IntermediateNode? BranchBody(StatementSyntax, IReadOnlyDictionary<string, IntermediateNode>)`.
- Produces (TemplateCompiler, private): `void EmitIf(IfOp, string container)`, `bool EmitBranchFn(IntermediateNode, string fnName)`, `static string IfSource(IReadOnlyList<IfBranch>)`, `static string IfCreate(IReadOnlyList<string>)`. Consumes existing `EmitNode`, `_create`, `_bindings`, `_consumedKey`, `_used`, `_if`, `Unique`.

- [ ] **Step 1: Write the failing happy-path test**

Create `tests/Filament.Generator.Tests/IfElseTests.cs`:

```csharp
using Xunit;

namespace Filament.Generator.Tests;

public class IfElseTests
{
    /// <summary>
    /// An @if / @else if / @else chain compiles to ONE keyed list() whose single item's value is
    /// the active branch index: source is a ternary chain, the key IS the index, and create()
    /// dispatches on it. Every branch condition lifts its field to a signal (read-by-template).
    /// </summary>
    [Fact]
    public void IfElseChain_CompilesToAKeyedList_BranchIndexIsTheKey()
    {
        var js = Compile(
            """
            <div id="wrap"><button id="t" @onclick="Next">t</button>@if (n == 0)
            {
                <span id="a">a</span>
            }
            else if (n == 1)
            {
                <span id="b">b</span>
            }
            else
            {
                <span id="c">c</span>
            }</div>

            @code {
                private int n = 0;
                void Next() { n = (n + 1) % 3; }
            }
            """);

        Assert.Contains("const n = signal(0);", js);                                        // lifted by conditions
        Assert.Contains("document.createComment('')", js);                                  // the anchor
        Assert.Contains("() => (n.value === 0) ? [0] : (n.value === 1) ? [1] : [2]", js);   // ternary source
        Assert.Contains("(i) => i,", js);                                                   // key = branch index
        Assert.Contains("i === 0 ?", js);                                                   // dispatch create
        Assert.Contains("document.createElement('span')", js);                              // branch subtrees
        Assert.DoesNotContain("[else-not-yet-implemented]", js);
        Assert.DoesNotContain("when(", js);                                                 // no new primitive
    }

    /// <summary>Compile an inline .razor from samples/IfElse so the runtime specifier resolves.</summary>
    static string Compile(string razor)
    {
        var dir = Path.Combine(RepoPaths.Root, "samples", "IfElse");
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

Run: `dotnet test tests/Filament.Generator.Tests/Filament.Generator.Tests.csproj --filter "FullyQualifiedName~IfElseChain_CompilesToAKeyedList"`
Expected: FAIL — the generator refuses with `else-not-yet-implemented`, so `exit != 0` and `Compile`'s `Assert.True(exit == 0, …)` throws.

- [ ] **Step 3: Replace `IfOp` and add `IfBranch`**

In `src/Filament.Generator/TemplatePlan.cs`, replace the `IfOp` record (~100-105) with:

```csharp
/// <summary>
/// `@if (c0) { &lt;b0&gt; } else if (c1) { &lt;b1&gt; } … else { &lt;bn&gt; }` -> a conditional list()
/// whose single item's value is the ACTIVE BRANCH INDEX, with a comment anchor. A plain @if is the
/// one-branch case, and it keeps its exact #81 emission.
/// </summary>
/// <param name="Branches">the if / else-if / else branches, in source order</param>
public sealed record IfOp(IReadOnlyList<IfBranch> Branches) : TemplateOp;

/// <param name="Cond">the branch condition, already translated to JS (e.g. "n.value === 0"),
/// or null for the trailing @else</param>
/// <param name="Body">the ONE markup node this branch produces</param>
public sealed record IfBranch(string? Cond, IntermediateNode Body);
```

- [ ] **Step 4: Rewrite `If()` to walk the else-chain, and add `BranchBody`**

In `src/Filament.Generator/CSharpFrontEnd.cs`, replace the whole `If` method (~582-611) with:

```csharp
    /// <summary>
    /// `@if (c0) { &lt;e0&gt; } else if (c1) { &lt;e1&gt; } … else { &lt;en&gt; }` -> IfOp with one
    /// IfBranch per branch (the trailing @else, if any, is a branch with a null condition), lowered
    /// to a keyed list() by TemplateCompiler. This cut's subset: each branch body is exactly one
    /// element. Multi-node branch bodies and nested control flow in a branch are refused PER BRANCH
    /// by BranchBody; @if at the template root is refused earlier by the root-code guard.
    /// </summary>
    IfOp? If(IfStatementSyntax ifs, IReadOnlyDictionary<string, IntermediateNode> markers)
    {
        var branches = new List<IfBranch>();
        var cur = ifs;
        while (true)
        {
            if (BranchBody(cur.Statement, markers) is not { } body) return null;
            branches.Add(new IfBranch(Expr(cur.Condition), body));

            if (cur.Else is not { } els) break;               // if / else-if chain ended, no @else
            if (els.Statement is IfStatementSyntax nested)    // "else if (...)"
            {
                cur = nested;
                continue;
            }
            if (BranchBody(els.Statement, markers) is not { } elseBody) return null;  // trailing "else { … }"
            branches.Add(new IfBranch(null, elseBody));
            break;
        }
        return new IfOp(branches);
    }

    /// <summary>
    /// One @if/@else branch body -> the single markup node it must produce, or null (a located
    /// refusal already emitted) if it is not exactly one element. Shared by every branch, so the
    /// "exactly one element" rule applies uniformly to if, else-if, and else.
    /// </summary>
    IntermediateNode? BranchBody(StatementSyntax stmt, IReadOnlyDictionary<string, IntermediateNode> markers)
    {
        // The ORIGINAL statement nodes, never a SyntaxFactory copy (see ForEach).
        IEnumerable<StatementSyntax> body = stmt is BlockSyntax b ? b.Statements : [stmt];
        var ops = RegionOps(body, markers);
        var markup = ops.OfType<MarkupOp>().ToList();

        if (markup.Count != 1 || ops.Count != markup.Count)
        {
            Refuse("unsupported-if-body",
                $"a template @if / @else branch body must be exactly ONE element and nothing else; this " +
                $"one produces {ops.Count} thing(s). @if lowers to a conditional list() whose create() " +
                "returns ONE root node per branch, so a body with two roots, a stray text node, or nested " +
                "control flow has no single thing to insert and remove. Refusing to emit.",
                stmt.SpanStart);
            return null;
        }
        return markup[0].Node;
    }
```

Note: the `else-not-yet-implemented` refusal is **gone** (its behavior is now compilation). `BranchBody(cur.Statement, …)` uses `cur.Statement.SpanStart` for the first branch, identical to the old `ifs.Statement.SpanStart`, so `IfNested`/`IfMultiBody` keep their `(2,1)` locations.

- [ ] **Step 5: Rewrite `EmitIf` for N branches (single-branch path unchanged)**

In `src/Filament.Generator/TemplateCompiler.cs`, replace the whole `EmitIf` method (~930-963) with:

```csharp
    void EmitIf(IfOp op, string container)
    {
        var id = _if++;
        var anchor = $"_if{id}";
        _create.Add($"const {anchor} = document.createComment('');");
        _used.Add("insert");
        _create.Add($"insert({container}, {anchor});");
        _used.Add("list");

        // PLAIN @if (one branch, no @else): the exact #81 emission, byte-for-byte, so the @if gate
        // and snapshot still hold. A 0/1 source, a constant key, the body function passed directly.
        if (op.Branches.Count == 1)
        {
            var fn = Unique("ifBody");
            if (!EmitBranchFn(op.Branches[0].Body, fn)) return; // body refused; nothing emitted
            _bindings.Add($"list({container}, () => ({op.Branches[0].Cond}) ? [0] : [], () => 0, {fn}, {anchor});");
            return;
        }

        // MULTI-BRANCH @if/@else if/@else: the single item's VALUE is the active branch index; the
        // key IS that index, so flipping any condition changes the key and list() swaps the branch.
        // Names carry `id` so two @if ops in one module never collide.
        var fns = new List<string>();
        for (var i = 0; i < op.Branches.Count; i++)
        {
            var fn = Unique($"ifBody{id}_{i}");
            if (!EmitBranchFn(op.Branches[i].Body, fn)) return;
            fns.Add(fn);
        }
        _bindings.Add($"list({container}, {IfSource(op.Branches)}, (i) => i, {IfCreate(fns)}, {anchor});");
    }

    /// <summary>
    /// Build one branch body into a `function {fnName}() { …; return root; }` binding, exactly as
    /// EmitList builds a row (fresh create/binding pair, adopted key scope). Returns false — nothing
    /// emitted — if the body was refused.
    /// </summary>
    bool EmitBranchFn(IntermediateNode bodyNode, string fnName)
    {
        var outerCreate = _create;
        var outerBindings = _bindings;
        var outerKey = _consumedKey;
        _create = [];
        _bindings = [];
        _consumedKey = null;

        var root = EmitNode(bodyNode, parent: null);
        var lines = new List<string>();
        lines.AddRange(_create);
        lines.AddRange(_bindings);

        _create = outerCreate;
        _bindings = outerBindings;
        _consumedKey = outerKey;

        if (root is null) return false;
        lines.Add($"return {root};");
        _bindings.Add($"function {fnName}() {{\n" + string.Join("\n", lines.Select(l => "  " + l)) + "\n}");
        return true;
    }

    /// <summary>`() => (c0) ? [0] : (c1) ? [1] : [n]` — a trailing @else is `: [n]`; a chain with no
    /// @else ends `: []` (nothing shows when none match, generalizing plain @if's `? [0] : []`).</summary>
    static string IfSource(IReadOnlyList<IfBranch> branches)
    {
        var parts = new List<string>();
        for (var i = 0; i < branches.Count; i++)
        {
            if (branches[i].Cond is { } c) parts.Add($"({c}) ? [{i}] : ");
            else return "() => " + string.Concat(parts) + $"[{i}]";   // trailing @else
        }
        return "() => " + string.Concat(parts) + "[]";
    }

    /// <summary>`(i) => i === 0 ? f0() : i === 1 ? f1() : fN()` — the last branch needs no test.</summary>
    static string IfCreate(IReadOnlyList<string> fns)
    {
        var parts = new List<string>();
        for (var i = 0; i < fns.Count - 1; i++) parts.Add($"i === {i} ? {fns[i]}() : ");
        return "(i) => " + string.Concat(parts) + $"{fns[^1]}()";
    }
```

- [ ] **Step 6: Run the happy-path test to verify it passes**

Run: `dotnet test tests/Filament.Generator.Tests/Filament.Generator.Tests.csproj --filter "FullyQualifiedName~IfElseChain_CompilesToAKeyedList"`
Expected: PASS.
If `const n = signal(0)` is missing (the field stayed `let n = 0`), a branch condition's reads were not counted — confirm `MarkConditionReads` runs at step 2c (`CSharpFrontEnd.cs:419-420`); it already walks `DescendantNodes().OfType<IfStatementSyntax>()`, which includes the nested `else if`, so no change is needed there.

- [ ] **Step 7: Verify plain `@if` is byte-for-byte unchanged**

Run: `dotnet test tests/Filament.Generator.Tests/Filament.Generator.Tests.csproj --filter "FullyQualifiedName~IfTests"`
Expected: all `IfTests` PASS (gate, snapshot, contract, closed-runtime, plain-if unit). If `Snapshot_EmittedIfJs_MatchesApprovedBytes` fails, the single-branch path diverged from #81 — the `op.Branches.Count == 1` block must reproduce the old `() => (cond) ? [0] : [], () => 0, {fn}` emission with `Unique("ifBody")` and anchor `_if{id}` exactly.

- [ ] **Step 8: Handle the now-compiling `IfElse.razor` fallout (keeps the suite green)**

The plain two-branch `Unsupported/IfElse.razor` compiles now, so it must leave the refusal theories (the same move #81 made for `If.razor`). In `tests/Filament.Generator.Tests/DiagnosticTests.cs`:
- Delete the row `[InlineData("IfElse.razor", 5, 1, "else-not-yet-implemented")]` from `ControlFlow_OutsideTheSubset_IsRefused_AtItsExactLocation` (~75), leaving `Foreach.razor`, `IfNested.razor`, `IfMultiBody.razor`.
- Delete `[InlineData("IfElse.razor")]` from `ARefusalWritesNoFile` (~485), leaving `IfNested`, `IfMultiBody`, `IfAtRoot`.

Then delete the fixture:

```bash
git rm tests/Filament.Generator.Tests/Unsupported/IfElse.razor
```

- [ ] **Step 9: Run the full suite**

Run: `dotnet test tests/Filament.Generator.Tests/Filament.Generator.Tests.csproj`
Expected: all green, 0 failed (the new `IfElseTests` unit test passes; the deferred `IfNested`/`IfMultiBody`/`IfAtRoot` still refuse; plain `@if` unregressed). If a `Foreach`/root/nested test broke, `RegionOps` recursion changed behavior for a case still meant to be refused — re-read Step 4 before "fixing" it (those refusals are intended).

- [ ] **Step 10: Commit**

```bash
git add src/Filament.Generator/TemplatePlan.cs src/Filament.Generator/CSharpFrontEnd.cs \
        src/Filament.Generator/TemplateCompiler.cs tests/Filament.Generator.Tests/IfElseTests.cs \
        tests/Filament.Generator.Tests/DiagnosticTests.cs
git rm tests/Filament.Generator.Tests/Unsupported/IfElse.razor
git commit -m "feat(@else): generalize @if to N branches — keyed list(), branch index is the key"
```

---

### Task 2: The canon gate — Blazor-faithful `ifelse.js` + alpha-equivalence

**Files:**
- Create (temporary): `baseline/Counter.Blazor/IfElseRef.razor` (read Blazor's `BuildRenderTree`; removed at the end)
- Create: `samples/IfElse/IfElse.razor` (gate input), `samples/IfElse/ifelse.js` (hand-written answer key)
- Create: `tests/Filament.Generator.Tests/Snapshots/IfElse.approved.js`
- Modify: `tests/Filament.Generator.Tests/RepoPaths.cs` (add `IfElseRazor`, `IfElseAnswerKey`)
- Modify: `tests/Filament.Generator.Tests/GateTests.cs` (add `Generate.IfElseToTemp()`)
- Modify: `tests/Filament.Generator.Tests/IfElseTests.cs` (gate + snapshot + contract tests)

**Interfaces:**
- Consumes: `Run.Node(RepoPaths.Canon, generated, answerKey)`, `Generate.IfElseToTemp()`, `Run.Generator`.
- Produces: `RepoPaths.IfElseRazor` → `samples/IfElse/IfElse.razor`, `RepoPaths.IfElseAnswerKey` → `samples/IfElse/ifelse.js`.

- [ ] **Step 1: Establish Blazor's DOM contract for `@if/@else if/@else`**

Create `baseline/Counter.Blazor/IfElseRef.razor` (not referenced by the app, only compiled):

```razor
<div id="wrap"><button id="t" @onclick="Next">t</button>@if (n == 0)
{
    <span id="a">a</span>
}
else if (n == 1)
{
    <span id="b">b</span>
}
else
{
    <span id="c">c</span>
}</div>

@code {
    private int n = 0;
    void Next() { n = (n + 1) % 3; }
}
```

Build and read Blazor's generated render tree:

```bash
dotnet build baseline/Counter.Blazor -c Debug
find baseline/Counter.Blazor/obj -name "IfElseRef*.g.cs"
```

Open the `.g.cs` and record, in a comment you will paste into `ifelse.js`: whether Blazor `OpenRegion`/`CloseRegion`s the branches (it does for if/else-if/else — compiler bookkeeping, **no DOM consequence**, not reproduced); whether any whitespace text node is `AddMarkupContent`ed between `</button>` and the conditional or after it before `</div>` (there is none in this source — the tags are adjacent); and that each branch's content is exactly one `<span>`. **The answer key's DOM must match this**, except the comment anchor (the disclosed divergence).

- [ ] **Step 2: Write the failing gate test**

Add to `tests/Filament.Generator.Tests/RepoPaths.cs`, after `IfAnswerKey` (~23):

```csharp
    public static string IfElseRazor => Path.Combine(Root, "samples", "IfElse", "IfElse.razor");

    /// <summary>The @else SPEC (decisions 21/51). Never edited to make a gate pass.</summary>
    public static string IfElseAnswerKey => Path.Combine(Root, "samples", "IfElse", "ifelse.js");
```

Add to the `Generate` class in `tests/Filament.Generator.Tests/GateTests.cs`, after `IfToTemp` (~259):

```csharp
    public static string IfElseToTemp() => ToTemp(RepoPaths.IfElseRazor, "IfElse");
```

Add the gate test to `IfElseTests.cs`:

```csharp
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the emitted module is alpha-equivalent to the
    /// hand-written samples/IfElse/ifelse.js. The key is the SPEC and REFERENCE; the generator is
    /// JUDGED. Never edit the key to make this pass except to correct it against the BASELINE.
    /// </summary>
    [Fact]
    public void Gate_GeneratedIfElse_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.IfElseToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.IfElseAnswerKey);
        Assert.True(exit == 0,
            "PHASE: @else gate FAILED. Generated module is NOT alpha-equivalent to samples/IfElse/ifelse.js.\n" +
            stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }
```

- [ ] **Step 3: Run it to verify it fails**

Run: `dotnet test tests/Filament.Generator.Tests/Filament.Generator.Tests.csproj --filter "FullyQualifiedName~Gate_GeneratedIfElse"`
Expected: FAIL — `samples/IfElse/IfElse.razor` and `ifelse.js` do not exist yet.

- [ ] **Step 4: Write the gate input `samples/IfElse/IfElse.razor`**

```razor
<div id="wrap"><button id="t" @onclick="Next">t</button>@if (n == 0)
{
    <span id="a">a</span>
}
else if (n == 1)
{
    <span id="b">b</span>
}
else
{
    <span id="c">c</span>
}</div>

@code {
    private int n = 0;
    void Next() { n = (n + 1) % 3; }
}
```

- [ ] **Step 5: Write the hand-written answer key `samples/IfElse/ifelse.js`**

Written to Blazor's DOM contract from Step 1. Canon is alpha-equivalence (names are normalized), so branch-function names may differ from the generator's `ifBody0_*`; structure and statement order must match. Baseline:

```js
/**
 * IfElse — hand-written Filament answer key for samples/IfElse/IfElse.razor.
 *
 * Blazor DOM contract (read from IfElseRef*.g.cs, decision-64 method): <div id="wrap"> holds
 * <button id="t"> then exactly ONE conditional <span>, whose id/text is a|b|c by branch. Blazor
 * OpenRegion/CloseRegions the if/else-if/else branches — compiler bookkeeping with NO DOM-visible
 * consequence (see samples/If/if.js finding #2) — so this key reproduces only the active branch's
 * DOM, not the regions. No whitespace text nodes: the source tags are adjacent (<RECORD FROM STEP 1>).
 *
 * The @if/@else lowers to ONE conditional list(): the single item's VALUE is the active branch index
 * (0/1/2), the key IS that index, and create() dispatches on it — so flipping a condition changes the
 * key and list() swaps the branch. A COMMENT ANCHOR positions it among its siblings: a DISCLOSED
 * +1-node divergence from Blazor (category of decision #20). Removing it needs next-sibling anchoring,
 * deferred. Zero new runtime primitive — same list() as @foreach and @if.
 *
 * Next() performs one write, so per decision #68's batch rule it gets NO batch(); it is named by one
 * @onclick and called nowhere else, so decision #68's single-use inlining folds it into the handler.
 */

import { signal, listen, insert, list } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const n = signal(0);

  const main = document.createElement('div');
  main.id = 'wrap';

  const btn = document.createElement('button');
  btn.id = 't';
  insert(btn, document.createTextNode('t'));
  insert(main, btn);

  const anchor = document.createComment('');
  insert(main, anchor);

  function branch0() {
    const span = document.createElement('span');
    span.id = 'a';
    insert(span, document.createTextNode('a'));
    return span;
  }
  function branch1() {
    const span = document.createElement('span');
    span.id = 'b';
    insert(span, document.createTextNode('b'));
    return span;
  }
  function branch2() {
    const span = document.createElement('span');
    span.id = 'c';
    insert(span, document.createTextNode('c'));
    return span;
  }
  list(main,
    () => (n.value === 0) ? [0] : (n.value === 1) ? [1] : [2],
    (i) => i,
    (i) => i === 0 ? branch0() : i === 1 ? branch1() : branch2(),
    anchor);

  listen(btn, 'click', () => {
    n.value = (n.value + 1) % 3;
  });

  insert(target, main);
}
```

- [ ] **Step 6: Run the generator by hand and reconcile with canon**

```bash
dotnet run --project src/Filament.Generator -- samples/IfElse/IfElse.razor samples/IfElse/.gen-check.js
node tools/canon.mjs samples/IfElse/.gen-check.js samples/IfElse/ifelse.js
rm -f samples/IfElse/.gen-check.js
```
Expected: `VERDICT: ALPHA-EQUIVALENT`, exit 0. If not, read the first-divergence token and adjust `ifelse.js` to match the generator — the generator is not edited. Likely reconciliation points: statement ORDER (branch functions vs `list()` vs `listen`), whether `Next` inlines, and the exact condition JS (`n == 0` → `n.value === 0`, confirmed mapped at `CSharpFrontEnd.cs:1918`).

- [ ] **Step 7: Run the gate test to verify it passes**

Run: `dotnet test tests/Filament.Generator.Tests/Filament.Generator.Tests.csproj --filter "FullyQualifiedName~Gate_GeneratedIfElse"`
Expected: PASS.

- [ ] **Step 8: Add the snapshot + contract tests**

Add to `IfElseTests.cs`:

```csharp
    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedIfElseJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.IfElseToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "IfElse.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>The snapshot is only a wall if it pins the actual contract: one anchor, the ternary
    /// index source, the index key, and the dispatch create.</summary>
    [Fact]
    public void EmittedIfElse_HonoursTheContract()
    {
        var js = File.ReadAllText(Generate.IfElseToTemp());
        Assert.Contains("document.createComment('')", js);
        Assert.Contains("() => (n.value === 0) ? [0] : (n.value === 1) ? [1] : [2]", js);
        Assert.Contains("(i) => i,", js);
        Assert.Contains("i === 0 ?", js);
        Assert.DoesNotContain("innerHTML", js);
        Assert.DoesNotContain("'@onclick'", js);   // descriptors resolved (decision 53)
    }
```

Run: `dotnet test … --filter "FullyQualifiedName~IfElseTests"` — the snapshot test writes `IfElse.approved.js` on first run and fails; **review that file**, then re-run and it passes.

- [ ] **Step 9: Remove the throwaway Blazor reference**

```bash
git status --short baseline/Counter.Blazor   # confirm only IfElseRef.razor is new
rm baseline/Counter.Blazor/IfElseRef.razor
```
The baseline stays pristine; the finding is recorded in `ifelse.js`'s header.

- [ ] **Step 10: Commit**

```bash
git add samples/IfElse/IfElse.razor samples/IfElse/ifelse.js \
        tests/Filament.Generator.Tests/Snapshots/IfElse.approved.js \
        tests/Filament.Generator.Tests/RepoPaths.cs tests/Filament.Generator.Tests/GateTests.cs \
        tests/Filament.Generator.Tests/IfElseTests.cs
git commit -m "test(@else): canon gate against a Blazor-faithful answer key + snapshot"
```

---

### Task 3: The else-branch deferred variant (multi-node `@else` body)

**Files:**
- Create: `tests/Filament.Generator.Tests/Unsupported/IfElseMultiBody.razor` (a multi-node `@else` body)
- Modify: `tests/Filament.Generator.Tests/DiagnosticTests.cs` (add the new fixture's rows)

**Interfaces:** consumes the `Refused(fixture)` helper and the `ControlFlow_OutsideTheSubset_IsRefused_AtItsExactLocation` / `ARefusalWritesNoFile` theories. (The `IfElse.razor` refusal rows were already removed in Task 1.)

- [ ] **Step 1: Add the else-branch deferred fixture**

Create `tests/Filament.Generator.Tests/Unsupported/IfElseMultiBody.razor` (the `@else` body has two elements → refused per-branch, proving `BranchBody` guards the else branch too):

```razor
<div id="w">@if (show)
{
    <span id="a">a</span>
}
else
{
    <span id="b">b</span><span id="c">c</span>
}</div>

@code { private bool show = true; void T() { show = !show; } }
```

- [ ] **Step 2: Determine the exact refusal location, then add the theory rows**

```bash
dotnet build src/Filament.Generator/Filament.Generator.csproj -c Debug
dotnet run --project src/Filament.Generator -- \
  tests/Filament.Generator.Tests/Unsupported/IfElseMultiBody.razor samples/IfElse/x.js
rm -f samples/IfElse/x.js
```
Read the `IfElseMultiBody.razor(line,col): FIL0001: [unsupported-if-body]` line. Then add to `ControlFlow_OutsideTheSubset_IsRefused_AtItsExactLocation` (using the exact `line`/`col` printed):

```csharp
    [InlineData("IfElseMultiBody.razor", <line>, <col>, "unsupported-if-body")]
```

And add to `ARefusalWritesNoFile`:

```csharp
    [InlineData("IfElseMultiBody.razor")]
```

- [ ] **Step 3: Run the diagnostic theories to verify they pass**

Run: `dotnet test … --filter "FullyQualifiedName~ControlFlow_OutsideTheSubset_IsRefused|FullyQualifiedName~ARefusalWritesNoFile"`
Expected: PASS for all rows (including the new `IfElseMultiBody` and the still-refused `IfNested`/`IfMultiBody`/`IfAtRoot`).

- [ ] **Step 4: Mutation-test the per-branch guard (temporary probe)**

Confirm the guard is load-bearing on the ELSE branch:
1. In `CSharpFrontEnd.BranchBody`, comment out the `return null;` inside the `markup.Count != 1` block → run `dotnet test … --filter "IfElseMultiBody"` → it must now FAIL (the else body compiles). Restore.

```bash
grep -rn "MUTATION PROBE" src/   # must return nothing after restoring
```

- [ ] **Step 5: Run the full suite**

Run: `dotnet test tests/Filament.Generator.Tests/Filament.Generator.Tests.csproj`
Expected: all green, 0 failed.

- [ ] **Step 6: Commit**

```bash
git add tests/Filament.Generator.Tests/Unsupported/IfElseMultiBody.razor \
        tests/Filament.Generator.Tests/DiagnosticTests.cs
git commit -m "test(@else): refuse a multi-node else body (per-branch guard, mutation-tested)"
```

---

### Task 4: Closed-runtime invariant + every-branch reactivity

**Files:**
- Modify: `tests/Filament.Generator.Tests/IfElseTests.cs` (add two tests)

**Interfaces:** consumes `Generate.IfElseToTemp()`, `Compile` (Task 1), the allowed-exports pattern from `IfTests.EmittedIf_OnlyCallsClosedRuntimePrimitives_NoNewExport`.

- [ ] **Step 1: Write the closed-runtime invariant test**

Add to `IfElseTests.cs`:

```csharp
    /// <summary>Closed-runtime invariant: @else emits NO new runtime primitive (reuses list()).
    /// The anchor is a DOM builtin (document.createComment), not a runtime import.</summary>
    [Fact]
    public void EmittedIfElse_OnlyCallsClosedRuntimePrimitives_NoNewExport()
    {
        var js = File.ReadAllText(Generate.IfElseToTemp());
        var import = js.Split('\n').First(l => l.StartsWith("import "));

        string[] allowed =
            ["signal", "computed", "effect", "batch", "untrack", "setText", "setAttr", "listen", "insert", "remove", "list"];
        foreach (var name in import[(import.IndexOf('{') + 1)..import.IndexOf('}')]
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.True(allowed.Contains(name),
                $"'{name}' is not a runtime export. @else must add NO new primitive (decision: reuse list()).");

        Assert.Contains("document.createComment(''", js);
        Assert.DoesNotContain("when", import);
    }
```

- [ ] **Step 2: Write the every-branch reactivity test**

A field read ONLY in a later branch condition must still lift to a signal (proves `MarkConditionReads` covers the whole chain). Add to `IfElseTests.cs`:

```csharp
    /// <summary>
    /// A field read ONLY in an `else if` condition (never in the @if condition or a binding) must
    /// still lift to a signal — otherwise that branch never reacts. Locks MarkConditionReads walking
    /// the whole else-chain (DescendantNodes over the method body already does this).
    /// </summary>
    [Fact]
    public void EveryBranchCondition_LiftsItsField_NotJustTheFirst()
    {
        var js = Compile(
            """
            <div id="wrap"><button id="t" @onclick="Flip">t</button>@if (a)
            {
                <span id="x">x</span>
            }
            else if (b)
            {
                <span id="y">y</span>
            }</div>

            @code {
                private bool a = true;
                private bool b = false;
                void Flip() { a = !a; b = !b; }
            }
            """);

        Assert.Contains("const a = signal(true);", js);    // read by the @if condition
        Assert.Contains("const b = signal(false);", js);   // read ONLY by the else-if condition
        Assert.Contains("(b.value) ? [1] : []", js);       // no trailing @else -> [] when none match
    }
```

- [ ] **Step 3: Run both tests to verify they pass**

Run: `dotnet test … --filter "FullyQualifiedName~EmittedIfElse_OnlyCallsClosedRuntimePrimitives|FullyQualifiedName~EveryBranchCondition_LiftsItsField"`
Expected: PASS. If `const b = signal(false)` is missing, `b` stayed a plain `let` — the else-if condition's reads were not marked; re-confirm `MarkConditionReads` walks `DescendantNodes()` (it does) and runs before `Body`/`TranslateSlots`.

- [ ] **Step 4: Verify the runtime SOURCE is byte-untouched**

Run: `git diff --stat src/filament-runtime/src`
Expected: **empty output**. If not, revert any runtime-source change — this feature must not touch it.

- [ ] **Step 5: Commit**

```bash
git add tests/Filament.Generator.Tests/IfElseTests.cs
git commit -m "test(@else): closed-runtime invariant + every-branch condition lifts"
```

---

### Task 5: In-browser behavioral verification (branch swap)

**Files:**
- Create: `src/filament-runtime/test/ifelse-behavior.test.ts` (DOM-env behavioral test of the answer key)

**Interfaces:** consumes the DOM-env pattern from `src/filament-runtime/test/if-behavior.test.ts`; the answer key `samples/IfElse/ifelse.js`.

- [ ] **Step 1: Read the existing behavioral test's setup**

Read `src/filament-runtime/test/if-behavior.test.ts` for the environment pragma/config, how a module is mounted, and how DOM state is asserted. Mirror it exactly.

- [ ] **Step 2: Write the failing behavioral test**

Create `src/filament-runtime/test/ifelse-behavior.test.ts` (adapt the environment pragma to match `if-behavior.test.ts`):

```ts
// @vitest-environment happy-dom   // <- match if-behavior.test.ts's environment
import { describe, it, expect } from 'vitest';
import { mount } from '../../../samples/IfElse/ifelse.js';

describe('@if/@else if/@else answer key behavior', () => {
  it('shows exactly the active branch and swaps on each click', () => {
    const root = document.createElement('div');
    document.body.appendChild(root);
    mount(root);

    const wrap = root.querySelector('#wrap')!;
    const btn = wrap.querySelector('#t') as HTMLButtonElement;
    const active = () => (['a', 'b', 'c'] as const).filter(id => wrap.querySelector('#' + id));

    // n = 0 -> only branch a; the comment anchor is present throughout.
    expect(active()).toEqual(['a']);
    expect([...wrap.childNodes].some(n => n.nodeType === Node.COMMENT_NODE)).toBe(true);

    btn.click();                       // n = 1
    expect(active()).toEqual(['b']);

    btn.click();                       // n = 2
    expect(active()).toEqual(['c']);

    btn.click();                       // n = 0 again — wraps
    expect(active()).toEqual(['a']);

    // The anchor survived every swap.
    expect([...wrap.childNodes].some(n => n.nodeType === Node.COMMENT_NODE)).toBe(true);
  });
});
```

- [ ] **Step 3: Run it to verify it fails, then passes**

Run: `cd src/filament-runtime && npm test -- ifelse-behavior`
Expected: FAIL if the import path or environment is wrong; fix to match `if-behavior.test.ts`, then PASS.

- [ ] **Step 4: Run the full runtime verify (size gate included)**

Run: `cd src/filament-runtime && npm run verify`
Expected: build + typecheck + tests green, **size gate 1,943 B / 2,048 B unchanged**.

- [ ] **Step 5: Commit**

```bash
git add src/filament-runtime/test/ifelse-behavior.test.ts
git commit -m "test(@else): in-browser branch-swap behavior of the answer key"
```

---

### Task 6: Record the arbitrage (DECISIONS + README)

**Files:**
- Modify: `DECISIONS.md` (append entry #82, in the repo's dense numbered style, French to match the ledger)
- Modify: `README.md` (scope statement + test count)

**Interfaces:** none (documentation). The repo's core discipline: every arbitrage gets a numbered DECISIONS entry (the diff IS the result, #21/#51).

- [ ] **Step 1: Append DECISIONS entry #82**

Append to `DECISIONS.md`. Record, at minimum: `@else`/`@else if` move from refused into the subset; the lowering is **ONE keyed `list()` whose single item's value is the active branch index** (source is a ternary chain; key = index; create dispatches) — **zero new runtime primitive**, runtime byte-untouched at 1,943 B; a plain `@if` keeps its exact #81 emission (single-branch path preserved); a chain with **no trailing `@else`** yields `[]` when none match, generalizing plain `@if`; the **comment anchor carries over** as a disclosed +1-node divergence (#20); the DOM contract was read from Blazor's `BuildRenderTree` **before** generator output, seeding a genuine divergence (#81 reviewer note); `MarkConditionReads` already covers the whole chain via `DescendantNodes`; the **still-deferred variants** (multi-node branch body, nested control flow in a branch, root-level `@if/@else`) each raise a located, mutation-tested diagnostic; and the **honest ceiling** — one construct family does not move §8, RADICAL stays "not eliminated, not established."

- [ ] **Step 2: Update README**

Update the scope statement to note `@else`/`@else if` are now in the subset (comment-anchor divergence disclosed), and bump the generator-suite test count to the new total (run the suite to get it).

- [ ] **Step 3: Commit**

```bash
git add DECISIONS.md README.md
git commit -m "docs(@else): record decision #82 (branch-index-keyed list() reuse, deferred variants)"
```

---

## Final verification (run after all tasks)

- [ ] `dotnet test tests/Filament.Generator.Tests/Filament.Generator.Tests.csproj` → all green, 0 failed.
- [ ] `cd src/filament-runtime && npm run verify` → green, size gate 1,943 B.
- [ ] `git diff --stat src/filament-runtime` → empty (runtime untouched).
- [ ] `node tools/canon.mjs` on a fresh generate of `samples/IfElse/IfElse.razor` vs `samples/IfElse/ifelse.js` → ALPHA-EQUIVALENT.
- [ ] `node tools/canon.mjs` on a fresh generate of `samples/If/If.razor` vs `samples/If/if.js` → still ALPHA-EQUIVALENT (plain `@if` unregressed).

## Notes for the implementer

- **Line numbers are hints as of writing** and drift as you edit; anchor on the exact code shown.
- **Preserve plain `@if` byte-for-byte.** The `op.Branches.Count == 1` path in `EmitIf` must reproduce #81's emission (`() => (cond) ? [0] : [], () => 0, ifBody, _if{id}`); the `If.approved.js` snapshot is the wall.
- **Names carry the op id in the multi-branch path** (`ifBody{id}_{i}`) so two `@if` ops in one module never collide; canon ignores names, so this never affects the gate.
- **`MarkConditionReads` needs no change** — `DescendantNodes().OfType<IfStatementSyntax>()` over the method body already includes each `else if`'s nested `IfStatementSyntax`. Task 4 Step 2 locks this; do not add a second walk.
- **Do not touch the runtime.** If you feel you need a new primitive, stop: the design chose `list()` reuse precisely to avoid that, and Task 4's invariant test will fail.
- **The deferred refusals are intended.** `IfNested`/`IfMultiBody`/`IfElseMultiBody`/`IfAtRoot` must stay refused; a test that makes one compile is asserting a bug.
```
