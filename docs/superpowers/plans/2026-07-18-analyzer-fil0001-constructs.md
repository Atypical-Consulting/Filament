# Filament.Analyzer — FIL0001 Construct Subset (increment 1b) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans or subagent-driven-development. Steps use `- [ ]`.

**Goal:** Extend the author-time analyzer to FIL0001 (out-of-subset C# **constructs**), reusing the increment-1a plumbing (shared `Filament.Subset`, generator delegation, analyzer, verifier tests, mutation guard). Slice by construct family; ship each independently.

**Architecture:** Same "keep the `Refuse` call, move the decision" pattern proven in 1a Task 2, generalised from types to syntax nodes: a shared `ConstructSubset` classifier is the single source of which constructs are in §5; the generator's woven `Refuse()` sites delegate to it (their `switch default:` becomes a `throw` wiring backstop); the analyzer walks `@code` and calls the same classifier. `Diagnostic`/`SourceOffset`/`Refuse` stay generator-side (1a refinement).

**Tech Stack:** as 1a — `Filament.Subset` + `Filament.Analyzer` on `netstandard2.0`/Roslyn `4.8.0`; generator on `net10.0`/Roslyn `5.6.0`; xUnit + `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing` `1.1.2` (Roslyn floor raised to `4.8.0`).

## Global Constraints

- **Runtime untouched** (`git diff --stat src/filament-runtime` empty). **Gates stay GREEN** (196 tests as of DECISIONS #83). **Single source** of each construct decision in `Filament.Subset`. **Every backstop mutation-tested.** **Locations read off the tool, never guessed.** This is tooling — §5 subset does not widen, §8 ceiling (RADICAL) does not move.

## Slices (roadmap — each its own DECISIONS entry, each shippable)

| # | Family | Generator site(s) | CodeTests fixtures | This plan |
|---|--------|-------------------|--------------------|-----------|
| 1b-i | **`unsupported-statement`** (statement kind) | `Statement()` default, `CSharpFrontEnd.cs:1586` | While, Switch, TryCatch, Throw, Using, Lock, Goto | **concrete below** |
| 1b-ii | `unsupported-expression` (expression kind) | `Expr()` default `:1733` | Await, IntDivision, TypeListNull | follow-on |
| 1b-iii | `unsupported-call` | `Invocation()` `:1814/1830`, `ListMutation` `:1664` | ConsoleCall | follow-on |
| 1b-iv | `unsupported-member` (member kind) | `Member()` `:738`, `Record()` `:757+` | Property, Constructor, NestedClass, RecordDecl, RecordMember | follow-on |
| 1b-v | `unsupported-modifier` / `unsupported-generic` | `FieldDecl` `:857`, `Method` `:985/996` | (Gate/*) | follow-on |
| 1b-vi | `reserved-name` / `name-collision` / `not-csharp` | `Compile` `:273/346`, `CheckJsNameCollisions` `:1959` | (Gate/*) | follow-on |

`RegionOps`/`Slot`/`ListMutation`-template refusals stay in the generator (template-seam / FIL0003 — out of the analyzer's scope, as in 1a).

---

## Slice 1b-i: `unsupported-statement`

The generator's `Statement(StatementSyntax)` (`CSharpFrontEnd.cs:1508-1594`) is a `switch` over the seven admitted statement kinds — `LocalDeclarationStatement`, `ExpressionStatement`, `IfStatement`, `ForStatement`, `ForEachStatement`, `ReturnStatement`, `BlockSyntax` — with `default: Refuse("unsupported-statement", …, s.SpanStart)`. The decision "which statement kinds are in §5" moves to `ConstructSubset.ClassifyStatement`; the generator validates first and its `default:` becomes a `throw`. The analyzer walks `@code` method bodies **top-down, mirroring the generator's traversal** (recurse into the bodies of supported containers; report and stop at an unsupported statement — so a `switch` is flagged once, not also its inner `break`). CodeTests assert only up to `[reason]` + location, so message text is not gate-critical, but is kept faithful.

### Task 1: `ConstructSubset.ClassifyStatement` in the shared module + unit tests

**Files:**
- Create: `src/Filament.Subset/ConstructSubset.cs`
- Create: `tests/Filament.Subset.Tests/ConstructSubsetTests.cs`

**Interfaces:**
- Produces: `Filament.Subset.Refusal(string Code, string Reason, string Message)` and
  `ConstructSubset.ClassifyStatement(StatementSyntax) → Refusal?` (null = in subset).

- [ ] **Step 1: Write failing tests**

`tests/Filament.Subset.Tests/ConstructSubsetTests.cs`:

```csharp
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Filament.Subset.Tests;

public class ConstructSubsetTests
{
    static StatementSyntax FirstStatement(string body)
    {
        var tree = CSharpSyntaxTree.ParseText("class C { void M() {" + body + "} }");
        return tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .First().Body!.Statements.First();
    }

    [Theory]
    [InlineData("int x = 0;")]
    [InlineData("x = 1;")]
    [InlineData("if (b) { }")]
    [InlineData("for (;;) { }")]
    [InlineData("foreach (var y in ys) { }")]
    [InlineData("return;")]
    [InlineData("{ }")]
    public void SupportedStatementKinds_ClassifyToNull(string body)
        => Assert.Null(ConstructSubset.ClassifyStatement(FirstStatement(body)));

    [Theory]
    [InlineData("while (b) { }")]
    [InlineData("switch (x) { }")]
    [InlineData("try { } catch { }")]
    [InlineData("throw new System.Exception();")]
    [InlineData("using (d) { }")]
    [InlineData("lock (o) { }")]
    [InlineData("goto done;")]
    public void UnsupportedStatementKinds_ClassifyToUnsupportedStatement(string body)
    {
        var r = ConstructSubset.ClassifyStatement(FirstStatement(body));
        Assert.NotNull(r);
        Assert.Equal("FIL0001", r!.Value.Code);
        Assert.Equal("unsupported-statement", r.Value.Reason);
    }
}
```

- [ ] **Step 2: Run — expect FAIL** (`ConstructSubset` does not exist)

Run: `dotnet test tests/Filament.Subset.Tests/Filament.Subset.Tests.csproj`
Expected: FAIL to compile / unsupported cases fail.

- [ ] **Step 3: Implement `ConstructSubset`**

`src/Filament.Subset/ConstructSubset.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Filament.Subset;

/// <summary>A construct refusal (FIL0001): code, reason slug, author message. Span-agnostic —
/// the caller supplies the location. Single source of the §5 CONSTRUCT subset, shared by the
/// generator's woven Refuse() sites and the analyzer (decisions 53/61).</summary>
public readonly record struct Refusal(string Code, string Reason, string Message);

public static class ConstructSubset
{
    /// <summary>null = the statement KIND is in §5; non-null = the FIL0001 refusal. Expression- and
    /// call-level refusals inside a supported statement are separate classifiers (slices 1b-ii/iii).</summary>
    public static Refusal? ClassifyStatement(StatementSyntax s) => s switch
    {
        LocalDeclarationStatementSyntax => null,
        ExpressionStatementSyntax => null,
        IfStatementSyntax => null,
        ForStatementSyntax => null,
        ForEachStatementSyntax => null,
        ReturnStatementSyntax => null,
        BlockSyntax => null,
        _ => new Refusal("FIL0001", "unsupported-statement",
            $"{Describe(s)} is not in the C# subset. Section 5 admits local declarations, " +
            "assignment and compound assignment, if/else, for, foreach, and calls to methods " +
            "declared in the same component. Refusing to emit."),
    };

    static string Describe(SyntaxNode n) =>
        n.Kind().ToString().Replace("Syntax", "") + " (`" + Trunc(n.ToString(), 40) + "`)";

    static string Trunc(string s, int n)
    {
        s = s.Replace("\r", "").Replace("\n", "\\n");
        return s.Length <= n ? s : s.Substring(0, n) + "...";
    }
}
```

- [ ] **Step 4: Run — expect PASS**

Run: `dotnet test tests/Filament.Subset.Tests/Filament.Subset.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Filament.Subset/ConstructSubset.cs tests/Filament.Subset.Tests/ConstructSubsetTests.cs
git commit -m "feat(subset): ConstructSubset.ClassifyStatement — single source of the §5 statement-kind subset"
```

### Task 2: Generator delegates `Statement`'s refusal to `ClassifyStatement`

**Files:**
- Modify: `src/Filament.Generator/CSharpFrontEnd.cs` (`Statement()`)

- [ ] **Step 1: Validate-first in `Statement`**

At the top of `Statement(StatementSyntax s)` (before the `switch`), insert:

```csharp
        if (Filament.Subset.ConstructSubset.ClassifyStatement(s) is { } refusal)
        {
            Refuse(refusal.Reason, refusal.Message, s.SpanStart);
            return [];
        }
```

Then replace the `switch`'s `default:` arm (the old `Refuse("unsupported-statement", …); return [];`) with a wiring backstop:

```csharp
            default:
                throw new GeneratorException(
                    $"FIL-WIRING: ClassifyStatement admitted {s.Kind()} but Statement() has no case for it. " +
                    "The subset decision and the translator have drifted. Refusing to emit.");
```

- [ ] **Step 2: Full generator suite — behaviour preserved**

Run: `dotnet test tests/Filament.Generator.Tests/Filament.Generator.Tests.csproj`
Expected: PASS (178). The seven `unsupported-statement` rows of `OutOfSubsetCsharp_IsRefused_AtItsExactLocation_NeverSilentlyEmitted` — While/Switch/TryCatch/Throw/Using/Lock/Goto at `(8,9)` — stay green (same code, reason, location).

- [ ] **Step 3: Confirm runtime untouched, single-sourced**

Run: `git diff --stat src/filament-runtime` (empty) and
`grep -n 'unsupported-statement' src/Filament.Generator/CSharpFrontEnd.cs` (only the comment at ~650 remains; the Refuse string is gone).

- [ ] **Step 4: Commit**

```bash
git add src/Filament.Generator/CSharpFrontEnd.cs
git commit -m "refactor(subset): Statement() delegates the statement-kind decision to ConstructSubset"
```

### Task 3: `StatementSubsetAnalyzer` (FIL0001) + shared `IsComponent`

**Files:**
- Create: `src/Filament.Analyzer/ComponentScope.cs` (extract `IsComponent`)
- Modify: `src/Filament.Analyzer/TypeSubsetAnalyzer.cs` (use `ComponentScope.IsComponent`)
- Create: `src/Filament.Analyzer/StatementSubsetAnalyzer.cs`
- Modify: `src/Filament.Analyzer/AnalyzerReleases.Unshipped.md` (add FIL0001)

- [ ] **Step 1: Extract `IsComponent` to a shared helper**

`src/Filament.Analyzer/ComponentScope.cs`:

```csharp
using Microsoft.CodeAnalysis;

namespace Filament.Analyzer;

internal static class ComponentScope
{
    public static bool IsComponent(INamedTypeSymbol type)
    {
        for (var b = type.BaseType; b is not null; b = b.BaseType)
            if (b.ToDisplayString() == "Microsoft.AspNetCore.Components.ComponentBase") return true;
        return false;
    }
}
```

In `TypeSubsetAnalyzer.cs`, delete its private `IsComponent` and call `ComponentScope.IsComponent(type)`.

- [ ] **Step 2: The statement analyzer**

`src/Filament.Analyzer/StatementSubsetAnalyzer.cs`:

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Filament.Subset;

namespace Filament.Analyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StatementSubsetAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Fil0001 = new(
        id: "FIL0001",
        title: "Construct is outside the Filament C# subset",
        messageFormat: "{0}",
        category: "Filament.Subset",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "@code admits local declarations, assignment, if/else, for, foreach, return and calls to the component's own methods.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Fil0001);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);
        context.RegisterSymbolStartAction(OnTypeStart, SymbolKind.NamedType);
    }

    static void OnTypeStart(SymbolStartAnalysisContext context)
    {
        if (!ComponentScope.IsComponent((INamedTypeSymbol)context.Symbol)) return;
        context.RegisterSyntaxNodeAction(OnMethod, SyntaxKind.MethodDeclaration);
    }

    static void OnMethod(SyntaxNodeAnalysisContext context)
    {
        var body = ((MethodDeclarationSyntax)context.Node).Body;
        if (body is not null) WalkBlock(body, context);
    }

    // Mirror the generator's Statement()/Nest()/Body() traversal: recurse into supported
    // containers, report and stop at an unsupported statement.
    static void WalkBlock(BlockSyntax block, SyntaxNodeAnalysisContext context)
    {
        foreach (var s in block.Statements) Walk(s, context);
    }

    static void Walk(StatementSyntax s, SyntaxNodeAnalysisContext context)
    {
        if (ConstructSubset.ClassifyStatement(s) is { } refusal)
        {
            context.ReportDiagnostic(Diagnostic.Create(Fil0001, s.GetLocation(), refusal.Message));
            return; // do not descend into an unsupported construct
        }
        switch (s)
        {
            case BlockSyntax b: WalkBlock(b, context); break;
            case IfStatementSyntax i:
                Walk(i.Statement, context);
                if (i.Else is { } e) Walk(e.Statement, context);
                break;
            case ForStatementSyntax f: Walk(f.Statement, context); break;
            case ForEachStatementSyntax fe: Walk(fe.Statement, context); break;
        }
    }
}
```

Add to `AnalyzerReleases.Unshipped.md` under `### New Rules`:

```
FIL0001 | Filament.Subset | Error | Construct is outside the Filament C# subset
```

- [ ] **Step 3: Build the analyzer — warning-free**

Run: `dotnet build src/Filament.Analyzer/Filament.Analyzer.csproj`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 4: Commit**

```bash
git add src/Filament.Analyzer
git commit -m "feat(analyzer): FIL0001 statement-subset analyzer; share IsComponent"
```

### Task 4: Analyzer verifier tests + mutation guard + DECISIONS #84

**Files:**
- Create: `tests/Filament.Analyzer.Tests/StatementSubsetAnalyzerTests.cs`
- Modify: `DECISIONS.md`

- [ ] **Step 1: Verifier tests**

`tests/Filament.Analyzer.Tests/StatementSubsetAnalyzerTests.cs`:

```csharp
using System.Threading.Tasks;
using Xunit;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    Filament.Analyzer.StatementSubsetAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Filament.Analyzer.Tests;

public class StatementSubsetAnalyzerTests
{
    const string ComponentBase =
        "namespace Microsoft.AspNetCore.Components { public class ComponentBase {} }\n";

    static Verify Method(string body) => new()
    {
        TestCode = ComponentBase +
            "class App : Microsoft.AspNetCore.Components.ComponentBase {\n" +
            "    void M(bool b) {\n" + body + "\n    }\n}",
    };

    [Fact]
    public async Task WhileLoop_IsFlagged()
        => await Method("        {|FIL0001:while (b) { }|}").RunAsync();

    [Fact]
    public async Task TryCatch_IsFlagged()
        => await Method("        {|FIL0001:try { } catch { }|}").RunAsync();

    [Fact]
    public async Task SupportedStatements_ProduceNoDiagnostics()
        => await Method("        int x = 0;\n        if (b) { x = 1; }\n        for (;;) { break; }").RunAsync();

    [Fact]
    public async Task UnsupportedStatement_IsFlaggedOnce_NotItsInnards()
        // switch is flagged; the inner `break;` (also an unsupported kind) is NOT, because we
        // stop descending at an unsupported construct — mirroring the generator's first-hit refuse.
        => await Method("        {|FIL0001:switch (x) { default: break; }|}\n        int x;").RunAsync();
}
```

Note the `for (;;) { break; }` supported case: `break` inside a supported `for` is reached by the walk — but the generator DOES translate `for` bodies, and `break` is... **verify against the tool**: if the generator admits `break` inside a loop (it has no case → it would refuse). Run Step 2 first; if `break` is refused by the generator, change the supported-case body to `for (;;) { x = 1; }` and keep the assertion tool-driven.

- [ ] **Step 2: Run — expect PASS**

Run: `dotnet test tests/Filament.Analyzer.Tests/Filament.Analyzer.Tests.csproj`
Expected: PASS. Adjust marker spans to tool-reported spans if needed (never guess columns).

- [ ] **Step 3: Mutation guard — both suites share one rule**

In `ConstructSubset.ClassifyStatement`, temporarily add `WhileStatementSyntax => null,` (accept while). Run:
- `dotnet test tests/Filament.Generator.Tests/… --filter OutOfSubsetCsharp_IsRefused_AtItsExactLocation_NeverSilentlyEmitted`
- `dotnet test tests/Filament.Analyzer.Tests/… --filter WhileLoop_IsFlagged`

Expected: **BOTH FAIL** (generator's `While.razor` row + analyzer's while test). Revert (edit the line out — do NOT `git checkout` uncommitted neighbours), re-run both → PASS.

- [ ] **Step 4: DECISIONS #84 + commit**

Append `## 84.` recording: statement-kind subset single-sourced in `ConstructSubset.ClassifyStatement`; generator `default:`→wiring `throw`; analyzer's generator-mirroring top-down walk (report-and-stop); mutation guard; honest ceiling unchanged. Then:

```bash
git add tests/Filament.Analyzer.Tests/StatementSubsetAnalyzerTests.cs DECISIONS.md
git commit -m "test(analyzer): FIL0001 statement verifier tests + mutation guard; record decision #84"
```

## Self-Review

- **Coverage:** shared decision (T1), generator delegation with gates as harness (T2), analyzer + shared IsComponent (T3), verifier tests + mutation guard + decision (T4). Remaining FIL0001 families are the roadmap table (follow-on).
- **Placeholders:** none — the one tool-driven check (`break` in a `for`) is explicitly resolved by running Step 2 before asserting.
- **Type consistency:** `Refusal(Code, Reason, Message)` and `ConstructSubset.ClassifyStatement(StatementSyntax) → Refusal?` used identically in T1 (tests), T2 (generator), T3 (analyzer).
