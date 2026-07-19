# Multi-node `@if` body — design

**Date:** 2026-07-19
**Status:** approved (design), pending spec review
**Kind:** MEASURED subset widening (BENCH entry, DECISIONS entry). Slice 1 of the control-flow-completeness frontier.

## Goal

Admit a **single-branch `@if` (no `@else`) whose body is more than one element** into the compiled subset. A
body like `<span id="a">a</span><span id="b">b</span>` compiles to a conditional `list()` with **one list
item per body node**, mounting and unmounting all body nodes together as direct children of the container —
no wrapper element. Measured against Blazor.

One sentence: generalize the plain-`@if` lowering from "one node per branch" to "one list item per body
node," so `@if (cond) { <a><b> }` lowers to `list(c, () => cond ? [0,1] : [], (i) => i, (i) => i===0?f0():f1(), anchor)`.

## Why this slice

`@if` (#81), `@else`/`@else if` (#82), and root-level control flow (#89) are in the subset. The immediate
refusal that remains for a plain `@if` is the **multi-node body** — `Unsupported/IfMultiBody.razor` is refused
`FIL0001 [unsupported-if-body] @ (2,1)`, and #81 recorded it as a *deliberate* deferral, not a bug:

> le corps multi-nœud (`unsupported-if-body` — le garde `markup.Count != 1 || ops.Count != markup.Count` refuse : **voulu**, pas un bug à corriger vers le support)

This closes that deferral for the single-branch case. It is the natural first step of the control-flow
frontier (`IfMultiBody → IfElseMultiBody → IfNested → Foreach`).

**Blazor-validity verified up front (the RZ9979 lesson).** `dotnet build baseline/IfMultiBody.Blazor`
**succeeds** — a single-branch `@if` with a two-element body is ordinary Blazor. `--dump`/`BuildRenderTree`
confirms Blazor compiles the body to a single opaque `AddMarkupContent(N, "<span id=\"a\">a</span><span id=\"b\">b</span>")` inside `if (show)` (both spans, adjacent, no wrapper, no interleaved text node).

## The constraint, and the lowering

`list()` (runtime `list.ts`) is **one-node-per-key**: `Row.n` is a single `Node`; `mount`/`unmount`/`insert`
and the reconcile anchor (`rows[ni+1].n`) all operate on one node. A branch's `create()` returns one root
node — which is exactly why `CSharpFrontEnd.BranchBody` refuses a body that produces more than one markup op.

**The lowering (approach ①, generator-only).** Treat each body node as its own list *item*. The active
condition enumerates the body nodes as the source; the key selects which node builder runs:

```js
const _if0 = document.createComment('');
insert(_w, _if0);
function ifBody0_0() { /* <span id="a">a</span> */ return sa; }
function ifBody0_1() { /* <span id="b">b</span> */ return sb; }
list(_w, () => (show.value) ? [0, 1] : [],  (i) => i,
     (i) => i === 0 ? ifBody0_0() : ifBody0_1(),  _if0);
```

This is #82's exact multi-branch machinery (`IfCreate` dispatch, a keyed source), generalized from "one node
per *branch*" to "one node per *body node*." When `show` flips true→false the source goes `[0,1]→[]` and
`reconcile` unmounts both spans; false→true mounts both, in source order, before the anchor. Both spans are
direct children of `#w`; the only divergence from Blazor is the same disclosed comment-anchor `+1` node
established at #81. **No runtime change** — `src/filament-runtime` stays byte-identical (firewall clean).

Rejected alternatives: a runtime node-range (`Row` holds a head/tail-anchored range) breaks the firewall and
is unneeded here; a wrapper `<div>` diverges from Blazor's DOM (no wrapper) and is unfaithful.

## The generator change

Two files, and it preserves #81 and #82 **byte-for-byte** in their existing cases.

### 1. `CSharpFrontEnd` — relax the branch body for the single-branch `@if` only

`IfBranch.Body` becomes a list of nodes:

```csharp
// TemplatePlan.cs
public sealed record IfBranch(string? Cond, IReadOnlyList<IntermediateNode> Body);
```

`BranchBody` returns the markup nodes, gated by an `allowMulti` flag:

```csharp
IReadOnlyList<IntermediateNode>? BranchBody(
    StatementSyntax stmt, IReadOnlyDictionary<string, IntermediateNode> markers, bool allowMulti)
{
    IEnumerable<StatementSyntax> body = stmt is BlockSyntax b ? b.Statements : [stmt];
    var ops = RegionOps(body, markers);
    var markup = ops.OfType<MarkupOp>().ToList();

    // Non-markup ops (nested @if/@foreach, stray text) are still refused: ops.Count must equal
    // markup.Count. allowMulti only lifts the "exactly ONE" cap to "one or more".
    var tooMany = allowMulti ? markup.Count < 1 : markup.Count != 1;
    if (tooMany || ops.Count != markup.Count)
    {
        Refuse("unsupported-if-body",
            $"a template @if / @else branch body must be {(allowMulti ? "one or more elements" : "exactly ONE element")} " +
            $"and nothing else; this one produces {ops.Count} thing(s). A body with a stray text node or nested " +
            "control flow has no element identity to insert and remove. Refusing to emit.",
            stmt.SpanStart);
        return null;
    }
    return markup.Select(m => m.Node).ToList();
}
```

`If()` passes `allowMulti: true` **only when there is no `@else`** (`ifs.Else is null`); every branch of an
`if/else-if/else` chain passes `allowMulti: false`, so `IfElseMultiBody` stays refused (its witness is intact
until slice 2):

```csharp
IfOp? If(IfStatementSyntax ifs, IReadOnlyDictionary<string, IntermediateNode> markers)
{
    var singleBranch = ifs.Else is null;         // plain @if, no else -> multi-node body allowed
    var branches = new List<IfBranch>();
    var cur = ifs;
    while (true)
    {
        if (BranchBody(cur.Statement, markers, allowMulti: singleBranch) is not { } body) return null;
        branches.Add(new IfBranch(Expr(cur.Condition), body));
        if (cur.Else is not { } els) break;
        if (els.Statement is IfStatementSyntax nested) { cur = nested; continue; }
        if (BranchBody(els.Statement, markers, allowMulti: false) is not { } elseBody) return null;
        branches.Add(new IfBranch(null, elseBody));
        break;
    }
    return new IfOp(branches);
}
```

### 2. `TemplateCompiler.EmitIf` — single-node paths byte-identical, multi-node path new

```csharp
if (op.Branches.Count == 1)
{
    var body = op.Branches[0].Body;
    if (body.Count == 1)
    {
        // EXACT #81 emission — byte-identical, so the @if gate + snapshot hold.
        var fn = Unique("ifBody");
        if (!EmitBranchFn(body[0], fn)) return;
        _bindings.Add($"list({container}, () => ({op.Branches[0].Cond}) ? [0] : [], () => 0, {fn}, {anchor});");
        return;
    }
    // NEW: multi-node body — one list item per body node.
    var fns = new List<string>();
    for (var i = 0; i < body.Count; i++)
    {
        var fn = Unique($"ifBody{id}_{i}");
        if (!EmitBranchFn(body[i], fn)) return;
        fns.Add(fn);
    }
    var keys = string.Join(", ", Enumerable.Range(0, fns.Count));
    _bindings.Add($"list({container}, () => ({op.Branches[0].Cond}) ? [{keys}] : [], (i) => i, {IfCreate(fns)}, {anchor});");
    return;
}

// MULTI-BRANCH (#82) — unchanged; each branch is one node (allowMulti was false).
var branchFns = new List<string>();
for (var i = 0; i < op.Branches.Count; i++)
{
    var fn = Unique($"ifBody{id}_{i}");
    if (!EmitBranchFn(op.Branches[i].Body[0], fn)) return;   // Body[0]: the strict one node
    branchFns.Add(fn);
}
_bindings.Add($"list({container}, {IfSource(op.Branches)}, (i) => i, {IfCreate(branchFns)}, {anchor});");
```

`EmitBranchFn(IntermediateNode, fn)` is unchanged (still one node → one create function). `IfCreate` and
`IfSource` are unchanged. The single-node single-branch and the multi-branch cases emit the identical bytes
they do today.

## Runtime

**Unchanged.** The multi-node body reuses `list()` exactly as `@foreach` and plain `@if` do; the comment
anchor is `document.createComment` (a DOM builtin, not a runtime import). `git diff --stat src/filament-runtime`
stays empty. But this IS a measured generator widening: emitted bytes change and a BENCH entry is added.

## The measured app — `IfMultiBody`

Following the `RootIf`/`RootForeach` pattern (#89): **the baseline `App.razor` is the single compiled
source** — the generator compiles it for the canon gate + snapshot, and Blazor publishes it for the oracle.

`baseline/IfMultiBody.Blazor/App.razor`:

```razor
@* Multi-node @if body (BENCH n°17): a single-branch @if whose body is TWO adjacent <span> elements,
   mounted/unmounted together as direct children of #w -- no wrapper. One list() item per body node
   (keys [0,1]); the comment anchor is the disclosed +1 node (decision 81). A toggle drives `show` so
   both spans are measured appearing and disappearing together, IN ORDER.

   No whitespace between </span> and <span>, nor between the button and @if, matching If.razor's
   contract: Razor turns SOURCE whitespace between siblings into text nodes, and there is none here. *@

<div id="w"><button id="toggle" @onclick="Toggle">toggle</button>@if (show)
{
    <span id="a">a</span><span id="b">b</span>
}</div>

@code {
    private bool show = true;
    private void Toggle() { show = !show; }
}
```

- `show` is reactive: read by the `@if` condition (via `MarkConditionReads`, #81) and assigned in `Toggle`.
- The body's two spans emit two create functions (`ifBody0_0`, `ifBody0_1`) and a source `() => (show.value) ? [0, 1] : []`, keyed by identity, dispatched by `IfCreate`.
- `Toggle` performs one write → no `batch()` (#68), single-use → inlined into the click handler.

Companion files: `samples/IfMultiBody/ifmulti.js` (hand-written answer key, its DOM contract read from
Blazor's own `BuildRenderTree` per the #64/#81 method), `samples/filament-ifmulti-gen/main.js` (host shim,
`App.g.js` gitignored), `baseline/IfMultiBody.Blazor/` (Blazor project modelled on `RootIf.Blazor`).

## The witness moves (refused → compiles)

`Unsupported/IfMultiBody.razor` (the minimal witness, no button) flips from refused to compiling, exactly as
`IfAtRoot` did at #89:

- **Removed** from `DiagnosticTests.ControlFlow_OutsideTheSubset_IsRefused_AtItsExactLocation` (`[Theory]`
  InlineData) and from `DiagnosticTests.ARefusalWritesNoFile` (`[InlineData]`).
- **New** `DiagnosticTests.IfMultiBody_NowCompiles_ToAMultiNodeConditionalList` (mirrors
  `IfAtRoot_NowCompiles_ToAConditionalAgainstTarget`): compiles the witness `exit 0`, asserts the emitted JS
  contains `list(` and `[0, 1]` and `(i) => i`, and does **not** contain `[unsupported-if-body]`.

`Unsupported/IfElseMultiBody.razor` and `Unsupported/IfNested.razor` **stay refused** `[unsupported-if-body]`
(slices 2 and 3). Their refusal-theory and `ARefusalWritesNoFile` entries are untouched — they remain the
boundary witnesses proving multi-branch bodies and nested control flow are still out of subset.

## The measurement — canon gate + snapshot + oracle

The full Phase-4 standard, and it also closes #81's open "à re-mesurer si `@if` entre dans une app mesurée."

1. **Canon gate** (the #81 family method): the generator compiles `baseline/IfMultiBody.Blazor/App.razor` to
   a module `canon` reports **alpha-equivalent** to `samples/IfMultiBody/ifmulti.js`, whose DOM contract is
   read from Blazor's `BuildRenderTree` (throwaway ref component, built-inspected-deleted; recorded in the
   answer key header).
2. **Byte snapshot** against `Snapshots/IfMultiBody.approved.js` (bootstrapped write-then-review).
3. **Playwright oracle** (BENCH n°17): both builds rendered, `#w`'s span children asserted identical across a
   toggle:
   - `bench/harness/bench.mjs` `APPS`: `ifmulti: { readySelector: '#toggle', observeSelector: '#w', scenarios: [] }`.
   - `verifyContract` clause (`app === 'ifmulti'`): read `#w > span` ids as a joined string. Initial `"a,b"`;
     click `#toggle` → `""` (both removed together); click again → `"a,b"` (both restored, in order). Assert
     on BOTH builds, identically. Order (`a,b`, not `b,a`) is the measurement that the one-item-per-node list
     preserves node order; the empty string is the measurement that both nodes unmount together.
   - `bench/build-filament.sh`: add `filament-ifmulti-gen` to the app list and its case arms (mirroring
     `filament-rootif-gen`): APPBASE `samples/filament-ifmulti-gen`, mode `production`, source
     `baseline/IfMultiBody.Blazor/App.razor`, generated `App.g.js`, sample dir `IfMultiBody`, publish
     `blazor-ifmulti`, css `baseline/IfMultiBody.Blazor/wwwroot/css/app.css`.
   - Publish the Blazor baseline to `bench/publish/blazor-ifmulti` via `dotnet publish`.
   - Record **BENCH n°17** (CORRECTION only). `HARNESS_VERSION` bump **disclosed** (`1.11.0 → 1.12.0`).

## Tests (TDD)

New `tests/Filament.Generator.Tests/IfMultiBodyTests.cs`, mirroring `IfTests`:

1. **Canon gate** — generated module alpha-equivalent to `samples/IfMultiBody/ifmulti.js`.
2. **Snapshot** — byte-exact against `Snapshots/IfMultiBody.approved.js`.
3. **Contract** — emitted JS contains `document.createComment('')`, `() => (show.value) ? [0, 1] : []`,
   `(i) => i`, two `document.createElement('span')`; does not contain `innerHTML`, `[unsupported-if-body]`.
4. **Closed-runtime** — import names ⊆ the closed runtime export set; no new primitive; `document.createComment` is a DOM builtin.

`RepoPaths`: add `IfMultiBodyRazor => baseline/IfMultiBody.Blazor/App.razor` and
`IfMultiBodyAnswerKey => samples/IfMultiBody/ifmulti.js`. `Generate`: add
`IfMultiBodyToTemp() => ToTemp(RepoPaths.IfMultiBodyRazor, "IfMultiBody")`.

Regression / negative controls (must stay green **unchanged**):

- `IfTests` (plain single-node `@if`) — byte-identical emission, snapshot unchanged.
- `IfElseTests`, `RootIfTests`, `RootForeachTests` — byte-identical, snapshots unchanged.
- `DiagnosticTests` refusals for `IfElseMultiBody`, `IfNested`, `Foreach` — still refused, unchanged.
- Mutation check: neutralize the `allowMulti` relaxation → `IfMultiBody` refuses again; restore. Neutralize
  the `ops.Count != markup.Count` guard → a text-in-body/nested-body probe wrongly compiles; restore. Each
  guard proven load-bearing (#61).

Full suite (subset + analyzer + generator + runtime) stays green.

## Non-goals / disclosure

- **Single-branch `@if` only.** Multi-node `@else`/`@else if` bodies stay refused (slice 2, IfElseMultiBody);
  nested control flow in a branch stays refused (slice 3, IfNested).
- **Element nodes only.** Text nodes interleaved between body elements stay refused (`ops.Count != markup.Count`) — a deferred sub-slice; the fixture has no interleaving whitespace so none arises.
- **No `@foreach` change** (slice 4), **no C# subset widening**, **no runtime change**.
- The comment-anchor `+1` node remains the one disclosed divergence from Blazor (#81); next-sibling anchoring
  is still deferred.

## Decision record

Append **DECISIONS #98** (French, house style): the multi-node `@if` body enters the subset for the
single-branch case — the plain-`@if` lowering generalized from one-node-per-branch to one-list-item-per-body-node
(`() => cond ? [0,1] : []`, keyed by identity, `IfCreate` dispatch), reusing `list()` with **no runtime
change**; the single-node `@if` (#81) and multi-branch `@if/@else` (#82) emissions stay **byte-identical** (the
new path triggers only for `Body.Count > 1` on a branch-less `@if`); `IfElseMultiBody`/`IfNested` stay refused
(`allowMulti` gated on `ifs.Else is null`), witnesses intact; the `IfMultiBody` witness flips refused→compiles;
Blazor-validity verified up front (RZ9979 lesson); measured vs Blazor by canon gate against a
`BuildRenderTree`-derived answer key + byte snapshot + Playwright oracle asserting both spans mount/unmount
together in order (BENCH n°17, `HARNESS_VERSION` bump disclosed); text-in-body / `@else` multi-node / nested
control flow deferred.
