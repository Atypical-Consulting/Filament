# Bound-Parameter Composition (deferred #88 sub-slice) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Admit a BOUND scalar component parameter — `<Display Value="@count" />` — into the composition subset, so the child's `@Value` is a LIVE reactive binding on the parent's signal, and MEASURE it against Blazor (child text tracks parent state across the composition boundary).

**Architecture:** #88 already inlines the child into the parent's `mount()` scope, so a reactive binding needs no prop-passing and no runtime instance — the child's `@Value` becomes `effect(() => setText(_tx, count.value))` referencing the PARENT's own signal directly. The work is getting the bound expression `@count` into the PARENT's compilation so it is translated and reactivity-analysed: component attribute expressions are not collected today (`Collect` skips attributes; #88 handled only static string literals). We harvest each component's `@`-valued params into `plan.FreeSlots`, so the parent compiles them (`count` lifts to a signal because the binding counts as a template read via `MarkTemplateReads`), then `EmitComposition` reads `SlotJs`→`count.value` and `SlotIsReactive`→`true` off the same node and threads them into the child's `_paramEnv` + a new `_paramReactive` set. The child's `IsReactive` honours the parent-supplied reactivity for a bound `[Parameter]`, so its `@Value` slot emits a reactive binding instead of a constant.

**Tech Stack:** C# Roslyn source generator (`Filament.Generator`), Blazor WASM baselines (net10.0), the Filament TS runtime (`signal`/`effect`/`setText`), the Playwright/CDP DOM-contract oracle (`bench/harness/bench.mjs`), xUnit, `tools/canon.mjs`.

## Global Constraints

- **Measure, don't reason (decisions 29/30).** The slice enters because a Blazor baseline and the generated app render the SAME DOM through the ONE oracle. This ships ONE measured app: a parent counter whose value displays in a composed child, driven by a click.
- **Answer keys are never edited to pass a gate (decisions 21/51);** transcribing the generator's faithful output INTO a new key IS the key's purpose. `boundcompose.js` is transcribed then frozen.
- **The 185+ in-element/composition gates are the parity net.** #88's static composition (Compose gate + snapshot) and all prior gates must stay byte-identical — the bound path must not perturb the static path.
- **Reverting a probe edit uses Edit, never `git checkout`.**
- **`bench.mjs` edits bump `HARNESS_VERSION`** (decisions 31/43/59): `1.6.0 → 1.7.0`.
- **Generator-only.** Composition is a FIL0003/template concern in `TemplateCompiler` + `CSharpFrontEnd`; the analyzer walks C# syntax and never sees templates, so its test count stays unchanged (verify it).
- **Refuse loud and located.** Anything outside the slice — a non-leaf/stateful child, a bound param whose expression is out of the parent's subset, a multi-root child — refuses with a located FIL0003, never a silent or wrong emission. Commit only when a task's deliverable is green; trunk-based, no remote.

---

### Task 1: Generator — bound scalar parameter compiles to a live reactive binding

**Files:**
- Modify: `src/Filament.Generator/TemplateCompiler.cs` (`PrepareComponent`: harvest component bindings; `EmitComposition`: translate bound params instead of refusing)
- Modify: `src/Filament.Generator/CSharpFrontEnd.cs` (`BindParameters` + `_paramReactive`; `IsReactive` bound-param case; `FirstBoundNonStringParameter` → static-only)
- Modify: `tests/Filament.Generator.Tests/DiagnosticTests.cs` OR `GateSubsetTests.cs` (reconcile the #88 `bound-parameter` refusal control)
- Create: `tests/Filament.Generator.Tests/BoundParameterTests.cs`

**Interfaces:**
- Consumes: `_code.SlotJs(node)`, `_code.SlotIsReactive(node)`, `LooksLikeComponent(tag)`, `plan.FreeSlots`, `EmitBinding` (reactive text path).
- Produces: bound `@Value` → `const _txN = document.createTextNode(''); effect(() => setText(_txN, count.value));` inlined into the parent, referencing the parent's signal. `childCode.BindParameters(js-bindings, reactive-names)`; `childCode` retains `IsReactive` = true for a reactively-bound `[Parameter]`.

- [ ] **Step 1: Write the failing test**

Create `tests/Filament.Generator.Tests/BoundParameterTests.cs`:

```csharp
using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// The first deferred #88 sub-slice: a BOUND scalar parameter (<Display Value="@count" />). #88's own
/// refusal called this "parent->child reactive plumbing that is not implemented". It IS now: the child's
/// @Value inlines into the parent's scope as a live effect on the parent's signal (decision 90).
/// BoundComposeTests MEASURES it against Blazor.
/// </summary>
public class BoundParameterTests
{
    // A bound reactive parameter makes the child's @Value a LIVE binding: an effect + setText on the
    // parent's signal, NOT a folded constant.
    [Fact]
    public void BoundReactiveParameter_InlinesAsALiveEffectOnTheParentSignal()
    {
        var js = File.ReadAllText(Generate.ToTempFixture("BoundParamInline.razor"));
        Assert.Contains("effect(", js);
        Assert.Contains("count.value", js);        // reads the PARENT's lifted signal
        Assert.DoesNotContain("[bound-parameter]", js);
    }
}
```

Create `tests/Filament.Generator.Tests/Unsupported/BoundParamInline.razor` (parent) — note the sibling child fixture must live in the SAME dir so `EmitComposition` resolves it:

```razor
<div id="wrap"><button id="inc" @onclick="Inc">inc</button><Display Value="@count" /></div>

@code {
    private int count = 0;
    void Inc() { count++; }
}
```

Create `tests/Filament.Generator.Tests/Unsupported/Display.razor` (the leaf child):

```razor
<span id="out">@Value</span>

@code {
    [Parameter] public int Value { get; set; }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Filament.Generator.Tests --filter BoundParameterTests`
Expected: FAIL — today `EmitComposition` refuses `Value="@count"` with `[bound-parameter]`.

- [ ] **Step 3: Harvest component bindings into the parent's compilation**

In `TemplateCompiler.cs`, add a walk that collects each component element's `@`-valued attribute expressions into `plan.FreeSlots`, and call it in `PrepareComponent` AFTER `Collect(...)` and BEFORE `code.Compile(codeNodes, plan)`:

```csharp
// Component bound parameters (decision 90): a <Display Value="@count" /> carries C# in an
// attribute that Collect() does not descend into. Harvest each into the plan so the parent
// COMPILES it -- @count is translated (count.value) and, crucially, counts as a template read
// so `count` lifts to a signal. EmitComposition reads SlotJs/SlotIsReactive back off the node.
void CollectComponentBindings(IntermediateNode node, TemplatePlan plan)
{
    if (node is MarkupElementIntermediateNode el && LooksLikeComponent(el.TagName))
        foreach (var attr in el.Children.OfType<HtmlAttributeIntermediateNode>())
            foreach (var expr in attr.Children.OfType<CSharpExpressionAttributeValueIntermediateNode>())
                plan.FreeSlots.Add(expr);
    foreach (var child in node.Children) CollectComponentBindings(child, plan);
}
```

Wire it in `PrepareComponent` (right after the root/child `Collect` block, before `code.Compile`):

```csharp
        CollectComponentBindings(method, plan);

        code.Compile(codeNodes, plan);
```

- [ ] **Step 4: Carry reactivity through BindParameters**

In `CSharpFrontEnd.cs`, add a `_paramReactive` set beside `_paramEnv`, and change `BindParameters` to accept it. Current:

```csharp
    public void BindParameters(IReadOnlyDictionary<string, string> bindings)
    {
        foreach (var kv in bindings) _paramEnv[kv.Key] = kv.Value;
    }
```

Replace with (keep the old signature working for #88's static callers by defaulting `reactive` to empty):

```csharp
    readonly HashSet<string> _paramReactive = new(StringComparer.Ordinal);

    /// <summary>Bind [Parameter] names to the parent's translated JS. `reactive` names those whose
    /// binding READS a parent signal, so the child's @Name is a live effect (decision 90) rather than
    /// a folded constant (#88's static case). Static string folds pass an empty `reactive`.</summary>
    public void BindParameters(IReadOnlyDictionary<string, string> bindings, IReadOnlyCollection<string>? reactive = null)
    {
        foreach (var kv in bindings) _paramEnv[kv.Key] = kv.Value;
        if (reactive is not null) foreach (var n in reactive) _paramReactive.Add(n);
    }
```

Add the bound-param case to `IsReactive` (so the child's `@Value` slot is marked reactive when the parent bound it reactively). In `IsReactive`, inside the `switch (_model.GetSymbolInfo(n).Symbol)`:

```csharp
                case IPropertySymbol ps when _paramReactive.Contains(ps.Name): return true;
```

Restrict `FirstBoundNonStringParameter` to STATIC folds only — a bound expression carries its own type (a reactive `int` display is faithful), so only a static string literal folded into a non-string param is a mistranslation. Track static names: in `BindParameters`, params NOT in `reactive` and bound to a `'...'` literal are static. Simplest: pass a `staticNames` set OR check `!_paramReactive.Contains(name)`. Change:

```csharp
    public string? FirstBoundNonStringParameter()
    {
        foreach (var name in _paramEnv.Keys)
            if (!_paramReactive.Contains(name)                       // reactive binds carry their own type
                && _paramsByName.TryGetValue(name, out var p)
                && p.Type.SpecialType != SpecialType.System_String)
                return name;
        return null;
    }
```

(NOTE: a bound-but-NON-reactive expression — `Value="@someConstField"` — is not in `_paramReactive` and would hit this check. That is acceptable for this slice: the measured case is reactive, and a non-reactive bound scalar is a disclosed follow-on. If Step 8's probe shows the answer key needs it, revisit; otherwise refuse non-reactive non-string binds as out-of-slice.)

- [ ] **Step 5: Translate bound params in EmitComposition instead of refusing**

In `TemplateCompiler.cs`, `EmitComposition`, replace the `bound-parameter` refusal. Current:

```csharp
        var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var attr in el.Children.OfType<HtmlAttributeIntermediateNode>())
        {
            if (attr.Children.OfType<CSharpExpressionAttributeValueIntermediateNode>().Any())
            {
                Diag("bound-parameter", ...);
                return null;
            }
            var value = string.Concat(attr.Children.OfType<HtmlAttributeValueIntermediateNode>()
                .SelectMany(h => h.Children.OfType<IntermediateToken>()).Select(t => t.Content));
            bindings[attr.AttributeName] = JsString(value);
        }
```

Replace with (a bound attribute translates its expression via the parent's front end; static attributes fold as before):

```csharp
        var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
        var reactive = new HashSet<string>(StringComparer.Ordinal);
        foreach (var attr in el.Children.OfType<HtmlAttributeIntermediateNode>())
        {
            var bound = attr.Children.OfType<CSharpExpressionAttributeValueIntermediateNode>().FirstOrDefault();
            if (bound is not null)
            {
                // BOUND parameter (decision 90): the parent already compiled this expression (harvested
                // into FreeSlots by CollectComponentBindings), so SlotJs is its translated JS and
                // SlotIsReactive says whether it reads a parent signal. The child inlines into the
                // parent's scope, so a reactive binding references the parent's signal directly.
                bindings[attr.AttributeName] = _code.SlotJs(bound);
                if (_code.SlotIsReactive(bound)) reactive.Add(attr.AttributeName);
                continue;
            }
            var value = string.Concat(attr.Children.OfType<HtmlAttributeValueIntermediateNode>()
                .SelectMany(h => h.Children.OfType<IntermediateToken>()).Select(t => t.Content));
            bindings[attr.AttributeName] = JsString(value);
        }
```

And pass reactivity to the child (find the `childCode.BindParameters(bindings);` line):

```csharp
        childCode.BindParameters(bindings, reactive);
```

- [ ] **Step 6: Run the bound-parameter test**

Run: `dotnet test tests/Filament.Generator.Tests --filter BoundParameterTests`
Expected: PASS — the emission contains `effect(` and `count.value`. If it instead folds a constant, `IsReactive`'s bound-param case did not fire (check `_paramReactive` is populated on the CHILD front end). If `count` reads as plain `count` (no `.value`), the harvest did not make it a template read (check `CollectComponentBindings` runs before `code.Compile` and adds the SAME node instance EmitComposition later looks up).

- [ ] **Step 7: Reconcile the #88 bound-parameter refusal control**

Find the test asserting `[bound-parameter]` (grep `bound-parameter` in tests — likely a ComposeTests case or a GateSubsetTests negative control). It now COMPILES for a reactive scalar. Update it: EITHER change its fixture to an out-of-slice bound param that STILL refuses (e.g. a bound param on a STATEFUL child → `composition-out-of-subset`), OR flip it to a positive assertion that a reactive scalar bind compiles. Preserve the spirit: a bound param that the slice does not cover still refuses loud+located. Read the existing test first and mirror its structure.

- [ ] **Step 8: Full generator suite green; in-element + static-composition gates byte-identical**

Run: `dotnet test tests/Filament.Generator.Tests`
Expected: PASS, including the Compose gate/snapshot (#88 static composition unchanged) and all prior snapshots. If the Compose snapshot reddens, the bound path perturbed the static path — STOP and diagnose.

- [ ] **Step 9: Commit**

```bash
git add src/Filament.Generator/TemplateCompiler.cs src/Filament.Generator/CSharpFrontEnd.cs tests/Filament.Generator.Tests/BoundParameterTests.cs tests/Filament.Generator.Tests/Unsupported/BoundParamInline.razor tests/Filament.Generator.Tests/Unsupported/Display.razor tests/Filament.Generator.Tests/DiagnosticTests.cs tests/Filament.Generator.Tests/GateSubsetTests.cs
git commit -m "feat(@else): bound scalar param inlines as a live effect on the parent signal (#88 sub-slice)"
```

---

### Task 2: BoundCompose measured app + alpha-equivalence gate

**Files:**
- Create: `baseline/BoundCompose.Blazor/{App.razor, Display.razor, BoundCompose.Blazor.csproj, Program.cs, _Imports.razor, wwwroot/index.html, wwwroot/css/app.css}`
- Create: `samples/BoundCompose/boundcompose.js` (answer key — transcribed)
- Create: `tests/Filament.Generator.Tests/Snapshots/BoundCompose.approved.js`
- Modify: `RepoPaths.cs`, `GateTests.cs` (`Generate`)
- Create: `tests/Filament.Generator.Tests/BoundComposeTests.cs`

**Interfaces:** Produces `RepoPaths.BoundComposeRazor`/`BoundComposeAnswerKey`, `Generate.BoundComposeToTemp()`.

- [ ] **Step 1: Author the Blazor baseline (parent + leaf child)**

`baseline/BoundCompose.Blazor/App.razor`:

```razor
@* Parent: a counter whose value is passed to a composed child as a BOUND parameter. The
   child's @Value is a LIVE reactive binding on the parent's `count` signal (decision 90) --
   #88's first deferred sub-slice. Clicking #inc updates the child's #out across the
   composition boundary. No whitespace between siblings inside #wrap, matching Compose. *@

<div id="wrap"><button id="inc" @onclick="Inc">inc</button><Display Value="@count" /></div>

@code {
    private int count = 0;
    void Inc() { count++; }
}
```

`baseline/BoundCompose.Blazor/Display.razor`:

```razor
@* Leaf-display child: [Parameter] only, no state/methods. @Value is a reactive text binding. *@
<span id="out">@Value</span>

@code {
    [Parameter] public int Value { get; set; }
}
```

Create the scaffold by cloning `baseline/RootIf.Blazor`'s (it also has `@onclick`): `BoundCompose.Blazor.csproj` (name swapped), `Program.cs` (`using BoundCompose.Blazor;`), `_Imports.razor` (Web namespace), `wwwroot/index.html` (`<title>BoundCompose</title>`), and:

```bash
mkdir -p baseline/BoundCompose.Blazor/wwwroot/css
cp baseline/Divide.Blazor/wwwroot/css/app.css baseline/BoundCompose.Blazor/wwwroot/css/app.css
```

- [ ] **Step 2: Verify it compiles and transcribe the answer key**

Probe: `dotnet src/Filament.Generator/bin/Debug/net10.0/Filament.Generator.dll baseline/BoundCompose.Blazor/App.razor samples/If/.probe.js` (an in-repo output so the runtime specifier resolves), inspect, `rm` it. Confirm exit 0 and that the module lifts `count` to a signal, wires `#inc`'s handler, and emits `effect(() => setText(_tx, count.value))` for the inlined child. If it REFUSES, fix `App.razor`/`Display.razor` to stay in subset (the refusal is located) — never weaken the generator.

Transcribe that emission VERBATIM into `samples/BoundCompose/boundcompose.js` (decisions 21/51), with a header docstring stating the Blazor DOM contract: `#wrap` contains `<button id="inc">inc</button>` and `<span id="out">` whose text is the live count; the child's `@Value` is an effect on the parent's `count` signal (the reactive plumbing crossing the boundary); handler batching per decision 68 (`count++` is one write → no batch). Import path relative to `samples/BoundCompose/`.

- [ ] **Step 3: Wire RepoPaths + Generate**

`RepoPaths.cs` (after the RootIf entries):

```csharp
    /// <summary>Bound-parameter composition (a reactive counter into a child) — the file Blazor compiles.</summary>
    public static string BoundComposeRazor => Path.Combine(Root, "baseline", "BoundCompose.Blazor", "App.razor");

    /// <summary>The bound-composition SPEC (decisions 21/51). Never edited to make a gate pass.</summary>
    public static string BoundComposeAnswerKey => Path.Combine(Root, "samples", "BoundCompose", "boundcompose.js");
```

`GateTests.cs` `Generate`:

```csharp
    public static string BoundComposeToTemp() => ToTemp(RepoPaths.BoundComposeRazor, "BoundCompose");
```

- [ ] **Step 4: Write the gate test**

Create `tests/Filament.Generator.Tests/BoundComposeTests.cs` mirroring `RootIfTests` (alpha-equivalence gate against `boundcompose.js`; an emission assertion for `effect(` + `count.value`; a self-seeding snapshot `BoundCompose.approved.js`).

- [ ] **Step 5: Run, self-seed snapshot, re-run green**

Run twice: `dotnet test tests/Filament.Generator.Tests --filter BoundComposeTests` (first seeds the snapshot). Expected: all PASS; the gate confirms `boundcompose.js` is a faithful transcription. If the gate fails, fix the KEY, never the generator.

- [ ] **Step 6: Build the Blazor baseline**

Run: `dotnet build baseline/BoundCompose.Blazor -c Release`
Expected: `Build succeeded` (catches an invalid-Blazor app before Task 4's publish).

- [ ] **Step 7: Commit**

```bash
git add baseline/BoundCompose.Blazor samples/BoundCompose tests/Filament.Generator.Tests/RepoPaths.cs tests/Filament.Generator.Tests/GateTests.cs tests/Filament.Generator.Tests/BoundComposeTests.cs tests/Filament.Generator.Tests/Snapshots/BoundCompose.approved.js
git reset -q baseline/BoundCompose.Blazor/bin baseline/BoundCompose.Blazor/obj 2>/dev/null
git commit -m "test(@else): bound-parameter composition baseline + alpha-equivalence gate"
```

---

### Task 3: Measurement wiring — oracle contract + build label

**Files:**
- Modify: `bench/harness/bench.mjs` (`HARNESS_VERSION` → 1.7.0, `APPS.boundcompose`, `verifyContract` branch)
- Modify: `bench/build-filament.sh` (`ALL_LABELS` + 7 dispatchers for `filament-boundcompose-gen`)
- Create: `samples/filament-boundcompose-gen/main.js`
- Modify: `.gitignore`

- [ ] **Step 1: bench.mjs**

`HARNESS_VERSION` → `'1.7.0'` (comment: `1.7.0: 'boundcompose' contract (bound-parameter composition). 1.6.0: rootforeach/rootif...`). Add to `APPS`:

```javascript
  // Correctness-only: verifyContract clicks #inc and asserts the composed child's #out tracks
  // the parent's count across the composition boundary. Bound-parameter composition, decision 90.
  boundcompose: {
    readySelector: '#inc',
    observeSelector: '#wrap',
    scenarios: [],
  },
```

Add the `verifyContract` branch (after the `rootif` branch):

```javascript
    if (app === 'boundcompose') {
      return ctx.page.evaluate(() => {
        const out = { problems: [], observed: {} };
        if (!document.querySelector('#inc')) { out.problems.push('missing required element: #inc'); return out; }
        const read = () => { const e = document.querySelector('#out'); return e ? e.textContent.trim() : null; };
        out.observed.initial = read();
        if (out.observed.initial !== '0') { out.problems.push(`#out initial is "${out.observed.initial}", expected "0"`); return out; }
        document.querySelector('#inc').click();
        out.observed.afterInc = read();
        // THE MEASUREMENT: the child's @Value is a LIVE binding on the parent's signal. A generator
        // that folded the value at mount (no reactivity) would leave #out at "0" here.
        if (out.observed.afterInc !== '1') out.problems.push(`#out after #inc is "${out.observed.afterInc}", expected "1" (bound param did not track the parent signal)`);
        return out;
      });
    }
```

- [ ] **Step 2: build-filament.sh**

Add `filament-boundcompose-gen` to `ALL_LABELS` and a case to each of the 7 dispatchers, following the `filament-compose-gen` pattern: `project_for` → `samples/filament-boundcompose-gen`; `mode_for` → add to the production alternation; `razor_for` → `$REPO_ROOT/baseline/BoundCompose.Blazor/App.razor`; `generated_js_for` → `App.g.js`; `title_for` → `BoundCompose`; `blazor_label_for` → `blazor-boundcompose`; `css_for` → `$REPO_ROOT/baseline/BoundCompose.Blazor/wwwroot/css/app.css`.

- [ ] **Step 3: -gen entry point + gitignore**

Create `samples/filament-boundcompose-gen/main.js` (clone of `filament-compose-gen/main.js`, `import { mount } from './App.g.js'`, docstring naming the boundcompose contract — `#out` tracks the parent count on `#inc`). Add `samples/filament-boundcompose-gen/App.g.js` to `.gitignore`.

- [ ] **Step 4: Syntax-check + sanity build**

```bash
node --check bench/harness/bench.mjs
bash bench/build-filament.sh filament-boundcompose-gen
```

Expected: bench.mjs OK; the label emits `App.g.js` from `App.razor` (exit 0). The `blazor-boundcompose not published` NOTE is expected (Task 4).

- [ ] **Step 5: Commit**

```bash
git add bench/harness/bench.mjs bench/build-filament.sh samples/filament-boundcompose-gen/main.js .gitignore
git commit -m "test(@else): DOM-contract oracle + build label for bound-parameter composition (HARNESS 1.7.0)"
```

---

### Task 4: Run the measurement, record BENCH n°12

**Files:** Modify `BENCH.md` (append entry n°12).

- [ ] **Step 1: Publish the Blazor baseline**

```bash
dotnet publish baseline/BoundCompose.Blazor -c Release -o bench/publish/blazor-boundcompose
```

- [ ] **Step 2: Build the -gen label** (if stale): `bash bench/build-filament.sh filament-boundcompose-gen`.

- [ ] **Step 3: Run the oracle, --contract-only, both apps**

```bash
node bench/harness/bench.mjs --dir bench/publish/blazor-boundcompose/wwwroot --app boundcompose --label blazor-boundcompose       --headless --contract-only
node bench/harness/bench.mjs --dir bench/publish/filament-boundcompose-gen   --app boundcompose --label filament-boundcompose-gen --headless --contract-only
```

Expected: `contract met` for BOTH — `#out` goes `0 → 1` on `#inc` in each. A Filament failure (`#out` stuck at `0`) means the bound binding is not reactive — STOP, do not paper over it.

- [ ] **Step 4: Record BENCH n°12**

Append `BENCH.md` entry n°12 (French house style, matching n°11's structure: Ce qui est mesuré / Environnement / Protocole / Commande pour rejouer / Résultat / Ce que cette entrée N'établit PAS). State: both apps render `#out` `0 → 1` on `#inc`; the child's displayed value tracks the parent's `count` across the composition boundary; `HARNESS_VERSION` 1.6.0 → 1.7.0 disclosed; this closes the FIRST of #88's deferred sub-slices (bound parameter). End with `*Fin de l'entrée n°12. Ne pas modifier — ajouter une entrée n°13 pour toute rectification.*`

- [ ] **Step 5: Commit**

```bash
git add BENCH.md
git commit -m "test(@else): measure bound-parameter composition vs Blazor (BENCH n°12)"
```

---

### Task 5: DECISIONS #90, memory, final verification

**Files:** Modify `DECISIONS.md`; update the composition memory + `MEMORY.md`.

- [ ] **Step 1: DECISIONS #90**

Append `## 90.` (French house style, mirroring #88/#89). Cover: (a) the bound scalar parameter enters composition — the child's `@Value` is a LIVE effect on the parent's signal, working because #88 already inlines the child into the parent's scope (no prop-passing, no runtime instance); (b) the plumbing — `CollectComponentBindings` harvests the `@`-expression into the parent's `FreeSlots` so it is translated AND counts as a template read (`count` lifts to a signal), then `EmitComposition` reads `SlotJs`/`SlotIsReactive` off the node and threads them into the child via `_paramReactive`; (c) `FirstBoundNonStringParameter` now applies to STATIC folds only — a bound expression carries its own type; (d) generator-only, analyzer count unchanged; (e) MEASURED (BENCH n°12): `#out` tracks the parent count on `#inc`; (f) the honest ceiling — reactive scalar / leaf-display child / text display only; still deferred: stateful child, nested composition, non-scalar params, bound child ATTRIBUTES (which need reactive attributes in the base subset first). RADICAL unchanged.

- [ ] **Step 2: Update memory**

Update `memory/double-division-widened-subset.md`: add a section noting the first #88 sub-slice (bound parameter, #90) is closed and measured; update the `MEMORY.md` index hook.

- [ ] **Step 3: Final verification**

```bash
dotnet test tests/Filament.Generator.Tests
dotnet test tests/Filament.Subset.Tests
dotnet test tests/Filament.Analyzer.Tests    # expect UNCHANGED count (no analyzer change)
git diff --stat -- src/filament-runtime      # expect EMPTY
git status --porcelain                        # expect clean (only gitignored build output)
```

Expected: generator suite green incl. the new gates + BoundParameterTests; subset/analyzer counts unchanged; runtime diff empty; tree clean.

- [ ] **Step 4: Commit**

```bash
git add DECISIONS.md
git commit -m "docs(@else): record decision #90 (bound-parameter composition, first #88 sub-slice)"
```

---

## Self-Review

**Spec coverage:** core reactive plumbing (Task 1) → measured app + gate (Task 2) → oracle/build wiring (Task 3) → measurement + BENCH (Task 4) → journal (Task 5). The user's chosen depth (full reactive plumbing) is covered by the harvest→translate→reactive-binding path and the click-driven measurement.

**Placeholder scan:** Answer-key and snapshot BYTES are produced by transcribing the generator's real output (decisions 21/51) — method concrete (Task 2 Step 2), bytes await the generator, exactly as prior keys. Task 1 Step 7 references "grep the existing bound-parameter control" because the exact test file/name is discovered at execution; the reconciliation intent is specified.

**Type/name consistency:** `Generate.BoundComposeToTemp`, `RepoPaths.BoundCompose*`, label `filament-boundcompose-gen` / `blazor-boundcompose`, `App.g.js`, app `boundcompose`, selectors `#inc`/`#out`/`#wrap`, `_paramReactive`, `CollectComponentBindings` — consistent across Tasks 1–5.
