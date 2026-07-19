# Reactive attributes (string-valued `class`) — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Admit a reactive string `class` binding (`class="@expr"`) into the compiled subset — an emission mirroring `EmitBinding` — and measure it against Blazor for DOM-contract equivalence.

**Architecture:** Generator-only widening. `EmitAttribute` gets a middle case (between event-unwrap and the `dynamic-attribute` refusal) gated on an attribute-name allowlist `{ class }`: reactive → `effect(() => setAttr(el, name, js))` in `_bindings`, non-reactive → `setAttr(el, name, js)` in `_create`. The value expression is harvested into `plan.FreeSlots` (new `CollectDynamicAttributes`) so `SlotJs`/`SlotIsReactive` answer for it. `setAttr` already ships in the runtime — no runtime change. Measured via the Playwright DOM-contract oracle (correctness-only), recorded as BENCH n°13.

**Tech Stack:** C# (Razor IR → JS), xUnit, Node (canon.mjs alpha-equivalence + snapshot), Playwright oracle (bench/harness), Blazor baseline app.

## Global Constraints

- **Answer keys / SPEC files are NEVER edited to make a gate pass** (decisions 21/51). `samples/ReactiveAttr/reactiveattr.js` is the reference; the generator is judged.
- **Nothing is spliced.** Attribute value JS comes from `_code.SlotJs(node)` (front end translation), never from author text.
- **No C# subset widening.** The demo `@code` uses only already-admitted constructs (int/string fields, literal reassignment, `++`).
- **Runtime is untouched** (`src/filament-runtime/**`): `setAttr` already exists and is in `RuntimeExports`.
- **Existing behaviour preserved:** `@bind`, boolean attributes, mixed literal+expression, and every non-`class` dynamic attribute stay refused `[dynamic-attribute]`, with the refusal message continuing to echo `Trunc(expr)`.
- **Emission order invariant:** the reactive `setAttr` lands in `_bindings` (before `_attach`), so its first run writes into the detached tree — the attach-last / C3 property holds.
- This IS a measured widening: the generator's emitted bytes change and a BENCH entry is added. That is the opposite of the DX-slice firewall; it is expected here.
- Full suite (subset + analyzer + generator + runtime) green at the end.

---

### Task 1: `ReactiveAttr` reference — baseline app + answer key + test wiring

**Files:**
- Create: `baseline/ReactiveAttr.Blazor/App.razor`
- Create: `baseline/ReactiveAttr.Blazor/ReactiveAttr.Blazor.csproj`, `Program.cs`, `_Imports.razor`, `wwwroot/index.html`, `wwwroot/css/app.css` (copied/adapted from `baseline/Divide.Blazor`)
- Create: `samples/ReactiveAttr/reactiveattr.js` (the answer key)
- Modify: `tests/Filament.Generator.Tests/RepoPaths.cs` (add `ReactiveAttrRazor`, `ReactiveAttrAnswerKey`)
- Modify: `tests/Filament.Generator.Tests/GateTests.cs` (add `Generate.ReactiveAttrToTemp()`)

**Interfaces:**
- Produces: `RepoPaths.ReactiveAttrRazor` → `baseline/ReactiveAttr.Blazor/App.razor`; `RepoPaths.ReactiveAttrAnswerKey` → `samples/ReactiveAttr/reactiveattr.js`; `Generate.ReactiveAttrToTemp()` → temp path of the generator's emission (Task 2 consumes these).

- [ ] **Step 1: Create the baseline `App.razor`** (shared DOM contract; blank lines between siblings are `"\n\n"` text nodes — do not remove them)

```razor
@* Root component. Rendered directly into #app by Program.cs.

   The markup below is the SHARED DOM CONTRACT and must not be altered. The blank
   lines between the three siblings are part of the contract (Blazor ships them as
   "\n\n" text nodes; so does the generator; so does reactiveattr.js). See counter.js.

   The point of this app is class="@statusClass" -- a REACTIVE string attribute. The
   `class` binding updates in lockstep with the @currentCount text binding as state
   changes, which is what the DOM-contract oracle asserts against Blazor's own DOM. *@

<h1 id="title">Counter</h1>

<p id="status" class="@statusClass">Current count: <span id="counter-value">@currentCount</span></p>

<button id="increment" @onclick="Increment">Click me</button>

@code {
    private int currentCount = 0;
    private string statusClass = "zero";

    private void Increment()
    {
        currentCount++;
        statusClass = "counting";
    }
}
@* Only what the component actually uses: the Web namespace supplies @onclick. *@
@using Microsoft.AspNetCore.Components.Web
```

- [ ] **Step 2: Scaffold the rest of the Blazor project** by copying `baseline/Divide.Blazor`'s non-razor files and renaming

```bash
cd /Users/phmatray/Repositories/dotnet/Filament
for f in ReactiveAttr.Blazor.csproj Program.cs _Imports.razor wwwroot/index.html wwwroot/css/app.css; do
  mkdir -p "baseline/ReactiveAttr.Blazor/$(dirname "$f")"
done
# copy the divide project scaffolding; rename the csproj
cp baseline/Divide.Blazor/Program.cs           baseline/ReactiveAttr.Blazor/Program.cs
cp baseline/Divide.Blazor/_Imports.razor        baseline/ReactiveAttr.Blazor/_Imports.razor 2>/dev/null || true
cp baseline/Divide.Blazor/wwwroot/index.html    baseline/ReactiveAttr.Blazor/wwwroot/index.html
cp baseline/Divide.Blazor/wwwroot/css/app.css   baseline/ReactiveAttr.Blazor/wwwroot/css/app.css
cp baseline/Divide.Blazor/Divide.Blazor.csproj  baseline/ReactiveAttr.Blazor/ReactiveAttr.Blazor.csproj
command ls -R baseline/ReactiveAttr.Blazor
```

Then open each copied file and adjust any `Divide`-specific identifiers (assembly name, root namespace, any title text) to `ReactiveAttr`. The `index.html` must ship `<div id="app">Loading…</div>` exactly as Divide's does (the shared shell). If `Divide.Blazor` has no `_Imports.razor`, skip it.

- [ ] **Step 3: Verify the Blazor project restores/builds**

Run: `dotnet build baseline/ReactiveAttr.Blazor -c Release`
Expected: build succeeds (Razor compiles `App.razor` as a real Blazor component — this is the live baseline the oracle publishes).

- [ ] **Step 4: Write the answer key** `samples/ReactiveAttr/reactiveattr.js`

The binding order is derived from the emit walk: the `<p>`'s attributes are processed before its children, so the **`class` effect precedes the `@currentCount` text effect** in the bindings block. `Increment` performs two writes → the handler is wrapped in `batch(...)`.

```js
/**
 * ReactiveAttr — hand-written Filament app. Reference for the reactive-`class` widening (BENCH n°13).
 *
 * ANSWER KEY (decisions 21/51): the generator's emission from baseline/ReactiveAttr.Blazor/App.razor
 * is snapshot- and alpha-equivalence-tested against this file. Every line is written the way a COMPILER
 * would emit it. Never edited to make a gate pass.
 *
 * The source, transcribed exactly (the blank lines between the three siblings are "\n\n" text nodes —
 * the shared DOM contract, see counter.js):
 *
 *     <h1 id="title">Counter</h1>
 *
 *     <p id="status" class="@statusClass">Current count: <span id="counter-value">@currentCount</span></p>
 *
 *     <button id="increment" @onclick="Increment">Click me</button>
 *
 *     @code {
 *         private int currentCount = 0;
 *         private string statusClass = "zero";
 *         private void Increment() { currentCount++; statusClass = "counting"; }
 *     }
 *
 * THE POINT: `class="@statusClass"` is a REACTIVE attribute. `statusClass` is read by the template
 * (the class attribute) AND assigned outside construction (in Increment), so it lifts to a Signal and
 * the class binding is `effect(() => setAttr(p, 'class', statusClass.value))` — the SAME reactive rule
 * as a text binding, with the write target being an attribute (setAttr) instead of a Text node
 * (setText). setAttr already ships in the runtime; nothing new was added there.
 *
 * The binding block emits the class effect BEFORE the text effect: the <p>'s attributes are walked
 * before its children, so the class effect (from the attribute) precedes the @currentCount effect
 * (from the inner span). Both first-run against the DETACHED tree, so neither makes a MutationRecord;
 * attach is last.
 */

import { signal, effect, batch, setText, setAttr, listen, insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  // -- @code: state -----------------------------------------------------------
  const currentCount = signal(0);
  const statusClass = signal('zero');

  // -- create(): the tree, built detached -------------------------------------

  // <h1 id="title">Counter</h1>
  const h1 = document.createElement('h1');
  h1.id = 'title';
  insert(h1, document.createTextNode('Counter'));

  // <p id="status" class="@statusClass">Current count: <span id="counter-value">@currentCount</span></p>
  const p = document.createElement('p');
  p.id = 'status';
  insert(p, document.createTextNode('Current count: '));
  const span = document.createElement('span');
  span.id = 'counter-value';
  const t = document.createTextNode('');
  insert(span, t);
  insert(p, span);

  // <button id="increment" @onclick="Increment">Click me</button>
  const button = document.createElement('button');
  button.id = 'increment';
  insert(button, document.createTextNode('Click me'));

  // -- bindings ---------------------------------------------------------------
  // class first (the <p> attribute), then the @currentCount text (the inner span).
  effect(() => setAttr(p, 'class', statusClass.value));
  effect(() => setText(t, currentCount.value));

  // -- events -----------------------------------------------------------------
  // Increment writes twice (currentCount and statusClass), so the handler batches:
  // one flush, both signals, one settle.
  listen(button, 'click', () => batch(() => {
    currentCount.value++;
    statusClass.value = 'counting';
  }));

  // -- attach: last, so the effects' first run made no MutationRecord ----------
  insert(target, h1);
  insert(target, document.createTextNode('\n\n'));
  insert(target, p);
  insert(target, document.createTextNode('\n\n'));
  insert(target, button);
}
```

- [ ] **Step 5: Wire `RepoPaths`** — add after the `BoundComposeAnswerKey` members in `tests/Filament.Generator.Tests/RepoPaths.cs`

```csharp
    /// <summary>Reactive `class` attribute (a counter whose #status class tracks state) — the file Blazor compiles.</summary>
    public static string ReactiveAttrRazor => Path.Combine(Root, "baseline", "ReactiveAttr.Blazor", "App.razor");

    /// <summary>The reactive-attribute SPEC (decisions 21/51). Never edited to make a gate pass.</summary>
    public static string ReactiveAttrAnswerKey => Path.Combine(Root, "samples", "ReactiveAttr", "reactiveattr.js");
```

- [ ] **Step 6: Wire `Generate`** — add after `BoundComposeToTemp()` in `tests/Filament.Generator.Tests/GateTests.cs`

```csharp
    public static string ReactiveAttrToTemp() => ToTemp(RepoPaths.ReactiveAttrRazor, "ReactiveAttr");
```

- [ ] **Step 7: Verify the project still builds** (the generator is unchanged; this only confirms wiring compiles)

Run: `dotnet build tests/Filament.Generator.Tests`
Expected: build succeeds. (`Generate.ReactiveAttrToTemp()` is not yet exercised.)

- [ ] **Step 8: Commit**

```bash
git add baseline/ReactiveAttr.Blazor samples/ReactiveAttr/reactiveattr.js \
        tests/Filament.Generator.Tests/RepoPaths.cs tests/Filament.Generator.Tests/GateTests.cs
git commit -m "test(reactive-attr): ReactiveAttr baseline app + answer key + test wiring"
```

---

### Task 2: The generator feature — allowlist + harvest + emission

**Files:**
- Modify: `src/Filament.Generator/TemplateCompiler.cs` (allowlist, `CollectDynamicAttributes`, `DynamicValue`, `EmitAttribute` middle case, updated refusal message)
- Create: `tests/Filament.Generator.Tests/ReactiveAttrTests.cs`
- Create: `tests/Filament.Generator.Tests/Snapshots/ReactiveAttr.approved.js` (bootstrapped by the snapshot test)

**Interfaces:**
- Consumes: `Generate.ReactiveAttrToTemp()`, `RepoPaths.ReactiveAttrAnswerKey`, `RepoPaths.Canon` (Task 1).
- Produces: emission for allow-listed dynamic `class` attributes; unchanged refusal for everything else.

- [ ] **Step 1: Write the failing gate + behaviour + snapshot tests** — create `tests/Filament.Generator.Tests/ReactiveAttrTests.cs`

```csharp
using Xunit;

namespace Filament.Generator.Tests;

public class ReactiveAttrTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the module emitted from baseline/ReactiveAttr.Blazor/App.razor
    /// is alpha-equivalent to the hand-written samples/ReactiveAttr/reactiveattr.js. The spec is the
    /// reference; the generator is judged. reactiveattr.js's Blazor-faithfulness is what the DOM-contract
    /// oracle measures (baseline/ReactiveAttr.Blazor vs filament-reactiveattr-gen, BENCH n°13).
    /// </summary>
    [Fact]
    public void Gate_GeneratedReactiveAttr_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.ReactiveAttrToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.ReactiveAttrAnswerKey);
        Assert.True(exit == 0,
            "reactive-attribute gate FAILED. Generated module is NOT alpha-equivalent to samples/ReactiveAttr/reactiveattr.js.\n"
            + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>The `class` attribute is a live effect over setAttr on the lifted signal, not a splice.</summary>
    [Fact]
    public void EmittedReactiveAttr_BindsClassWithSetAttrEffect()
    {
        var js = File.ReadAllText(Generate.ReactiveAttrToTemp());
        Assert.Contains("effect(", js);
        Assert.Contains("setAttr(", js);
        Assert.Contains("'class'", js);
        Assert.Contains("statusClass.value", js);
        Assert.DoesNotContain("[dynamic-attribute]", js);
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedReactiveAttrJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.ReactiveAttrToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "ReactiveAttr.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
```

- [ ] **Step 2: Run the tests to verify they FAIL** (the generator still refuses `class="@statusClass"`)

Run: `dotnet test tests/Filament.Generator.Tests --filter ReactiveAttr`
Expected: FAIL — `Gate_…` fails because the generator refuses (exit non-zero, `[dynamic-attribute]` in output); the emission cannot be produced.

- [ ] **Step 3: Add the attribute-name allowlist** — in `TemplateCompiler.cs`, immediately after the `PropertyAttributes` dictionary (around line 163)

```csharp
    /// <summary>
    /// Attribute names whose value MAY be a compiled dynamic expression (reactive or create-time),
    /// mirroring EmitBinding's text path. An ALLOWLIST, like PropertyAttributes and AllowedDirectives:
    /// `class` is the MEASURED one (BENCH n°13); every other name keeps the dynamic-attribute refusal,
    /// which is precisely what keeps @bind's lowered `value=` refused with its exact message. Widening
    /// this set is a NEW measured slice each time — boolean/present-absent attributes (disabled) need a
    /// different emission (present/absent, not setAttr of "true"), so they are not admitted by adding a
    /// name here.
    /// </summary>
    static readonly HashSet<string> DynamicAttributes = new(StringComparer.OrdinalIgnoreCase) { "class" };
```

- [ ] **Step 4: Add `CollectDynamicAttributes` + `DynamicValue`** — in `TemplateCompiler.cs`, right after `CollectComponentBindings` (around line 485)

```csharp
    /// <summary>
    /// Reactive/dynamic ATTRIBUTE values on plain elements (the reactive-`class` slice, BENCH n°13).
    /// Collect() filters out HtmlAttributeIntermediateNode, so an attribute expression is never harvested
    /// there; without a slot the front end never compiles it and SlotJs/SlotIsReactive cannot answer.
    /// Harvest the value expression of an ALLOW-LISTED attribute into FreeSlots — the same harvest
    /// CollectComponentBindings does for a component's bound params — so EmitAttribute can read SlotJs /
    /// SlotIsReactive back off the SAME node. Two guards (in DynamicValue) keep everything else out: the
    /// value must be a single pure C# expression (no literal part), and it must NOT be an event handler.
    /// </summary>
    void CollectDynamicAttributes(IntermediateNode node, TemplatePlan plan)
    {
        if (node is MarkupElementIntermediateNode el && !LooksLikeComponent(el.TagName))
            foreach (var attr in el.Children.OfType<HtmlAttributeIntermediateNode>())
                if (DynamicAttributes.Contains(attr.AttributeName) && DynamicValue(attr) is { } expr)
                    plan.FreeSlots.Add(expr);
        foreach (var child in node.Children) CollectDynamicAttributes(child, plan);
    }

    /// <summary>
    /// The single pure C# expression value of an attribute that is NOT an event handler, or null. The ONE
    /// predicate both the harvest (CollectDynamicAttributes) and the emission (EmitAttribute) consult, so
    /// they cannot disagree about which attributes are dynamic values (decision 53: wiring described twice
    /// drifts). Pure = exactly one CSharpExpressionAttributeValueIntermediateNode and NO literal
    /// (HtmlAttributeValue) part — a concatenation (`class="box @x"`) returns null and stays refused. An
    /// event handler (its value unwraps as an EventCallback) returns null and keeps its listen() path.
    /// </summary>
    static CSharpExpressionAttributeValueIntermediateNode? DynamicValue(HtmlAttributeIntermediateNode attr)
    {
        var csharp = attr.Children.OfType<CSharpExpressionAttributeValueIntermediateNode>().ToList();
        var html = attr.Children.OfType<HtmlAttributeValueIntermediateNode>().ToList();
        if (csharp.Count != 1 || html.Count != 0) return null;
        var expr = string.Concat(csharp[0].Children.OfType<IntermediateToken>().Select(t => t.Content));
        return TryUnwrapEventCallback(expr, out _) ? null : csharp[0];
    }
```

- [ ] **Step 5: Call the collector** — in `PrepareComponent`, immediately after `CollectComponentBindings(method, plan);` (around line 270)

```csharp
        CollectComponentBindings(method, plan);
        CollectDynamicAttributes(method, plan);
```

- [ ] **Step 6: Add the emission middle case + update the refusal message** — in `EmitAttribute`, inside the `if (csharp.Count > 0)` block, AFTER the `TryUnwrapEventCallback` branch's closing `}` and BEFORE the existing `Diag("dynamic-attribute", …)` call

Insert the emission:

```csharp
            // REACTIVE / DYNAMIC ATTRIBUTE VALUE (the `class` slice, BENCH n°13). Only an allow-listed
            // attribute whose value is a single pure C# expression reaches here as an emission; everything
            // else (a non-allow-listed name, a concatenation, an event handler) falls through to the
            // refusal below. Mirrors EmitBinding exactly: SlotJs is the front end's translation (never a
            // splice), SlotIsReactive decides effect-vs-create-time. The effect lands in _bindings (before
            // attach), so its first setAttr writes into the detached tree and makes no MutationRecord.
            if (DynamicAttributes.Contains(name) && DynamicValue(attr) is { } valueNode)
            {
                var js = _code.SlotJs(valueNode);
                _used.Add("setAttr");
                if (_code.SlotIsReactive(valueNode))
                {
                    _used.Add("effect");
                    _bindings.Add($"effect(() => setAttr({v}, {JsString(name)}, {js}));");
                }
                else
                {
                    _create.Add($"setAttr({v}, {JsString(name)}, {js});");
                }
                return;
            }
```

Then replace the existing `Diag("dynamic-attribute", …)` message so it names the allowlist (keep the `Trunc(expr)` echo so `@bind`'s `BindConverter` still shows):

```csharp
            Diag("dynamic-attribute",
                $"attribute '{name}' on <{el.TagName}> carries the C# expression \"{Trunc(expr)}\". This " +
                "compiler compiles a reactive/dynamic value only for ALLOW-LISTED attributes (currently: " +
                $"{string.Join(", ", DynamicAttributes.Order())}); '{name}' is not one of them, and this is " +
                "neither a resolved event handler nor a static value. A dynamic value on an un-measured " +
                "attribute — or a mixed literal+expression value (class=\"box @x\") — has no measurement " +
                "covering it. Refusing to emit.",
                attr.Source ?? el.Source);
            return;
```

- [ ] **Step 7: Run the ReactiveAttr tests — gate + behaviour pass, snapshot bootstraps**

Run: `dotnet test tests/Filament.Generator.Tests --filter ReactiveAttr`
Expected: `Gate_…` and `EmittedReactiveAttr_…` PASS; `Snapshot_…` FAILS on first run with "wrote …ReactiveAttr.approved.js; review + re-run" (it just created the approved file).

- [ ] **Step 8: Review the bootstrapped snapshot, then re-run**

Read `tests/Filament.Generator.Tests/Snapshots/ReactiveAttr.approved.js` and confirm it matches the expected shape: `import { signal, effect, batch, setText, setAttr, listen, insert }`, a `const statusClass = signal('zero');`, `effect(() => setAttr(_elN, 'class', statusClass.value));`, the batched click handler, and the two `"\n\n"` attach nodes. If the binding ORDER differs from the answer key (class effect vs text effect), reconcile `reactiveattr.js` to the generator's actual order — the generator's walk order is the truth; the answer key describes the same module.

Run: `dotnet test tests/Filament.Generator.Tests --filter ReactiveAttr`
Expected: all three PASS.

- [ ] **Step 9: Run the FULL generator suite — nothing else regressed** (especially the `dynamic-attribute` refusal for `@bind`)

Run: `dotnet test tests/Filament.Generator.Tests`
Expected: all green, including `DiagnosticTests.Bind_IsRefused_AtItsExactLocation` (unchanged — `value` is not allow-listed, so `@bind` still refuses `[dynamic-attribute]` naming `BindConverter`).

- [ ] **Step 10: Commit**

```bash
git add src/Filament.Generator/TemplateCompiler.cs \
        tests/Filament.Generator.Tests/ReactiveAttrTests.cs \
        tests/Filament.Generator.Tests/Snapshots/ReactiveAttr.approved.js
git commit -m "feat(reactive-attr): compile reactive class=\"@expr\" via setAttr (allowlisted)"
```

---

### Task 3: The boundary — non-`class` dynamic attribute stays refused

**Files:**
- Create: `tests/Filament.Generator.Tests/Unsupported/DynamicTitle.razor`
- Modify: `tests/Filament.Generator.Tests/DiagnosticTests.cs` (add the boundary test)

**Interfaces:**
- Consumes: `DiagnosticTests.Refused(...)` (existing helper, as `Bind.razor` uses).

- [ ] **Step 1: Create the fixture** `tests/Filament.Generator.Tests/Unsupported/DynamicTitle.razor` — a dynamic attribute that is NOT allow-listed

```razor
<p id="box" title="@caption">hello</p>

@code {
    private string caption = "hi";

    private void Touch()
    {
        caption = "changed";
    }
}
@using Microsoft.AspNetCore.Components.Web
```

- [ ] **Step 2: Write the boundary test** — add to `tests/Filament.Generator.Tests/DiagnosticTests.cs`, next to `Bind_IsRefused_AtItsExactLocation`

```csharp
    /// <summary>
    /// THE ALLOWLIST IS A MEASURED BOUNDARY, NOT FOLKLORE. `class` compiles (ReactiveAttrTests); every
    /// OTHER dynamic attribute stays refused [dynamic-attribute]. `title="@caption"` reads reactive state
    /// exactly as `class` would, and setAttr would be correct for it — but no measurement covers it, so it
    /// is refused with a message that names the allowlist. This is what keeps the widening honest: a name
    /// is admitted only when a BENCH entry measures it.
    /// </summary>
    [Fact]
    public void DynamicNonClassAttribute_IsRefused_AtItsExactLocation()
    {
        var d = Refused("DynamicTitle.razor");

        Assert.Contains("DynamicTitle.razor(1,15): FIL0003: [dynamic-attribute]", d);
        Assert.Contains("class", d);   // the message names the allowlist
        Assert.Contains("caption", d); // and echoes the refused expression
    }
```

- [ ] **Step 3: Run and confirm the location** (the `(1,15)` column is a guess — correct it to the reported span if it differs)

Run: `dotnet test tests/Filament.Generator.Tests --filter DynamicNonClassAttribute`
Expected: PASS. If it fails only on the `(1,15)` column, read the actual location from the assertion message and update the literal to match — the location is real output, not a target to force.

- [ ] **Step 4: Run the whole diagnostics suite**

Run: `dotnet test tests/Filament.Generator.Tests --filter DiagnosticTests`
Expected: all green (Bind + the new boundary test + the rest).

- [ ] **Step 5: Commit**

```bash
git add tests/Filament.Generator.Tests/Unsupported/DynamicTitle.razor \
        tests/Filament.Generator.Tests/DiagnosticTests.cs
git commit -m "test(reactive-attr): non-class dynamic attribute stays refused (allowlist boundary)"
```

---

### Task 4: The measurement — oracle wiring + BENCH n°13

**Files:**
- Create: `samples/filament-reactiveattr-gen/main.js` (host shim; `App.g.js` is emitted by the build and gitignored)
- Modify: `.gitignore` (ignore `samples/filament-reactiveattr-gen/App.g.js`)
- Modify: `bench/harness/bench.mjs` (`APPS` entry, `verifyContract` clause, `HARNESS_VERSION` bump)
- Modify: `bench/build-filament.sh` (cases for `filament-reactiveattr-gen`)
- Modify: `bench/publish-baseline.sh` (mapping for `blazor-reactiveattr`)
- Modify: `BENCH.md` (append entry n°13)

**Interfaces:**
- Consumes: the generator (Task 2), `baseline/ReactiveAttr.Blazor/App.razor` (Task 1).

- [ ] **Step 1: Create the host shim** `samples/filament-reactiveattr-gen/main.js` (adapted from `samples/filament-boundcompose-gen/main.js`)

```js
/**
 * Entry point for the `filament-reactiveattr-gen` label — the reactive-`class` app.
 *
 * It mounts the JS the generator emits from baseline/ReactiveAttr.Blazor/App.razor (a counter whose
 * #status element carries class="@statusClass"). Like the compose/divide/boundcompose labels it is NOT
 * weighed or timed: it exists only so the DOM-contract oracle can click #increment and assert the
 * reactive `class` attribute tracks state in lockstep with the text binding, against Blazor's own DOM.
 *
 * App.g.js IS NOT COMMITTED AND IS NOT EDITABLE: build-filament.sh deletes and re-emits it from
 * App.razor on every build, so the app the oracle loads is always this run's generator output.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
```

- [ ] **Step 2: Ignore the emitted `App.g.js`** — add to `.gitignore` beside the other `-gen` lines

```
samples/filament-reactiveattr-gen/App.g.js
```

- [ ] **Step 3: Register the app in `bench/harness/bench.mjs`** — add to the `APPS` object (after the `boundcompose` entry, around line 333)

```js
  // Correctness-only: verifyContract clicks #increment and asserts a REACTIVE `class` attribute on
  // #status tracks state (zero -> counting) in lockstep with the #counter-value text (0 -> 1), against
  // Blazor's own rendered DOM. The measurement of the reactive-`class` widening (BENCH n°13).
  reactiveattr: {
    readySelector: '#increment',
    observeSelector: '#status',
    scenarios: [],
  },
```

- [ ] **Step 4: Add the `verifyContract` clause** — in `bench/harness/bench.mjs`, inside `verifyContract`, after the `counter` block (around line 1524)

```js
    if (app === 'reactiveattr') {
      return ctx.page.evaluate(() => {
        const out = { problems: [], observed: {} };
        for (const sel of ['#increment', '#status', '#counter-value']) {
          if (!document.querySelector(sel)) out.problems.push(`missing required element: ${sel}`);
        }
        if (out.problems.length) return out;

        const cls = () => document.querySelector('#status').getAttribute('class');
        const txt = () => document.querySelector('#counter-value').textContent.trim();
        out.observed.initialClass = cls();
        out.observed.initialText = txt();
        // Blazor's own initial render: class="zero", count "0". If either already read the post-click
        // value the assertions below would be vacuous.
        if (out.observed.initialClass !== 'zero') {
          out.problems.push(`#status class initial is "${out.observed.initialClass}", expected "zero"`);
          return out;
        }
        if (out.observed.initialText !== '0') {
          out.problems.push(`#counter-value initial is "${out.observed.initialText}", expected "0"`);
          return out;
        }
        document.querySelector('#increment').click();
        out.observed.afterClass = cls();
        out.observed.afterText = txt();
        // THE MEASUREMENT: a reactive `class` binding updates the attribute in lockstep with the text
        // binding — both against Blazor's OWN rendered DOM. A stale class here means the attribute
        // binding did not track (no effect, or a create-time write where a live one was needed).
        if (out.observed.afterClass !== 'counting') {
          out.problems.push(`#status class after #increment is "${out.observed.afterClass}", expected "counting"`);
        }
        if (out.observed.afterText !== '1') {
          out.problems.push(`#counter-value after #increment is "${out.observed.afterText}", expected "1"`);
        }
        return out;
      });
    }
```

- [ ] **Step 5: Bump `HARNESS_VERSION`** — in `bench/harness/bench.mjs` (line 72)

```js
export const HARNESS_VERSION = '1.8.0';   // 1.8.0: 'reactiveattr' contract (reactive class attribute). 1.7.0: 'boundcompose' (bound-parameter composition). 1.6.0: rootforeach/rootif. 1.5.0: compose. 1.4.0: divide.
```

- [ ] **Step 6: Add `build-filament.sh` cases** — in `bench/build-filament.sh`, add a `filament-reactiveattr-gen` arm to each per-label `case` (mirror the `filament-boundcompose-gen` arms): the label list (~line 180), APP_SOURCE_DIR (~193 → `samples/filament-reactiveattr-gen`), mode (~207 → `production`), entry razor (~233 → `$REPO_ROOT/baseline/ReactiveAttr.Blazor/App.razor`), output js name (~252 → `App.g.js`), source name (~267 → `ReactiveAttr`), baseline label (~285 → `blazor-reactiveattr`), css path (~311 → `$REPO_ROOT/baseline/ReactiveAttr.Blazor/wwwroot/css/app.css`).

- [ ] **Step 7: Add the `publish-baseline.sh` mapping** — in `bench/publish-baseline.sh`, add a `blazor-reactiveattr` arm to the project map (~line 107) → `baseline/ReactiveAttr.Blazor`.

- [ ] **Step 8: Build the generator and the Filament app**

```bash
cd /Users/phmatray/Repositories/dotnet/Filament
dotnet build src/Filament.Generator -c Release
bash bench/build-filament.sh filament-reactiveattr-gen
```
Expected: `build-filament.sh` emits `samples/filament-reactiveattr-gen/App.g.js` from `App.razor` and bundles a static root. If it errors, fix the `build-filament.sh` cases before proceeding.

- [ ] **Step 9: Run the oracle contract-only and record the REAL result**

```bash
# the built static root — see run-phase3-measure.sh's dir_for helper for the exact path
node bench/harness/bench.mjs --dir "$(bash -c 'source bench/build-filament.sh 2>/dev/null; dir_for filament-reactiveattr-gen' 2>/dev/null || echo bench/publish/filament-reactiveattr-gen)" \
  --app reactiveattr --contract-only
```
Expected: `[bench] --contract-only: contract met, skipping weight/timing.` — the reactive `class` goes `zero → counting` and the count `0 → 1`, matching Blazor. Capture the actual stdout for the BENCH entry. If the path helper differs, resolve the built directory from `build-filament.sh`'s output and point `--dir` at it. **The BENCH entry records only what this run actually printed — no figure is hand-authored.**

- [ ] **Step 10: Append BENCH n°13** to `BENCH.md` (French, house style), mirroring n°12's structure: CORRECTION only (no C1/C3/C4); the measured result (`class` zero→counting, count 0→1, identical to Blazor); `HARNESS_VERSION 1.7.0 → 1.8.0` disclosed (bench.mjs changed: new `reactiveattr` branch + `APPS` entry); the reserves (reactive string `class` ONLY — boolean/present-absent, mixed literal+expression, and every non-`class` attribute name refused loud+located; §8 RADICAL unchanged); end-of-entry marker.

- [ ] **Step 11: Commit**

```bash
git add samples/filament-reactiveattr-gen/main.js .gitignore \
        bench/harness/bench.mjs bench/build-filament.sh bench/publish-baseline.sh BENCH.md
git commit -m "bench(reactive-attr): DOM-contract oracle + BENCH n°13 (reactive class)"
```

---

### Task 5: DECISIONS #94 + memory + finish

**Files:**
- Modify: `DECISIONS.md` (append #94, French)
- Modify/create: memory under `/Users/phmatray/.claude/projects/-Users-phmatray-Repositories-dotnet-Filament/memory/` (+ `MEMORY.md` index line)

- [ ] **Step 1: Append DECISIONS #94** (French, house style) — the reactive-`class` widening: the `dynamic-attribute` refusal narrowed to an emission for the `class` allowlist, mirroring `EmitBinding` (SlotJs/SlotIsReactive; effect-vs-create-time); the harvest into FreeSlots via `CollectDynamicAttributes` and the single `DynamicValue` predicate shared with emission (decision 53 — no drift); runtime unchanged (`setAttr` already shipped); the attribute-name allowlist as the MEASURED boundary and exactly why `@bind`/boolean/other-names stay refused; measured vs Blazor via the oracle (BENCH n°13, `HARNESS_VERSION 1.8.0` disclosed); boolean/present-absent, mixed literal+expression, and other attribute names deferred.

- [ ] **Step 2: Update memory** — add a new memory file recording the reactive-`class` widening (measured, n°13, generator-only, allowlist boundary, `setAttr` already-shipped, deferred sub-slices) and a one-line pointer in `MEMORY.md`. Cross-link `[[double-division-widened-subset]]` (the other measured widenings) and note the runtime firewall stayed clean while this remained a measured generator change.

- [ ] **Step 3: Full suite — everything green**

```bash
cd /Users/phmatray/Repositories/dotnet/Filament
dotnet test                                   # subset + analyzer + generator
( cd src/filament-runtime && npm test )       # runtime (unchanged, must stay green)
```
Expected: all pass. Confirm the runtime diff is empty: `git diff --stat -- src/filament-runtime` prints nothing.

- [ ] **Step 4: Commit the decision + memory**

```bash
git add DECISIONS.md
git commit -m "docs(reactive-attr): DECISIONS #94 — reactive class attribute (measured widening)"
```

- [ ] **Step 5: Finish** — announce and use `superpowers:finishing-a-development-branch`: verify tests, detect environment (expected: `main`, no remote → trunk-based, work is landed), present the standard options.

## Self-review notes

- **Spec coverage:** allowlist (§Task 2/3), harvest+emission mirroring EmitBinding (Task 2), `class`-only measured app (Task 1/4), `@bind`/boolean/mixed/other-name deferrals (Task 2 message + Task 3 boundary test), runtime untouched (Task 5 diff check), BENCH n°13 + HARNESS bump (Task 4), DECISIONS #94 (Task 5). All present.
- **Type consistency:** `DynamicValue` returns `CSharpExpressionAttributeValueIntermediateNode?` and is consumed by both `CollectDynamicAttributes` and `EmitAttribute`; `SlotJs`/`SlotIsReactive` take `IntermediateNode` (the returned node is one). `Generate.ReactiveAttrToTemp` / `RepoPaths.ReactiveAttr*` names are consistent across Tasks 1–2.
- **Known live-adjust points (real output, not targets):** the answer-key binding order (Task 2 Step 8) and the `DynamicTitle.razor(1,15)` column (Task 3 Step 3) — both reconcile to the generator's actual output, never the reverse; and the `--dir` path for the oracle (Task 4 Step 9).
