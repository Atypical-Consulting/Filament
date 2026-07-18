# Double division enters the §5 subset — design

**Goal:** Admit `double`-valued division `/` into the compiled C# subset (§5), keep `int/int`
refused, and gate the widening against Blazor's *actual rendered behaviour* through the existing
Playwright DOM-contract oracle.

**Status:** design approved by the owner through four decisions (below). Next step: implementation
plan.

---

## Context

The analyzer effort (decisions #83–#86) made the subset's refusals visible at author time but did
**not widen the subset by a single construct**. Decision #80 pins the honest verdict: the RADICAL
variant's *viability condition* is satisfied and measured for both apps, but RADICAL "n'est ni
éliminée ni établie comme architecture — le sous-ensemble §5 reste étroit." The narrow subset is the
named lever.

Decision #77 closed by naming three open §5 false positives — constructs the subset *should* accept
but the generator refuses. The cleanest and highest-value one is division: today `/` is refused for
**all** operands (the operator table simply omits `DivideExpression`), and the refusal message even
contradicts itself, claiming "Section 5 admits ... arithmetic and comparison operators" while
refusing `r / 2.0`. This design closes the `double` half of that false positive.

### Owner decisions locked before this spec

1. **Direction:** widen §5 via the `double`-division false positive (over: pay measurement debts /
   component composition / finish the analyzer tail).
2. **Epistemic basis:** admit it only when **measured**, not on the IEEE-754 identity argument alone.
   Neither answer key (`counter.js`, `rows.js`) contains a division, so a new measured artifact must
   be manufactured. This preserves the repo's "measure, don't reason" invariant.
3. **Instrument:** the **existing Playwright/CDP DOM-contract oracle** (decisions #29/#30), the one
   trusted framework-agnostic instrument — **correctness-only**, no new C1/C3/C4 timing runs (a
   trivial app's weight/speed carries no signal; the correctness oracle is the whole point).
4. (Implicit) The `int/int` refusal **stays**, and the other two #77 false positives (component
   composition, root-level control flow) are **out of scope**.

---

## The rule: type-aware division

`/` is admitted **exactly when its result type is `double`** — i.e. at least one operand is `double`
(C# promotes `int/double`, `double/int`, `double/double` to `double`). Justification:

- **`double` division is faithful.** C#'s `double /` and JS's `/` are the same IEEE-754 operation,
  edge cases included (`1.0/0.0 → Infinity`, `0.0/0.0 → NaN`). Emitting `/` verbatim is exact.
- **`int/int` is not, and stays refused.** C# truncates toward zero (`7/2 == 3`); JS does not
  (`7/2 === 3.5`). Emitting `/` would be a **silently wrong number** — §10's forbidden mode.

Division is the **only** operator whose subset membership depends on operand *types* rather than
*syntax*. That is why `JsBinaryOperator` stays a purely syntactic table and division is decided
semantically — exactly like the `(int)double → Math.trunc` cast already is (both need the
`SemanticModel`; both live beside, not inside, the syntactic operator table).

`%` (modulo) is **not** affected: C# and JS `%` agree on both `int` and `double` (truncated
remainder, sign follows the dividend), so its existing blanket admission is already correct.

---

## Single source — `Filament.Subset.ConstructSubset`

The decision lives once, in the shared module the generator and analyzer both call (decisions
#53/#61). Add:

```csharp
// Division is the one operator whose subset membership depends on operand TYPES, not syntax:
// C#'s int/int truncates and JS's `/` does not (7/2 = 3 vs 3.5), but C#'s double division and
// JS's `/` are the same IEEE-754 op. So `/` is admitted exactly when its RESULT is double.
public static bool IsFaithfulDivision(BinaryExpressionSyntax b, SemanticModel model) =>
    b.IsKind(SyntaxKind.DivideExpression) &&
    model.GetTypeInfo(b).Type?.SpecialType == SpecialType.System_Double;
```

`ClassifyExpression` decides division **before** the generic form-check, so its refusal can carry a
*true, division-specific* message (fixing the self-contradiction #77 flagged) instead of the generic
"unsupported-expression" text:

```csharp
public static Refusal? ClassifyExpression(ExpressionSyntax e, SemanticModel model)
{
    if (e is BinaryExpressionSyntax b && b.IsKind(SyntaxKind.DivideExpression))
    {
        if (IsFaithfulDivision(b, model)) return null;                 // double: in §5
        return new Refusal("FIL0001", "unsupported-expression",        // int/int: refused, truthfully
            $"{Describe(b)} is integer division: C# truncates 7/2 to 3 where JS's `/` yields 3.5, so " +
            "emitting `/` would be a silently wrong number (spec 10). Section 5's `/` requires a " +
            "double operand. Refusing to emit.");
    }
    // ... existing form switch unchanged; DivideExpression no longer reaches it ...
}
```

The `int/int` refusal keeps the **reason slug `unsupported-expression`** (so `CodeTests` /
`GateSubsetTests` code+reason assertions are stable); only the message text improves.

**Analyzer: zero code change.** `ConstructSubsetAnalyzer` calls `ClassifyExpression`, so the widening
and the truer message flow through automatically. This is the extraction's payoff: one edit to
`IsFaithfulDivision` reddens **both** a generator fixture and an analyzer test (mutation-tested).

---

## Generator emission — `CSharpFrontEnd.Expr`

Because `JsBinaryOperator` still returns `null` for `DivideExpression` (it must — that is what keeps
`int/int` from being blanket-blessed), an admitted `double` division would fall through `Expr()`'s
switch to the `FIL-WIRING` default. Add one emission case, mirroring the cast case, **before** the
generic binary case:

```csharp
// Double division: C#'s double `/` and JS's `/` are the same IEEE-754 op (int/int is refused
// upstream in ClassifyExpression). Faithful, so emit it verbatim.
case BinaryExpressionSyntax b when Filament.Subset.ConstructSubset.IsFaithfulDivision(b, _model):
    return $"{Expr(b.Left)} / {Expr(b.Right)}";
```

This is validate-then-translate working as designed: `ClassifyExpression` (line ~1691) has already
refused `int/int`, so only `double` division reaches the switch; the `FIL-WIRING` default remains the
drift backstop.

---

## The measured artifact — a differential division app

A **new, isolated** sample so division is the *only* variable (discipline #59) and the frozen,
byte-pinned Counter/Rows answer keys are untouched.

### The app and the divergent input

`baseline/Divide.Blazor/App.razor` (real Blazor, shared DOM contract, one screen, invariant culture):

```razor
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

**Why `7.0`:** it makes integer and double division **diverge**. Blazor renders `7` initially and
`3.5` after one click. Had the generator wrongly emitted integer semantics, Filament would render
`3` — a *wrong number the oracle sees*. So the measurement tests **the generator's emission**, not
the IEEE-754 math (which is a priori). `3.5`, `1.75`, `0.875` are all exact binary fractions, so C#
`double.ToString()` (invariant) and JS `Number.toString()` render byte-identically; a single halve
(`7 → 3.5`) keeps it minimal and exact.

### Files

- `baseline/Divide.Blazor/` — Blazor project (models `Counter.Blazor`): `App.razor`,
  `_Imports.razor`, `Divide.Blazor.csproj`, `Program.cs`. Source of the `blazor-divide` baseline.
  The generator also compiles **this very file** (RADICAL's "pure .razor" claim), as Rows does.
- `samples/Divide/divide.js` — hand-written **answer key**, Blazor-faithful, `signal(7)` + `effect`
  binding + `listen` click → `value.value = value.value / 2.0`, including the `\n\n` whitespace text
  nodes between siblings (per `counter.js`'s "SHARED DOM CONTRACT" note). Never edited to pass a gate
  (decisions #21/#51).
- `samples/Divide/Divide.approved.js` — byte snapshot of the generator's output (per `If`/`IfElse`).
- `samples/filament-divide-gen/main.js` — imports the generated module's `mount` (per
  `filament-counter-gen`). `Divide.g.js` is generated by `build-filament.sh`, gitignored, regenerated.

### Gates

**Automated (`dotnet test`), the permanent wall:**

| Test | Asserts |
|---|---|
| `ConstructSubsetTests` (subset unit) | `dbl / 2.0`, `dbl / i`, `i / dbl` → `null` (in §5); `i / 2` → refusal |
| `ConstructSubsetAnalyzerTests` (analyzer) | `double` division → **no** diagnostic; `int` division → flagged |
| `DivideTests.Gate_GeneratedDivide_IsAlphaEquivalentToAnswerKey` | generator output ≡ `divide.js` via `canon` |
| `DivideTests.Snapshot_EmittedDivideJs_MatchesApprovedBytes` | byte-stable emission |
| `GateSubsetTests` — negative control `Section5_DoubleDivision_CompilesClean` | emitted JS contains `/` |
| `GateSubsetTests` — the disclosed-false-positive theory | **close** its `double` row (goes RED deliberately, per the file's own convention); keep `int/int` as a *correct* refusal |
| `CodeTests` `IntDivision.razor` | still `FIL0001` / `unsupported-expression` at `(8,24)` |
| `GateSubsetTests.Section5_Operators_CompileClean` | update the "DIVISION IS ABSENT ON PURPOSE" comment |

**Measured (Playwright oracle, correctness-only), Blazor as the authority:**

- Extend `bench/harness/bench.mjs`: add `divide` to `APPS` and a `divide` branch to
  `verifyContract` that loads the app, asserts `#divide-value` reads `7`, clicks `#halve`, and
  asserts it reads `3.5`. Bump `HARNESS_VERSION` `1.3.0 → 1.4.0` (source hash changes → disclose per
  #31/#43/#59).
- `bench/build-filament.sh`: generate `samples/filament-divide-gen/Divide.g.js` from
  `baseline/Divide.Blazor/App.razor` and mount a `filament-divide-gen` label; verify from the
  **artifact** (file exists **and** carries the generator banner), never from the exit code.
- A run (extend `bench/run-*.sh` or a focused `run-divide-correctness.sh`) drives the oracle over
  `blazor-divide` **and** `filament-divide-gen`; both must render `3.5`. The expected `3.5` is
  **Blazor's own** rendered value, not a hand-asserted constant.
- Record an **append-only** `BENCH.md` entry: the correctness result, the `HARNESS_VERSION` bump, and
  the honest note that this artifact gates **correctness only** (no weight/speed).

**Execution:** the implementer builds all of the above and *attempts* the measured run in-session
(`npm install` + `npx playwright install chromium` + `dotnet publish` WASM). If the environment
blocks the browser or the WASM publish, the run is **handed to the owner** with exact commands — the
automated gates still stand, and the measured entry is filled on the owner's machine. This is
disclosed, not silently skipped.

---

## What stays refused (non-goals of this change)

- **`int/int` division** — refused, now with a *true* message. `IntDivision.razor` and the `int` row
  of the disclosed-false-positive theory remain green as **correct** refusals.
- **Component composition** and **root-level control flow** — the other two #77 false positives.
  Untouched.
- **No weight/speed measurement** for the divide app (owner decision #3).

---

## Risks and open points

- **Harness hash discipline (#43/#59).** Editing `bench.mjs` changes `HARNESS_SOURCE_FILES`'s hash.
  Must bump `HARNESS_VERSION` and disclose in the same `BENCH.md` entry; do not let the hash go stale.
- **Double formatting parity.** Guaranteed only for exact binary fractions; the chosen input
  (`7 → 3.5`) is exact on both sides. The app pins invariant culture (Blazor WASM default) so no
  `3,5` locale drift.
- **Signal lifting.** `value` is read by the template and assigned outside construction ⇒ lifted to a
  signal ⇒ `value.value` on both sides; the answer key must match, verified by `canon`.
- **In-session measurement may not complete** (browser/WASM). Mitigated by the hand-off path above;
  the widening is never admitted on the automated gates alone without the measured entry being filled.

---

## Decision journal

On completion, append **decision #87** to `DECISIONS.md` (French, house style): `double` division
enters §5 — the type-aware admission, `int/int` stays refused, the self-contradicting message fixed,
and — the point the owner insisted on — the widening **measured** against Blazor through the #29/#30
oracle rather than admitted on the IEEE-754 argument, with the `HARNESS_VERSION` bump disclosed.
