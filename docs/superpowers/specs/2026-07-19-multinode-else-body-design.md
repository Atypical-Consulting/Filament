# Multi-node `@else` / `@else if` body — design

**Date:** 2026-07-19
**Status:** approved (design), pending spec review
**Kind:** MEASURED subset widening (BENCH entry, DECISIONS entry). Slice 2 of the control-flow-completeness frontier.

## Goal

Admit a **multi-node body in any branch of an `@if/@else if/@else` chain** (not just a branch-less `@if`, which
slice 1 / #98 covered). `@if (show) { <a> } else { <b><c> }` compiles to a conditional `list()` keyed by a
**global node index**, where each branch owns a contiguous index range, so flipping the condition swaps all of
the active branch's nodes together. Measured against Blazor.

One sentence: generalize the multi-branch `@if` emission from "one item per branch (index `i`)" to "one item per
**node**, each branch owning an index **range**," which subsumes #82 (one node per branch) and #98 (single-branch
multi-node) byte-for-byte and closes `IfElseMultiBody`.

## Why this slice

Slice 1 (#98) admitted a multi-node body only for a branch-less `@if`: `BranchBody`'s `allowMulti` flag is lifted
only when `ifs.Else is null`. `Unsupported/IfElseMultiBody.razor` (`@if { <a> } else { <b><c> }`) is therefore
still refused `FIL0001 [unsupported-if-body] @ (6,1)`. This is slice 2 of the frontier
(`IfMultiBody✓ → IfElseMultiBody → IfNested → Foreach`).

**Blazor-validity verified up front (RZ9979 lesson).** `dotnet build baseline/IfElseMultiBody.Blazor` succeeds;
`BuildRenderTree` gives the contract: `if (show)` → one `AddMarkupContent` for `<span id="a">`; the `else` block →
two `AddMarkupContent` for `<span id="b">` and `<span id="c">`. Two `<span>`, adjacent, direct children of `#w`,
no wrapper, no interleaved text.

## The lowering — global-node-index ranges

Each branch owns a contiguous range of global node indices; the active branch's whole range is the source, keyed
by identity, dispatched by index:

```js
const _if0 = document.createComment(''); insert(_w, _if0);
function ifBody0_0() { /* <span a> */ return sa; }   // global index 0 (branch 0)
function ifBody0_1() { /* <span b> */ return sb; }   // global index 1 (branch 1)
function ifBody0_2() { /* <span c> */ return sc; }   // global index 2 (branch 1)
list(_w, () => (show.value) ? [0] : [1, 2],  (i) => i,
     (i) => i === 0 ? ifBody0_0() : i === 1 ? ifBody0_1() : ifBody0_2(),  _if0);
```

`show=true` → `[0]` (span a); `show=false` → `[1, 2]` (span b, c in order). Flipping changes the keys entirely, so
`reconcile` unmounts the old branch's nodes and mounts the new branch's, in order. Generator-only, reuses
`list()`, **no runtime change**. The comment anchor is the same disclosed `+1` node (#81).

## The generator change

One file of substance (`TemplateCompiler.EmitIf`) plus removing the now-vacuous flag. Preserves #81/#82/#98
**byte-for-byte**.

### 1. `CSharpFrontEnd` — `allowMulti` becomes vacuous, removed

Every branch now allows a multi-node body, so the flag (added in slice 1) goes away. `If` drops the `singleBranch`
computation and calls `BranchBody(stmt, markers)` for every branch; `BranchBody` refuses only zero-markup or a
non-markup op (nested control flow, stray text):

```csharp
IReadOnlyList<IntermediateNode>? BranchBody(StatementSyntax stmt, IReadOnlyDictionary<string, IntermediateNode> markers)
{
    IEnumerable<StatementSyntax> body = stmt is BlockSyntax b ? b.Statements : [stmt];
    var ops = RegionOps(body, markers);
    var markup = ops.OfType<MarkupOp>().ToList();

    if (markup.Count < 1 || ops.Count != markup.Count)
    {
        Refuse("unsupported-if-body",
            $"a template @if / @else branch body must be one or more elements and nothing else; this one " +
            $"produces {ops.Count} thing(s). @if lowers to a conditional list() whose create() returns one " +
            "root node per item, so a body with a stray text node or nested control flow has no single thing " +
            "to insert and remove. Refusing to emit.",
            stmt.SpanStart);
        return null;
    }
    return markup.Select(m => m.Node).ToList();
}
```

`IfNested` (a branch whose only op is a nested `@if` → `markup.Count == 0`) still refuses at `(2,1)`; the message
is the same "one or more elements" slice 1 already produced for it.

### 2. `TemplateCompiler.EmitIf` — #81 fast path + one general range-based path

```csharp
// #81 fast path: a plain @if with a single-node body. Byte-identical to #81 (constant key, direct fn).
if (op.Branches.Count == 1 && op.Branches[0].Body.Count == 1)
{
    var fn = Unique("ifBody");
    if (!EmitBranchFn(op.Branches[0].Body[0], fn)) return;
    _bindings.Add($"list({container}, () => ({op.Branches[0].Cond}) ? [0] : [], () => 0, {fn}, {anchor});");
    return;
}

// GENERAL: each branch owns a contiguous range of global node indices; the active branch's whole range is
// the source, keyed by identity. Covers single-branch multi-node (#98) and multi-branch with any node
// counts. One node per branch is the degenerate range [i..i] and emits #82's exact bytes.
var allFns = new List<string>();
var ranges = new List<IReadOnlyList<int>>();
for (var b = 0; b < op.Branches.Count; b++)
{
    var body = op.Branches[b].Body;
    var idxs = new List<int>();
    for (var n = 0; n < body.Count; n++)
    {
        var fn = Unique($"ifBody{id}_{allFns.Count}");
        if (!EmitBranchFn(body[n], fn)) return;
        idxs.Add(allFns.Count);
        allFns.Add(fn);
    }
    ranges.Add(idxs);
}
_bindings.Add($"list({container}, {IfSourceRanges(op.Branches, ranges)}, (i) => i, {IfCreate(allFns)}, {anchor});");
```

The slice-1 `Count == 1` multi-node sub-block is **deleted** — the GENERAL path reproduces its bytes exactly (one
branch, range `[0, 1, …]`, trailing `[]`). `IfCreate` is unchanged.

### 3. `IfSource` → `IfSourceRanges`

Replace `IfSource` (per-branch single index `[i]`) with a range-aware version:

```csharp
/// <summary>`() => (c0) ? [r0…] : (c1) ? [r1…] : [rN…]` — each branch's whole global-index range; a
/// trailing @else is `: [rN…]`, a chain with no @else ends `: []`. One index per branch reproduces
/// IfSource exactly.</summary>
static string IfSourceRanges(IReadOnlyList<IfBranch> branches, IReadOnlyList<IReadOnlyList<int>> ranges)
{
    var parts = new List<string>();
    for (var i = 0; i < branches.Count; i++)
    {
        var keys = string.Join(", ", ranges[i]);
        if (branches[i].Cond is { } c) parts.Add($"({c}) ? [{keys}] : ");
        else return "() => " + string.Concat(parts) + $"[{keys}]";   // trailing @else
    }
    return "() => " + string.Concat(parts) + "[]";
}
```

### Byte-identity (verified against the snapshots)

- **#82** (`IfElse`, three branches × one node): ranges `[[0],[1],[2]]`, fns `ifBody0_0/_1/_2` →
  `list(_el0, () => (n.value === 0) ? [0] : (n.value === 1) ? [1] : [2], (i) => i, (i) => i === 0 ? ifBody0_0() : i === 1 ? ifBody0_1() : ifBody0_2(), _if0)` — **identical** to `IfElse.approved.js`.
- **#98** (`IfMultiBody`, one branch × two nodes): range `[[0,1]]`, trailing `[]` →
  `() => (show.value) ? [0, 1] : [], (i) => i, (i) => i === 0 ? ifBody0_0() : ifBody0_1()` — **identical** to `IfMultiBody.approved.js`.
- **#81** (`If`, `RootIf`, one branch × one node): the fast path — **untouched**.

## Runtime

**Unchanged.** Reuses `list()`; `git diff --stat src/filament-runtime` empty.

## The measured app — `IfElseMultiBody`

`baseline/IfElseMultiBody.Blazor/App.razor` (the RootIf pattern: one compiled source, `RepoPaths.IfElseMultiBodyRazor` points here):

```razor
@* Multi-node @else body (BENCH n°18): an @if/@else where the @if branch is ONE <span> and the @else
   branch is TWO adjacent <span>s. Flipping `show` swaps the whole branch — one node out, two in (and
   back), all direct children of #w, no wrapper. Global-index ranges: branch 0 = [0], branch 1 = [1,2].
   No whitespace between siblings (no stray text nodes). *@

<div id="w"><button id="toggle" @onclick="Toggle">toggle</button>@if (show)
{
    <span id="a">a</span>
}
else
{
    <span id="b">b</span><span id="c">c</span>
}</div>

@code {
    private bool show = true;
    private void Toggle() { show = !show; }
}
```

Companion files: `samples/IfElseMultiBody/ifelsemulti.js` (answer key, contract from `BuildRenderTree`),
`samples/filament-ifelsemulti-gen/main.js` (host shim, `App.g.js` gitignored),
`baseline/IfElseMultiBody.Blazor/` (Blazor project modelled on `IfMultiBody.Blazor`).

## The witness moves (refused → compiles)

`Unsupported/IfElseMultiBody.razor` flips refused→compiles:
- **Removed** from `DiagnosticTests.ControlFlow_OutsideTheSubset_IsRefused_AtItsExactLocation` and from
  `ARefusalWritesNoFile`.
- **New** `DiagnosticTests.IfElseMultiBody_NowCompiles_ToARangedConditionalList`: compiles `exit 0`, asserts the
  emitted JS contains `? [0] :` and `[1, 2]` and `(i) => i`, and not `[unsupported-if-body]`.

`Unsupported/IfNested.razor` stays refused `[unsupported-if-body]` at `(2,1)`; `Foreach.razor` stays refused
`[unsupported-foreach]`.

## The measurement — canon gate + snapshot + oracle

1. **Canon gate**: generator compiles `App.razor` → module alpha-equivalent to `samples/IfElseMultiBody/ifelsemulti.js`.
2. **Snapshot** `Snapshots/IfElseMultiBody.approved.js` (bootstrapped).
3. **Playwright oracle** (BENCH n°18):
   - `APPS`: `ifelsemulti: { readySelector: '#toggle', observeSelector: '#w', scenarios: [] }`.
   - `verifyContract` (`app === 'ifelsemulti'`): read `#w > span` ids joined. Initial `"a"`; click `#toggle` →
     `"b,c"`; click → `"a"`. Assert on BOTH builds. The `"b,c"` (order, two nodes) is the measurement that the
     whole `@else` range mounts, in order; the return to `"a"` that the swap is clean.
   - `build-filament.sh`: `filament-ifelsemulti-gen` arms (mirror `filament-ifmulti-gen`).
   - Publish to `bench/publish/blazor-ifelsemulti`.
   - **BENCH n°18**, `HARNESS_VERSION 1.12.0 → 1.13.0` disclosed.

## Tests (TDD)

New `tests/Filament.Generator.Tests/IfElseMultiBodyTests.cs`, mirroring `IfMultiBodyTests`: canon gate; snapshot;
contract (`() => (show.value) ? [0] : [1, 2]`, `(i) => i`, three `document.createElement('span')`, no
`[unsupported-if-body]`); closed-runtime (import ⊆ closed set). `RepoPaths.IfElseMultiBodyRazor` +
`IfElseMultiBodyAnswerKey`; `Generate.IfElseMultiBodyToTemp`.

Regression / negative controls (unchanged): `IfTests`, `IfElseTests`, `RootIfTests`, `RootForeachTests`,
`IfMultiBodyTests` — all byte-identical snapshots. `IfNested`/`Foreach` still refused. Mutation check: neutralize
`ops.Count != markup.Count` → a nested-body probe wrongly compiles; restore.

## Non-goals / disclosure

- Nested control flow in a branch stays refused (slice 3, `IfNested`); in-element `@foreach` stays refused (slice 4).
- Text nodes interleaved between body elements stay refused; the fixture has none.
- No C# subset widening; no runtime change. Comment-anchor `+1` node remains the one disclosed divergence.

## Decision record

Append **DECISIONS #99** (French, house style): multi-node `@else`/`@else if` body enters §5 — the multi-branch
emission generalized to per-branch global-index ranges (`() => c0 ? [r0] : … : [rN]`, identity key, `IfCreate`
dispatch), subsuming #82 (one node/branch) and #98 (single-branch multi-node) byte-for-byte; slice 1's `allowMulti`
flag removed (vacuous), its multi-node block deleted (subsumed); #81 keeps its `() => 0` fast path; `IfNested`/
`Foreach` still refused, witnesses intact; `IfElseMultiBody` witness flipped refused→compiles; runtime UNCHANGED;
Blazor-validity verified up front; measured triple (canon + snapshot + oracle `a → b,c → a`, BENCH n°18, HARNESS
bump disclosed); nested control flow / `@foreach` / interleaved text deferred.
