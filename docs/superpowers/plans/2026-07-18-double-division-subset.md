# Double division enters §5 — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Admit `double`-valued division `/` into the compiled C# subset (§5), keep `int/int` refused, single-source the decision, and gate the widening against Blazor's actual rendered behaviour through the existing Playwright DOM-contract oracle.

**Architecture:** The subset decision lives once in `Filament.Subset.ConstructSubset` (type-aware, needs the `SemanticModel`, exactly like the existing `(int)double` cast). The generator delegates to it and gains one emission case; the analyzer is unchanged (single-sourced) and gains only tests. A new isolated `Divide` app — whose input makes integer and double division **diverge** (`7.0 → 3.5`) — is gated automatically (alpha-equivalence + snapshot) and measured against Blazor through the correctness-only DOM oracle.

**Tech Stack:** C# / Roslyn (`Filament.Subset` netstandard2.0 @ Roslyn 4.8; `Filament.Generator` net10.0 @ Roslyn 5.6), xUnit, Node (`canon.mjs`), Playwright/CDP bench harness, Blazor WASM baseline.

## Global Constraints

- **The rule:** `/` is admitted **iff its result type is `double`** (`model.GetTypeInfo(divideExpr).Type?.SpecialType == SpecialType.System_Double`). `int/int` stays refused (C# truncates, JS does not → silently wrong number, §10).
- **Single source (decisions #53/#61):** the division decision is added to `Filament.Subset.ConstructSubset` ONLY. The analyzer calls it and gets **zero code change**. One edit to the shared rule must redden **both** a generator test and an analyzer test (mutation-tested).
- **`JsBinaryOperator` stays purely syntactic** — do NOT add `DivideExpression` to it (that would blanket-bless `int/int`). Division is decided semantically alongside the cast.
- **`int/int` refusal keeps reason slug `unsupported-expression`** so existing code+reason+location assertions stay stable; only the message text improves, and it must still contain the substring `DivideExpression` (via `Describe`).
- **Answer keys are never edited to make a gate pass** (decisions #21/#51).
- **Measured, not reasoned:** the widening is validated against Blazor via the #29/#30 Playwright oracle (correctness-only, no C1/C3/C4). If the in-session run is blocked (browser/WASM), hand the run to the owner with exact commands — never silently skip; the automated gates still stand.
- **Invariant culture** on the Blazor baseline (default for Blazor WASM; `InvariantGlobalization=true`) so `3.5` never renders as `3,5`.
- **Harness hash discipline (#31/#43/#59):** editing `bench.mjs` changes `HARNESS_SOURCE_FILES`'s hash → bump `HARNESS_VERSION` `1.3.0 → 1.4.0` and disclose in the same `BENCH.md` entry.

---

## Task 1: Widen §5 — double division, end-to-end

One atomic behaviour: the shared rule admits double division, the generator emits it, and the disclosed-false-positive gate is reconciled. A reviewer cannot sensibly accept the rule change while rejecting the emission — they are one behaviour.

**Files:**
- Modify: `src/Filament.Subset/ConstructSubset.cs` (the `ClassifyExpression` method + a new helper)
- Modify: `src/Filament.Generator/CSharpFrontEnd.cs:1708` (add one `Expr()` case)
- Test: `tests/Filament.Subset.Tests/ConstructSubsetTests.cs` (subset unit)
- Test: `tests/Filament.Generator.Tests/GateSubsetTests.cs` (negative control + close the false positive)

**Interfaces:**
- Produces: `Filament.Subset.ConstructSubset.IsFaithfulDivision(BinaryExpressionSyntax b, SemanticModel model) : bool` — Task 2 (analyzer tests) and Task 3 (the sample) rely on double division being admitted.

- [ ] **Step 1: Write the failing subset unit tests**

In `tests/Filament.Subset.Tests/ConstructSubsetTests.cs`, add the three double-result divisions to the SUPPORTED theory `SupportedExpressionForms_ClassifyToNull` (the `ParseExpr` fixture already declares `int i`, `double dbl`):

```csharp
    [InlineData("dbl / 2.0")]   // double / double -> double result: faithful
    [InlineData("dbl / i")]     // double / int    -> double result: faithful
    [InlineData("i / dbl")]     // int / double    -> double result: faithful
```

Leave `[InlineData("i / 2")]` (integer division) in the UNSUPPORTED theory `UnsupportedExpressionForms_ClassifyToUnsupportedExpression` exactly as-is.

- [ ] **Step 2: Run the subset tests to verify they fail**

Run: `dotnet test tests/Filament.Subset.Tests --filter SupportedExpressionForms_ClassifyToNull`
Expected: FAIL — the three new cases currently classify to a non-null `unsupported-expression` refusal (division is absent from the operator table).

- [ ] **Step 3: Implement the type-aware rule in `ConstructSubset`**

In `src/Filament.Subset/ConstructSubset.cs`, add the helper (place it just above `ClassifyExpression`):

```csharp
    /// <summary>Division is the one operator whose subset membership depends on operand TYPES, not
    /// syntax: C#'s int/int truncates and JS's `/` does not (7/2 = 3 vs 3.5), but C#'s double
    /// division and JS's `/` are the same IEEE-754 op. So `/` is admitted exactly when its RESULT
    /// is double. Pure of the operator table on purpose — JsBinaryOperator must NOT bless it, or
    /// int/int would be admitted too.</summary>
    public static bool IsFaithfulDivision(BinaryExpressionSyntax b, SemanticModel model) =>
        b.IsKind(SyntaxKind.DivideExpression) &&
        model.GetTypeInfo(b).Type?.SpecialType == SpecialType.System_Double;
```

Then change `ClassifyExpression` so division is decided **before** the generic form-check (add the block at the very top of the method body, before `var supported = e switch`):

```csharp
        // Division: the one operator whose admission is TYPE-dependent, and whose refusal deserves a
        // TRUE reason rather than the generic "arithmetic operators are admitted" text (decision 77).
        if (e is BinaryExpressionSyntax bin && bin.IsKind(SyntaxKind.DivideExpression))
        {
            if (IsFaithfulDivision(bin, model)) return null;               // double result: in §5
            return new Refusal("FIL0001", "unsupported-expression",         // int/int: refused, truthfully
                $"{Describe(bin)} is integer division: C# truncates 7/2 to 3 where JS's `/` yields 3.5, " +
                "so emitting `/` would be a silently wrong number (spec 10). Section 5's `/` requires a " +
                "double operand. Refusing to emit.");
        }
```

(The generic `e switch` below is unchanged — `DivideExpression` no longer reaches it. `JsBinaryOperator` is untouched.)

- [ ] **Step 4: Run the subset tests to verify they pass**

Run: `dotnet test tests/Filament.Subset.Tests`
Expected: PASS — double divisions classify to null; `i / 2` still refused with reason `unsupported-expression`.

- [ ] **Step 5: Write the failing generator negative control + close the false positive**

In `tests/Filament.Generator.Tests/GateSubsetTests.cs` (the `NegativeControls` class), add:

```csharp
    /// <summary>
    /// SECTION 5 ADMITS "arithmetic operators" AND DOUBLE DIVISION IS NOW ONE OF THEM.
    /// C#'s double `/` and JS's `/` are the same IEEE-754 op, so `r / 2.0` compiles and emits `/`.
    /// The divergent-input measurement (baseline/Divide.Blazor) is what proves the emitted `/`
    /// renders Blazor's number; this control only pins that it IS emitted.
    /// </summary>
    [Fact]
    public void Section5_DoubleDivision_CompilesClean()
    {
        var js = Compiles(
            """
            <p><span id="a">@r</span></p>
            <button id="go" @onclick="Go">go</button>

            @code {
                private double r = 7.0;
                private void Go() { r = r / 2.0; }
            }
            """);
        Assert.Contains("/ 2.0", js);   // the division is emitted verbatim, not refused
    }
```

Then reconcile the disclosed-false-positive theory: `double` division is no longer a false positive. Replace the `[Theory]`/`[InlineData]` pair `Division_IsADisclosedFalsePositive_ButIsLoudAndLocated` with an integer-only `[Fact]` (keep the `int/int` case, which is a *correct* refusal), and rewrite its doc comment:

```csharp
    /// <summary>
    /// INTEGER DIVISION IS REFUSED, AND CORRECTLY: C#'s int/int truncates (7/2 == 3) where JS's `/`
    /// yields 3.5, so emitting `/` would be a silently wrong NUMBER — section 10's forbidden mode.
    /// Loud and located, writes no file. DOUBLE division is NOT here anymore: it entered §5 (see
    /// Section5_DoubleDivision_CompilesClean), because for double operands C#'s `/` and JS's `/` are
    /// the same IEEE-754 op and there is no mismatch to protect against.
    /// </summary>
    [Fact]
    public void IntegerDivision_IsRefused_LoudAndLocated()
    {
        var (exit, stderr, wrote) = Compile(
            """
            <p><span id="a">@n</span></p>
            <button id="go" @onclick="Go">go</button>

            @code {
                private int n = 0;
                private void Go() { n = n / 2; }
            }
            """);

        Assert.NotEqual(0, exit);
        Assert.False(wrote, "a refusal must write no file");
        Assert.Contains("FIL0001: [unsupported-expression]", stderr);
        Assert.Contains("DivideExpression", stderr);       // still named, via Describe(bin)
        Assert.Contains("integer division", stderr);        // the true reason, not the old self-contradiction
        Assert.Matches(@"\(\d+,\d+\): FIL0001", stderr);
    }
```

Also update the comment in `Section5_Operators_CompileClean` — replace the "DIVISION IS ABSENT ON PURPOSE ... see Division_IsADisclosedFalsePositive" note with: `// Double division is covered by Section5_DoubleDivision_CompilesClean; integer division stays refused (IntegerDivision_IsRefused_LoudAndLocated).`

- [ ] **Step 6: Run the generator negative control to verify it fails**

Run (build the generator first): `dotnet build src/Filament.Generator -c Debug && dotnet test tests/Filament.Generator.Tests --filter Section5_DoubleDivision_CompilesClean`
Expected: FAIL — `Compiles()` asserts exit 0, but with Step 3 done the generator now *admits* double division and falls through `Expr()`'s switch to the `FIL-WIRING` default (no emission case yet), so it exits non-zero.

- [ ] **Step 7: Add the generator emission case**

In `src/Filament.Generator/CSharpFrontEnd.cs`, immediately **before** the existing generic binary case at line ~1708 (`case BinaryExpressionSyntax b when Filament.Subset.ConstructSubset.JsBinaryOperator(b) is { } op:`), insert:

```csharp
            // Double division: C#'s double `/` and JS's `/` are the same IEEE-754 op (int/int is
            // refused upstream in ClassifyExpression). Faithful, so emit it verbatim. Decided
            // semantically, exactly like the (int)double cast below — JsBinaryOperator stays syntactic.
            case BinaryExpressionSyntax b when Filament.Subset.ConstructSubset.IsFaithfulDivision(b, _model):
                return $"{Expr(b.Left)} / {Expr(b.Right)}";
```

- [ ] **Step 8: Run the full generator + subset suites to verify green**

Run: `dotnet build src/Filament.Generator -c Debug && dotnet test tests/Filament.Generator.Tests tests/Filament.Subset.Tests`
Expected: PASS — `Section5_DoubleDivision_CompilesClean` green (emits `/ 2.0`), `IntegerDivision_IsRefused_LoudAndLocated` green, and every prior gate (Counter/Rows/If/IfElse alpha-equivalence, the 20-case subset gate) unchanged.

- [ ] **Step 9: Commit**

```bash
git add src/Filament.Subset/ConstructSubset.cs src/Filament.Generator/CSharpFrontEnd.cs \
        tests/Filament.Subset.Tests/ConstructSubsetTests.cs tests/Filament.Generator.Tests/GateSubsetTests.cs
git commit -m "feat(subset): double division enters §5 — type-aware admission, int/int stays refused"
```

---

## Task 2: Analyzer coverage — the single-source payoff

No analyzer code changes: `ConstructSubsetAnalyzer` calls `ClassifyExpression`, so the widening and truer message already flow through. This task proves it, and completes the mutation guard (one edit to `IsFaithfulDivision` reddens a generator test from Task 1 AND an analyzer test here).

**Files:**
- Test: `tests/Filament.Analyzer.Tests/ConstructSubsetAnalyzerTests.cs`

**Interfaces:**
- Consumes: double division admitted by `ConstructSubset.ClassifyExpression` (Task 1). The analyzer's `Body(...)` helper already declares `private double dbl = 0;`.

- [ ] **Step 1: Write the tests**

Add to `tests/Filament.Analyzer.Tests/ConstructSubsetAnalyzerTests.cs` (in the "expression forms" section):

```csharp
    [Fact]
    public async Task DoubleDivision_IsNotFlagged()
        // double / double is faithful in JS -> in §5 -> no diagnostic.
        => await Body("        double x = dbl / 2.0;").RunAsync();

    [Fact]
    public async Task IntegerDivision_IsFlagged()
        // int / int truncates in C# but not JS -> refused.
        => await Body("        int x = {|FIL0001:i / 2|};").RunAsync();
```

- [ ] **Step 2: Run the analyzer tests to verify they pass**

Run: `dotnet test tests/Filament.Analyzer.Tests --filter Division`
Expected: PASS — `DoubleDivision_IsNotFlagged` produces no diagnostic; `IntegerDivision_IsFlagged` flags exactly `i / 2`.

- [ ] **Step 3: Verify the mutation guard spans the seam**

Temporarily break the shared rule to confirm both sides redden, then revert **by Edit** (never `git checkout` — it drops uncommitted neighbours):
- In `ConstructSubset.IsFaithfulDivision`, change `System_Double` → `System_Single`.
- Run: `dotnet build src/Filament.Generator -c Debug && dotnet test tests/Filament.Generator.Tests tests/Filament.Analyzer.Tests --filter Division`
- Expected: BOTH `Section5_DoubleDivision_CompilesClean` (generator) AND `DoubleDivision_IsNotFlagged` (analyzer) go RED — proof the two share one rule.
- Revert the edit (Edit `System_Single` back to `System_Double`), rebuild, re-run: PASS.

- [ ] **Step 4: Commit**

```bash
git add tests/Filament.Analyzer.Tests/ConstructSubsetAnalyzerTests.cs
git commit -m "test(analyzer): double division not flagged, int division flagged — single-source proven"
```

---

## Task 3: The Divide sample — Blazor baseline, answer key, automated gate

The measured artifact's code side, plus its automated regression wall (alpha-equivalence + snapshot), mirroring Counter/If/IfElse.

**Files:**
- Create: `baseline/Divide.Blazor/App.razor`, `_Imports.razor`, `Program.cs`, `Divide.Blazor.csproj`
- Create: `samples/Divide/divide.js` (answer key)
- Create: `tests/Filament.Generator.Tests/Snapshots/Divide.approved.js` (generated in Step 5)
- Create: `tests/Filament.Generator.Tests/DivideTests.cs`
- Modify: `tests/Filament.Generator.Tests/RepoPaths.cs` (add `DivideRazor`, `DivideAnswerKey`)
- Modify: `tests/Filament.Generator.Tests/GateTests.cs` (add `Generate.DivideToTemp()`)

**Interfaces:**
- Consumes: `IsFaithfulDivision` / double-division emission (Task 1).
- Produces: `RepoPaths.DivideRazor`, `RepoPaths.DivideAnswerKey`, `Generate.DivideToTemp()`.

- [ ] **Step 1: Create the Blazor baseline app (the file the generator compiles — no drift, like Rows)**

`baseline/Divide.Blazor/App.razor`:

```razor
@* Root component. Rendered directly into #app by Program.cs.

   The markup below is the SHARED DOM CONTRACT and must not be altered. The blank
   lines between the three siblings are part of the contract (Blazor ships them as
   "\n\n" text nodes; so does the generator; so does divide.js). See counter.js. *@

<h1 id="title">Divide</h1>

<p>Value: <span id="divide-value">@value</span></p>

<button id="halve" @onclick="Halve">Halve</button>

@code {
    private double value = 7.0;

    private void Halve()
    {
        value = value / 2.0;
    }
}
```

`baseline/Divide.Blazor/_Imports.razor`:

```razor
@using Microsoft.AspNetCore.Components.Web
```

`baseline/Divide.Blazor/Program.cs`:

```csharp
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Divide.Blazor;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

await builder.Build().RunAsync();
```

`baseline/Divide.Blazor/Divide.Blazor.csproj` (identical size-affecting PropertyGroup to Counter.Blazor, so the baselines stay comparable):

```xml
<Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <OverrideHtmlAssetPlaceholders>true</OverrideHtmlAssetPlaceholders>
    <InvariantGlobalization>true</InvariantGlobalization>
    <PublishTrimmed>true</PublishTrimmed>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly" Version="10.0.9" />
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly.DevServer" Version="10.0.9" PrivateAssets="all" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Wire the test paths**

In `tests/Filament.Generator.Tests/RepoPaths.cs`, add:

```csharp
    /// <summary>THE FILE BLAZOR COMPILES (no Filament stand-in; no drift, like Rows).</summary>
    public static string DivideRazor => Path.Combine(Root, "baseline", "Divide.Blazor", "App.razor");

    /// <summary>The double-division SPEC (decisions 21/51). Never edited to make a gate pass.</summary>
    public static string DivideAnswerKey => Path.Combine(Root, "samples", "Divide", "divide.js");
```

In `tests/Filament.Generator.Tests/GateTests.cs` (the `Generate` class, beside `IfElseToTemp`):

```csharp
    public static string DivideToTemp() => ToTemp(RepoPaths.DivideRazor, "Divide");
```

- [ ] **Step 3: Write the failing DivideTests**

`tests/Filament.Generator.Tests/DivideTests.cs`:

```csharp
using Xunit;

namespace Filament.Generator.Tests;

public class DivideTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the module emitted from baseline/Divide.Blazor/App.razor
    /// is alpha-equivalent to the hand-written samples/Divide/divide.js. The key is the SPEC and the
    /// REFERENCE; the generator is JUDGED. divide.js's Blazor-faithfulness is what the DOM-contract
    /// oracle measures (baseline/Divide.Blazor rendered vs filament-divide-gen rendered).
    /// </summary>
    [Fact]
    public void Gate_GeneratedDivide_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.DivideToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.DivideAnswerKey);
        Assert.True(exit == 0,
            "double-division gate FAILED. Generated module is NOT alpha-equivalent to samples/Divide/divide.js.\n" +
            stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>The emitted division is faithful JS `/`, and the state is a lifted signal.</summary>
    [Fact]
    public void EmittedDivide_HalvesADoubleSignalWithFaithfulSlash()
    {
        var js = File.ReadAllText(Generate.DivideToTemp());
        Assert.Contains("value.value = value.value / 2.0;", js);   // faithful `/`, on the signal
        Assert.DoesNotContain("Math.trunc", js);                    // NOT integer division
        Assert.DoesNotContain("[unsupported-expression]", js);
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedDivideJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.DivideToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "Divide.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
```

- [ ] **Step 4: Write the answer key by transcribing the generator's faithful mapping**

`samples/Divide/divide.js` — the Blazor-faithful reference, written the way the compiler emits it (model exactly on `samples/Counter/counter.js`: a lifted `signal`, one `effect` per binding point, the `\n\n` whitespace text nodes, mount attaches last). The `import` specifier must match what the generator emits (relative path to `src/filament-runtime/src/index.ts`):

```javascript
/**
 * Divide — hand-written Filament app. The ANSWER KEY for baseline/Divide.Blazor/App.razor.
 *
 * Every line is written the way the COMPILER emits it, not the way a human would prefer.
 * The point of this app: value = value / 2.0 is DOUBLE division. C#'s double `/` and JS's
 * `/` are the same IEEE-754 op, so it maps to `/` verbatim — unlike int/int, which truncates
 * in C# and would be a silently wrong number in JS (spec 10) and is refused. 7.0 / 2.0 = 3.5,
 * a value integer division (== 3) could never produce: that divergence is what the DOM-contract
 * oracle measures baseline/Divide.Blazor against.
 */

import { signal, effect, setText, listen, insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  // @code { private double value = 7.0; } — read by the template, assigned in Halve() -> lifted.
  const value = signal(7.0);

  // <h1 id="title">Divide</h1>
  const h1 = document.createElement('h1');
  h1.id = 'title';
  insert(h1, document.createTextNode('Divide'));

  // <p>Value: <span id="divide-value">@value</span></p>
  const p = document.createElement('p');
  insert(p, document.createTextNode('Value: '));
  const span = document.createElement('span');
  span.id = 'divide-value';
  const t = document.createTextNode('');
  insert(span, t);
  insert(p, span);

  // <button id="halve" @onclick="Halve">Halve</button>
  const button = document.createElement('button');
  button.id = 'halve';
  insert(button, document.createTextNode('Halve'));

  // @value
  effect(() => setText(t, value.value));

  // private void Halve() { value = value / 2.0; } — DOUBLE division maps to `/` verbatim.
  listen(button, 'click', () => {
    value.value = value.value / 2.0;
  });

  // attach last; the two "\n\n" nodes are App.razor's blank lines between siblings.
  insert(target, h1);
  insert(target, document.createTextNode('\n\n'));
  insert(target, p);
  insert(target, document.createTextNode('\n\n'));
  insert(target, button);
}
```

- [ ] **Step 5: Reconcile answer key + snapshot against the generator's ACTUAL output**

The answer key above is predicted from `counter.js`'s shape; the generator is the authority on exact bytes (import specifier, literal form of `7.0`/`2.0`, spacing). Reconcile:

Run: `dotnet build src/Filament.Generator -c Debug`
Run: `dotnet run --project src/Filament.Generator -- baseline/Divide.Blazor/App.razor /tmp/divide.g.js && cat /tmp/divide.g.js`
- Compare the emitted body to `divide.js`. If the generator writes `signal(7)` vs `signal(7.0)`, or a different import path, **the answer key is transcribed to match the generator's faithful output** (that is what an answer key IS — decisions #21/#51 forbid editing it to pass a *broken* gate, but transcribing the compiler's faithful mapping is its purpose). The canon gate (Step 6) then confirms alpha-equivalence.
- Delete the placeholder snapshot so Step 6 regenerates it: `rm -f tests/Filament.Generator.Tests/Snapshots/Divide.approved.js`

- [ ] **Step 6: Run DivideTests — approve the snapshot, verify the gate**

Run: `dotnet test tests/Filament.Generator.Tests --filter DivideTests`
Expected: first run — `Snapshot_EmittedDivideJs_MatchesApprovedBytes` FAILS with "wrote ...; review + re-run" (it writes `Divide.approved.js`). Review that file (it must carry the generator banner and the `/ 2.0` division), then re-run.
Run again: `dotnet test tests/Filament.Generator.Tests --filter DivideTests`
Expected: PASS — `Gate_GeneratedDivide_IsAlphaEquivalentToAnswerKey` (canon exit 0), `EmittedDivide_HalvesADoubleSignalWithFaithfulSlash`, and the snapshot all green. If canon reports NOT alpha-equivalent, fix `divide.js` to match the generator's faithful output (Step 5), not the reverse.

- [ ] **Step 7: Gitignore the transient generated file if needed, then commit**

Confirm `samples/Divide/.gen-*.js` and `/tmp/divide.g.js` are not staged (the `.gen-*` temp is deleted by `ToTemp`). Commit:

```bash
git add baseline/Divide.Blazor samples/Divide/divide.js \
        tests/Filament.Generator.Tests/DivideTests.cs \
        tests/Filament.Generator.Tests/RepoPaths.cs tests/Filament.Generator.Tests/GateTests.cs \
        tests/Filament.Generator.Tests/Snapshots/Divide.approved.js
git commit -m "test(divide): Blazor baseline + answer key + alpha-equivalence gate for double division"
```

---

## Task 4: Harness wiring — the correctness-only DOM oracle

Extend the trusted Playwright oracle to drive the divide app and assert Blazor's rendered value. Correctness-only: a `--contract-only` mode runs `verifyContract` and exits without timing/weight.

**Files:**
- Modify: `bench/harness/bench.mjs` (`HARNESS_VERSION`, `APPS.divide`, `verifyContract` divide branch, `--contract-only` flag + early exit)
- Modify: `bench/build-filament.sh` (add the `filament-divide-gen` label to the label tables)

**Interfaces:**
- Consumes: `baseline/Divide.Blazor/App.razor` (Task 3), the generator (Task 1).

- [ ] **Step 1: Bump the harness version (hash discipline #31/#43/#59)**

In `bench/harness/bench.mjs`, change `export const HARNESS_VERSION = '1.3.0';` → `'1.4.0';` with a one-line comment: `// 1.4.0: added the 'divide' contract (double-division correctness oracle).`

- [ ] **Step 2: Add the divide app shape**

In `bench/harness/bench.mjs`, add to the `APPS` object (correctness-only: no timing scenarios):

```javascript
  divide: {
    readySelector: '#halve',
    observeSelector: '#divide-value',
    scenarios: [],   // correctness-only: verifyContract drives it, no timed runs
  },
```

- [ ] **Step 3: Add the divide branch to `verifyContract`**

In `verifyContract`, after the `if (app === 'counter') { ... }` block, add:

```javascript
    if (app === 'divide') {
      return ctx.page.evaluate(async () => {
        const out = { problems: [], observed: {} };
        for (const sel of ['#halve', '#divide-value']) {
          if (!document.querySelector(sel)) out.problems.push(`missing required element: ${sel}`);
        }
        if (out.problems.length) return out;

        const read = () => document.querySelector('#divide-value').textContent.trim();
        out.observed.initial = read();
        // 7.0 renders "7" in both C# (invariant) and JS. If it already read the post-click
        // value the assertion below would be vacuous.
        if (out.observed.initial !== '7') {
          out.problems.push(`#divide-value initial is "${out.observed.initial}", expected "7"`);
          return out;
        }
        document.querySelector('#halve').click();
        out.observed.afterHalve = read();
        // THE MEASUREMENT: 7.0 / 2.0 = 3.5 (double division). Integer division would render "3".
        // This is Blazor's own rendered value; the generated app must match it.
        if (out.observed.afterHalve !== '3.5') {
          out.problems.push(
            `#divide-value after #halve is "${out.observed.afterHalve}", expected "3.5". ` +
            `"3" means integer-division semantics leaked into the emitted JS.`);
        }
        return out;
      });
    }
```

- [ ] **Step 4: Add the `--contract-only` mode**

In `parseArgs`, add a case: `case '--contract-only': o.contractOnly = true; break;`. In `main`, immediately after the `verifyContract` gate reports "DOM contract OK", add an early return when `opts.contractOnly` is set (before `measureWeight`):

```javascript
    if (opts.contractOnly) {
      process.stderr.write('[bench] --contract-only: contract met, skipping weight/timing.\n');
      return 0;
    }
```

- [ ] **Step 5: Add the filament-divide-gen label to build-filament.sh**

In `bench/build-filament.sh`, add `filament-divide-gen` to `ALL_LABELS`, and a case to each of: `project_for` (`echo "samples/filament-divide-gen"`), `mode_for` (`production`), `razor_for` (`echo "$REPO_ROOT/baseline/Divide.Blazor/App.razor"`), `generated_js_for` (`echo "Divide.g.js"`), `title_for` (`echo "Divide"`), `blazor_label_for` (`echo "blazor-divide"`), `css_for` (`echo "$REPO_ROOT/baseline/Divide.Blazor/wwwroot/css/app.css"`). (A `-stats` variant is NOT needed — divide has no C3.)

- [ ] **Step 6: Create the Filament divide-gen entry point**

`samples/filament-divide-gen/main.js` (model on `samples/filament-counter-gen/main.js`):

```javascript
import { mount } from './Divide.g.js';
mount(document.getElementById('app'));
```

Add `samples/filament-divide-gen/Divide.g.js` to `.gitignore` (it is regenerated every build; mirror the `Counter.g.js` ignore rule).

- [ ] **Step 7: Verify the harness parses and the label resolves (no browser needed)**

Run: `node --check bench/harness/bench.mjs && echo "bench.mjs OK"`
Run: `./bench/build-filament.sh --list | grep filament-divide-gen`
Expected: `bench.mjs OK` and the label listed.

- [ ] **Step 8: Commit**

```bash
git add bench/harness/bench.mjs bench/build-filament.sh samples/filament-divide-gen/main.js .gitignore
git commit -m "bench(divide): correctness-only DOM oracle + filament-divide-gen label (HARNESS_VERSION 1.4.0)"
```

---

## Task 5: Run the measurement, record the BENCH.md entry

The differential: Blazor renders `3.5`, Filament renders `3.5`. Attempt in-session; hand off to the owner with exact commands if the browser/WASM steps are blocked.

**Files:**
- Modify/Create: `BENCH.md` (append-only entry)
- Create (transient, not committed): `bench/publish/blazor-divide/`, `bench/publish/filament-divide-gen/`

- [ ] **Step 1: Install the browser driver (once)**

Run: `cd bench/harness && npm ci && npx playwright install chromium`
Expected: playwright + chromium available. If this fails (network/sandbox), STOP and hand off (Step 5).

- [ ] **Step 2: Publish the Blazor baseline**

Run: `dotnet publish baseline/Divide.Blazor -c Release -o bench/publish/blazor-divide`
Expected: `bench/publish/blazor-divide/wwwroot/index.html` exists. If the WASM publish is blocked, hand off (Step 5).

- [ ] **Step 3: Build the Filament divide-gen app**

Run: `./bench/build-filament.sh filament-divide-gen`
Expected: `bench/publish/filament-divide-gen/{index.html,app.js}` built; the script prints "emitted N B of JS" and verifies the generator banner.

- [ ] **Step 4: Drive both apps through the correctness oracle**

Run (Blazor — static root is `<label>/wwwroot`):
```
node bench/harness/bench.mjs --dir bench/publish/blazor-divide/wwwroot --app divide --label blazor-divide --headless --contract-only
```
Run (Filament — static root is `<label>`, NO wwwroot):
```
node bench/harness/bench.mjs --dir bench/publish/filament-divide-gen --app divide --label filament-divide-gen --headless --contract-only
```
Expected: both print `[bench] DOM contract OK: {"initial":"7","afterHalve":"3.5"}` and exit 0. A `"3"` for `afterHalve` (exit 3, "DOM CONTRACT NOT MET") would mean integer-division semantics leaked — that is the measurement catching a generator bug.

- [ ] **Step 5: If any of Steps 1–4 is blocked, hand off**

Report to the owner exactly which step was blocked and why, and provide the Step 1–4 command block verbatim to run on their machine. Do NOT proceed to declare the widening "measured" until the oracle has actually run — the automated gates (Tasks 1–3) stand regardless, but decision #87's "measured" claim waits for this.

- [ ] **Step 6: Record the append-only BENCH.md entry**

Append a new numbered `BENCH.md` entry (do not rewrite existing ones): the `divide` correctness result (`blazor-divide` and `filament-divide-gen` both render `7 → 3.5`), the `HARNESS_VERSION` bump to `1.4.0`, and the honest note that this artifact gates **correctness only** (no C1/C3/C4 — a trivial app's weight/speed carries no signal), with the divergent-input rationale (why `7.0/2.0` and not, say, `4.0/2.0`). If the run was handed off, record the entry once the owner reports the result, not before.

- [ ] **Step 7: Commit**

```bash
git add BENCH.md
git commit -m "bench(divide): correctness measured — Blazor and Filament both render 7 -> 3.5"
```

---

## Task 6: Record the decision and update memory

**Files:**
- Modify: `DECISIONS.md` (append decision #87)
- Modify: memory (`analyzer-fil0002-done-fil0001-next.md` or a new file, + `MEMORY.md`)

- [ ] **Step 1: Append decision #87 to DECISIONS.md**

In French, house style, append `## 87. La division `double` entre dans le sous-ensemble §5 — l'admission SÉMANTIQUE (type du résultat), int/int refusé, et la mesure DIFFÉRENTIELLE contre Blazor`. Cover: the type-aware rule (result type double, single-sourced in `ConstructSubset`, `JsBinaryOperator` deliberately left syntactic); `int/int` stays refused with a *true* message (the #77 self-contradiction fixed); the analyzer changed by zero lines (single-source payoff, mutation-tested across the seam); and — the point the owner insisted on — the widening **measured** against Blazor through the #29/#30 oracle (correctness-only, divergent input `7.0/2.0 → 3.5`, `HARNESS_VERSION 1.4.0` disclosed), NOT admitted on the IEEE-754 argument alone. State the honest ceiling: §5 widened by one construct; RADICAL still "ni éliminée ni établie". If the measurement was handed off, say so.

- [ ] **Step 2: Update memory**

Update `memory/analyzer-fil0002-done-fil0001-next.md` (or add a short new project memory) to record: double division shipped (§5 widened, decision #87), the type-aware pattern, the divide measured artifact + `HARNESS_VERSION 1.4.0`, and whether the measurement ran or was handed off. Update the `MEMORY.md` index line.

- [ ] **Step 3: Commit**

```bash
git add DECISIONS.md
git commit -m "docs: record decision #87 (double division enters §5, measured against Blazor)"
```

---

## Self-review notes

- **Spec coverage:** rule (Task 1) · generator emission (Task 1) · analyzer single-source (Task 2) · measured artifact code side + alpha-equivalence + snapshot (Task 3) · Playwright oracle correctness-only + HARNESS_VERSION bump (Task 4) · the measurement run + BENCH.md (Task 5) · #77 message fix (Task 1, Step 5) · disclosed-false-positive theory closed (Task 1) · decision #87 (Task 6). All covered.
- **Green at every commit:** Task 1 is atomic (rule + emission + gate reconciliation land together); Tasks 2–4 are additive. No task commits a red tree.
- **Type consistency:** `IsFaithfulDivision(BinaryExpressionSyntax, SemanticModel)` is defined in Task 1 and consumed by the generator (Task 1) and referenced by Task 2's mutation guard; `Generate.DivideToTemp()`, `RepoPaths.DivideRazor/DivideAnswerKey` defined and used consistently in Task 3.
- **Hand-off honesty:** Task 5 never lets the automated gates masquerade as the measurement; #87's "measured" claim waits for the oracle to actually run.
