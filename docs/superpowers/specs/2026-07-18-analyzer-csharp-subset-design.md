# Design: `Filament.Analyzer` — author-time diagnostics for the C# subset

**Date:** 2026-07-18
**Status:** approved (design), pending implementation plan
**Context:** `src/Filament.Core` and `src/Filament.Analyzer` have been empty directories since
DECISIONS #21. #22's original design — the author writes `Signal<T>.Value` and the generator maps it
1:1 to `.value` — was **superseded** in Phase 3 (#62–#72) by the plain-field lifting model: the author
writes ordinary Blazor C# (`private int currentCount = 0; … currentCount++;`) and the generator lifts a
field to a signal iff the template reads it and something assigns it outside its construction site
(`CSharpFrontEnd`: `IsSignal => ReadByTemplate && AssignedOutsideConstruction`). Under that model
**nothing consumes a C# `Signal<T>`**, so Core was never filled. The generator's refusals
(`FIL0001` out-of-subset construct, `FIL0002` out-of-subset type, `FIL0003` out-of-subset Razor, plus
`FIL-WIRING` for tool faults — #61) are already real, with exact `file(line,col)` spans — but they only
fire when the generator **console executable** runs (#58). They do **not** appear in the IDE as the
author types. This increment gives them an author-time home.

## Goal

Surface the generator's existing `FIL0001`/`FIL0002` `@code` refusals as **live IDE diagnostics** —
red squiggles a Blazor author sees *before* running the generator — via a Roslyn `DiagnosticAnalyzer`.
The analyzer and the generator must decide "is this in the C# subset?" by calling **one shared
module**, so the subset is never described in two places (#53, #61). This is developer tooling: it does
**not** widen the §5 subset, does **not** change any emitted byte, and does **not** move the §8 verdict.
RADICAL stays "not eliminated, not established."

## Decisions taken during brainstorming

1. **Objective = author-time analyzer, not a C# runtime.** The genuinely-missing piece is *where* the
   refusals fire (author-time vs. generator-run), not new diagnostic IDs (they already exist and are
   correct) and not a C# `Signal<T>` (nothing authors against it). **`Filament.Core` stays deleted.**
2. **Scope = the C# `@code` subset only (`FIL0001`/`FIL0002`).** The `@code` block compiles to real C#
   members a Roslyn analyzer sees natively. The Razor *template* subset (`FIL0003`) is deferred: raw
   Razor markup is invisible to a C# analyzer except as generated render-tree calls, and matching those
   is a separate, harder increment.
3. **Drift safety = extract a shared subset module** (not a parity-tested reimplementation). The purist
   single-source path: one module owns the subset decision; the generator is refactored to call it; the
   analyzer calls the same. Chosen deliberately over the lower-risk parity-test option, accepting a
   refactor of the gated front end because the four GREEN gates make that refactor falsifiable.
4. **Targeting = whole-project opt-in by reference.** Any project that references `Filament.Analyzer`
   gets **every** Blazor component (`ComponentBase`-derived / every `.razor` `@code`) checked — the
   model "this is a Filament project, so every component must be in-subset." No marker attribute; a
   `[FilamentComponent]` opt-in for mixed projects is a deferred refinement.

## Architecture — three projects

> **Refinement recorded during implementation (DECISIONS #83).** Two things changed from the table
> below, both in service of the spec's actual goal (single-source the *subset decision*): (1)
> `Diagnostic` and `SourceOffset` were **kept generator-side, not moved down** — `Diagnostic` is a Razor-
> `SourceSpan` stderr-formatting type, while the analyzer wants Roslyn `Location`s; the shared module
> exposes only the decision (`TypeSubset.Classify` + a Location-agnostic `TypeRefusal`). (2) The first
> increment implements **`FIL0002` (types) only** — a complete vertical slice — because the generator's
> refusals are woven into its translation walk and extract cleanly only one category at a time;
> `FIL0001` constructs are increment 1b. The shared type is `TypeSubset.Classify`, not a `SubsetValidator`
> class (that name is reserved for the 1b construct work).

| Project | TFM | Role |
|---|---|---|
| **`Filament.Subset`** *(new)* | `netstandard2.0` | The shared truth. Owns the `Diagnostic` record and `SourceOffset` (moved down from the generator) and a new `SubsetValidator` that walks `@code` C# against a `SemanticModel` and returns the `FIL0001`/`FIL0002` refusals **without translating**. References `Microsoft.CodeAnalysis.CSharp` with `PrivateAssets=all` — the consumer (generator host / analyzer host) provides Roslyn. |
| **`Filament.Analyzer`** *(new)* | `netstandard2.0` | A thin `DiagnosticAnalyzer` that runs `SubsetValidator` over the host compilation's Blazor components and maps each `Diagnostic` to a Roslyn `Diagnostic` at the same span. No code fixes (deferred). |
| **`Filament.Generator`** *(existing)* | `net10.0` | Refactored to reference `Filament.Subset` and call `SubsetValidator` as its first pass (see below). Emission untouched. |

`Filament.Core` is not created (YAGNI — no consumer for a C# `Signal<T>`).

## The crux — validate-then-translate refactor of the generator

Today the generator refuses **during** translation: `Refuse()` calls are interleaved with emission, and
"out-of-subset" is a `switch` `default:` fall-through (`CSharpFrontEnd.cs`). There is no separable
predicate — the subset is *implicit in which cases the translator handles*. The refactor makes it
explicit by splitting detection from emission:

1. `CSharpFrontEnd.Compile()` runs `SubsetValidator.Validate(model, members)` **first**.
2. If it returns refusals → those become `Diagnostics` (external behavior unchanged).
3. Only on a clean pass does it translate — and the `default:` cases for the **moved** refusals become
   `throw GeneratorException` ("the validator should have caught this; reaching here is `FIL-WIRING`"),
   because a validated tree is guaranteed in-subset.

This is a strict improvement to the generator (detection vs. emission finally separated, in the spirit
of Phase 3's separation work) and it is what makes the subset single-sourced. **The four GREEN gates
(Counter/Rows/If/IfElse alpha-equivalence) plus the 178-test suite are the behavior-preservation
harness** — they must stay green through the refactor, unchanged. Behavior the harness pins: the
validator reports **in source order** and reproduces the exact `(code, reason, line, col)` that each
`Unsupported/` fixture currently asserts (each fixture is a single-refusal case).

## Scope

### Moved into `SubsetValidator` (surfaced by the analyzer)
The pure `@code`-member refusals: unsupported member kinds (`unsupported-member`, incl. record members
and member generics), unsupported types (`FIL0002` — `unsupported-type`, `unresolved-type`),
unsupported expressions/statements in bodies (`unsupported-expression` — `await`, lambda, object
creation, `List<T>` constructed with args, LINQ, `try/catch`, `throw`, `goto`, `lock`, `switch`, …),
unsupported modifiers (`unsupported-modifier`), reserved names (`reserved-name`), and name collisions
(`name-collision`). These are decided over `@code` member syntax + the `SemanticModel` alone.

### Explicitly out of this increment — stays in the generator, unmoved, unsurfaced
- **The `@foreach`/template-seam refusals** (`unsupported-foreach`, `unsupported-template-statement`) —
  they depend on template context, are FIL0003-adjacent, and stay inline in the generator.
- **All Razor-template refusals (`FIL0003`)** — a separate, harder increment (raw Razor isn't C#).
- **Code fixes / a `[FilamentComponent]` marker** — deferred refinements.

## Risk & first spike

**Risk #1 — Roslyn version.** A `DiagnosticAnalyzer` must load under the IDE/build **host's** Roslyn,
which may differ from the generator's pinned `Microsoft.CodeAnalysis.CSharp 5.6.0` (#70). A single
`Filament.Subset` assembly compiled against one Roslyn version must be consumable by both the net10
generator and the netstandard2.0 analyzer-under-host.

**Spike (first task, everything waits on it):** stand up a trivial `netstandard2.0` analyzer, confirm
the Roslyn version the net10 host accepts, and **pin `Filament.Subset` to the lowest Roslyn version
common to host + generator** — aligning the generator's `Microsoft.CodeAnalysis.CSharp` reference if
needed. That alignment, if required, is a small change guarded by the generator's existing tests.
`SubsetValidator` uses only stable Roslyn surface (`SyntaxKind`, `ISymbol`, `SemanticModel`, node
walking), so a low common version is expected to suffice; the spike proves it rather than assuming it.

## Testing

- **Generator gates unchanged** — Counter/Rows/If/IfElse alpha-equivalence + the 178-test suite prove
  the refactor is behavior-preserving. No gate output changes.
- **`SubsetValidator` unit tests** — run it directly over the `Unsupported/Code/*` and
  `Unsupported/Gate/*` corpus; assert exact `(code, reason, line, col)`.
- **Analyzer tests** — `Microsoft.CodeAnalysis.Testing` (`CSharpAnalyzerVerifier`) with `[|marked|]`
  spans over the same fixtures, proving the squiggle lands on the right token with the right `FIL00xx`.
- **Shared-module mutation check** (the project's idiom — an untested backstop is a claim, #61):
  neutralize one `SubsetValidator` rule and confirm **both** a generator test *and* an analyzer test go
  red — proving both genuinely consume the one shared module, not a copy (#53's "the test measured a
  COPY of the wiring" is what this guards against).
- **Suite** grows and stays green.

## DECISIONS #83

Records: the objective correction (author-time analyzer, not a C# `Signal<T>` — #22's model superseded
by plain-field lifting, Core stays deleted); the extract-shared-module choice over parity-testing, and
why single-sourcing the subset honors #53/#61; the validate-then-translate refactor of `CSharpFrontEnd`
and the gates as its behavior-preservation harness; the scope boundary (pure `@code` refusals moved;
`@foreach`/template-seam and `FIL0003` stay); the whole-project opt-in targeting; and the Roslyn-version
risk with its spike-first resolution. **Honest ceiling unchanged:** this is tooling — no emitted byte
moves, the §5 subset does not widen, and RADICAL stays "not eliminated, not established."
