# Nested `@if` in a branch body — design

**Date:** 2026-07-19
**Status:** approved (autonomous), pending implementation
**Kind:** MEASURED subset widening (BENCH entry, DECISIONS entry). Slice 3 of the control-flow-completeness frontier.

## Goal

Admit a **nested `@if`/`@else` inside an `@if` branch body**. `@if (show) { @if (other) { <a> } }` compiles to a
single conditional `list()` whose source is a **decision tree** (nested ternary) over all conditions, with one
global-indexed builder per leaf markup node. Measured against Blazor.

One sentence: generalize the branch-body handling so a branch may contain a nested `@if` (an `IfOp`), and replace
`IfSourceRanges` with a **recursive `IfExpr`** that walks the whole nested structure into
`() => c0 ? (nested) : … : []`, keyed by global leaf index — which reproduces #82/#98 byte-for-byte when there is
no nesting.

## Why this slice

Slices 1 (#98) and 2 (#99) admit multi-node bodies but every node must be **markup**; a branch whose content is a
nested `@if` is still refused `FIL0001 [unsupported-if-body] @ (2,1)` (`Unsupported/IfNested.razor`). This is
slice 3 (`IfMultiBody✓ → IfElseMultiBody✓ → IfNested → Foreach`).

**Representable and reactive already** (verified): `RegionOps` recurses (`If()` → nested `IfOp`), and
`MarkConditionReads` uses `DescendantNodes().OfType<IfStatementSyntax>()` — so the **inner** condition (`other`) is
already lifted to a signal. **Blazor-validity**: `dotnet build baseline/IfNested.Blazor` will confirm nested `@if`
is valid Blazor (it is — standard) before relying on it.

## The lowering — recursive decision-tree source

A branch's active-index expression is: **its global leaf indices** if the branch is markup-only; the **nested
`IfOp`'s recursive expression** if the branch's sole content is a nested `@if`. The whole `@if` flattens to ONE
`list()` at the container:

```js
const _if0 = document.createComment(''); insert(_w, _if0);
function ifBody0_0() { /* <span a> */ return sa; }   // global leaf index 0
list(_w, () => (show.value) ? ((other.value) ? [0] : []) : [],  (i) => i,  (i) => ifBody0_0(),  _if0);
```

`show && other` → `[0]` (span a mounts); otherwise `[]`. Short-circuit `?:` matches nested-`@if` evaluation
exactly (the inner condition is read only when the outer holds — so the effect subscribes to `other` only while
`show` is true, exactly as two nested `list()`s would). Generator-only, reuses `list()`, **runtime unchanged**.
The comment anchor is the same disclosed `+1` node.

**Faithfulness of flattening:** a leaf's DOM presence is `⋀ (its enclosing conditions)`, and leaves mount in
global-index (source) order before the anchor — exactly Blazor's rendered set and order. Nested `@if/@else` is
covered too (the inner `IfOp` has multiple branches; `IfExpr` recurses through them):
`@if (a) { @if (b) { X } else { Y } }` → `() => (a) ? ((b) ? [0] : [1]) : []`.

## The generator change

### 1. `IfBranch.Body` → a list of ops

```csharp
// TemplatePlan.cs
public sealed record IfBranch(string? Cond, IReadOnlyList<TemplateOp> Body);
```

A branch body is now the `RegionOps` output (markup ops and/or a nested `IfOp`), not bare markup nodes.

### 2. `CSharpFrontEnd.BranchBody` — allow a single nested `@if`

```csharp
IReadOnlyList<TemplateOp>? BranchBody(StatementSyntax stmt, IReadOnlyDictionary<string, IntermediateNode> markers)
{
    IEnumerable<StatementSyntax> body = stmt is BlockSyntax b ? b.Statements : [stmt];
    var ops = RegionOps(body, markers);
    var allMarkup = ops.Count >= 1 && ops.All(o => o is MarkupOp);
    var singleNestedIf = ops.Count == 1 && ops[0] is IfOp;

    if (!allMarkup && !singleNestedIf)
    {
        Refuse("unsupported-if-body",
            $"a template @if / @else branch body must be one or more elements, OR a single nested @if, and " +
            $"nothing else; this one produces {ops.Count} thing(s). Mixing markup with nested control flow, a " +
            "@foreach in a branch, or a stray text node is not in the subset. Refusing to emit.",
            stmt.SpanStart);
        return null;
    }
    return ops;
}
```

`IfNested` (branch = `[nested IfOp]`) is now `singleNestedIf` → admitted. A branch mixing markup + a nested `@if`,
or containing a `@foreach`, or multiple nested `@if`s, still refuses (deferred). Markup-only branches (slices 1/2)
unchanged.

### 3. `TemplateCompiler.EmitIf` — #81 fast path + recursive `IfExpr`

```csharp
// #81 FAST PATH: a plain @if whose body is a single markup node. Byte-identical to #81.
if (op.Branches.Count == 1 && op.Branches[0].Body is [MarkupOp only])
{
    var fn = Unique("ifBody");
    if (!EmitBranchFn(((MarkupOp)op.Branches[0].Body[0]).Node, fn)) return;
    _bindings.Add($"list({container}, () => ({op.Branches[0].Cond}) ? [0] : [], () => 0, {fn}, {anchor});");
    return;
}

// GENERAL: recursively flatten the whole nested @if/@else structure into ONE list(). Every leaf markup
// node gets a global index + builder (DFS source order); the source is the decision tree; the key is the
// global index. No nesting reproduces #82/#98 bytes exactly.
var fns = new List<string>();
var src = IfExpr(op, id, fns);
if (src is null) return;   // a leaf body was refused
_bindings.Add($"list({container}, () => {src}, (i) => i, {IfCreate(fns)}, {anchor});");
```

`IfExpr` / `BranchExpr` (replacing `IfSourceRanges`):

```csharp
/// <summary>Recursive decision-tree expr for one @if: `(c0) ? <b0> : (c1) ? <b1> : <bN>`, trailing @else
/// has no test, no @else ends `: []`. Returns null if a leaf body was refused.</summary>
string? IfExpr(IfOp op, int id, List<string> fns)
{
    var parts = new List<string>();
    for (var i = 0; i < op.Branches.Count; i++)
    {
        if (BranchExpr(op.Branches[i], id, fns) is not { } b) return null;
        if (op.Branches[i].Cond is { } c) parts.Add($"({c}) ? {b} : ");
        else return string.Concat(parts) + b;   // trailing @else
    }
    return string.Concat(parts) + "[]";
}

/// <summary>A branch's active-index expr: its global leaf indices `[i, …]` if markup-only, or the
/// parenthesized nested decision tree if its sole content is a nested @if.</summary>
string? BranchExpr(IfBranch branch, int id, List<string> fns)
{
    if (branch.Body is [IfOp nested])                       // single nested @if
        return IfExpr(nested, id, fns) is { } e ? $"({e})" : null;

    var idxs = new List<int>();                             // markup-only (slices 1/2)
    foreach (var op in branch.Body)
    {
        var fn = Unique($"ifBody{id}_{fns.Count}");
        if (!EmitBranchFn(((MarkupOp)op).Node, fn)) return null;
        idxs.Add(fns.Count);
        fns.Add(fn);
    }
    return "[" + string.Join(", ", idxs) + "]";
}
```

### Byte-identity (verified by construction against the snapshots)

- **#82** (no nesting, one node/branch): `BranchExpr` returns `[i]` → `IfExpr` = `(c0) ? [0] : (c1) ? [1] : [2]` — identical to `IfSourceRanges`/`IfElse.approved.js`.
- **#98** (single-branch, two nodes): `BranchExpr` = `[0, 1]` → `() => (show.value) ? [0, 1] : []` — identical.
- **#81** (single-branch, one markup node): fast path — untouched.

`IfSourceRanges` (slice 2) is removed; `IfExpr` subsumes it. `IfCreate`, `EmitBranchFn` unchanged.

## Runtime

**Unchanged.** `git diff --stat src/filament-runtime` empty.

## The measured app — `IfNested`

`baseline/IfNested.Blazor/App.razor` (two toggles, to drive all four condition combinations):

```razor
@* Nested @if (BENCH n°19): an @if(other) inside an @if(show) branch. #a is present iff show && other.
   Flattens to one list() with a decision-tree source () => show ? (other ? [0] : []) : []. Two toggles
   exercise all four combinations. No whitespace between siblings. *@

<div id="w"><button id="tshow" @onclick="ToggleShow">show</button><button id="tother" @onclick="ToggleOther">other</button>@if (show)
{
    @if (other)
    {
        <span id="a">a</span>
    }
}</div>

@code {
    private bool show = true;
    private bool other = true;
    private void ToggleShow() { show = !show; }
    private void ToggleOther() { other = !other; }
}
```

Answer key `samples/IfNested/ifnested.js` (contract from `BuildRenderTree`). Host shim
`samples/filament-ifnested-gen/main.js`. Baseline modelled on `IfMultiBody.Blazor`.

## Witness flip

`Unsupported/IfNested.razor` flips refused→compiles: removed from
`ControlFlow_OutsideTheSubset_IsRefused_AtItsExactLocation` and `ARefusalWritesNoFile`; new
`IfNested_NowCompiles_ToADecisionTreeConditionalList` (asserts `(show.value) ?`, `(other.value) ?`, `? [0] :`, no
`[unsupported-if-body]`). `Foreach.razor` stays refused `[unsupported-foreach]`. A **new** boundary witness for
the deferred *mixed markup+nested* case is added: `Unsupported/IfNestedMixed.razor`
(`@if (show) { <p id="x">x</p> @if (other) { <span id="a">a</span> } }`) → still refused `[unsupported-if-body]`,
so the "sole content" boundary stays under test.

## Measurement — canon gate + snapshot + oracle

Same triple. Oracle `ifnested`: `readySelector '#tshow'`, `observeSelector '#w'`. `verifyContract`: initial
`#w>span` = `"a"`; click `#tother` → `""`; click `#tother` → `"a"`; click `#tshow` → `""`; click `#tshow` → `"a"`
— asserting the conjunction on both builds. `build-filament.sh` arms mirror `filament-ifmulti-gen`. Publish
`blazor-ifnested`. **BENCH n°19**, `HARNESS 1.13.0 → 1.14.0` disclosed.

## Tests (TDD)

`IfNestedTests.cs` (canon/snapshot/contract/closed-runtime). `RepoPaths.IfNestedRazor`/`IfNestedAnswerKey`;
`Generate.IfNestedToTemp`. Regression: `IfTests`/`IfElseTests`/`IfMultiBodyTests`/`IfElseMultiBodyTests`/`RootIf`/
`RootForeach` byte-identical. Mutation: neutralize `singleNestedIf` → `IfNested` refuses again; restore.

## Non-goals / disclosure

Deferred: a branch **mixing** markup with a nested `@if`, **multiple** nested `@if`s as siblings in one branch,
nested `@foreach`, and in-element `@foreach` (slice 4). No C# subset widening; no runtime change.

## Decision record

Append **DECISIONS #100** (French): nested `@if`/`@else` in a branch enters §5 via a recursive decision-tree
source (`IfExpr`) flattening the whole structure into one `list()`; subsumes #82/#98 byte-for-byte; `IfBranch.Body`
becomes a `TemplateOp` list; `BranchBody` admits a single nested `@if` (markup-only OR single-nested-if; mixed /
multiple / nested-foreach refused, new witness `IfNestedMixed`); short-circuit `?:` matches nested-`@if`
evaluation & subscription; runtime unchanged; measured triple (BENCH n°19, HARNESS bump disclosed); mixed/multiple/
foreach deferred.
