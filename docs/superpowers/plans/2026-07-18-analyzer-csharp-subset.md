# Filament.Analyzer — C# Type-Subset Diagnostics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship an author-time Roslyn `DiagnosticAnalyzer` that surfaces the generator's `FIL0002` (out-of-subset **type**) refusals as live IDE diagnostics, with the type-subset decision single-sourced in a shared `Filament.Subset` module that both the generator and the analyzer call.

**Architecture:** Extract the type-subset predicate out of `CSharpFrontEnd.CheckType` into `Filament.Subset.TypeSubset.Classify` (a pure, span-agnostic function over a Roslyn `ITypeSymbol`). The generator's `CheckType` becomes a thin adapter that calls `Classify` and feeds any refusal into its existing `Refuse(...)` path — behavior byte-identical, proven by the existing suite. A new `netstandard2.0` `Filament.Analyzer` walks every Blazor component in a referencing project, calls the same `Classify`, and reports `FIL0002` at the offending type's location.

**Tech Stack:** C# / .NET 10 (generator, tests), `netstandard2.0` (shared module + analyzer, for Roslyn-analyzer compatibility), Roslyn (`Microsoft.CodeAnalysis.CSharp`), xUnit, `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit`.

## Scope of THIS plan (increment 1a — read first)

The approved spec (`docs/superpowers/specs/2026-07-18-analyzer-csharp-subset-design.md`) scopes increment 1 to the C# `@code` subset, `FIL0001` **and** `FIL0002`. This plan deliberately implements **only `FIL0002` (types)** as a complete, shippable vertical slice, because:

- The generator's refusals are **woven into its translation walk** (`Refuse()` interleaved with emission; out-of-subset = a `switch` `default:` fall-through). Extracting them is per-category work — there is no single predicate to lift.
- `CheckType` is the **one self-contained predicate** already isolated as a method, so it extracts cleanly and exercises the *entire* new stack (shared module → generator delegation → analyzer → verifier tests → mutation guard → DECISIONS entry).
- It retires both spec risks on the smallest real surface: **Risk #1 (Roslyn version)** via the Task 0 spike, and **the generator-delegation-preserves-the-gates risk** via Task 2.

The `FIL0001` construct categories (`unsupported-statement`, `unsupported-expression`, `unsupported-call`, `unsupported-member`, `unsupported-modifier`, `unsupported-generic`, `reserved-name`, `name-collision`, `not-csharp`) are **increment 1b — a follow-on plan** that reuses the `SubsetValidator`/analyzer plumbing this plan builds. This matches the repo's one-airtight-slice-at-a-time discipline (`@if` #81, then `@else` #82).

## Refinement to the spec (flag to reviewer)

The spec said "move `Diagnostic` and `SourceOffset` down into `Filament.Subset`." This plan **does not** — they stay in the generator. Reason: `Diagnostic` is a Razor-`SourceSpan`-based **stderr-formatting** type (its whole job is `"file(line,col): CODE: [reason] message"`), which is generator-specific; the analyzer wants Roslyn `Location`s, not Razor spans. The thing that actually honors #53/#61 is single-sourcing the **subset decision**, which `TypeSubset.Classify` does. The shared module therefore exposes only the decision (`Classify` + `ScalarTypes` + `ListElement`) and a small span-agnostic result record. Nothing about the subset is described twice. If the reviewer prefers the literal spec, moving `Diagnostic`/`SourceOffset` is additive and can be done later without touching `Classify`.

## Global Constraints

- **Runtime untouched.** No file under `src/filament-runtime/` changes. `git diff --stat src/filament-runtime` must stay empty. (Spec: "no emitted byte moves.")
- **Gates stay GREEN.** Counter/Rows/If/IfElse alpha-equivalence and the full test suite (178 passing as of #82) must pass unchanged after every task. They are the behavior-preservation harness for the generator refactor.
- **Single source of the subset decision.** After Task 2, the type subset (which types are in §5) is decided in exactly one place: `Filament.Subset.TypeSubset.Classify`. The generator must not re-list scalar/List/record admissibility anywhere else.
- **Every backstop is mutation-tested** (an untested guard is a claim — #61). The shared rule's guard is proven by neutralizing it and confirming **both** a generator test and an analyzer test go red (Task 4).
- **Locations are read off the tool, never reasoned about** (repo rule, CodeTests header). Any asserted `(line,col)` comes from a real run.
- **Analyzer + shared module target `netstandard2.0`** and reference `Microsoft.CodeAnalysis.CSharp` with `PrivateAssets="all"` (the host provides Roslyn). Exact version is pinned by Task 0.
- **Package versions are per-project** (no `Directory.Packages.props`, no `nuget.config`). Razor packages are frozen at `6.0.36`, `Microsoft.CodeAnalysis.CSharp` at `5.6.0` in the generator (#52/#70) — do not "upgrade" them.
- **`Filament.Core` is not created** (YAGNI — no consumer for a C# `Signal<T>`).

---

### Task 0: Roslyn-version spike — a trivial `netstandard2.0` analyzer loads under the net10 host

**Files:**
- Create: `spike/Filament.Spike.Analyzer/Filament.Spike.Analyzer.csproj`
- Create: `spike/Filament.Spike.Analyzer/HelloAnalyzer.cs`
- Create: `spike/Filament.Spike.Analyzer.Tests/Filament.Spike.Analyzer.Tests.csproj`
- Create: `spike/Filament.Spike.Analyzer.Tests/HelloAnalyzerTests.cs`

**Interfaces:**
- Produces: **the pinned Roslyn version** for `Filament.Subset` and `Filament.Analyzer`, recorded in this task's Step 6 note. Every later task's `Microsoft.CodeAnalysis.*` `<PackageReference>` uses that version.

**Purpose:** prove a `netstandard2.0` analyzer built against a chosen `Microsoft.CodeAnalysis.CSharp` version actually loads and reports under the .NET 10 (10.0.301) toolchain, and that the `Analyzer.Testing.XUnit` harness runs. Everything else waits on this answer. This is a spike: it is deleted in Step 7 once the version is recorded.

- [ ] **Step 1: Create the spike analyzer project**

`spike/Filament.Spike.Analyzer/Filament.Spike.Analyzer.csproj` — start with the **lowest** candidate that the net10 SDK's analyzer host is known to load; `4.8.0` is the first candidate:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write a trivial analyzer**

`spike/Filament.Spike.Analyzer/HelloAnalyzer.cs`:

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Filament.Spike.Analyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HelloAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new(
        "FILSPIKE", "Spike", "class '{0}' seen by the spike analyzer",
        "Spike", DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(ctx =>
        {
            var decl = (ClassDeclarationSyntax)ctx.Node;
            ctx.ReportDiagnostic(Diagnostic.Create(Rule, decl.Identifier.GetLocation(), decl.Identifier.Text));
        }, SyntaxKind.ClassDeclaration);
    }
}
```

- [ ] **Step 3: Write a verifier test**

`spike/Filament.Spike.Analyzer.Tests/Filament.Spike.Analyzer.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Analyzer.Testing" Version="1.1.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../Filament.Spike.Analyzer/Filament.Spike.Analyzer.csproj" />
  </ItemGroup>
</Project>
```

`spike/Filament.Spike.Analyzer.Tests/HelloAnalyzerTests.cs`:

```csharp
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    Filament.Spike.Analyzer.HelloAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

public class HelloAnalyzerTests
{
    [Fact]
    public async Task FlagsAClass()
    {
        await new Verify
        {
            TestCode = "class {|FILSPIKE:Foo|} {}",
        }.RunAsync();
    }
}
```

- [ ] **Step 4: Run the spike test**

Run: `dotnet test spike/Filament.Spike.Analyzer.Tests/Filament.Spike.Analyzer.Tests.csproj`
Expected: PASS. If it fails to **build the analyzer** or the host refuses to load it (version-mismatch warning `CS9057`/`RS1041` or the diagnostic never fires), bump `Microsoft.CodeAnalysis.CSharp` in Step 1 to the next candidate (`4.11.0`, then `4.14.0`) and the testing package to a matching line, and re-run.

- [ ] **Step 5: Confirm the generator still builds against its pinned Roslyn**

Run: `dotnet build src/Filament.Generator/Filament.Generator.csproj`
Expected: PASS. This confirms the version chosen for the analyzer does **not** force a generator change yet (the generator keeps `5.6.0`; the shared module will pin the analyzer-compatible version independently — see Task 1 note).

- [ ] **Step 6: Record the resolved version**

Edit this file: replace the placeholder below with the version that passed.

> **RESOLVED:** `Filament.Subset` + `Filament.Analyzer` pin `Microsoft.CodeAnalysis.CSharp` **4.8.0** (`PrivateAssets="all"`). Analyzer **test** projects use `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing` **1.1.2** **plus** explicit `Microsoft.CodeAnalysis.CSharp` **4.8.0** and `Microsoft.CodeAnalysis.CSharp.Workspaces` **4.8.0** — the testing package declares only a Roslyn *floor*, so NuGet otherwise resolves the ancient `1.0.1` and the analyzer (built vs 4.8.0) fails to load with `CS1705`. The generator keeps `5.6.0` and builds clean (0 warnings), so 4.8.0 forces no generator change. **Because `Filament.Subset` marks Roslyn `PrivateAssets="all"`, its Roslyn does NOT flow transitively** — every consumer (generator, subset tests, analyzer) brings its own. `TypeSubset.Classify` uses only stable Roslyn surface (`ITypeSymbol`, `SpecialType`, `SymbolEqualityComparer`, `ToDisplayString`), satisfied by both 4.8.0 and 5.6.0.

- [ ] **Step 7: Delete the spike, commit the recorded decision**

```bash
rm -rf spike
git add docs/superpowers/plans/2026-07-18-analyzer-csharp-subset.md
git commit -m "spike(analyzer): pin Roslyn version for netstandard2.0 analyzer (record in plan)"
```

---

### Task 1: Scaffold `Filament.Subset` and reference it from the generator — no behavior change

**Files:**
- Create: `src/Filament.Subset/Filament.Subset.csproj`
- Create: `src/Filament.Subset/TypeSubset.cs`
- Modify: `src/Filament.Generator/Filament.Generator.csproj` (add ProjectReference)
- Modify: `Filament.sln` (add the project)

**Interfaces:**
- Produces: `Filament.Subset.TypeRefusal` (record) and `Filament.Subset.TypeSubset` (static class, stub `Classify` returning `null` for now). Task 2 fills `Classify`.

- [ ] **Step 1: Create the shared project** (use the version from Task 0 Step 6)

`src/Filament.Subset/Filament.Subset.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>disable</ImplicitUsings>
    <RootNamespace>Filament.Subset</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add the result record and a stub decision**

`src/Filament.Subset/TypeSubset.cs`:

```csharp
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Filament.Subset;

/// <summary>A type-subset refusal: the reason slug and the author-facing message. Span-agnostic:
/// the caller (generator or analyzer) supplies the location. All FIL0002.</summary>
public readonly record struct TypeRefusal(string Reason, string Message);

/// <summary>Spec 5's type list — the single source of the FIL0002 type subset, shared by the
/// generator's CheckType and the analyzer. Pure over an ITypeSymbol; no diagnostics, no spans.</summary>
public static class TypeSubset
{
    /// <summary>null = in subset; non-null = the refusal to report at the caller's location.</summary>
    public static TypeRefusal? Classify(
        ITypeSymbol? type, IReadOnlyCollection<INamedTypeSymbol> componentRecords, bool allowList = true)
        => null; // Task 2 fills this
}
```

- [ ] **Step 3: Reference the shared project from the generator**

In `src/Filament.Generator/Filament.Generator.csproj`, inside the existing `<ItemGroup>` (after the package refs), add:

```xml
    <ProjectReference Include="../Filament.Subset/Filament.Subset.csproj" />
```

- [ ] **Step 4: Add both projects to the solution**

```bash
dotnet sln Filament.sln add src/Filament.Subset/Filament.Subset.csproj
```

- [ ] **Step 5: Build and run the full suite — nothing changed**

Run: `dotnet build Filament.sln && dotnet test tests/Filament.Generator.Tests/Filament.Generator.Tests.csproj`
Expected: PASS (178 passing). No behavior change — the generator references the module but does not call it yet.

- [ ] **Step 6: Commit**

```bash
git add src/Filament.Subset src/Filament.Generator/Filament.Generator.csproj Filament.sln
git commit -m "feat(subset): scaffold Filament.Subset shared module, referenced by the generator"
```

---

### Task 2: Move the type-subset decision into `TypeSubset.Classify`; `CheckType` delegates

**Files:**
- Modify: `src/Filament.Subset/TypeSubset.cs` (implement `Classify`)
- Modify: `src/Filament.Generator/CSharpFrontEnd.cs:957-994` (`CheckType`, `ScalarTypes`, `ListElement`)
- Create: `tests/Filament.Subset.Tests/Filament.Subset.Tests.csproj`
- Create: `tests/Filament.Subset.Tests/TypeSubsetTests.cs`

**Interfaces:**
- Consumes: `TypeSubset.Classify(ITypeSymbol?, IReadOnlyCollection<INamedTypeSymbol>, bool)` from Task 1.
- Produces: a `Classify` that returns the exact reason/message strings the generator formerly inlined. `CheckType` keeps its `(ITypeSymbol?, int at, bool allowList)` signature and its `Refuse(...)` calls, so every caller and every asserted location is unchanged.

- [ ] **Step 1: Write the failing shared-module test**

`tests/Filament.Subset.Tests/Filament.Subset.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <!-- Filament.Subset marks Roslyn PrivateAssets=all, so it does not flow transitively;
         the tests construct real compilations and need their own Roslyn reference. -->
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/Filament.Subset/Filament.Subset.csproj" />
  </ItemGroup>
</Project>
```

`tests/Filament.Subset.Tests/TypeSubsetTests.cs` — build a real compilation, ask for named types, feed their symbols to `Classify`:

```csharp
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Filament.Subset;
using Xunit;

namespace Filament.Subset.Tests;

public class TypeSubsetTests
{
    static ITypeSymbol TypeOfField(string decls, string fieldName, out Compilation comp)
    {
        var tree = CSharpSyntaxTree.ParseText("using System;using System.Collections.Generic;class C {" + decls + "}");
        comp = CSharpCompilation.Create("t", new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
        var model = comp.GetSemanticModel(tree);
        var field = tree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax>()
            .First(v => v.Identifier.Text == fieldName);
        return ((IFieldSymbol)model.GetDeclaredSymbol(field)!).Type;
    }

    [Theory]
    [InlineData("int x;", "x")]
    [InlineData("double x;", "x")]
    [InlineData("bool x;", "x")]
    [InlineData("string x;", "x")]
    [InlineData("List<int> x;", "x")]
    [InlineData("List<string> x;", "x")]
    public void InSubsetTypes_ClassifyToNull(string decls, string field)
    {
        var t = TypeOfField(decls, field, out _);
        Assert.Null(TypeSubset.Classify(t, System.Array.Empty<INamedTypeSymbol>()));
    }

    [Theory]
    [InlineData("decimal x;", "x")]
    [InlineData("long x;", "x")]
    [InlineData("float x;", "x")]
    [InlineData("object x;", "x")]
    [InlineData("DateTime x;", "x")]
    [InlineData("List<long> x;", "x")]
    public void OutOfSubsetTypes_ClassifyToUnsupportedType(string decls, string field)
    {
        var t = TypeOfField(decls, field, out _);
        var r = TypeSubset.Classify(t, System.Array.Empty<INamedTypeSymbol>());
        Assert.NotNull(r);
        Assert.Equal("unsupported-type", r!.Value.Reason);
    }

    [Fact]
    public void ErrorType_ClassifiesToUnresolvedType()
    {
        var t = TypeOfField("Nonexistent x;", "x", out _);
        var r = TypeSubset.Classify(t, System.Array.Empty<INamedTypeSymbol>());
        Assert.Equal("unresolved-type", r!.Value.Reason);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/Filament.Subset.Tests/Filament.Subset.Tests.csproj`
Expected: FAIL — `Classify` returns null for everything (the out-of-subset and error-type cases assert non-null).

- [ ] **Step 3: Implement `Classify` by moving the logic out of `CheckType`**

Replace the stub body in `src/Filament.Subset/TypeSubset.cs` (keep the record and namespace):

```csharp
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Filament.Subset;

public readonly record struct TypeRefusal(string Reason, string Message);

public static class TypeSubset
{
    static readonly HashSet<SpecialType> Scalars = new()
    {
        SpecialType.System_Int32, SpecialType.System_Double,
        SpecialType.System_Boolean, SpecialType.System_String,
    };

    public static TypeRefusal? Classify(
        ITypeSymbol? type, IReadOnlyCollection<INamedTypeSymbol> componentRecords, bool allowList = true)
    {
        if (type is null || type.TypeKind == TypeKind.Error)
            return new TypeRefusal("unresolved-type", "this type does not resolve. Refusing to emit.");

        if (Scalars.Contains(type.SpecialType)) return null;
        if (IsComponentRecord(type, componentRecords)) return null;

        if (allowList && ListElement(type) is { } element)
        {
            if (Scalars.Contains(element.SpecialType)) return null;
            if (IsComponentRecord(element, componentRecords)) return null;

            return new TypeRefusal("unsupported-type",
                $"'{type.ToDisplayString()}' is not in the C# subset. Section 5 admits List<T> of int, double, " +
                "bool, string, or of a record declared in the component. Refusing to emit.");
        }

        return new TypeRefusal("unsupported-type",
            $"'{type.ToDisplayString()}' is not in the C# subset. Section 5 admits int, double, bool, " +
            "string, and List<T> of those or of a record declared in the component. Refusing to emit.");
    }

    static bool IsComponentRecord(ITypeSymbol type, IReadOnlyCollection<INamedTypeSymbol> records) =>
        records.Any(r => SymbolEqualityComparer.Default.Equals(r, type));

    public static ITypeSymbol? ListElement(ITypeSymbol type) =>
        type.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.List<T>" &&
        type is INamedTypeSymbol { TypeArguments.Length: 1 } n
            ? n.TypeArguments[0]
            : null;
}
```

- [ ] **Step 4: Run the shared-module test — it passes**

Run: `dotnet test tests/Filament.Subset.Tests/Filament.Subset.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Make the generator delegate to `Classify`**

In `src/Filament.Generator/CSharpFrontEnd.cs`, replace the body of `CheckType` (lines 957-988) with a thin adapter that keeps the `Refuse(...)` calls but takes the decision from the shared module:

```csharp
    /// <summary>Spec 5's type list, resolved through the Compilation. FIL0002's whole job —
    /// the DECISION now lives in Filament.Subset.TypeSubset.Classify (single source, #53/#61).</summary>
    bool CheckType(ITypeSymbol? type, int at, bool allowList = true)
    {
        var records = _recordsByName.Values.Select(r => r.Symbol).ToArray();
        if (Filament.Subset.TypeSubset.Classify(type, records, allowList) is { } refusal)
        {
            Refuse(refusal.Reason, refusal.Message, at, "FIL0002");
            return false;
        }
        return true;
    }
```

Then delete the now-duplicate `ScalarTypes` dictionary (lines 62-68) **only if** nothing else uses it — check first:

Run: `grep -n 'ScalarTypes' src/Filament.Generator/CSharpFrontEnd.cs`
If `ScalarTypes` is referenced elsewhere (e.g. `DefaultOf` or a JS-type mapping), **leave it** — it is not part of the subset decision, and this task is not about deduping unrelated tables. Also delete the generator's private `ListElement` (lines 990-994) and repoint any caller to `Filament.Subset.TypeSubset.ListElement`:

Run: `grep -n 'ListElement' src/Filament.Generator/CSharpFrontEnd.cs`
For each hit outside the deleted method, replace `ListElement(` with `Filament.Subset.TypeSubset.ListElement(`.

- [ ] **Step 6: Run the FULL suite — the gates prove behavior is preserved**

Run: `dotnet test tests/Filament.Generator.Tests/Filament.Generator.Tests.csproj`
Expected: PASS (178). In particular `OutOfSubsetCsharp_IsRefused_AtItsExactLocation_NeverSilentlyEmitted` must stay green for every FIL0002 row — `TypeLong(5,13)`, `TypeFloat(5,13)`, `TypeDecimal(5,13)`, `TypeObject(5,13)`, `TypeDict(5,13)`, `TypeArray(5,13)`, `TypeDateTime(5,13)`, `TypeList(5,13)`, `Lambda(8,9)`, `Linq(8,9)`, `ObjectCreate(8,9)`, `Typeof(8,9)` — same code, reason, and location. If any moved, the extraction changed behavior; revert and diff the message/`allowList` handling.

- [ ] **Step 7: Confirm the runtime is untouched and add the test project to the sln**

```bash
git diff --stat src/filament-runtime   # must print nothing
dotnet sln Filament.sln add tests/Filament.Subset.Tests/Filament.Subset.Tests.csproj
```

- [ ] **Step 8: Commit**

```bash
git add src/Filament.Subset/TypeSubset.cs src/Filament.Generator/CSharpFrontEnd.cs \
        tests/Filament.Subset.Tests Filament.sln
git commit -m "refactor(subset): single-source the FIL0002 type subset; generator delegates to TypeSubset.Classify"
```

---

### Task 3: `Filament.Analyzer` — report FIL0002 on out-of-subset types in Blazor components

**Files:**
- Create: `src/Filament.Analyzer/Filament.Analyzer.csproj`
- Create: `src/Filament.Analyzer/TypeSubsetAnalyzer.cs`
- Modify: `Filament.sln`

**Interfaces:**
- Consumes: `Filament.Subset.TypeSubset.Classify` and `TypeRefusal`.
- Produces: `Filament.Analyzer.TypeSubsetAnalyzer` reporting descriptor id `FIL0002`. Task 4 tests it.

- [ ] **Step 1: Create the analyzer project** (version from Task 0)

`src/Filament.Analyzer/Filament.Analyzer.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <RootNamespace>Filament.Analyzer</RootNamespace>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0" PrivateAssets="all" />
    <ProjectReference Include="../Filament.Subset/Filament.Subset.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write the analyzer**

`src/Filament.Analyzer/TypeSubsetAnalyzer.cs`:

```csharp
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Filament.Subset;

namespace Filament.Analyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TypeSubsetAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Fil0002 = new(
        id: "FIL0002",
        title: "Type is outside the Filament C# subset",
        messageFormat: "{0}",
        category: "Filament.Subset",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "@code may use only int, double, bool, string, List<T> of those, and records declared in the component.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Fil0002);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        // Razor @code compiles to GENERATED C#; we must opt in to see it.
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);
        context.RegisterSymbolAction(OnNamedType, SymbolKind.NamedType);
    }

    static void OnNamedType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (!IsComponent(type)) return;

        var records = type.GetTypeMembers()
            .Where(m => m.TypeKind == TypeKind.Struct || m.IsRecord)
            .ToArray();

        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            var node = reference.GetSyntax(context.CancellationToken);
            var model = context.Compilation.GetSemanticModel(node.SyntaxTree);
            foreach (var typeSyntax in TypePositions(node))
            {
                var resolved = model.GetTypeInfo(typeSyntax, context.CancellationToken).Type;
                if (TypeSubset.Classify(resolved, records) is { } refusal)
                    context.ReportDiagnostic(Diagnostic.Create(Fil0002, typeSyntax.GetLocation(), refusal.Message));
            }
        }
    }

    // The type-bearing positions the generator's CheckType covers: field decls, local decls,
    // method return types and parameter types. (Increment 1a scope.)
    static IEnumerable<TypeSyntax> TypePositions(SyntaxNode typeDecl)
    {
        foreach (var member in typeDecl.DescendantNodes())
            switch (member)
            {
                case FieldDeclarationSyntax f: yield return f.Declaration.Type; break;
                case LocalDeclarationStatementSyntax l: yield return l.Declaration.Type; break;
                case MethodDeclarationSyntax m:
                    yield return m.ReturnType;
                    foreach (var p in m.ParameterList.Parameters)
                        if (p.Type is { } pt) yield return pt;
                    break;
            }
    }

    static bool IsComponent(INamedTypeSymbol type)
    {
        for (var b = type.BaseType; b is not null; b = b.BaseType)
            if (b.ToDisplayString() == "Microsoft.AspNetCore.Components.ComponentBase") return true;
        return false;
    }
}
```

- [ ] **Step 3: Build the analyzer**

Run: `dotnet build src/Filament.Analyzer/Filament.Analyzer.csproj`
Expected: PASS, no `RS1041`/`RS2008` analyzer-authoring warnings escalated to errors. (`FIL0002` needs no release-tracking file because `EnforceExtendedAnalyzerRules` + a described descriptor satisfy `RS2008`; if `RS2008` still fires, add `dotnet_diagnostic.RS2008.severity = none` via an `.editorconfig` in `src/Filament.Analyzer/` and note it.)

- [ ] **Step 4: Add to the solution and commit**

```bash
dotnet sln Filament.sln add src/Filament.Analyzer/Filament.Analyzer.csproj
git add src/Filament.Analyzer Filament.sln
git commit -m "feat(analyzer): FIL0002 type-subset analyzer over Blazor components"
```

---

### Task 4: Analyzer verifier tests + the mutation guard

**Files:**
- Create: `tests/Filament.Analyzer.Tests/Filament.Analyzer.Tests.csproj`
- Create: `tests/Filament.Analyzer.Tests/TypeSubsetAnalyzerTests.cs`
- Modify: `Filament.sln`

**Interfaces:**
- Consumes: `Filament.Analyzer.TypeSubsetAnalyzer`.

The verifier compiles **plain C#**, not Razor, so the tests model the generated component class directly: a class deriving from a stub `ComponentBase`, with the same field/local shapes the fixtures use. `{|FIL0002:...|}` marks the expected span.

- [ ] **Step 1: Create the test project**

`tests/Filament.Analyzer.Tests/Filament.Analyzer.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Analyzer.Testing" Version="1.1.2" />
    <!-- Raise the Roslyn floor to the analyzer's 4.8.0; the testing package declares only a floor,
         so NuGet otherwise resolves 1.0.1 and the analyzer fails to load (CS1705). Proven in Task 0. -->
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Workspaces" Version="4.8.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/Filament.Analyzer/Filament.Analyzer.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write the failing tests**

`tests/Filament.Analyzer.Tests/TypeSubsetAnalyzerTests.cs`:

```csharp
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    Filament.Analyzer.TypeSubsetAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Filament.Analyzer.Tests;

public class TypeSubsetAnalyzerTests
{
    const string ComponentBase =
        "namespace Microsoft.AspNetCore.Components { public class ComponentBase {} }\n";

    static Verify Case(string body) => new()
    {
        TestCode = ComponentBase +
            "class App : Microsoft.AspNetCore.Components.ComponentBase {\n" + body + "\n}",
    };

    [Fact]
    public async Task OutOfSubsetFieldType_IsFlagged()
    {
        await Case("    private {|FIL0002:decimal|} x = 0;").RunAsync();
    }

    [Fact]
    public async Task OutOfSubsetLocalType_IsFlagged()
    {
        await Case(
            "    private void M() {\n" +
            "        System.Collections.Generic.List<{|FIL0002:long|}> ys = null;\n" +
            "    }").RunAsync();
    }

    [Fact]
    public async Task InSubsetTypes_ProduceNoDiagnostics()
    {
        await Case(
            "    private int a = 0;\n" +
            "    private double b = 0;\n" +
            "    private bool c = false;\n" +
            "    private string d = null;\n" +
            "    private System.Collections.Generic.List<int> e = null;").RunAsync();
    }

    [Fact]
    public async Task NonComponentClass_IsNotChecked()
    {
        // No ComponentBase base type -> whole-project opt-in still ignores plain classes.
        await new Verify { TestCode = "class Plain { private decimal x = 0; }" }.RunAsync();
    }
}
```

Note on the `List<long>` case: `Classify` refuses the **List type** at the `List<...>` position; the generator reports at the declaration's type. The analyzer reports at the `TypeSyntax` for the local's declared type. Run once (Step 4); if the span the verifier reports is the whole `List<long>` rather than `long`, move the `{|FIL0002:...|}` marker to wrap `System.Collections.Generic.List<long>` and keep the test — the location is read off the tool, never guessed (repo rule).

- [ ] **Step 3: Verify the testing-package versions**

Confirm the csproj above pins `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing` `1.1.2` with the `4.8.0` floor-raising refs (Task 0 resolved these).

- [ ] **Step 4: Run — expect pass**

Run: `dotnet test tests/Filament.Analyzer.Tests/Filament.Analyzer.Tests.csproj`
Expected: PASS. If a span assertion fails, adjust the `{|FIL0002:...|}` marker to the tool-reported span (Step 2 note) and re-run — do not change the analyzer to match a guessed column.

- [ ] **Step 5: Mutation guard — prove BOTH suites consume the one shared rule**

Temporarily break the shared decision: in `src/Filament.Subset/TypeSubset.cs`, add `System.Decimal` to the accepted set by inserting at the top of `Classify` (after the null/error check): `if (type.SpecialType == SpecialType.System_Decimal) return null;`.

Run both:
- `dotnet test tests/Filament.Generator.Tests/Filament.Generator.Tests.csproj --filter OutOfSubsetCsharp_IsRefused_AtItsExactLocation_NeverSilentlyEmitted`
- `dotnet test tests/Filament.Analyzer.Tests/Filament.Analyzer.Tests.csproj --filter OutOfSubsetFieldType_IsFlagged`

Expected: **BOTH FAIL** — `TypeDecimal` now compiles in the generator (its theory row goes red) and the analyzer stops flagging `decimal` (its test goes red). This is the proof that both genuinely call `TypeSubset.Classify` and not a copy (#53). **Then revert the mutation** and re-run both — expect PASS.

- [ ] **Step 6: Add to sln and commit**

```bash
git checkout src/Filament.Subset/TypeSubset.cs   # ensure the mutation is reverted
dotnet sln Filament.sln add tests/Filament.Analyzer.Tests/Filament.Analyzer.Tests.csproj
git add tests/Filament.Analyzer.Tests Filament.sln
git commit -m "test(analyzer): FIL0002 verifier tests + shared-rule mutation guard"
```

---

### Task 5: DECISIONS #83, spec-refinement note, and full-suite green

**Files:**
- Modify: `DECISIONS.md` (append entry #83)
- Modify: `docs/superpowers/specs/2026-07-18-analyzer-csharp-subset-design.md` (record the refinement)

- [ ] **Step 1: Full solution build and test**

Run: `dotnet build Filament.sln && dotnet test Filament.sln`
Expected: PASS across all test projects (generator, subset, analyzer). Record the new total count.

- [ ] **Step 2: Confirm the honest-ceiling invariants**

Run: `git diff --stat src/filament-runtime`
Expected: empty. No emitted byte moved; the §5 subset did not widen; this is tooling only.

- [ ] **Step 3: Append DECISIONS #83**

Add to `DECISIONS.md` a `## 83.` entry recording: the objective correction (author-time analyzer, not a C# `Signal<T>` — #22's model superseded by plain-field lifting; `Filament.Core` not created); **increment 1a scope** (FIL0002 types only; FIL0001 constructs are increment 1b); the extract-shared-module choice (single-sourcing the *decision* in `TypeSubset.Classify`, #53/#61) and the **refinement** that `Diagnostic`/`SourceOffset` stay generator-side because the analyzer wants Roslyn `Location`s; the validate-in-`Classify`/`CheckType`-delegates refactor with the gates as its behavior-preservation harness; the whole-project opt-in targeting; the resolved Roslyn pin (Task 0); and the mutation guard proving both consumers share one rule. **Honest ceiling unchanged:** RADICAL stays "not eliminated, not established."

- [ ] **Step 4: Record the spec refinement**

In the spec's Architecture section, add a one-paragraph note: `Diagnostic`/`SourceOffset` were kept generator-side (not moved down); the shared module exposes the Location-agnostic `TypeSubset.Classify`, which is what single-sources the subset decision. Cross-reference DECISIONS #83.

- [ ] **Step 5: Commit**

```bash
git add DECISIONS.md docs/superpowers/specs/2026-07-18-analyzer-csharp-subset-design.md
git commit -m "docs(analyzer): record decision #83 (FIL0002 author-time analyzer, shared TypeSubset)"
```

---

## Follow-on (not this plan): increment 1b — FIL0001 construct subset

Reuses everything above. Extract, category by category, into `Filament.Subset` (a `SubsetValidator` alongside `TypeSubset`) and surface via the analyzer, each with its own gate + mutation guard:
`unsupported-statement` (while/switch/try-catch/throw/using/lock/goto), `unsupported-expression` (await, int-division, `List<T>` with args), `unsupported-call` (Console etc.), `unsupported-member` (property/constructor/nested-class/record-member/expression-bodied), `unsupported-modifier`, `unsupported-generic`, `reserved-name`, `name-collision`, `not-csharp`. Each is a woven `Refuse()` site in `CSharpFrontEnd`; extract with the same "keep the `Refuse` call, move the decision" pattern proven in Task 2.

## Self-Review

**Spec coverage:** Objective (author-time analyzer, Core not created) → Tasks 1/3/5. Scope narrowed to FIL0002 with FIL0001 as explicit follow-on → stated up front + Task 5 records it. Drift safety (extract shared module) → Task 2 (`TypeSubset.Classify` single source) + Task 4 mutation guard. Targeting (whole-project opt-in) → Task 3 `IsComponent` + Task 4 `NonComponentClass_IsNotChecked`. Roslyn-version risk + spike-first → Task 0. Testing (gates unchanged, validator unit tests, analyzer verifier tests, mutation check) → Tasks 2/4. DECISIONS #83 → Task 5. Runtime untouched → Global Constraints + Task 2 Step 7 + Task 5 Step 2. **Gap accepted and disclosed:** FIL0001 is out of this plan by design.

**Placeholder scan:** The only intentional blanks are `X.Y.Z` / `A.B.C` (Roslyn versions), resolved by Task 0 Step 6 and consumed by later csprojs — flagged at each use. No TBD/TODO logic.

**Type consistency:** `TypeSubset.Classify(ITypeSymbol?, IReadOnlyCollection<INamedTypeSymbol>, bool) → TypeRefusal?` and `TypeRefusal(string Reason, string Message)` are used identically in Task 2 (generator adapter + shared tests) and Task 3 (analyzer). `TypeSubset.ListElement` is public and repointed in the generator. Descriptor id `FIL0002` matches the generator's code and the fixtures' asserted code.
