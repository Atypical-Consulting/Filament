# The non-goals honesty register — what the eleven closed features do at their edges

**Status:** Proposed · **Date:** 2026-07-21 · **Scope:** post-ADR-0003 hardening (decisions #129–#139, BENCH n°49–n°57)

## The finding

A ten-agent probe re-entered the eleven spec-§3 framework features that [ADR 0003](../../adr/0003-bucket-b-nongoals-closed.md)
records as closed, wrote its own witnesses, built each one with `dotnet build` against a copy of the matching
`baseline/*.Blazor` project, ran both sides in real Chromium, and then handed every claim to a second agent whose
only job was to refute it. What came back is that the promise spec §10 makes — **the compiler either maps a
construct faithfully or refuses it with a located diagnostic, and never emits silently-wrong JavaScript** — is
false in **sixteen measured places** inside those eleven features: a form that navigates away on submit, a
code-behind base that contributes nothing at exit 0, a `RenderFragment` inlined at every hole instead of one, a
fragment forwarded two levels and dropped, an `@ref` compiled into a free variable, a parameterised `@page` that
becomes a blank screen, a typed `HttpClient` that fetches a different URL than Blazor. Four more shapes crash the
tool outright — three of them `FIL-WIRING` from one root cause, and one a .NET stack overflow that aborts the
process with SIGABRT. Closing the eleven proved the compile-time model absorbs a framework's *surface*. Probing
them proves the surface was closed at its **centre** and left open at its **edges**. The next work is not more
surface. It is honesty at those edges.

## How to read this register

Every entry below was **run**. Each carries the witness the probe wrote, the verbatim diagnostic or the observed
wrong output, the Blazor evidence (a `dotnet build` that succeeded, and where the difference is a render, a live
browser reading of both sides), and the root cause in `src/` with the line the probe read it at. **Line numbers
are HEAD `057dd43`.**

Where the refutation pass rejected part of a claim, that is stated in the entry. `refuted: unfaithful-mapping`
almost never means "not a gap": in nearly every case the verifier confirmed the refusal reproduces verbatim AND
confirmed the Blazor source builds, and rejected only the *proposed mapping*. Those entries stay in the register
with the refutation attached, because a gap whose obvious fix is measurably wrong is more dangerous than one
whose fix is unknown. The two verdicts that are true dismissals — `already-admitted` and `invalid-blazor` — are
in [Dismissed](#dismissed--probed-and-not-a-defect), with their evidence, because knowing what is *not* a defect
is also a result.

## The register

| # | Defect | Class | Feature | Root cause (HEAD `057dd43`) |
|---|---|:--:|---|---|
| A1 | Plain `<form @onsubmit>` emits no `preventDefault` — the page navigates and reloads | A | forms #138 | `TemplateCompiler.cs:1639` (`_preventDefault` populated only in `EmitEditForm`) |
| A2 | `<EditForm Model>` with no `OnValidSubmit` emits no submit listener at all — same reload | A | forms #138 | `TemplateCompiler.cs:1592` `EmitEditForm` |
| A3 | Code-behind partial base (`Base.razor` + `Base.razor.cs`) contributes nothing, at exit 0 | A | `@inherits` #136 | `TemplateCompiler.cs:336` `File.Exists(basePath)` |
| A4 | Two-level `@inherits` chain — the second link is neither followed nor gated | A | `@inherits` #136 | `TemplateCompiler.cs:330–349` (no recursion) |
| A5 | One un-named fragment is inlined at **every** fragment hole | A | `RenderFragment` #131 | `TemplateCompiler.cs:1473` `Fragment? _fragment` |
| A6 | A named slot colliding with a sibling `.razor` emits the **decoy component** | A | `RenderFragment` #131 | `TemplateCompiler.cs:1808` `fragmentNodes`, resolved after sibling lookup |
| A7 | A fragment forwarded two levels is **silently dropped** | A | `RenderFragment` #131 | `TemplateCompiler.cs:1512` `_fragment = null` in `EmitFragment` |
| A8 | `@ref` on an element inside a region compiles to a free variable | A | `@ref` #132 | `TemplateCompiler.cs:1436` `RefTargetJs` |
| A9 | A parameterised/constrained `@page` is emitted verbatim into an equality table → **CLOSED #163** | A | routing #139 | `RouterEmitter.cs:73`, `RazorFrontEnd.cs:152` `RouteOf` |
| A10 | Second and later `@page` directives are silently dropped | A | routing #139 | `RazorFrontEnd.cs:152` `RouteOf` = `FirstOrDefault` |
| A11 | `EndsWith("HttpClient")` admits a valid typed client and rewrites its URL | A | DI #133/#147 | `TemplateCompiler.cs:381` |
| A12 | `<CascadingValue IsFixed="true">` is silently ignored | A | cascade #134 | `TemplateCompiler.cs:1532` `EmitCascadingValue` (reads `Value`/`Name` only) |
| A13 | A `bool` in text position renders `true` where C# renders `True` | A | composition #129 | `src/filament-runtime/src/dom.ts:37` `setText` + the generator's fold |
| A14 | A field written only through a method the template calls is never promoted to a signal | A | reactivity (#67) | `CSharpFrontEnd.cs:893` `MarkTemplateReads` |
| A15 | A value crossing a `@typeparam` bypasses the type-directed formatter | A | generics #135 | `TemplateCompiler.cs:1797` `BindParameters` (no formatter carried) |
| A16 | `<InputText>` emits none of Blazor's `valid`/`modified`/`aria-invalid` state | A | forms #138 | `TemplateCompiler.cs:1658` `EmitInputText` |
| B1 | A region as a **direct child** of component child content → `FIL-WIRING`, no location | B | forms/cascade/#131 | `TemplateCompiler.cs:1210` reached from `EmitEditForm`/`EmitCascadingValue` |
| B2 | A region at the **root of a fragment** → the same `FIL-WIRING` | B | `RenderFragment` #131 | `TemplateCompiler.cs:1487` `EmitFragment` walks nodes one by one |
| B3 | `@ChildContent` **inside** a region in the child → `FIL-WIRING`, no location | B | `RenderFragment` #131 | `TemplateCompiler.cs:1498` (fragment with no container) |
| B4 | Recursive (and mutually recursive) component use → stack overflow, exit 134 | B | composition #129 | `TemplateCompiler.cs:1725` `EmitComposition` re-enters unbounded |
| C1 | With 2+ `@inject`, the caret always points at the **first** one, in reverse order | C | DI #133 | `TemplateCompiler.cs:364` `FirstOrDefault(d => d.Name == "inject")` |
| C2 | Any other Forms component with `@bind-Value` leaks two spanless `FIL0001`s | C | forms #138 | `TemplateCompiler.cs:624` and `:928` (hardcoded two tag names) |
| C3 | A base's own `@inject`/`@using` are dropped, so the base's own code is refused | C | `@inherits` #136 | `TemplateCompiler.cs:349` (only `@code` nodes are merged) |
| C4 | `@inherits ComponentBase` is refused while the qualified spelling is admitted | C | `@inherits` #136 | `TemplateCompiler.cs:330` |
| C5 | Every route-parameter refusal is reported as `[unresolved-name]` | C | routing #139 | `CSharpFrontEnd.cs` name binding; no page-parameter channel exists |
| C6 | Diagnostics from a merged base blame the base for names the merge dropped | C | `@inherits` #136 | `TemplateCompiler.cs:349` |
| C7 | `"no injected services to reach for"` is wrong when an `@inject` **is** present | C | DI #133 | `CSharpFrontEnd.cs` unresolved-name message |
| C8 | The author-time analyzer reports **nothing** on `.razor` `@code`, for any type | C | analyzer | `TypeSubsetAnalyzer.cs:34` `GeneratedCodeAnalysisFlags.Analyze` without `ReportDiagnostics` |
| D1 | `<InputCheckbox @bind-Value>` (bool) | D | forms #138 | `TemplateCompiler.cs:1253` catch-all component arm |
| D2 | `@inherits` over a base declared in a sibling `.cs` | D | `@inherits` #136 | `TemplateCompiler.cs:333` sibling-`.razor`-only path |
| D3 | `@foreach` over a `[Parameter]` collection inside the child | D | generics/composition | `CSharpFrontEnd.cs:1113` `ForEach()` field-only gate |
| D4 | A component parameter bound to the `@foreach` loop variable | D | composition #129 | `TemplateCompiler.cs:1774` `bound-parameter` |
| D5 | A static non-string component parameter (`<Leaf V="1" />`) | D | composition #129 | `CSharpFrontEnd.cs:277` `FirstBoundNonStringParameter` |
| D6 | `RenderFragment<T>` as a `[Parameter]` | D | templated components | `TypeSubset.cs:126` `IsRenderFragment` (`TypeArguments.Length: 0`) |
| D7 | `<InputNumber @bind-Value>` (int) — **naive mapping refuted** | D | forms #138 | `TemplateCompiler.cs:1253` |
| D8 | `@inject NavigationManager` — **naive mapping refuted** | D | routing #139 | `TemplateCompiler.cs:386` |
| D9 | Named fragment elements / multiple named fragments — **naive mapping refuted** | D | `RenderFragment` #131 | `TemplateCompiler.cs:1808` |
| D10 | Explicit type argument `<Box TItem="int">` — **naive mapping refuted** | D | generics #135 | `TemplateCompiler.cs:1838` unknown-parameter check |
| D11 | Namespace-qualified / subfolder `@inherits` base — **naive mapping refuted** | D | `@inherits` #136 | `TemplateCompiler.cs:333` |
| D12 | Route parameters as a whole — **every proposed piece refuted** → **CLOSED #163** | D | routing #139 | `RouterEmitter.cs:44`, `mount(target)` has no parameter channel |

---

## Class A — silent divergence

*Compiles, runs, and renders or behaves differently from Blazor. This is exactly what §10 forbids.*

### A1 — a plain `<form @onsubmit="Save">` navigates away

**Witnesses.** `D1PlainForm.razor`, `H10PlainFormLambda.razor`, `X1SubmitPlusKeydown.razor`.

**Observed.** The generator admits the form at exit 0 (`D1PlainForm.razor -> D1PlainForm.g.js (1617 B)`, zero
diagnostics) and emits a bare listener:

```
listen(_el0, 'submit', () => {
    saved.value = name.value;
  });
```

The same compiler on `baseline/Forms.Blazor/App.razor` (which uses `<EditForm OnValidSubmit>`) emits
`listen(_el0, 'submit', (e) => { e.preventDefault(); … })`. Live A/B on identical source: the Blazor dev server
returned `{"out":"handled","url":"http://127.0.0.1:8941/","stillMounted":true}` — no navigation. The Filament
module on `127.0.0.1:8913` reported `Page navigated to http://127.0.0.1:8913/index.html?`, and the
post-navigation probe read `{"loads":2,"sessionLoads":"2"}` with `#out` back to `""` and the input cleared: a real
document reload.

**Why Blazor differs.** Blazor's shipped `blazor.webassembly.958z1vx7fr.js` contains `_={submit:!0}` and
`Object.prototype.hasOwnProperty.call(_,t.type)&&t.preventDefault()` inside the delegated dispatcher, reached
whenever `d.getHandler(e)` is non-null. Any submit event with a registered handler is preventDefaulted,
unconditionally.

**Blazor validity.** `baseline/Forms.Blazor` copied to a scratch project with the plain-form `App.razor`:
`Build succeeded. 0 Warning(s) 0 Error(s)` (net10.0 BlazorWebAssembly, Components.WebAssembly 10.0.9).

**Root cause.** `_preventDefault` (`TemplateCompiler.cs:242`) is populated only at `TemplateCompiler.cs:1639`,
inside `EmitEditForm`. A plain `<form>` never reaches that line.

**Fix.** Add the element to `_preventDefault` for a `submit` handler on any `<form>`, not only an `<EditForm>`.
**Constraint the verifier found:** `_preventDefault` is a `HashSet<string>` of *element* names read per handler at
`TemplateCompiler.cs:458`, and `X1SubmitPlusKeydown.razor` (`<form @onsubmit="Save" @onkeydown="OnKey">`) compiles
today with both listeners on `_el0`; in `HandlerArrow` the preventDefault branch is tested *before* the
takes-event branch. An element-keyed fix would preventDefault the keydown (Blazor's list is submit-only) and call
that handler with no event argument. **The key must be `(element, event)`.**

### A2 — `<EditForm Model>` with no `OnValidSubmit` also navigates away

**Witness.** `F6NoCallback.razor` (and its twin `F6WithCallback`).

**Observed.** `F6NoCallback.razor -> out/F6NoCallback.js (1533 B)`, exit 0. The module contains
`const _el0 = document.createElement('form');` and its **only** listener is the input's `change`; there is no
`listen(_el0, 'submit'…)` and no `preventDefault`. The three-arm browser oracle (document-level bubble listener
registered after the framework's):

```
## BLAZOR EditForm Model, NO OnValidSubmit / submit seen: {"defaultPrevented":true,"target":"FORM"} / marker survived: alive / navigations: []
## BLAZOR EditForm Model + OnValidSubmit   / submit seen: {"defaultPrevented":true,…}              / marker survived: alive / navigations: []
## FILAMENT emitted F6NoCallback.js        / submit seen: undefined / marker survived: null / navigations: ["http://127.0.0.1:5233/index.html?"]
```

Filament navigated and lost the app. Blazor did not, with or without a callback — `EditForm` always registers
`onsubmit`, so `preventDefault` is unconditional.

**Blazor validity.** Scratch copy of `baseline/Forms.Blazor` with the callback-less `EditForm`:
`Build succeeded. 0 Warning(s) 0 Error(s)`.

**Root cause.** `EmitEditForm` (`TemplateCompiler.cs:1592`) records a handler only when `OnValidSubmit` is present.
Today's divergent behaviour is **pinned as intended** by `tests/Filament.Generator.Tests/GateSubsetTests.cs:437`
(`Assert.DoesNotContain("listen(", js)`).

**Fix.** Emit `listen(form,'submit',(e)=>{e.preventDefault();})` even with no callback, and flip that assertion.
Note `_preventDefault` alone emits nothing — it is only read while rendering `_handlers` entries — so a real
handler entry must be added.

### A3 — a code-behind partial base contributes nothing, silently

**Witness.** `CounterBase.razor` (`@code { protected int count = 0; }`) + `CounterBase.razor.cs`
(`public partial class CounterBase : ComponentBase { protected override void OnInitialized() { count = 7; } }`) +
`App.razor` (`@inherits CounterBase`, `<span id="out">@count</span>`).

**Observed.** `App.razor -> app.js (861 B)`, **exit 0, zero diagnostics**. Emitted body: `const count = 0;` and
`insert(_el1, document.createTextNode(count));` — no `signal`, no `// -- init:` section, no `onInitialized` call.
Executed under node against a stub DOM: `FILAMENT-RENDERED>>><div id="wrap"><span id="out">0</span></div><<<`.
Blazor's answer, taken from the real component through `HtmlRenderer` (net10.0):
`BLAZOR-RENDERED-HTML>>><div id="wrap"><span id="out">7</span></div><<<`. **0 vs 7, exit 0, no diagnostic.**

**Blazor validity.** `dotnet build -v q -nologo` → `Build succeeded. 0 Warning(s) 0 Error(s)`.

**Control.** With `OnInitialized` moved into the base's `@code`, the same semantics compile to
`const count = signal(0); function onInitialized() { count.value = 7; } … onInitialized();` — the machinery a
spliced `.cs` member list would land on already exists.

**Boundary the probe sharpened.** A code-behind member the *template names* is caught: adding
`protected string label = "hello";` to the `.cs` and `@label` to the template gives
`error App.razor(3,60): FIL0001: [unresolved-name] 'label' is not declared in this component. …` The silent leak
is specifically **unnamed behaviour** — lifecycle overrides and methods the template never mentions.

**Root cause.** DECISIONS #136 states the refusal was intended ("Une base ecrite dans un .cs lui est invisible …
Ce cas est donc refuse"), but the only gate is `TemplateCompiler.cs:336` `if (!File.Exists(basePath))`. A
code-behind partial satisfies that check while contributing nothing, so the intended refusal never fires. No
disclosure of `.razor.cs` exists anywhere in the repo (`grep -rn -E "razor\.cs|code-behind|partial class"` over
`*.md`/`*.cs`/`*.razor` finds only `.razor.css` in `docs/REAL-APPS.md`).

**Fix.** After the sibling `.razor` resolves, test for a sibling `<Base>.razor.cs` and refuse with a located
diagnostic naming it — or merge it, which is the same insertion point A3's control already proves works.

### A4 — a two-level `@inherits` chain drops the grandparent

**Witnesses.** `App.razor : BBase.razor : CBase.razor`, in both a grandparent-hook shape and a
name-crosses-levels shape.

**Observed.** `App.razor -> out/p19.js (1335 B)`, exit 0, no diagnostic — and
`diff out/p19.js ctl/ctl.js` against a control with `BBase`'s `@inherits CBase` line **deleted** is
**byte-identical**. The emitted module contains no `document.body.classList.add`. Blazor, run live
(`dotnet run --urls http://127.0.0.1:5178`, Chrome): `{"bodyClass":"ready","hasReady":true}` — Blazor **does**
invoke the grandparent's `OnInitialized` through two levels.

**Control proving the link is the cause.** The same payload one link closer (`App @inherits CBase`) **refuses
loudly**: `error CBase.razor(6,9): FIL0001: [unsupported-call] 'JS.InvokeVoidAsync("document.body.classList.add", "ready")' is not a call to a method declared in this component.`
The extra level turns a located refusal into silence.

**Blazor validity.** `Build succeeded. 0 Warning(s) 0 Error(s)` for both shapes.

**Root cause.** `TemplateCompiler.cs:330–349` resolves and merges exactly one base; nothing recurses on the base's
own `@inherits`, and no gate notices it.

**Fix.** Make the base merge recursive (base-first). **Correction to the original claim:** no `File.Exists`
refusal can fire here — both `BBase.razor` and `CBase.razor` exist. What recursion actually does is carry the
grandparent's `@code` into the compilation, where the pure-`@code` case compiles and the interop case trips the
existing `[unsupported-call]` gate. The merge model already gates every collision recursion would multiply
(`[unsupported-modifier] 'virtual'`, `'override'`, `'new'`, `[name-collision] … both map to the JS binding
'onInitialized'`).

### A5 — one un-named fragment is inlined at every hole

**Witness.** `Card.razor` = `<div id="card"><h3 id="title">@Title</h3><div id="head">@Header</div><div id="body">@ChildContent</div></div>`
with `[Parameter]` `Title`/`Header`/`ChildContent`; `App.razor` passes only bare content.

**Observed.** `App.g.js (1924 B)`, exit 0, no diagnostic. The parent's fragment is duplicated at **both** holes —
two elements carry `id='mark'`, both live-bound:

```
const _el3 = document.createElement('div'); _el3.id = 'head';
const _el4 = document.createElement('span'); _el4.id = 'mark';
…
const _el5 = document.createElement('div'); _el5.id = 'body';
const _el6 = document.createElement('span'); _el6.id = 'mark';
…
effect(() => setText(_tx0, count.value));
effect(() => setText(_tx1, count.value));
```

**Blazor's ground truth, taken two ways.** Codegen: `App_razor.g.cs:39`
`__builder.AddAttribute(4, "ChildContent", (RenderFragment)((__builder2) => { … span id=mark … }));` and **no**
`AddAttribute` for `"Header"`; `Card_razor.g.cs:44` `__builder.AddContent(7, Header)` with `Header` never
assigned (a null `RenderFragment` renders nothing). Runtime, real WASM app read through chrome-devtools:
`{"marks":1,"headHTML":"<div id=\"head\"></div>","bodyHTML":"<div id=\"body\"><span id=\"mark\">0</span></div>"}`.
Blazor: **one** `#mark`, `#head` **empty**. Filament: two `#mark`, `#head` filled.

**Blazor validity.** `dotnet build -v q` → `Build succeeded. 0 Warning(s) 0 Error(s)`.

**Root cause.** `TemplateCompiler.cs:1473` `Fragment? _fragment;` is single and un-named; `EmitFragment`
(`:1487`) inlines it at every slot reached via `TemplateCompiler.cs:1186`
(`case CSharpExpressionIntermediateNode frag when _code.SlotIsFragment(frag)`).
`CSharpFrontEnd.cs:555 FragmentParameterNames` already exposes the parameter names to key on.

**Fix.** Key fragments by parameter name (`Dictionary<string,Fragment>`), bare content keying `"ChildContent"`,
and emit nothing for an absent key. **Constraint:** the existing refusal at `TemplateCompiler.cs:1844` fires only
when the child declares *no* fragment parameter at all; a probe where `Card` declares only `Header` and the parent
passes bare content **builds in Blazor** (Razor still emits `AddAttribute(4, "ChildContent", …)` for a property
that does not exist), so a naive dictionary would silently drop it. The fix must also refuse when the bare
content's `"ChildContent"` key matches no declared parameter.

### A6 — a named slot colliding with a sibling `.razor` emits the decoy component

**Witness.** `App.razor` = `<Card Title="hits"><Body><span id="body">@count</span></Body></Card>`;
`Card.razor` declares `[Parameter] public RenderFragment? Body` rendered at `@Body`; a sibling `Body.razor`
exists and renders `<p id="decoy">DECOY @ChildContent</p>`.

**Observed.** `f1clash2/App.razor -> f1clash2/App.g.js (1687 B)`, exit 0, **no diagnostic**, emitting the decoy:

```
const _el3 = document.createElement('p');
_el3.id = 'decoy';
insert(_el3, document.createTextNode('DECOY '));
```

Filament DOM: `<div id="card"><h3 id="title">hits</h3><p id="decoy">DECOY <span id="body">0</span></p></div>`.
Blazor DOM: `<div id="card"><h3 id="title">hits</h3><span id="body">0</span></div>`.

**Why Blazor differs.** `App_razor.g.cs` opens exactly one component —
`__builder.OpenComponent<global::Fragment.Blazor.Card>(2);` then
`__builder.AddAttribute(4, "Body", (RenderFragment)…)`. `OpenComponent<…Body>` appears **nowhere**.

**The sharpest evidence.** A counterfactual whose only change is `Card` declaring `ChildContent` instead of `Body`
flips the meaning completely in Blazor (`__builder2.OpenComponent<global::Fragment.Blazor.Body>(5);` — the decoy
*is* instantiated). Filament's output for the two sources is **byte-identical**: `diff` silent, both
`02e628107ee836f80462ea602d5d2ad5c722315c`, 1687 B. The emission is invariant to the declaration that decides the
meaning: accidentally right in one case, silently wrong in the other.

**Depth boundary.** `<Card><Body><Body>…</Body></Body></Card>` builds clean in Blazor and the name match wins only
for **immediate** children (one level down `<Body>` is the component again). Filament emits **two** decoys where
Blazor renders one — the mis-compile scales.

**Blazor validity.** `dotnet build … --no-incremental` → `Build succeeded. 0 Warning(s) 0 Error(s)`, two
independent project dirs, no warning.

**Root cause.** Sibling-component resolution runs before any slot-name check;
`TemplateCompiler.cs:1808` computes `fragmentNodes = el.Children.Where(c => c is not HtmlAttributeIntermediateNode)`
as a flat list with no name partition. `FragmentTests.cs` has six tests, none covering named slots or a clash.

**Fix.** Test an immediate child element's tag name against the resolved child's `FragmentParameterNames`
**before** sibling-file resolution, scoped exactly to `fragmentNodes`. Both arms were verified against Blazor.

### A7 — a fragment forwarded two levels is silently dropped

**Witness.** `App.razor` = `<Middle><span id="body">@count</span></Middle>`; `Middle.razor` = `<Inner>@ChildContent</Inner>`
(and the wrapped variant `<Inner><div id="hold">@ChildContent</div></Inner>`); `Inner.razor` = `<div id="inner">@ChildContent</div>`.

**Observed.** Exit 0; stderr holds only `App.razor -> app.g.js (1115 B)` — 329 bytes, no `FIL0001/0002/0003`.
The emitted `mount()` builds `#wrap`, `#inner` and the button and **nothing else**;
`grep -nE "body|span|setText|effect"` returns one hit and it is a comment. The wrapped variant creates `#hold`
and leaves it empty. **The drop is total.**

**Blazor.** `Build succeeded. 0 Warning(s) 0 Error(s)`, and codegen shows the render:
`App_razor.g.cs` `AddAttribute(3,"ChildContent", …span id=body…)`; `Middle_razor.g.cs`
`AddAttribute(1,"ChildContent", (RenderFragment)((__builder2)=>{ __builder2.AddContent(2, ChildContent); }))`;
`Inner_razor.g.cs` `AddContent(2, ChildContent)`. Blazor DOM:
`<div id="wrap"><div id="inner"><span id="body">0</span></div><button id="inc">inc</button></div>`.

**Root cause.** `EmitFragment` sets `_fragment = null` (`TemplateCompiler.cs:1512`) before walking the fragment's
nodes, so `Middle`'s fragment — whose only node **is** `@ChildContent` — has nothing to resolve against.

**Fix, built and run by the verifier.** The one-line fix in the original claim (`_fragment = savedFragment;`)
**stack-overflows** (`savedFragment` *is* `frag`, so the fragment re-inlines itself; measured on both witnesses,
and byte-identical to before on the depth-1 baseline, i.e. it implements nothing). The corrected mapping: add an
`Outer` field to the `Fragment` record, populate it at the `EmitComposition` construction site with the fragment
in scope where the nodes were **written**, and set `_fragment = frag.Outer` in `EmitFragment`. Results, run:
`#body` correctly nested inside `#inner` (and inside `#hold` for the wrapped variant), with
`effect(() => setText(_tx0, count.value))` live on the **grandparent's** signal, and the depth-1
`Fragment.Blazor` baseline **byte-identical** — no regression. ~3 lines.

### A8 — `@ref` inside a region compiles to a free variable

**Witnesses.** `R2C.razor` (`@ref` on an `<li>` in a `@foreach` row), `R7.razor` (`@ref` in an `@if` body), each
with `async Task Focus() { await row.FocusAsync(); }` at component scope.

**Observed.** `R2C.razor -> r2c.js (1760 B)`, exit 0, no diagnostic. The ref name is emitted **inside** the
region's local function while the handler references it at mount scope:

```
function createN(n) { const row = document.createElement('li'); … return row; }
…
listen(_el2, 'click', async () => { await row.focus(); });
```

Executed in node + happy-dom with `listen` wrapped to surface the swallowed rejection:
`HANDLER REJECTED: ReferenceError: row is not defined` (and `box is not defined`), `activeElement.id = ""` both
times. Reproduced in **real Chrome** on three pages:
`p3 {"active":"","err":"ReferenceError: row is not defined"}`, `p2 {"active":"","err":"ReferenceError: box is not defined"}`.
`p1` (field `box`, element `id="box"`) passes **only by the HTML named-window-access coincidence**
(`hasWindowBox:"object"`) — a witness for this slice must not name the element after the field.

**Control.** The unmodified `baseline/ElemRef.Blazor/App.razor` through the same generator and harness:
`activeElement.id = "box"`. The only difference is region scope.

**Blazor validity.** `dotnet build -v q --nologo` → `Build succeeded. 0 Warning(s) 0 Error(s)` for both shapes.

**Root cause.** `TemplateCompiler.cs:1436` `RefTargetJs` checks only `_code.IsElementRefField(name)`; nothing
consults the emission scope. `tests/Filament.Generator.Tests/ElemRefTests.cs` has no region case.

**Fix.** A located `FIL0001` when an `@ref` target is emitted under a region — trivially faithful, since a refusal
emits nothing. **Caveat on the hoist alternative:** `let row;` assigned inside `createN` gives
last-created-wins, and `list()` never re-runs `create` for a persisting key, so after a reorder it can diverge
from Blazor's capture. That variant needs a Blazor browser oracle before it ships.

### A9 — a parameterised or constrained `@page` becomes a blank screen

> **CLOSED — decision 163, BENCH n°69 (2026-07-22).** Not by the "honest floor" proposed below, but by
> the larger slice D12 describes. A route template is now PARSED and gated: `{Id}`, `:int`, `:long` and
> `:bool` compile through a real segment matcher, and every shape that cannot be converted exactly —
> including `Hmalformed`'s `/h/{Id`, `Dcatch`'s `/{*slug}`, `Fopt`'s `/f/{Id:int?}`, `Gbogus`'s
> `:notaconstraint` and `:guid` — is refused with a **located FIL0003**, which is what this entry asked
> for. `Cdecl`'s "declared and simply unread" case compiles and renders. The correction recorded below —
> that a route parameter with no matching `[Parameter]` is runtime-invalid Blazor — became its own
> refusal rather than being imitated.

**Witnesses.** `Cdecl.razor` (`@page "/c/{Id:int}"` + `[Parameter] public int Id`, unused in markup),
`Dcatch` (`/{*slug}`), `Eplain` (`/e/{Id}`), `Fopt` (`/f/{Id:int?}`), `Gbogus` (`/g/{Id:notaconstraint}`),
`Hmalformed` (`/h/{Id`).

**Observed.** Every one is admitted at exit 0 with no diagnostic and its route literal copied verbatim —
including the unbalanced brace:

```
Cdecl.razor -> out_Cdecl/Cdecl.g.js (760 B)  route /c/{Id:int}        EXIT=0
Hmalformed.razor -> (765 B)  route /h/{Id      (unbalanced brace!)     EXIT=0
```

The emitted table is matched by string equality
(`routes.find(([r]) => r === path) ?? routes.find(([r]) => r === '*')`, `RouterEmitter.cs:73`). Driving the
router's **own bytes** under node:

```
{"/":"mountHome","/item/7":"(NOTHING MOUNTED -> blank page)","/item/{id:int}":"mountItem","/nope":"(NOTHING MOUNTED -> blank page)"}
```

Blazor on the corrected witness renders fine: `/c/5` → `{"path":"/c/5","where":"c"}`. A working Blazor page
becomes a blank screen with no diagnostic.

**Correction to the original witness (kept, because it matters).** A route parameter with **no** matching
`[Parameter]` is compile-valid but **runtime-invalid** Blazor: `/e/7` returned
`{"errVisible":true,"html":"<!--!--><!--!--><!--!--><!--!-->"}` with `blazor-error-ui` shown. The claim survives
only on `Cdecl`, where the parameter is declared and simply unread.

**Blazor validity.** `dotnet build` → `Build succeeded. 0 Warning(s) 0 Error(s)` for all six route shapes; only
`@page "*"` fails, with `RZ9988` (see [Dismissed](#dismissed--probed-and-not-a-defect)).

**Root cause.** `RazorFrontEnd.RouteOf` (`RazorFrontEnd.cs:152`) reads the directive token and hands it through
unexamined; `RouterEmitter.Emit` (`RouterEmitter.cs:44`) writes it into an equality table. No gate inspects the
route's **shape**.

**Fix (the honest floor).** Refuse a route template containing `{` or `*` with a located `FIL0003` —
`DirectiveSpyPass` captures `DirectiveSite.Source` for `page`, so the span is available. A real matcher is a
separate, larger slice (D12) and is unfaithful if naive.

### A10 — second and later `@page` directives are silently dropped

**Witness.** `Two.razor` carrying both `@page "/two"` and `@page "/deux"`.

**Observed.** `router -> app.js (1893 B, 2 pages)`, exit 0, nothing on stderr; the table is
`const routes = [ ['/', mountHome], ['/two', mountTwo], ];` — `'/deux'` absent. Swapping the directive order drops
`'/two'` instead: silent both ways. Running the emitted app: `/deux` gives `app.innerHTML = ""`, a blank page with
no error.

**Blazor.** `Build succeeded.`; `Two_razor.g.cs` carries **two** `RouteAttribute`s; served and driven headless,
`/two` and `/deux` both render `<h2 id="where">two</h2>`, and the control `/nope` renders `not found`.

**Root cause.** `RazorFrontEnd.RouteOf` is `Directives.FirstOrDefault(d => d.Name == "page")`.

**Fix, with the two corrections the verifier measured.** `RoutesOf` (a `Where`) plus one table row per route is
**not sufficient**: (1) taken literally it emits the page import twice —
`SyntaxError: Identifier 'mountTwo' has already been declared`, so the module would not parse; imports must be
de-duplicated. (2) Blazor keeps the **same component instance** across two routes of one component
(`RouteView` renders the same type): measured `Blazor: /two n=2 → click → /deux n=2` against
`Filament: /two n=2 → click → /deux n=0`, because `render()` does `target.textContent = ''` and remounts. A
faithful version needs `render()` to skip teardown when the matched mount is already mounted.
Side check: Blazor with `@page "/two"` twice builds clean but dies at run time
(`System.InvalidOperationException: The following routes are ambiguous:`), so a build-time refusal there is
defensible.

### A11 — the `EndsWith("HttpClient")` gate admits a valid typed client and rewrites its URL

**Witness.** `WrapperHttpClient` — a typed client whose constructor takes `HttpClient`, name ending in
`HttpClient`, registered with `AddHttpClient<WrapperHttpClient>(c => c.BaseAddress = new Uri("https://example.com/api/"))`,
injected as `@inject WrapperHttpClient Api` and used as `await Api.GetStringAsync("weather")`.

**Observed.** `B_WrapperHttpClient.razor -> B_WrapperHttpClient.g.js (1631 B)`, exit 0, no diagnostic, emitting
`val.value = await __getText('weather');` — a **document-relative** fetch. Blazor, against a real `HttpListener`:
`SERVER SAW: http://127.0.0.1:8791/api/weather`. Blazor requests `/api/weather`; Filament requests
`<document-base>/weather`. Silent.

**Blazor validity.** `Build succeeded.` in a `baseline/HttpJson.Blazor` copy, and the request line was **observed**
on the wire, not inferred.

**Boundary control.** `@inject WeatherClient Api` (no suffix) refuses correctly with `FIL0003
[unsupported-directive]` + `FIL0001 [unsupported-call]`, exit 1.

**Root cause.** `TemplateCompiler.cs:376` / `:381` — `typeName.EndsWith("IJSRuntime")` and
`typeName.EndsWith("HttpClient")`, a pure lexical suffix test, even though
`Filament.Subset.TypeSubset.IsJsRuntime` (`TypeSubset.cs:143`) exists and is not consulted.

**Fix.** Exact-name matching (`HttpClient` / `System.Net.Http.HttpClient`, `IJSRuntime` /
`Microsoft.JSInterop.IJSRuntime`). **The claim's proposed fix is not implementable:** `inject.TypeName`
(`TemplateCompiler.cs:365`) is a raw directive string and `code.Compile(…)` (`:398`) builds the semantic model
*after* the gate; the author's type lives in a `.cs` file this compiler never reads, so a resolved-symbol
comparison could never bind.

### A12 — `<CascadingValue IsFixed="true">` is silently ignored

**Witness.** Two consumers under two identical `<CascadingValue Value="@level" IsFixed="true">` cascades,
differing only in whether the consumer's own `[Parameter]` changes: `<GaugeA Tag="static"/>` (`#a`) and
`<GaugeB Tag="@tick"/>` (`#b`), both rendering only `@Level`.

**Observed.** Exit 0, no diagnostic, emitting the same live cascade a non-fixed one emits
(`effect(() => setText(_tx0, level.value));`).

```
BLAZOR   : {"initial":{"a":"1","b":"1"},"afterClick1":{"a":"1","b":"2"},"afterClick2":{"a":"1","b":"3"}}
FILAMENT : {"initial":{"a":"1","b":"1"},"afterClick1":{"a":"2","b":"2"},"afterClick2":{"a":"3","b":"3"}}
```

Filament is **wrong on `#a`** (2, 3 where Blazor renders 1, 1) and right on `#b`.

**What `IsFixed` actually means, measured.** Not "read once": Blazor still re-supplies the live supplier value
through `WithCascadingParameters` whenever the consumer is re-parameterised for any other reason. `#b` therefore
updates 1→2→3 **under a fixed cascade**.

**Blazor validity.** `dotnet build` of a copied `Cascade.Blazor` with `IsFixed="true"` →
`Build succeeded. 0 Warning(s) 0 Error(s)`.

**Root cause.** `EmitCascadingValue` (`TemplateCompiler.cs:1532`) reads only `Value` and `Name`;
`grep -rn IsFixed` over the repo returns **0 hits**.

**Fix.** **Refuse** `IsFixed` with a located diagnostic. The proposed frozen capture (`const _casc0 = level.value;`)
was refuted by measurement: it fixes `#a` and **breaks** `#b`, rendering `1` where Blazor renders `3`. Filament's
inlined model has no counterpart for "re-supplied on re-parameterisation", so refusal is the honest answer until
one exists.

### A13 — a `bool` in text position renders `true` where C# renders `True`

**Witness.** `<BLeaf On="@flag" />` where `BLeaf.razor` renders `On: @On` — the **admitted reactive** composition
path.

**Observed.** Compiles clean (1382 B) to `effect(() => setText(_tx0, flag.value));`. Blazor, rendered through
`HtmlRenderer` under `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` (the baseline sets `InvariantGlobalization=true`):
`<span id="bleaf">On: True</span>`; `csharp true.ToString() = [True]`. JS: `setText` is `node.data = v`
(`src/filament-runtime/src/dom.ts:37`) and `String(true)` is `true`.

**Blazor validity.** `dotnet build` → `Build succeeded. 0 Warning(s) 0 Error(s)`. The int and double arms are
faithful (`String(1)` = `1`, `String(1.5)` = `1.5`, matching Blazor's `1` / `1.5`); only `bool` diverges.

**Root cause.** No `bool` formatter exists in text position; `setText` receives the raw JS value.

**Fix.** Emit a `True`/`False` formatter for a `bool` in text position, in the same place decision 113's `__f32`
and decision 115's `__dtStr` are emitted. Until then, the `bool` arm of D5 must stay refused.

### A14 — a field written only through a method the template calls is never promoted to a signal

**Witness.** `C_methodread/App.razor` — `int count`, a `Format()` method the template calls (`@Format()`), and a
click handler that does `count++`. `E_asyncmethodread/App.razor` is the async twin (the write happens in an
`await` continuation).

**Observed.** Admitted, 1154 B, and the emitted JS is **not reactive**: `let count = 0;` (a plain `let`, not
`signal()`), `insert(_el0, document.createTextNode(format()));` (one-shot, no `effect()`),
`listen(_el1, 'click', () => { count++; });`. Driven in real browsers with the repo's own Playwright 1.61.1 from
`bench/harness/node_modules`:

```
FILAMENT: before="n=0" afterClick="n=0" errors=[]
BLAZOR  : before="n=0" afterClick="n=1" errors=[]
```

**Control that isolates it.** `D_asyncnowrite`, where the template reads the field **directly**, correctly emits
`const msg = signal('idle')` + `effect(() => setText(_tx0, msg.value))`. Signal promotion needs a **template**
read; a read inside a method called from the template does not promote.

**Evidence edge, stated honestly.** The measured pair was Blazor-with-`StateHasChanged` against
Filament-with-it-erased (the witness was built to test that erasure). A strict same-source A/B — the same file
without `StateHasChanged()` on both sides — was **not run**. Blazor's `ComponentBase` re-renders after every
handler regardless, so the expected result is unchanged, but this slice must run that A/B before it ships.

**Root cause.** `MarkTemplateReads` (`CSharpFrontEnd.cs:893`) marks fields the template names; the call graph
(phase 3, `:928`) is built afterwards and is not fed back into promotion.

**Fix.** Propagate template reads through the call graph: a field read by a method the template calls is a field
the template reads. This is the same transitivity decision 160 already applies to `computed()` dependencies.

### A15 — a value crossing a `@typeparam` bypasses the type-directed formatter

**Witness.** `private float f = 0.1f;` + `<Box Value="@f" />`, where `Box.razor` has `@typeparam TItem` and
renders `@Value` (T inferred = `float`).

**Observed.** Exit 0, emitting `const f = signal(Math.fround(0.1)); effect(() => setText(_tx0, f.value));`.
`C# float.ToString()` → `0.1`; `node String(Math.fround(0.1))` → `0.10000000149011612`. Decision 113's `__f32`
shortest-round-trip formatter is bypassed whenever the value flows through a `@typeparam`.

**Root cause.** `BindParameters` (`TemplateCompiler.cs:1797` / `CSharpFrontEnd.cs:234`) carries the parent's JS
expression into `_paramEnv`; the child renders it with no formatter, because the child's declared type is the
type parameter.

**Fix.** Carry the *inferred* type alongside `SlotJs` and select the formatter at the child's render site — the
same table decisions 112–115 already use. The same shape applies to `decimal` (`[object Object]`) and `DateTime`
(raw ticks), both of which the templated-fragment verifier measured on the adjacent path.

### A16 — `<InputText>` emits none of Blazor's field-state classes

**Witness.** `baseline/Forms.Blazor/App.razor` itself — the **shipped** forms slice.

**Observed.** Blazor renders `<input id="name" class="valid" _bl_2="">`, and after a change
`class="modified valid"`. The shipped Filament output for that same file is a bare
`const _el1 = document.createElement('input'); _el1.id = 'name';` — **no class at all**, ever. Filament's
`forms` DOM contract (harness 1.50.0) asserts `#live`/`#out` only, so this has never been measured.

**Root cause.** `EmitInputText` (`TemplateCompiler.cs:1658`) emits the value effect and the change listener and
nothing else; there is no `EditContext` field state to derive a class from.

**Fix.** Either emit the `valid`/`modified valid` class transitions from the bind's own touched/parse state — a
per-bound-field flag the generator already has enough information to track — or **disclose** the divergence in
BENCH and in the forms contract. What must not continue is that the contract asserts around it.

---

## Class B — crash

*The tool aborts. Loud, so not a lie — but a broken tool, and in one case a broken tool with no location at all.*

### B1 — a region as a direct child of component child content

**Witnesses.** `J2IfInForm.razor`, `J3ForeachInForm.razor` (under `<EditForm>`), `J6IfDirectCascading.razor`
(under `<CascadingValue>`), `K3IfInPanel.razor` (under a plain user component with
`[Parameter] RenderFragment ChildContent`), `E1` (EditForm + direct-child `@if`), `C9`/`C10`.

**Observed, verbatim, exit 1, no `.js` written, no `file(line,col)`:**

```
error FIL-WIRING: raw template C# (if (show) {) reached the emitter. The collect walk turns every CSharpCodeIntermediateNode into a region (decision 54's reassembly), so this one was never planned and nothing here can be trusted. This is the TOOL being broken, not the input.
```

Same message with `(foreach (string e in errors) {)` for the loop form.

**Broader than the original claim.** The probe wrote its own `Panel.razor` (`<div class="panel">@ChildContent</div>`)
and reproduced the identical crash. This is **not** an `EditForm`/`CascadingValue` quirk; it is every
`ComponentChildContentIntermediateNode`, including plain decision-131 composition.

**The accidental workaround is real.** `J7IfInDivInForm` — the same `@if` wrapped in a `<div>` — compiles
(2117 B) and emits `list(_el2, () => (show.value) ? [0] : [], () => 0, ifBody, _if0);`, which is exactly the shape
that would be emitted against `_el0` (the form). Same machinery, no new runtime op.

**Blazor validity.** All witnesses dropped into a copy of `baseline/Forms.Blazor` and built:
`Build succeeded. 0 Warning(s) 0 Error(s)`, with `strings … | grep` confirming
`__Blazor.Forms.Blazor.J6IfDirectCascading`, `J2IfInForm`, `J3ForeachInForm`, `J7IfInDivInForm` and `K3IfInPanel`
in the produced dll. No `RZ9979`-class rejection anywhere.

**Root cause — and the correction to the original diagnosis.** The claim said the collect walk never plans the
region. That is **empirically false**: a discriminating experiment (`K1BadIfInForm.razor`, the same shape with an
undeclared name) produces a real located refusal —
`error K1BadIfInForm.razor(1,52): FIL0001: [unresolved-name] 'nosuchfield' is not declared in this component…` —
byte-identical in kind to the same `@if` under a `<div>`. The region **is** planned and its reassembled C# **is**
compiled; `Collect` already recurses into `ComponentChildContentIntermediateNode` via its `else Collect(kid, plan)`
arm (`TemplateCompiler.cs:658`). The defect is **emit-side only**: `EmitEditForm` (`TemplateCompiler.cs:1592`) and
`EmitCascadingValue` (`:1532`) walk `content.Children` calling `EmitNode` per child and never call
`EmitOps(_code.OpsFor(content), v)` the way `EmitElement` does at `TemplateCompiler.cs:1317`; the raw
`CSharpCodeIntermediateNode` then falls into the throw at `TemplateCompiler.cs:1210`. The escape hatch one line
above (`case CSharpCodeIntermediateNode when _diagnostics.Count > 0: return null;`, `:1208`) is exactly why the
bad-name variants surface correctly.

**Fix.** Route child content through the same `EmitOps` path every other container takes — parent = the created
`<form>` element, or `target` for a root-level `CascadingValue`, exactly as decision 89 does for root regions.

### B2 — a region at the root of a fragment

**Witnesses.** `F3if`, `F3eachroot` — `<Card Title="hits">@if (count > 0) { <span id="body">@count</span> }</Card>`
with a sibling baseline `Card.razor`.

**Observed.** The same `FIL-WIRING` text, exit 1, no location. The wrapped control
(`<Card Title="hits"><div id="hold">@if…</div></Card>`) compiles at exit 0 (1812 B).

**Blazor validity.** `Build succeeded. 0 Warning(s) 0 Error(s)` for both the `@if` and the `@foreach(@key)`
variants; `App_razor.g.cs` shows plain conditional `ChildContent` in the parent's scope.

**Root cause — same family as B1, and the claim's mechanism is again inverted.** `--dump-ir` shows `<Card>` is a
`MarkupElementIntermediateNode` (siblings are not tag-helper-resolved), so `Collect` **does** descend and **does**
plan a region with `Container=<Card>` — proved by a located diagnostic from inside the fragment-root `@if`:
`error App.razor(1,40): FIL0001: [unresolved-name] 'nope' is not declared in this component.` The defect is that
`EmitComposition` builds `fragmentNodes` (`TemplateCompiler.cs:1808`) and `EmitFragment` (`:1487`) walks those
nodes individually through `EmitNode`, never consulting the planned region.

**Fix.** The same fix as B1: route the fragment's node list to the anchored region emission with the child's
element as container. **B1 and B2 are one slice.** No fixture in `tests/Filament.Generator.Tests` pins either
shape (`FragmentTests.cs` has no region case).

### B3 — `@ChildContent` inside a region in the child

**Witness.** `f6/App.razor` + `Card.razor` = `@if (Title == "t") { @ChildContent }`, and the reactive toggling
form `@if (Show) { @ChildContent }`.

**Observed, verbatim, exit 1, no location:**

```
error FIL-WIRING: a RenderFragment reached the emitter with no container to insert into. A fragment slot is always a child of the element the composed child declared it in. This is the TOOL being broken, not the input.
```

**Half the construct is already admitted.** `@if (Title=="t") { <div id="hole">@ChildContent</div> }` compiles at
exit 0, and the reactive `@if (Show) { <div id="hole">@ChildContent</div> }` compiles too, emitting
`function ifBody() { … return _el2; } list(_el1, () => (show.value) ? [0] : [], () => 0, ifBody, _if0);`. Only the
**bare** `@if(c){ @ChildContent }` crashes.

**Blazor validity.** `dotnet build` = `Build succeeded. 0 Warning(s) 0 Error(s)` for both forms.

**This is the one defect whose faithful fix is not obviously generator-only.** The verifier hand-wrote the
proposed mapping (every non-disputed line copied verbatim from the generator's own emission for the working
variant) and ran it in Chromium against the Blazor answer key:

| | Blazor | proposed mapping, reading 1 | proposed mapping, reading 2 |
|---|---|---|---|
| initial (`Show=true`) | `<div id="card"><span id="a">A</span><span id="b">B</span></div>` | `THREW at mount: HierarchyRequestError` — nothing renders | `<span id="b">B</span><span id="a">A</span>` — **order reversed at frame zero** |
| `Show=false` | `<div id="card"></div>` | — | `<span id="b">B</span>` — **orphan** |
| `Show=true` again | identical to initial | — | duplicates **accumulate** |

Reading 2 fails **silently** (`pageErrors: []`). The reasons, read from source: `insert(parent, node)` is
`parent.insertBefore(node, null)` and a `Comment` is `CharacterData`, so the anchor is a sibling position and can
never be a parent; `list.ts`'s contract is **one node per key** (`r.n = scope(r, () => create(item))`,
`unmount(r) => remove(r.n)`) while a `RenderFragment` is N top-level nodes (multi-node fragments **are** admitted
today); and `EmitNode`'s fragment case returns `null`, so `EmitBranchFn`'s `root` is null and it bails at
`if (root is null) return false`.

**Fix.** A faithful mapping needs either `list()` rows that own **N** nodes — a change to the **frozen** runtime —
or a wrapper element Blazor does not render. Until one of those is decided, the honest fix is a **located
refusal** replacing the `FIL-WIRING`. See [Slices](#slices), S16.

### B4 — recursive component use aborts the process

**Witness.** `Node.razor` = `<span class="node">@Label@if (Label == "x") { <Node Label="done" /> }</span>` with
`[Parameter] public string Label`; `App.razor` = `<div id="wrap"><Node Label="x" /></div>`. Mutual recursion
(`Alpha` ↔ `Beta`) behaves identically.

**Observed.** `EXIT=134` (SIGABRT), `stderr_bytes=1274263`, `"Stack overflow."`, **no `out.js`**, no diagnostic.
Frame census:

```
2442 EmitNode / 2442 EmitElement / 1221 EmitComposition / 1220 EmitOps / 1220 EmitIf / 1220 EmitBranchFn
```

**This witness terminates in Blazor.** `dotnet build` → `Build succeeded. 0 Warning(s) 0 Error(s)`, and the real
renderer produced `RENDERED: <div id="wrap"><span class="node">x<span class="node">done</span></span></div>`.
(The original unguarded witness is dismissed below — Blazor's own renderer never terminates on it.)

**Isolation control.** The same file with the recursive tag replaced by `<em class="deep">done</em>`: `EXIT=0`,
1181 B, emitting `list(_el1, () => ('x' === 'x') ? [0] : [], () => 0, ifBody, _if0);`. Recursion is the sole
cause, and parameters fold to compile-time constants, so unrolling is not obviously impossible — the original
claim's "cannot be unrolled" is conservative, not wrong.

**Root cause.** `EmitComposition` (`TemplateCompiler.cs:1725`) re-enters `RazorFrontEnd.Parse` (`:1795`) and
`PrepareComponent` (`:1817`) with no cycle set.

**Fix.** A composition-cycle guard: carry the resolved child paths on the way down and emit a located `FIL0003`
naming the cycle. A refusal cannot mis-render, so this fix has no faithfulness risk.

---

## Class C — refusal quality

*Refuses correctly, but the diagnostic lies about where, or blames the author for something the compiler failed
to carry over.*

### C1 — with two or more `@inject`, the caret always points at the first one

**Witnesses.** `P20_RefusedInjectFarDown.razor` (L3 `@inject IJSRuntime JS`, L5 `@inject NavigationManager Nav`),
`P_ThreeReal.razor` (three refused injects on L2/L3/L4).

**Observed.** The refusal is correct and **mis-located**:

```
P20_RefusedInjectFarDown.razor: refusing to emit (1 diagnostic(s)):
  error P20_RefusedInjectFarDown.razor(3,1): FIL0003: [unsupported-directive] @inject NavigationManager is not in the subset. …
```

`NavigationManager` is on line **5**; the caret reads `(3,1)`, the **admitted** `IJSRuntime` inject. With three
refused injects, all three carets read `(2,1)` and the diagnostics come out in **reverse source order**.

**Control.** A single refused inject alone on line 5 reports `(5,1)` — correct. The existing witness
`tests/Filament.Generator.Tests/Unsupported/Inject.razor` has one inject on L1 and is correct **by coincidence**,
which is why this was never noticed.

**Blazor validity.** Both probe sources built in a copy of `baseline/JsInterop.Blazor`: `Build succeeded.`

**Root cause.** `TemplateCompiler.cs:364`,
`var span = parse.Directives.FirstOrDefault(d => d.Name == "inject").Source;` computed **inside** the
`foreach (var inject in RazorFrontEnd.Injects(cls))` loop.

**Fix, with the refutation of the obvious one.** "Pair the Nth `ComponentInjectIntermediateNode` with the Nth
`inject` entry of `parse.Directives`" is **wrong**: a console probe printing both lists shows they are
**anti-parallel** — `parse.Directives` is document order, `ComponentInjectIntermediateNode` children are
**reverse** document order (both nodes are `@<synthesised>`, i.e. spanless, confirmed by `--dump-ir`). The fix
must match on the directive's **tokens** (type name + member name), not on index.

### C2 — any other Forms component with `@bind-Value` leaks two spanless diagnostics

**Witnesses.** `<InputNumber @bind-Value="model.Age" />` inside an `<EditForm>`; identical for `InputCheckbox`,
`InputDate`, `InputTextArea`, and for the region twin (`InputNumber` inside an `@if`).

**Observed.** Three diagnostics where one is honest:

```
App.razor: refusing to emit (3 diagnostic(s)):
  error <no source span>: FIL0001: [unsupported-call] 'global::Microsoft.AspNetCore.Components.CompilerServices.Run...' is not a call to a method declared in this component. …
  error <no source span>: FIL0001: [unsupported-expression] ParenthesizedLambdaExpression (`() => model.Age`) is not in the C# subset. …
  error App.razor(1,47): FIL0003: [component-composition] <InputNumber> resolved to a framework component, and it is not one of the three the subset admits (<CascadingValue>, <EditForm>, <InputText>). …
```

`--dump-ir` confirms the cause: the `ValueChanged` (`RuntimeHelpers.CreateInferredEventCallback`) and
`ValueExpression` (`() => model.Age`) nodes are `@<synthesised>`, hence the `<no source span>`. The author never
wrote either.

**Root cause.** The decision-138 suppression filter is hardcoded to two tag names at
`TemplateCompiler.cs:624` (`node is ComponentIntermediateNode { TagName: "InputText" or "EditForm" }`) and its
region twin at `:928`.

**Fix, and the proof it is safe.** The verifier built the patch in a scratch copy —
`t.StartsWith("Microsoft.AspNetCore.Components.Forms.", StringComparison.Ordinal)` at both sites. Every affected
component collapses to exactly one diagnostic, the honest `FIL0003`; `baseline/Forms.Blazor/App.razor` stays
byte-identical; and a regression sweep of the patched vs shipped generator over **191 `.razor` files**
(`baseline/`, both fixture dirs, `samples/`, `examples/`), comparing exit code, emitted bytes and full diagnostic
text, reported `SWEEP TOTAL: same=191 diffs=0`. Spoofing is closed: an author cannot place a sibling component in
that namespace, because `@namespace` is itself refused.

**Honest correction to the wording.** "Every form component" overstates it — the leak tracks `@bind-Value`
lowering, not Forms membership. `DataAnnotationsValidator` and `ValidationSummary` produce only the single
`FIL0003`. `ValidationMessage For="@(() => model.Name)"` produces one `FIL0001` at a **real** span (1,117),
because that lambda is author-written; the widened filter swallows it too, which is harmless (the `FIL0003` still
refuses and the sweep shows zero byte change). The filter is really "suppress the parameters of a component we
are about to refuse anyway".

### C3 — a base's own `@inject` / `@using` are dropped, and its own code is then refused

**Witnesses.** `p17` (base declares `@inject IJSRuntime JS` and its own method uses `JS`), `p28` (base declares
`@using System.Text.Json` and its own method calls `JsonSerializer.Serialize`), plus the silent-drop witnesses
`p15` and `p27`.

**Observed.** The base's own code is refused, at the base's own line:

```
error JBase.razor(8,15): FIL0001: [unsupported-call] 'JS.InvokeVoidAsync("localStorage.setItem", "fil", "hello")' is not a call to a method declared in this component. … a Filament module ships no BCL. Refusing to emit.
error JBase.razor(6,36): FIL0001: [unsupported-call] 'JsonSerializer.Serialize(count)' is not a call to a method declared in this component. …
```

The directives themselves are dropped **silently**: `p15` (bare base `@inject`) and `p27` (base
`@using Nope.NotAThing`, which the derived would refuse) are both **byte-identical** (`cmp`) to the
no-directive sanity module, 1323 B.

**Blazor validity.** Two real projects built from `baseline/Inherits.Blazor`: `V17.Blazor` and `V28.Blazor`, both
`Build succeeded. 0 Warning(s) 0 Error(s)`. The Razor codegen confirms it —
`JBase_razor.g.cs` contains `[InjectAttribute] private IJSRuntime JS { get; set; }` and `App_razor.g.cs`
`public partial class App : JBase`.

**Mapping proved by construction.** Moving **only the directive** to the derived while leaving the code in the
base: `x2-using-on-derived` → exit 0, 1360 B, emits `json.value = JSON.stringify(count);`, byte-identical
(`cmp`) to the all-in-derived control; `x3-inject-on-derived` → exit 0, 1401 B, emits decision 133's erasure
verbatim. The negative control with no `@using` anywhere refuses, so the directive really is load-bearing. Every
downstream stage already produces correct JS from base-declared code; **the sole missing step is carrying the
directive over.**

**Root cause.** `TemplateCompiler.cs:349` merges the base's `@code` nodes only
(`codeNodes.InsertRange(0, …)`); the base's directive list is never consulted.

**Objection raised and answered.** Blazor scopes these per file — a derived using the *base's* `@using` fails
`CS0103`, and a derived using the *base's* `@inject` fails `CS0122`. A flattened merge admits both. But that
flattening is **already** the shipped status quo of decision 136: `p29-private-base` (base declares
`private int count` / `private void Inc()`, derived's markup uses them) compiles in Filament while the identical
Blazor project fails `CS0122` twice. The over-admission is pre-existing, not introduced — the refinement is to
harvest per declaring file.

**Correction.** The claim's third leg, `@typeparam`, is mis-stated: `@inherits BoxBase<int>` fails at base-path
resolution (`resolves to a same-directory component BoxBase<int>.razor, which does not exist` — the name is used
textually), a **generic-base-resolution** gap, not a dropped `@typeparam`.

### C4 — `@inherits ComponentBase` is refused while the qualified spelling is admitted

**Witness.** `baseline/Counter.Blazor` with `@inherits ComponentBase` on line 1.

**Observed.**

```
error App.razor(1,1): FIL0003: [unsupported-directive] @inherits ComponentBase resolves to a same-directory component ComponentBase.razor, which does not exist. A base component must be a sibling .razor file: it is the only C# this compiler reads, so a base declared in a .cs file would silently contribute nothing and leave the module missing exactly the state the base holds. Refusing to emit.
```

**Blazor validity, and the proof it is a no-op.** `Build succeeded. 0 Warning(s) 0 Error(s)`.
`obj/…/App_razor.g.cs` emits `public partial class App : ` then `#line (1,11)-(1,24)` then the author's literal
`ComponentBase`; the identical project **without** the directive emits
`public partial class App : global::Microsoft.AspNetCore.Components.ComponentBase`. Same type.

**Already-exercised behaviour.** `@inherits Microsoft.AspNetCore.Components.ComponentBase` is **already admitted**
today (exit 0, 1649 B) and `cmp` against the same file with no `@inherits` reports **BYTE-IDENTICAL**
(`1d00cdf7c301ad4a324e6e68c486d02543e7085e` both). Only the unqualified spelling falls into sibling resolution.

**Root cause.** `TemplateCompiler.cs:330`, the `cls.BaseType != ComponentBaseType` test, followed by the
sibling-path build at `:333`.

**Fix, with the shape the verifier corrected.** The predicate must be a **fallback after** the sibling-existence
check, not a replacement of the test at `:330`. A sibling `ComponentBase.razor` legally **shadows** the framework
base in real Blazor (`Build succeeded.`, generated
`public partial class ComponentBase : global::Microsoft.AspNetCore.Components.ComponentBase`) and Filament
**compiles that pair correctly today** (exit 0, emitting `const currentCount = signal(5)` from the sibling).
Simulating the one-predicate patch on that pair produces `FIL0001 [unresolved-name] 'currentCount'` +
`FIL0003 [unresolved-name] … 'Increment'`, exit 1 — a loud regression of a currently-correct compile.

### C5 — every route-parameter refusal is reported as `[unresolved-name]`

**Witnesses.** `Item.razor` (`@page "/item/{id}"` + `[Parameter] public string Id`, read as `@Id`),
`Files.razor` (`@page "/files/{*rest}"`), `Plain.razor` (`@page "/files"` with an unrelated `[Parameter]`),
`Greeting.razor` compiled standalone (a plain `[Parameter] string Name`, no routing at all).

**Observed.** All four produce the same diagnostic family:

```
error Item.razor(3,54): FIL0001: [unresolved-name] 'Id' is not declared in this component. The subset admits state and methods declared in the SAME component (spec 5); a Filament module has no `this`, no base class and no injected services to reach for. Refusing to emit.
```

`Greeting.razor` — no `@page`, no route, no parameter in the URL — reports the identical message at `(1,29)`.

**Why that is a lie about the cause.** A `[Parameter]` only resolves when a **parent** inlines it at compile time;
`RouterEmitter`'s generated router calls `hit[1](target)` — `mount(target)`, one argument — so a routed page has
**no channel** to receive a route value of any type. The message blames the author's declaration for a missing
plumbing channel, and it says so in language ("is not declared in this component") that is factually wrong: the
parameter **is** declared, three lines above.

**Fix.** A distinct diagnostic for a `[Parameter]` on a routed page, naming the real reason (a routed page has no
parameter channel today) and the route parameter it corresponds to. This is a prerequisite for D12, which is what
would eventually make the construct work.

### C6 — a merged base's diagnostics blame the base for names the merge dropped

**Witnesses.** The two-level chain (A4) and the code-behind base (A3).

**Observed.** Two-level: `error BBase.razor(3,28): FIL0001: [unresolved-name] 'count' is not declared in this component.`
while `dotnet build` of those same three files succeeds. Code-behind, all-members-in-the-`.cs` variant:
`error App.razor(3,32): FIL0001: [unresolved-name] 'count' is not declared in this component. …`

In both cases the name **is** declared, in a file the merge chose not to read. The diagnostic points at the
reader and describes the author's program as wrong.

**Root cause.** Shared with A3/A4/C3: `TemplateCompiler.cs:330–349` merges exactly one sibling `.razor`'s
`@code`, and every later stage reports against whatever file it happens to be compiling.

**Fix.** Whenever a base is resolved, refuse **at the `@inherits` directive** with a message naming what was not
merged, before name resolution can produce a misdirected `[unresolved-name]`.

### C7 — "no injected services to reach for" is wrong when an `@inject` is present

**Witnesses.** `P14_MarkupOnlyUse.razor` (`@inject IJSRuntime JS` + `<p id="out">@JS</p>`), `P14l`
(`title="@JS"`), `P14m` (`$"{JS}"` inside `@code`).

**Observed.** The refusal is correct — `@JS` as a **value** has no faithful mapping, because the service is
erased and there is no `JS` binding in the emitted module at all (proved by `P14i`/`P14k`, which compile the same
name as a **call receiver** to `localStorage.setItem('fil','hello')` and `__getText('/data.json')`). But the
message says `… a Filament module has no `this`, no base class and no injected services to reach for.` when an
`@inject` **is** present and the name **did** bind.

**Refutation of the original claim, in full.** The claim was that an injected name is not resolvable from a
template expression. It is: `P14i` and `P14k` compile from markup. And there is no template/`@code` asymmetry —
the bare-value read is refused inside `@code` with the same diagnostic. The real rule is uniform: **admitted as a
call receiver at the erasable call sites, refused as a value**, which is DECISIONS #133's stated boundary. Only
the wording is wrong.

**Fix.** Reword: name the service, say it exists only as a call receiver, and name the calls that erase.

### C8 — the author-time analyzer reports nothing on `.razor` `@code`

**Witness.** `baseline/ElemRef.Blazor` copied to scratch with
`<Analyzer Include="…/Filament.Analyzer.dll"/>` + `Filament.Subset.dll`; the analyzer's presence confirmed by
`dotnet build -v diag | grep Filament.Analyzer.dll` → 5 hits on the csc `/analyzer` list, no `CS8032`.

**Observed.** Treatment, default config, clean build of the pristine baseline `App.razor`:
`Build succeeded.` and `--- FIL0002 count: 0 ---`. **Negative control** — `private object control = null!;`
added to `@code`, a type the repo's own `OutOfSubsetFieldType_IsFlagged` pins as `FIL0002`:
`Build succeeded. / 1 Warning(s) / 0 Error(s)`, only `warning CS0414`. **Zero `FIL0002`.**
**Positive control** — a hand-written `Widget.cs : ComponentBase` in the same project:

```
Widget.cs(7,13): error FIL0002: 'object' is not in the C# subset. …
Widget.cs(8,13): error FIL0002: 'Microsoft.AspNetCore.Components.ElementReference' is not in the C# subset. …
```

**Root cause.** `TypeSubsetAnalyzer.cs:34` calls
`ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze)` **without** `ReportDiagnostics`, so
diagnostics raised in the Razor source-generator tree are suppressed. The analyzer analyses `.razor` `@code` and
then throws the answers away.

**Consequence.** The author-time half of the subset story — decisions 149's analyzer work — does not reach the
file type the project is about. Every "the analyzer follows" claim in DECISIONS should be read as "for
hand-written `.cs` only" until this is fixed.

**Fix.** Add `GeneratedCodeAnalysisFlags.ReportDiagnostics`, then measure: the flag will surface every
generator/analyzer divergence at once, including the `ElementReference` position rule (`CSharpFrontEnd.cs:1729`
bypasses `CheckType` deliberately; `TypeSubset.Classify` has no such arm), so the slice must pair the flag with
the position rule in `TypeSubsetAnalyzer.TypePositions`.

---

## Class D — capability gap

*Correctly refused today, real Blazor code, and a faithful mapping is plausible. The first six survived
refutation; the last six are gaps whose obvious mapping was measured and found wrong — they are listed so nobody
implements the obvious thing.*

### D1 — `<InputCheckbox @bind-Value="model.Agreed">` (bool)

**Refused** with the honest `FIL0003 [component-composition]` (plus C2's two spanless leaks). The nearest
already-admitted alternative is **also** refused, so this is not "already admitted" in disguise:
`error RawCb.razor(1,89): FIL0003: [unsupported-bind] @bind on <input> binds 'model.Agreed', which is not a bool field that is already a signal.`

**Blazor validity and behaviour, both measured.** Built and **run**: initial
`<input id="agreed" type="checkbox" class="valid" value="True">` with `.checked=false`, `hasAttribute('checked')=false`,
`#live="False"`; after `.click()`, `.checked=true`, still no `checked` attribute, `#live="True"`. Blazor drives
**only** the `.checked` property.

**Mapping is already shipped, character for character.** `baseline/CheckBind.Blazor/App.razor` compiles to
`setAttr(_el0,'type','checkbox'); effect(() => { _el0.checked = on.value; }); listen(_el0,'change',(e) => { on.value = e.target.checked; });`
(decision 107), and the property-signal half is decision 138's, already shipped.

**Two DOM deltas.** `class="valid" → "modified valid"` is A16 and pre-exists on the admitted `InputText`.
`value="True"` is checkbox-specific and genuinely missing from the sketch — one static literal attribute.

### D2 — `@inherits` over a base declared in a sibling `.cs`

**Refused** verbatim, matching the claim word for word including the two cascading `[unresolved-name]` errors:

```
error App.razor(1,1): FIL0003: [unsupported-directive] @inherits CounterBase resolves to a same-directory component CounterBase.razor, which does not exist. A base component must be a sibling .razor file: it is the only C# this compiler reads, so a base declared in a .cs file would silently contribute nothing and leave the module missing exactly the state the base holds. Refusing to emit.
```

**Blazor validity.** `Build succeeded. 0 Warning(s) 0 Error(s)` with `CounterBase.cs : ComponentBase` and
`App.razor` untouched. The build succeeding **is** the proof the base contributes: the template names `count` and
`Inc`, which only bind because they are inherited.

**Mapping faithful, and it degrades to refusals rather than wrong renders.** Cross-file source mapping is real,
not asserted — a base member outside the subset already reports against the **base** file
(`error CounterBase.razor(2,5): FIL0001: [unsupported-member] PropertyDeclaration …`). Base state as an
auto-property refuses; `protected static` refuses; base and derived both overriding `OnInitialized` refuses with
`[name-collision]`.

**Caveat.** The literal mapping ("parse `<BaseName>.cs` in the same directory") is not Blazor's name resolution —
a valid decoy project was built where a filename-only lookup splices the wrong class. But the **shipped `.razor`
path has the identical weakness** (`Path.Combine(dir, baseName + ".razor")`), so this is a pre-existing
resolution simplification, not a reason the construct is a non-gap. It is a **documented** narrowing
(DECISIONS #136, ADR 0003 line 32 "closed, **narrowly**") — documented-on-purpose is not the same as
not-a-gap.

### D3 — `@foreach` over a `[Parameter]` collection inside the child

**Refused**, three shapes, verbatim:

```
error Grid.razor(2,20): FIL0001: [unsupported-foreach] @foreach iterates 'Items', which is not a List<T>, T[] or Dictionary<K,V> field declared in this component. list() reconciles against a source it can SUBSCRIBE to. Refusing to emit.
error Grid.razor(11,24): FIL0002: [unsupported-type] 'System.Collections.Generic.List<TItem>' is not in the C# subset. …
```

**Blazor validity.** `Build succeeded. 0 Warning(s) 0 Error(s)` for both the concrete `List<int>` child and the
`@typeparam TItem` child (Blazor infers `TItem=int`).

**Root cause.** `CSharpFrontEnd.cs:1113` — `ForEach()` opens with
`if (_model.GetSymbolInfo(fe.Expression).Symbol is not IFieldSymbol fs …)`. A `[Parameter]` is an
`IPropertySymbol`, so it never reaches the four admitted source shapes. The same field-only gate blocks
`@Items.Count` on a parameter.

**Mapping is a source-resolution branch over machinery that ships.** Probe `p13` (`<Grid Value="@rows.Count" />`
over a reassigned `List`) emits `const rows = signal([1,2,3]); effect(() => setText(_tx0, rows.value.length));` —
so `SlotJs` for `@rows` is literally the `() => {f.Js}.value` string `ForEach()` already builds. The non-reactive
cases fail **safe** (`EmitComposition:1774` refuses first with `bound-parameter … is not reactive parent state`),
and the child stays a leaf display because the collection is a parameter, not a field.

**Honest sizing.** The **generic** half needs real type-argument substitution into the child's semantic model
(`Classify` must resolve `TItem` from the composition site), materially harder than decision 135's erasure; and a
`Dictionary` parameter needs the source lambda to fold to `() => [...x]`, not the bare `SlotJs`.

### D4 — a component parameter bound to the `@foreach` loop variable

**Refused**, with columns matching exactly:

```
error App.razor(3,30): FIL0001: [unresolved-name] 's' is not declared in this component. …
error App.razor(3,18): FIL0003: [bound-parameter] the parameter 'Label' on <Row> is bound to 's', which is not reactive parent state. …
```

(Without `@key` the file first trips the pre-existing `[unsupported-foreach] a @foreach in the subset must carry
@key on its element` — the claimed pair needs `@key` present.)

**Blazor validity.** `Build succeeded. 0 Warning(s) 0 Error(s)` in a copy of `baseline/RowActions.Blazor`.

**Mapping is a one-token delta from shipped code.** The same markup with `Label="fixed"` compiles **today** and
already inlines the child inside the row factory with `s` lexically in scope. The element path with the same
expression already emits exactly the predicted JS, and the row-scope lowering already discriminates
constant-per-row from reactive (probe `p3` with `@(s + _next)` emits a per-row
`effect(() => setText(_tx0, s + _next.value));`).

**Staleness (#125) does not bite here** — with `@key="s"` and `Label="@s"` the key **is** the value, so a
persisting key implies an unchanged value. **Caveat for the slice:** the wider form `<Child Label="@r.Label" />`
keyed on `@key="r.Id"` would go stale if a row field is mutated post-construction; the new path must reuse the
element path's gate.

### D5 — a static non-string component parameter

**Refused**:

```
error App.razor(1,16): FIL0003: [composition-out-of-subset] parameter 'V' of <Leaf> is not a string. The static-leaf slice folds a STRING attribute value into the child; a numeric or bool parameter would fold a string where a number is meant. Refusing to emit rather than mistranslate.
```

**Blazor validity.** `Build succeeded.`; `App_razor.g.cs` contains
`AddComponentParameter(3, nameof(VLeaf.V), RuntimeHelpers.TypeCheck<global::System.Int32>(1))`.

**int arm faithful, bool arm not.** Rendered through `HtmlRenderer`:
`<span id="vleaf">Value: 1</span><span id="bleaf">On: True</span><span id="dleaf">D: 1.5</span>`. JS `String(1)`
is `1` (faithful), `String(true)` is `true` — **not** `True` (see A13).

**Two factual corrections the probe measured.** (a) "Razor has already resolved the attribute to a typed
constant" is false for this front end — `--dump-ir` shows `HtmlAttributeIntermediateNode … attr 'V'` →
`LazyIntermediateToken … [HTML] "1"`, raw markup text; the type is only available from the child's Roslyn model.
(b) The value is an arbitrary C# **expression**, not a literal: `dotnet build` accepted `V="1 + 2"`, `V="1_000"`,
`V="0x10"`, `V="int.MaxValue"`, `On="!false"`, and **rejected** `On="True"`.

**Fix.** Admit the int arm gated to literal/constant-folded values; scope the bool arm out until A13's formatter
lands.

### D6 — `RenderFragment<T>` as a `[Parameter]`

**Refused** on the **type**, independent of any call site (a declaration-only probe refuses identically):

```
error Card.razor(5,24): FIL0002: [unsupported-type] 'Microsoft.AspNetCore.Components.RenderFragment<int>' is not in the C# subset. Section 5 admits int, long, float, double, decimal, DateTime, bool, string, and List<T> of those or of a record declared in the component. Refusing to emit.
```

**Blazor validity.** `Build succeeded.` for the `@typeparam`, the concrete and the templated-list forms
(nullability warnings only).

**Mapping could not be shown unfaithful.** Blazor's own lowering is
`, 4, (v) => (__builder2) => { … AddContent(7, v …` and `CreateCard_0<TItem>(…, RenderFragment<TItem> __arg1)` —
a lambda authored in the **parent's** scope taking one context value, which is the shape `EmitFragment` already
implements for the non-generic case (it swaps `_file`/`_code`/`_regions` so the fragment compiles where it was
written), plus one binding for the context name. Generics already erase (decision 135). DECISIONS.md records this
as an explicit maintained **deferral**, not an impossibility.

**Two corrections to the framing.** (a) "Trivial on its own" is wrong: `@Template(Value)` is an **invocation**,
not the whole-expression read `CSharpFrontEnd.cs:552 IsFragmentParameter` recognises, and `CSharpFrontEnd.cs:3250`
currently refuses a fragment used inside a larger expression, so relaxing `IsComponentsType`'s
`TypeArguments.Length: 0` alone changes no emitted byte. (b) The canonical use additionally hits
`FIL0003 [bound-parameter]` and needs per-row instancing through `list()`, because Blazor invokes the template
once per item. **See B3** — the context-per-render requirement is what makes this slice collide with the runtime
freeze.

### D7 — `<InputNumber @bind-Value>` (int): the naive mapping is refuted

**Gap real** (refusal reproduced verbatim, `Build succeeded.` on the Blazor side, app runs). **The proposed
mapping — a bare `<input type="number">` with decision 108's revert converter — was refuted by measurement.**
Blazor with `InputNumber` and a plain `@bind` number input side by side, driven by native-setter + change events:

```
pristine : ageOuter=<input step="any" type="number" id="age" class="valid">   plainOuter=<input id="plain" type="number">
valid "7": ageValue=7 class="modified valid" live=7 submits=0     plainValue=7 plainout=7
overflow "99999999999": ageValue="99999999999" class="modified invalid" aria-invalid="true" live=7 (model NOT written)   plainValue REVERTED to "7"
SUBMIT while parse FAILED: submits STAYS 1, out stays 7  -> OnValidSubmit SUPPRESSED
```

A control proves the suppression is Blazor's, not the browser's: the DOM submit event fires every time
(1→2→3), `formCheckValidity` is true, and only `EditContext` stops `OnValidSubmit`. The shipped `EditForm`
emission has **no validity gate**, so the sketch would (a) show `7` where Blazor shows `99999999999`, (b) drop
`class="modified invalid"`/`aria-invalid`/`step="any"`, and (c) **fire `Save` on a model Blazor refuses to
submit**. `InputText` is safe only because a string bind cannot fail to parse.

**Consequence for the slice.** A faithful `InputNumber` requires a per-field parse-state gate on submit — i.e.
the beginning of an `EditContext` — which is a materially larger slice than `InputText` was.

### D8 — `@inject NavigationManager`: the naive mapping is refuted

**Gap real** (refusal verbatim at `TemplateCompiler.cs:386`; `Build succeeded.` and the app ran).
**Parts of the mapping hold and are worth banking:** `Nav.Uri == location.href` and
`Nav.BaseUri == document.baseURI` matched exactly in the live app
(`{"uri":"http://localhost:5000/about","base":"http://localhost:5000/","href":…,"baseURI":…}`), and relative URLs
are not a divergence (with `<base href="/">`, shipped by both sides, `pushState` resolves against the document
base, matching `ToAbsoluteUri`).

**`NavigateTo` diverges, measured three ways.** Blazor's shipped bundle is
`navigateTo(e,t){…} function Ue(e,t,n){const r=xe(e); !t.forceLoad&&Le(r)?Ve(r,…):(…location.href=e)}` — absolutize,
test within-base-space, **full page load if outside**, hash-only navs short-circuit to a scroll.

| | Blazor | proposed `pushState` mapping |
|---|---|---|
| external URL | tab fully loads `127.0.0.1:8791/home` | `THREW SecurityError … cannot be created in a document with origin …`, nothing rendered, handler aborted |
| same-route nav | `{"beforeSameNav":"3","afterSameNav":"3"}` — state preserved | `{"n_before":"3","n_after":"0"}` — router remount destroys state |
| hash nav | `{"n_before":"3","after_hash_n":"3"}` | `{"n_before":"2","n_after":"0"}` |

### D9 — named fragment elements and multiple named fragments: the naive mapping is refuted

**Gap real.** `error App.razor(1,35): FIL0003: [unresolved-component] <Header> resolves to a same-directory component Header.razor, which does not exist. Composition resolves a child as a sibling .razor file; a framework component such as <EditForm> is a spec 3 non-goal and has no sibling here. Refusing to emit.`
(each diagnostic emitted **twice** — the same duplication A5 explains). `Build succeeded.` for the named form,
the explicit `<ChildContent>` form and the `Grid`/`RowTemplate` form.

**Mapping core confirmed:** with both a real sibling `Body.razor` and a `Body` fragment parameter, Blazor emits
`AddAttribute(5, "Body", RenderFragment…)` — the fragment parameter beats the same-named component, which is
A6's fix.

**Refuted on whitespace and on Razor's own errors.** Blazor **discards** the leftover whitespace between named
fragment elements (`grep -c 'AddContent([0-9]*, "\n'` → **0**); Filament's IR carries it
(`LazyIntermediateToken … [HTML] "\n    "`) and Filament's documented policy **materialises** it
(`insert(_el1, document.createTextNode('\n    '));`). "Everything left over is the ChildContent hole" therefore
renders an extra DOM text node in the standard multi-line authoring style. It also **admits what Blazor
rejects**: loose content gives `error RZ9996: Unrecognized child content inside component 'Card'.` and an
attribute on the fragment element gives `error RZ9997: Unrecognized attribute 'class' on child content element
'Header'.` And the `RowTemplate` third is not addressed at all — `FragmentParameterNames` filters through
`IsRenderFragment → type is INamedTypeSymbol { TypeArguments.Length: 0 }` (`TypeSubset.cs:126`), so
`RenderFragment<T>` can never enter the proposed partition.

### D10 — explicit type argument `<Box TItem="int" Value="@count" />`: the naive mapping is refuted

**Gap real.** `error App.razor(1,16): FIL0003: [composition-out-of-subset] <Box> has no parameter 'TItem'. Box.razor declares: Value. Refusing to emit.`
`Build succeeded.` on the Blazor side.

**Refuted.** The proposal drops any binding whose key is a `@typeparam` name, unconditionally. C# permits any type
the bound expression **implicitly converts** to, and the drop erases the conversion. Measured:
`<Box TItem="double" Value="@big" />` over `private long big = 9007199254740993L` builds in Blazor, and
`C# long → 9007199254740993` vs `C# double → 9007199254740992` — Blazor prints **…992**. Dropping the binding
leaves the compiler in exactly the no-attribute state (proved by generating the same file with the attribute
deleted: byte-identical), and Filament emits `const big = signal(9007199254740993n)` → `String(…)` →
**…993**. Wrong number, silently, at exit 0. Decision 135's own faithfulness argument is quoted on this:
generics are admitted *"UNIQUEMENT LA OU L'EXPRESSION DU PARENT EST DEJA TYPE-CORRECTE"*.

**Faithful shape.** Resolve the named type and compare it to the bound expression's type; admit only when
identical (or provably representation-preserving) and refuse otherwise.

### D11 — namespace-qualified / subfolder `@inherits` base: the naive mapping is refuted

**Gap real.** Both refusals reproduced (`@inherits Inherits.Blazor.CounterBase resolves to a same-directory
component Inherits.Blazor.CounterBase.razor, which does not exist`, plus
`@using Q2.Blazor.Shared does not name a namespace in the reference assemblies`), and both sources
`Build succeeded.`

**Refuted, with a silent-wrong-render counterexample.** In `Q4.Blazor` — `CounterBase.cs` (`count = 0`, `Inc` +1)
in the root namespace, `Widgets/CounterBase.razor` (`count = 100`, `Inc` +10) — Blazor binds the **`.cs` class**
(proved by a `.cs`-only member the derived uses building clean). `find -name CounterBase.razor` returns exactly
**one** stem match, and it is the wrong type: "last dotted segment as file stem, search the razor set" would merge
100/+10 where Blazor renders 0/+1, unambiguously, with no diagnostic. In `Q3.Blazor` the **only** discriminator
between two candidate bases is the `@using` line, which the mapping throws away. And folder ≠ namespace:
`@namespace Totally.Elsewhere` on a file in `Shared/` makes the folder-derived name fail `CS0234`.

**Faithful shape.** The app's real namespace assignment (RootNamespace + folder + per-file `@namespace` + types
from `.cs` files and referenced assemblies) — i.e. the project compilation the generator explicitly does not
have.

### D12 — route parameters as a whole: every proposed piece was refuted

**Gap real.** Refused at `FIL0001 [unresolved-name]` (C5), and every route shape is valid Blazor.

**Each proposed piece, measured against real Blazor:**

- **The value channel.** `mount(target)` takes one argument (grep over `Home.g.js`/`About.g.js`/`Missing.g.js`
  confirms a single parameter), so a captured group has nowhere to go. This, not nullability, is the blocker:
  removing `int?` entirely still fails, and a plain `[Parameter]` on a non-routed component fails identically.
- **Instance reuse.** Blazor **reuses** the component on a parameter-only navigation:
  `{"start":{"url":"/item/7","n":"0","inits":"1","sets":"1"}, "bumped":{"n":"3"}, "paramNav":{"url":"/item/8","n":"3","inits":"1","sets":"2"}, "afterBack":{"n":"3","sets":"3"}, "reEnteredVia/":{"n":"0","inits":"1"}}`
  — state survives, `OnInitialized` does **not** re-run, only `OnParametersSet` fires. The emitted router
  re-mounts unconditionally, so `/item/7 → /item/8` would render `n=0` where Blazor renders `n=3` **and** re-run
  the shipped `onInitialized()`. The repo's own proof does not cover this: `bench/harness/bench.mjs:2574-2585`
  drives a **component** change, not a parameter change.
- **`:int`.** `(-?\d+)` + `Number()` diverges in both directions: Blazor matches `/item/+5` and `/item/%205`
  (`NumberStyles.Integer`, `AllowLeadingWhite`) where the regex rejects; Blazor does **not** match
  `/item/99999999999` or `/item/2147483648` where the regex matches and renders a page. The repo already knows
  this shape — `TemplateCompiler.cs:1412-1414` (int `@bind`) uses `/^\s*[+-]?\d+\s*$/` **plus** an Int32 range
  check for exactly this reason.
- **`:guid`.** Blazor **normalises**: `/doc/AAAAAAAA-BBBB-…` renders lowercase; the `N` format (32 hex, no
  dashes) matches and prints dashed; `B` and `P` formats match. An opaque string diverges on match **and** text,
  and `Guid` equality is case/format-insensitive, so two URLs Blazor treats as the same id compare unequal.
- **Catch-all.** `^/files/(.*)$` does not match `/files`, but Blazor renders the files page with `Rest=null`
  there, so Filament would fall through to `/{*rest}` and render the **wrong page**. And Blazor is
  most-specific-wins, order-independently (`/files/new` renders `newfile` even with a catch-all present), while
  the emitted table is a linear `find` in declaration order — precedence ranking is **new router bytes**,
  contradicting "costs zero new router bytes".

**Consequence.** "Route parameters" is one slice — a real matcher, a precedence order, and a way for the router
to pass values into `mount()` — and it costs generated app bytes that must be disclosed in BENCH, exactly as
decision 139 did.

> **CLOSED — decision 163, BENCH n°69 (2026-07-22), on exactly those terms.** All three pieces shipped
> together. The **value channel** is `mount(target, __route = {})`, added only to a page that captures.
> **Instance reuse** is a channel the page RETURNS and the router calls instead of re-mounting, so the
> `n=3` measurement above is reproduced rather than broken — neutralising that path is control 1, and it
> reports `expected "3", got "0"`. **`:int`** ships `NumberStyles.Integer` + the Int32 range, the pair
> this entry pointed at in `TemplateCompiler.cs:1412-1414`. **Precedence** is the one objection answered
> by moving rather than paying: ranking at run time WOULD be new router bytes, so the compiler sorts the
> table by ASP.NET Core's own precedence digits and the router keeps its linear scan — Blazor's ordering
> at zero bytes. **`:guid` and the catch-all were NOT shipped**: both are refused with a located
> diagnostic, because this entry's measurements are precisely why a naive version of either is wrong.
> Cost disclosed as decision 139 did: **+298 B gzip**, isolated at equal page count, and **0** for an app
> that declares no route parameter.

---

## Dismissed — probed, and not a defect

1. **"`<CascadingValue>`/`<EditForm>` whose body contains ANY control-flow region crashes."** Over-broad, and the
   claimed cause is wrong. `W9` (`@foreach` inside a `<ul>` under a cascade) compiles at exit 0 (1516 B), emitting
   `list(_el0, () => items.value, (n) => n, createN, null);` — a **DOM-identical rewrite** of the crashing witness,
   since `CascadingValue` renders no DOM. `W10` compiles (1659 B) and carries the cascade **into** the region.
   `E2` (EditForm + wrapped `@if`) compiles (1401 B). Only the **direct-child** shape crashes, which is B1. The
   claimed cause ("the collect walk does not descend") is refuted by a `PROOF` probe that put refused C# inside
   the crashing region and got **located** `FIL0001`s from inside it.
2. **"The analyzer false-positives `FIL0002` on `private ElementReference box;`."** Does not happen: a clean build
   of the pristine baseline with the analyzer really loaded gives `FIL0002 count: 0`. What the probe found instead
   is larger and is registered as **C8**.
3. **`@page "*"`.** Invalid Blazor:
   `Pages/Star.razor(1,1): error RZ9988: The @page directive must specify a route template. The route template must be enclosed in quotes and begin with the '/' character.`
   No valid Razor source can produce the `'*'` literal, so the emitted router's `routes.find(([r]) => r === '*')`
   fallback (`RouterEmitter.cs:73`) is **dead code**; Blazor's only catch-all spelling is `/{*rest}`, which goes
   in verbatim and never matches (A9).
4. **The typed-`HttpClient` witness `class WeatherHttpClient : HttpClient {}`.** Builds, but never resolves:
   `InvalidOperationException: A suitable constructor for type 'WeatherHttpClient' could not be located. A Typed client must provide a constructor taking a 'System.Net.Http.HttpClient' as a parameter.`
   The asserted Blazor behaviour never happens. A11 survives only on the rebuilt `WrapperHttpClient` witness.
5. **The unbounded recursion witness** (`<span class="node">@Label<Node Label="y" /></span>`). Blazor's own
   renderer never terminates on it (`HtmlRenderer` still running at 00:55, output 0 bytes). B4 survives only on
   the guarded, finitely-rendering witness.
6. **Loose content and attributes on named fragment elements.** Blazor rejects both:
   `RZ9996: Unrecognized child content inside component 'Card'.` and
   `RZ9997: Unrecognized attribute 'class' on child content element 'Header'.` A named-slot slice must reproduce
   those refusals, not admit the source.
7. **"An `@inject`'d name is not resolvable from a template expression."** False as stated — `@JS.InvokeVoidAsync(…)`
   and `@Http.GetStringAsync(…)` both compile from markup, and the bare-value read is refused **identically**
   inside `@code`. Only the message wording is wrong (C7).
8. **`@inherits BoxBase<int>` as a "dropped `@typeparam`".** Mis-stated: it fails at base-path resolution, a
   generic-base-resolution gap (C3's correction).

---

## Slices

Each slice names the defects it closes, its witnesses, and whether the runtime firewall stays empty.
**`git diff -- src/filament-runtime` must be EMPTY on every slice but one, and that one is called out.**

### S1 — the submit contract *(A1, A2, A16 disclosed)*

Closes A1 and A2 in one place: every `<form>` with a submit handler gets `preventDefault`, and an `<EditForm>`
with no `OnValidSubmit` still registers one. Witnesses `D1PlainForm.razor`, `H10PlainFormLambda.razor`,
`F6NoCallback.razor`, `X1SubmitPlusKeydown.razor`. **Generator-only.** Two constraints from the probe: key
`_preventDefault` on **(element, event)**, and flip
`tests/Filament.Generator.Tests/GateSubsetTests.cs:437`, which pins today's wrong behaviour. A16 is disclosed in
BENCH in the same slice, or closed with it.

### S2 — child content is a container *(B1, B2 — one root cause)*

`EmitEditForm`, `EmitCascadingValue` and `EmitFragment` route their node lists through
`EmitOps(_code.OpsFor(container), parent)` instead of walking `content.Children`, exactly as `EmitElement` does at
`TemplateCompiler.cs:1317` and the root does at decision 89. Witnesses `J2IfInForm`, `J3ForeachInForm`,
`J6IfDirectCascading`, `K3IfInPanel`, `E1`, `C9`, `C10`, `F3if`, `F3eachroot`, with `J7IfInDivInForm` /
`W9` / `W10` / `E2` / `f6b` as the already-working controls that must stay byte-identical. **Generator-only.**
This slice turns three crash shapes into compiles, and it must add the region fixtures `FragmentTests.cs` does not
have.

### S3 — fragment slots have names, and fragments nest *(A5, A6, A7, D9)*

`_fragment` becomes a name-keyed dictionary; the partition happens **before** sibling-component resolution and is
scoped to immediate children; the `Fragment` record gains `Outer` so a fragment forwarded through a child resolves
against the scope it was written in. Witnesses `TwoFrag`, `OnlyHeader`, `f1clash2` + its byte-identity
counterfactual `cfact`, the depth case, `F4`, `F4b`. **Generator-only** (the `Outer` fix was built and run: correct
DOM, live binding on the grandparent's signal, depth-1 baseline byte-identical). D9's named-element form is the
same machinery but must additionally reproduce Blazor's whitespace discard and the `RZ9996`/`RZ9997` refusals — it
may be a follow-on slice.

### S4 — composition cycle guard *(B4)*

Carry resolved child paths down `EmitComposition` and emit a located `FIL0003` naming the cycle. Witnesses:
guarded self-recursion `F`, mutual recursion `Alpha`/`Beta`, with the non-recursive control `G` staying
byte-identical. **Generator-only**, refusal-only, no faithfulness risk.

### S5 — `@ref` under a region *(A8)*

A located `FIL0001` when an `@ref` target is emitted under a region. Witnesses `R2C.razor` (`@foreach`),
`R7.razor` / `p2` (`@if`), with the baseline `ElemRef` control unchanged. **Generator-only.** The hoist
alternative is explicitly out of this slice until a Blazor reorder oracle exists.

### S6 — a route is a matcher or a refusal *(A9, A10, C5)*

Two steps, in order. **(a)** Refuse a route template containing `{` or `*` with a located `FIL0003` from
`DirectiveSpyPass`'s span, and give a route `[Parameter]` its own diagnostic instead of `[unresolved-name]`
(C5). **(b)** Multiple `@page`: `RoutesOf`, de-duplicated imports, and an identity guard in `render()` so a
same-component route change does not tear down. Witnesses `Cdecl`, `Dcatch`, `Eplain`, `Fopt`, `Gbogus`,
`Hmalformed`, `Two.razor`. **Generator-only in the sense that matters — `git diff -- src/filament-runtime` stays
empty — but step (b)'s identity guard costs generated app bytes and must be disclosed in BENCH**, which is ADR
0003's routing rule.

> **STEP (a) SHIPPED — and went further than "refuse"; STEP (b) DID NOT.** Decision 163 / BENCH n°69.
> Rather than refusing every `{`, the compiler now MATCHES `{Id}`, `:int`, `:long` and `:bool` and
> refuses only what it cannot convert exactly (`*`, `?`, `:guid`, unknown constraints, malformed braces)
> — the refusal this section asked for is what those shapes get, located, from the `@page` span. C5's
> misdirected `[unresolved-name]` is gone: a route `[Parameter]` binds, and a mismatched one gets
> `[route-parameter-type]`. The identity guard of step (b) shipped as part of it and its bytes are
> disclosed (**+298 B gzip**, isolated).
>
> **A10 IS STILL OPEN.** `RouteOf` is still `Directives.FirstOrDefault(d => d.Name == "page")`, so a
> component carrying two `@page` directives still has the second one silently dropped. Nothing in
> decision 163 touched it, and the `Two.razor` witness still reproduces it.

### S7 — `@inject` gates tell the truth *(A11, C1, C7)*

Exact-name matching instead of `EndsWith`; token-matched spans for multi-`@inject` refusals (the index pairing is
refuted — the two lists are anti-parallel); reworded unresolved-name message. Witnesses `WrapperHttpClient`,
`WeatherClient` (boundary), `P20_RefusedInjectFarDown`, `P_ThreeReal`, `P14_MarkupOnlyUse`. **Generator-only**,
tiny.

### S8 — `@inherits` says what it did *(A3, A4, C3, C4, C6, and D2 optionally)*

One root region of code (`TemplateCompiler.cs:330–349`) produces five defects: refuse (or merge) a code-behind
partial; recurse the base chain; carry the base's `@inject`/`@using`; make `ComponentBase` a **fallback after**
the sibling check; and refuse at the `@inherits` directive rather than letting a misdirected `[unresolved-name]`
escape. Witnesses `CounterBase.razor` + `.razor.cs`, `App:BBase:CBase`, `p17`/`p28`/`p15`/`p27`, the explicit
`ComponentBase` file, and the shadowing pair `CB2` which must keep compiling. **Generator-only.**

### S9 — text position is type-directed everywhere *(A13, A15)*

A `bool` in text position renders `True`/`False`; a value crossing a `@typeparam` or a composition boundary keeps
its formatter (`__f32`, `__dec*`, `__dtStr`). Witnesses `<BLeaf On="@flag" />`, `<Box Value="@f" />` (float),
and the decimal/DateTime variants the templated-fragment verifier measured. **Generator-only** (every formatter
already ships as emitted code). This slice is a precondition for D5's bool arm.

### S10 — a field the template reads through a method is still state *(A14)*

Propagate template reads through the phase-3 call graph into phase-2 promotion — the transitivity decision 160
already applies to `computed()`. Witnesses `C_methodread`, `E_asyncmethodread`, with `D_asyncnowrite` as the
already-correct control. **Generator-only.** This slice must first run the strict same-source A/B the probe did
not run (see A14's evidence edge).

### S11 — `IsFixed` is refused *(A12)*

A located `FIL0003` on `<CascadingValue IsFixed>`. Witnesses `GaugeA`/`GaugeB` under one fixed cascade.
**Generator-only.** The frozen-capture implementation is explicitly **not** this slice — it was measured to break
the `#b` case.

### S12 — the analyzer reaches `.razor` *(C8)*

Add `GeneratedCodeAnalysisFlags.ReportDiagnostics` and pair it with the `ElementReference` **position** rule in
`TypeSubsetAnalyzer.TypePositions`, mirroring `CSharpFrontEnd.cs:1729`. Witnesses the pristine `ElemRef`
baseline (must stay clean), the `object` negative control (must flag), `Widget.cs` (must keep flagging).
**Analyzer-only** — generator and runtime untouched. Expect a burst of newly-visible divergences; the slice's
real work is triaging them, and `tests/Filament.Analyzer.Tests` has no `ElementReference`/`RenderFragment`/
`EventCallback`/`IJSRuntime` case to lean on.

### S13 — `<InputCheckbox @bind-Value>` *(D1)*

Decision 107's emission, verbatim, plus the static `value="True"`. **Generator-only.** Depends on C2 (S14 below)
only for diagnostic cleanliness, not for correctness.

### S14 — the Forms suppression filter is namespace-based *(C2)*

`StartsWith("Microsoft.AspNetCore.Components.Forms.")` at `TemplateCompiler.cs:624` and `:928`.
**Generator-only**, and already proven safe by a 191-file sweep with `same=191 diffs=0`.

### S15 — composition reaches rows and non-strings *(D4, D5-int, D3-concrete)*

Three widenings over one machinery: the loop variable crosses the composition boundary (`<Row Label="@s" />` under
`@key="s"`), a static **int** parameter folds as a number, and a concrete `List<int>`/`T[]` `[Parameter]` becomes
a `@foreach` source (`ForEach()` resolving an `IPropertySymbol` through `_paramEnv`). Witnesses `p1`/`p2`/`p3`
(row), `VLeaf`/`BLeaf` (static), the `Grid` probes. **Generator-only.** The generic half of D3 is **not** in this
slice — it needs type-argument substitution into the child's semantic model.

### S16 — `RenderFragment<T>`, and the one place the freeze is in question *(D6, B3)*

The honest first step is a **located refusal** replacing B3's `FIL-WIRING`. The capability itself is blocked on a
decision this register cannot make: a templated fragment must be re-invoked per render and per item, and both
the measured readings of the obvious mapping produce wrong DOM. A faithful implementation needs **`list()` rows
that own N nodes — a change to the frozen runtime — or a wrapper element Blazor does not render.**
**This is the only slice in this program that is not generator-only, and it must not be started without an
explicit decision on the freeze.**

---

## Explicit non-goals of this program

Each of the following was probed, and in each case the verifier **measured** that the proposed erase-or-emit
mapping renders differently from Blazor. They stay refused, and the refusal is now backed by evidence rather than
by argument.

**`OnParametersSet` / `OnParametersSetAsync`.** Blazor's real order, logged in a running app:
`init|initAsync:pre` at +8 ms, `…|initAsync:post|paramsSet|paramsSetAsync:pre` at +1520 ms — `OnParametersSet`
runs **after the first paint** and **after** `OnInitializedAsync`'s continuation. Head to head with one field
written by both hooks: Blazor renders `init` then `B` and settles on `B`; the proposed slot (immediately after
`onInitialized()`) renders `B` at +7 ms then `A` at +1540 ms and settles on `A`. First paint **and** final DOM
both differ. The proposed gate (`_paramReactive.Count == 0`) is vacuous for an entry module, which has no
parameters at all, and the justifying scenario (a composed leaf-display child carrying the hook) is unreachable:
`CSharpFrontEnd.cs:253 IsLeafDisplay` refuses a child with any method.

**`OnAfterRender` / `OnAfterRenderAsync`.** Blazor runs them and Blazor schedules **no render after them**, so the
first paint shows the pre-write values: `{"blazorFirstPaint":"hi","blazorAfterUnrelatedRender":"ready"}`. The
lifted field in Filament is a signal, so the write fires its effect immediately:
`{"filamentFirstPaint":"ready"}`. The proposed `if (firstRender)` gate is exactly the shape that diverges. (A
tighter gate — body wholly guarded **and** writing no field the template reads — could still be faithful, but that
is a different slice with a different witness.)

**`IDisposable` / `Dispose()`.** Measured: the root component of a WASM app is never disposed while the page
lives — Blazor holds `#out=2` through idle and reload. The proposed `mount()`-returns-a-disposer mapping renders
`999` into a still-attached DOM and the "disposed" component **keeps reacting to clicks** (`#out=1000`). Where
Blazor really does call `Dispose` (a child removed by an `@if`) is structurally unreachable in Filament — a child
with a `Dispose` method is stateful and refused by `[composition-out-of-subset]`. And nothing in §5 carries a
disposal obligation: `Timer`, `CancellationTokenSource`, `IDisposable`, `IJSObjectReference` are each refused by
`FIL0002`.

**`@implements` (any interface).** The proposed erase-when-every-member-is-declared gate has a measured
counterexample: `IHandleAfterRender` declares exactly one member the component declares, so the gate erases it.
Blazor renders `0/101/101`; the erased file through Filament renders `0/1/2`, at exit 0, with the interface method
emitted as a **dead function nothing calls**. (`CSharpFrontEnd.cs:1880` refuses `OnAfterRenderAsync` only under
the `override` modifier; the interface re-implementation form carries no `override`, which is why it compiles.)

**`StateHasChanged()` erasure.** The premise — "every admitted write goes through a signal setter, so the
re-render already happened" — is false, and A14 is the witness: with the call erased, the file is **admitted** and
emits `let count = 0;` with no `effect()`. Erasing would turn a loud located `FIL0001` into a silent wrong render.
Erasure becomes defensible only **after** S10 lands, and even then it must prove every field on the path is a
promoted signal.

**`base.OnInitialized()` erasure.** The premise — "the compiler already knows the callee is empty" — is true only
of Filament's own synthesised stub. Under `@inherits` (a shipped feature) decision 136 merges the base's members
**flat**, so `base.OnInitialized()` always binds to the empty stub while Blazor binds the real base body (IL
scan: `App::<>n__0 --call--> CounterBase::OnInitialized`). Measured: erasing renders `5`, Blazor renders `10`. A
narrower slice gated on the component having **no** `@inherits` is defensible; the mapping as proposed is not.

**The validation family** — `<DataAnnotationsValidator />`, `<ValidationMessage>`, `<ValidationSummary />`, and
`[Required]`/`[StringLength]`/`[Range]`/`[EmailAddress]`/`[MinLength]`/`[Compare]` on a model. Five independent
measurements say the compile-time model does not reach it:
1. Validation is **per-field on change**, not submit-time. With the validator and value `"ab"` under
   `MinLength(3)`, **before any submit**, Blazor renders `class="modified invalid" aria-invalid="true"`; the
   no-validator control renders `modified valid`. Filament's `InputText` emits no class at all (A16).
2. Submit validates the **whole object**, including properties bound to nothing: a model with an unbound
   `[Range(1,10)] int Age = 0` blocks `Save()` entirely. A per-signal predicate model has no predicate for a
   property with no signal.
3. `ValidationSummary` order is **first-touch insertion order**, not declaration order, and a prior touch
   permanently reorders every later submit (measured: `City → Name → Age` after typing in City). Model-level
   rules (`IValidatableObject`) belong to no property at all.
4. The attributes are **inert without the validator**, which is refused by the same `FIL0003` on policy, so within
   every program the subset admits, the faithful compile-time image of `[StringLength]` is **erasure to nothing**
   — the opposite of a predicate. Wiring a predicate anywhere observable makes Filament **block a submit Blazor
   performs**.
5. Every proposed predicate diverges from the BCL at the edges: `Required.IsValid("﻿")` is `True` in .NET and
   `false` under a `trim()` predicate; `Range(1,120)` on a string **throws** `OverflowException` on
   `"3000000000"` where the predicate quietly returns false, and calls `""` and `null` **valid** where the
   predicate rejects them; `StringLength` throws `InvalidCastException`/`InvalidOperationException` in three
   measured shapes.
   `<ValidationMessage>` without an enclosing `<EditForm>` **hard-fails** in Blazor
   (`InvalidOperationException: … requires a cascading parameter of type EditContext`), where an emitted empty
   region would render a working page.

**`@page "*"`.** Not a slice: `RZ9988` makes it unreachable from valid Blazor. The router's `'*'` fallback should
be deleted or repurposed, not fed.

---

## What each slice must measure

The repo's rule is that a slice is measured against real Blazor through the Playwright DOM-contract oracle in
`bench/harness/bench.mjs`. Several defects here are **invisible to today's contracts**, which is part of why they
survived; those slices need a **new assert**, named below.

| Slice | The observable difference the oracle must catch | New assert? |
|---|---|---|
| **S1** | *Did the page navigate away on submit?* Plant a marker on `window`/`document` before the click and assert it survives; assert `location.href` unchanged and the input's value still present. Both shells, `<form @onsubmit>` **and** callback-less `<EditForm>`. Plus a `@onkeydown` on the same form, asserting the key handler still receives its event. | **YES** — the `forms` contract (1.50.0) asserts `#live`/`#out` only and would pass while the page reloads |
| **S2** | *Is the region's content present, in the right container?* `@if`/`@foreach` as a direct child of `<EditForm>`, `<CascadingValue>` and a user component: the branch mounts and unmounts inside the form/cascade/child exactly as it does under the `<div>` control. | **YES** — a new contract; no fixture pins the shape |
| **S3** | *Is the row content present, and present once?* `#head` **empty** while `#body` holds one `#mark`; the decoy `<p id="decoy">` **absent**; a two-level forwarded fragment renders `#body` inside `#inner` and its text advances with the **grandparent's** signal. | **YES** — `FragmentTests.cs` has no named-slot, clash or depth case |
| **S4** | Nothing renders — a refusal has no DOM. The measurement is the **located diagnostic** plus the `dotnet build` that proves the recursive source valid, pinned as a fixture. State that plainly in BENCH rather than reporting a DOM number. | n/a (fixture) |
| **S5** | Same as S4 for the refusal. If the hoist variant is ever attempted, the oracle must drive a **reorder** and compare `document.activeElement.id` on both sides. The witness must **not** name the element after the field (`id="box"` passes by window-named-access coincidence). | n/a (fixture) |
| **S6** | *Did the page render, or is the target blank?* Navigate to a parameterised route and assert the refusal fires at build time instead. For multiple `@page`: `/deux` renders, **and** the counter's state survives the `/two → /deux` navigation (Blazor keeps `n=2`; a remount renders `0`). | **YES** — the `routing` contract (1.51.0) asserts the remount contract only for a **component** change |
| **S7** | *Which URL was requested?* The A11 half is oracle-visible at the HTTP layer: serve both shells against a recording server and assert the observed request line (`/api/weather` vs `/weather`). The C1/C7 halves are diagnostic fixtures. | **YES** — an HTTP-level assert; the `httpjson` contract (1.57.0) asserts rendered rows, not the request path |
| **S8** | *What number does the page show?* Code-behind base → `#out` = `7`; two-level chain → the grandparent's hook ran (`body.class` = `ready`); base-declared `@inject` → the base's own interop ran. The `inherits` contract (1.49.0) asserts `#out 0→1→2` and would pass while all three silently do nothing. | **YES** — three variants |
| **S9** | *Byte-identical text.* `On: True` (not `true`) for a bool in text position; `0.1` (not `0.10000000149011612`) for a float through a generic child; the decimal and DateTime variants likewise. This is the `floatcounter`/`decimalcounter` assert shape applied **through composition**. | **YES** |
| **S10** | *Does the DOM move?* Click and assert the counter advances on both sides, with a template that reads the field only through a method. Visible to a plain DOM assert — it simply has never been written. | **YES** |
| **S11** | Refusal fixture. If `IsFixed` is ever implemented, the oracle must drive **two** consumers under one fixed cascade — one whose own parameter changes and one whose does not — against the measured answer key `a: 1,1,1` / `b: 1,2,3`. | n/a (fixture) |
| **S12** | Not oracle-measurable: this is author-time. The measurement is the build output of three projects — pristine baseline (0 diagnostics), `object` control (flagged), `Widget.cs` (still flagged). | n/a (build) |
| **S13** | The checkbox's `.checked` **property** on both sides (Blazor never emits the `checked` attribute), `#live` following `False`/`True`, and the static `value="True"` present. | extends `checkbind` |
| **S14** | Diagnostic-count fixture (exactly one `FIL0003`), plus the 191-file byte sweep as the regression proof. | n/a (fixture) |
| **S15** | `<Row Label="@s" />` inside `@foreach`: the row's label text matches Blazor after an add and a reorder; `<Leaf V="1" />` renders `Value: 1`; `<Grid Items="@rows" />` renders the same `<li>` sequence as Blazor after a reassignment. | **YES** |
| **S16** | The refusal is a fixture. The capability, if it is ever unblocked, needs an oracle that clicks the parent and asserts the templated fragment's `#body` advances `0 → 1 → 2` — the exact stale-value trap `samples/Fragment/fragment.js` already warns about and decision #125 recorded. | **YES** |

## Invariants

Unchanged from every slice in this repo: baselines `dotnet build` first; the witness is measured against real
Blazor before the slice ships; French DECISIONS + append-only BENCH + a disclosed HARNESS bump; CI green on three
OSes. Two additions specific to this program:

1. **`git diff -- src/filament-runtime` stays EMPTY on S1–S15.** S16 is the single slice that may put the freeze
   in question, and it may not be started without an explicit decision on it.
2. **A slice that closes a class-A defect must add the assert that would have caught it**, and say in BENCH what
   the previous contract was blind to. Sixteen silent divergences survived eleven measured slices because the
   contracts asserted around them; the register is only closed when the oracle can no longer be satisfied by a
   module that renders the wrong thing.
