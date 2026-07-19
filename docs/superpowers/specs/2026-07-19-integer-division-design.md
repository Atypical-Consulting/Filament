# Integer division (`int/int`) — design

**Date:** 2026-07-19
**Status:** approved (autonomous), pending implementation
**Kind:** MEASURED subset widening (BENCH entry, DECISIONS entry). Slice 1 of the C# subset frontier.

## Goal

Admit `int / int` division into the §5 C# subset with a **faithful truncating lowering**: `a / b` (int result)
→ `Math.trunc(a / b)`, which truncates toward zero exactly as C# integer division does (`7/2 → 3`, `-7/2 → -3`).
This closes the deferral #87 recorded explicitly (`double` division landed; `int/int` was refused pending a
faithful lowering). Measured against Blazor.

## Why this slice

#87 admitted `double` division (verbatim `/`, same IEEE-754 op) and **refused `int/int`** because JS `/` is float
(`7/2 = 3.5`) while C# truncates (`= 3`) — emitting `/` would be a silently wrong number (spec 10). `Math.trunc`
makes it faithful: `Math.trunc(7/2) = 3`, matching C#. For 32-bit `int` operands the quotient is exact in a JS
double (< 2^31), so no precision is lost.

**Blazor-validity:** `int/int` is trivially valid C#/Blazor; `dotnet build baseline/DivideInt.Blazor` confirms.

## The change (generator + shared subset)

### 1. `Filament.Subset/ConstructSubset` — admit integer division

Add the classifier and stop refusing int division:

```csharp
/// <summary>Integer division: DivideExpression whose RESULT type is int. Faithful in JS via
/// Math.trunc (truncation toward zero, exactly C#'s int/int). Kept out of JsBinaryOperator (which is
/// syntactic) because, like IsFaithfulDivision, admission is TYPE-dependent.</summary>
public static bool IsIntegerDivision(BinaryExpressionSyntax b, SemanticModel model) =>
    b.IsKind(SyntaxKind.DivideExpression) &&
    model.GetTypeInfo(b).Type?.SpecialType == SpecialType.System_Int32;
```

In `ClassifyExpression`, the DivideExpression block admits int division too:

```csharp
if (e is BinaryExpressionSyntax bin && bin.IsKind(SyntaxKind.DivideExpression))
{
    if (IsFaithfulDivision(bin, model)) return null;   // double result: verbatim /
    if (IsIntegerDivision(bin, model)) return null;    // int result: Math.trunc (CSharpFrontEnd)
    return new Refusal("FIL0001", "unsupported-expression",   // neither int nor double (e.g. decimal): refused
        $"{Describe(bin)} divides operands whose result is neither int nor double; only those two numeric " +
        "divisions are in section 5. Refusing to emit.");
}
```

### 2. `Filament.Generator/CSharpFrontEnd.Expr` — emit `Math.trunc`

Beside the double-division case:

```csharp
case BinaryExpressionSyntax b when Filament.Subset.ConstructSubset.IsFaithfulDivision(b, _model):
    return $"{Expr(b.Left)} / {Expr(b.Right)}";

case BinaryExpressionSyntax b when Filament.Subset.ConstructSubset.IsIntegerDivision(b, _model):
    // C# int/int truncates toward zero (7/2 = 3); JS `/` is float. Math.trunc restores it. The call
    // parenthesizes its argument, so operand precedence is already handled by Expr().
    return $"Math.trunc({Expr(b.Left)} / {Expr(b.Right)})";
```

`currentCount = currentCount / 2` → `currentCount.value = Math.trunc(currentCount.value / 2)`.

## Runtime

**Unchanged.** `Math.trunc` is a JS builtin, not a runtime import. `git diff --stat src/filament-runtime` empty.

## The measured app — `DivideInt`

Modelled on `baseline/Divide.Blazor` but `int`, so the halved value is `3` (truncated), not `3.5` — a value that
proves truncation happened (a generator that emitted bare `/` would render `3.5`).

`baseline/DivideInt.Blazor/App.razor`:
```razor
@* Integer division (BENCH n°20): value halved by INT division. C# truncates 7/2 to 3; JS `/` yields 3.5,
   so the generator must emit Math.trunc(...) — the "3" the oracle asserts proves it did. Mirrors
   Divide.Blazor's DOM contract (blank-line "\n\n" text nodes between siblings). *@

<h1 id="title">DivideInt</h1>

<p>Value: <span id="divide-value">@value</span></p>

<button id="halve" @onclick="Halve">Halve</button>

@code {
    private int value = 7;

    private void Halve()
    {
        value = value / 2;
    }
}
```

Answer key `samples/DivideInt/divideint.js` (`value.value = Math.trunc(value.value / 2)`). Host shim
`samples/filament-divideint-gen/main.js`. Modelled on `Divide`.

## Measurement — canon gate + snapshot + oracle

Oracle `divideint`: mirror the `divide` clause but assert `#divide-value` initial `"7"` → click `#halve` → `"3"`
(NOT `"3.5"` — that would mean truncation leaked out). `readySelector '#halve'`, `observeSelector '#divide-value'`.
`build-filament.sh` arms mirror `filament-divide-gen`. Publish `blazor-divideint`. **BENCH n°20**,
`HARNESS 1.14.0 → 1.15.0` disclosed.

## Tests (TDD)

`DivideIntTests.cs` (canon/snapshot/contract asserting `Math.trunc(`/closed-runtime). `RepoPaths` +
`Generate.DivideIntToTemp`.

**Witness flip:** `Unsupported/Code/IntDivision.razor` — remove from `CodeTests`'
`OutOfSubsetCsharp_IsRefused` theory (it now compiles); add `IntDivision_NowCompiles_ToMathTrunc` (compiles,
emits `Math.trunc(`). `Filament.Subset.Tests.ConstructSubsetTests`: move `i / 2` out of the refused theory into
an admitted-division assertion. `Filament.Analyzer.Tests`: `IntegerDivision_IsFlagged` →
`IntegerDivision_IsNotFlagged` (no diagnostic); repurpose `DivisionNestedInSupportedExpression_IsFlaggedOnce` to
a **still-unsupported** nested expression (e.g. a decimal division `dec / 2m`) so the "flagged once" coverage
stays.

Regression: `DivideTests` (double) byte-identical.

## Non-goals / disclosure

- Only `int` and `double` division. `long`/`decimal` division stays refused (their types aren't in §5).
- **Div-by-zero disclosed:** C# `int/0` throws `DivideByZeroException`; JS `Math.trunc(a/0)` yields `Infinity`.
  Same category as the pre-existing `int`→`number` overflow divergence (JS doubles don't wrap at 2^31). The
  measured behaviour (normal operands) is faithful; the exceptional path is an accepted, disclosed edge.
- No runtime change.

## Decision record

Append **DECISIONS #101** (French): `int/int` enters §5 via `Math.trunc(a / b)` (truncation toward zero, exact for
32-bit ints), closing #87's deferral; admission is TYPE-dependent (`IsIntegerDivision`, result Int32), single-sourced
in `ConstructSubset` so the analyzer follows; measured vs Blazor (7 → 3, not 3.5; BENCH n°20); div-by-zero disclosed
(same category as int overflow); runtime unchanged; long/decimal division deferred.
