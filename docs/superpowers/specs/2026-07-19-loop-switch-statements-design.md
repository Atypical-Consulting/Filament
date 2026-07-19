# Loop & switch statements (`while` / `do-while` / `switch`) — design

**Date:** 2026-07-19
**Status:** approved (autonomous), pending implementation
**Kind:** MEASURED subset widening (BENCH entry, DECISIONS entry). Slice 2 of the C# subset frontier — a
combined "statement family" slice (mirrors #84's shared-statement framing).

## Goal

Admit `while`, `do-while`, and `switch` statements (plus `break`, which `switch` requires) into the §5 C# subset.
Each maps to its JS namesake — `while`→`while`, `do-while`→`do…while`, `switch`→`switch/case/break`. Measured
against Blazor.

## Why one combined slice

`while`, `do`, `switch`, `break` are all **statement-allowlist** additions in the shared
`ConstructSubset.ClassifyStatement`, each with a small emission case in `CSharpFrontEnd.Statement` modeled on the
existing `for` case. They form one family (loop/branch control flow in the `@code` seam); #84 established statements
as a single shared decision. One baseline app exercises all three; one BENCH/DECISIONS entry covers the family.

**Blazor-validity:** all three are ordinary C#; `dotnet build baseline/Loops.Blazor` confirms.

## The change (generator + shared subset)

### 1. `ConstructSubset.ClassifyStatement` — admit the four kinds

```csharp
WhileStatementSyntax => null,
DoStatementSyntax => null,
SwitchStatementSyntax => null,
BreakStatementSyntax => null,   // break is only valid inside a loop/switch (Roslyn enforces); needed for switch
```

(`continue`, `goto`, labelled statements stay refused — deferred.) The refusal message's allowlist prose gains
"while, do-while, switch".

### 2. `CSharpFrontEnd.Statement` — emit the JS namesakes

```csharp
case WhileStatementSyntax w:
{
    var lines = new List<string> { $"while ({Expr(w.Condition)}) {{" };
    lines.AddRange(Nest(w.Statement));
    lines.Add("}");
    return lines;
}

case DoStatementSyntax d:
{
    var lines = new List<string> { "do {" };
    lines.AddRange(Nest(d.Statement));
    lines.Add($"}} while ({Expr(d.Condition)});");
    return lines;
}

case BreakStatementSyntax:
    return ["break;"];

case SwitchStatementSyntax sw:
{
    var lines = new List<string> { $"switch ({Expr(sw.Expression)}) {{" };
    foreach (var section in sw.Sections)
    {
        foreach (var label in section.Labels)
        {
            if (label is CaseSwitchLabelSyntax cl) lines.Add($"case {Expr(cl.Value)}:");
            else if (label is DefaultSwitchLabelSyntax) lines.Add("default:");
            else return Refuse2("unsupported-statement",   // pattern / when-guard labels: deferred
                "a switch case with a pattern or `when` guard is not in the C# subset; only constant " +
                "case labels and default are. Refusing to emit.", label.SpanStart);
        }
        foreach (var stmt in section.Statements) lines.AddRange(Nest(stmt));
    }
    lines.Add("}");
    return lines;
}
```

`Nest` (existing) indents a nested statement/block. `switch` sections emit their labels then their statements
(the `break` inside is a `BreakStatementSyntax`, now admitted → `break;`). Pattern labels / `when` guards / `goto
case` are refused (deferred). The `Refuse2` shape follows the existing located-refusal helper used in `Statement`
(if the method already refuses via `Refuse(...)` returning `[]`, match that; a located FIL0001 either way).

### Faithfulness notes

- **Loops mutating a signal**: `while (n < 5) { n++; }` → `while (n.value < 5) { n.value++; }`. The final signal
  value matches C#; interior effect runs (if unbatched) only add renders, never change the final DOM the oracle
  reads. Same for `do-while`.
- **`switch`**: C# forbids implicit fall-through, so each non-empty section ends in `break`/`return`; emitting the
  same `case…break` is faithful. Empty fall-through (`case 1: case 2:`) → multiple `case` lines, one section —
  faithful. `switch` on `int`/`string` (both in §5) works.

## Runtime

**Unchanged.** All three are JS control-flow keywords, no runtime import. `git diff --stat src/filament-runtime` empty.

## The measured app — `Loops`

One app, three buttons, each handler using one construct; the oracle drives them in sequence.

`baseline/Loops.Blazor/App.razor`:
```razor
@* Loop & switch statements (BENCH n°21): three @code handlers exercising while, switch, do-while.
   The oracle clicks each and reads #v; the sequence 0 -> 5 -> 9 -> 3 proves each construct ran. *@

<h1 id="title">Loops</h1>

<p>n = <span id="v">@n</span></p>

<button id="bwhile" @onclick="DoWhile">while</button>
<button id="bswitch" @onclick="DoSwitch">switch</button>
<button id="bdo" @onclick="DoDo">do</button>

@code {
    private int n = 0;

    private void DoWhile()
    {
        n = 0;
        while (n < 5) { n = n + 1; }   // -> 5
    }

    private void DoSwitch()
    {
        switch (n)                      // n is 5 after DoWhile
        {
            case 5: n = 9; break;
            default: n = 0; break;
        }
    }

    private void DoDo()
    {
        n = 0;
        do { n = n + 1; } while (n < 3);  // -> 3
    }
}
```

Answer key `samples/Loops/loops.js` (handlers as `batch(() => { … })` per #68's multi-write rule, each with the
JS loop/switch). Host shim `samples/filament-loops-gen/main.js`. Baseline modelled on `Counter.Blazor`.

## Measurement — canon gate + snapshot + oracle

Oracle `loops`: `readySelector '#bwhile'`, `observeSelector '#v'`. `verifyContract`: initial `#v` `"0"`; click
`#bwhile` → `"5"`; click `#bswitch` → `"9"`; click `#bdo` → `"3"`. Assert on both builds. `build-filament.sh`
arms mirror `filament-counter-gen`. Publish `blazor-loops`. **BENCH n°21**, `HARNESS 1.15.0 → 1.16.0` disclosed.

## Tests (TDD)

`LoopsTests.cs` (canon/snapshot/contract asserting `while (`, `do {`, `switch (`, `break;`/closed-runtime).
`RepoPaths` + `Generate.LoopsToTemp`.

**Witness flips:** `Unsupported/Code/While.razor`, `Unsupported/Code/Switch.razor` — remove from `CodeTests`
refusal theory, add `While_NowCompiles`/`Switch_NowCompiles`. `Unsupported/Gate/DoWhile.razor` — remove from
`GateSubsetTests` refusal theory, add `DoWhile_NowCompiles`. `ConstructSubsetTests`: add while/do/switch/break to
the admitted-statement assertions (and remove any that asserted them refused). `ConstructSubsetAnalyzerTests`:
`While_IsFlagged`/`Switch_IsFlagged` → not-flagged (single-sourced follows). Keep a still-unsupported statement
(`goto`/`lock`/`try`) as the "flagged" witness.

Regression: existing statement tests (`for`/`foreach`/`if`) byte-identical.

## Non-goals / disclosure

- `continue`, `goto`, `goto case`, labelled statements, switch **pattern**/`when` labels, switch **expressions**
  (`x switch { … }`) stay refused (deferred).
- No runtime change.

## Decision record

Append **DECISIONS #102** (French): `while`/`do-while`/`switch` (+`break`) enter §5, mapping to their JS namesakes;
statement-family admission single-sourced in `ConstructSubset` so the analyzer follows; measured vs Blazor (a
`Loops` app, `0 → 5 → 9 → 3`, BENCH n°21); loops mutating a signal are faithful on the final DOM; runtime
unchanged; continue/goto/pattern-labels/switch-expressions deferred.
