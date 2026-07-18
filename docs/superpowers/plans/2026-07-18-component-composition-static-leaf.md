# Static-leaf component composition — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Admit one-level-deep component composition with a static string parameter — a leaf display child, resolved same-directory and inline-expanded at compile time — and gate it against Blazor's rendered DOM via the Playwright oracle.

**Architecture:** `EmitElement`'s `LooksLikeComponent` refusal (TemplateCompiler.cs:798) becomes the interception point. A resolvable sibling `<Greeting>.razor` is parsed, its `[Parameter]` props bound to the parent's static attribute values, and its template walked inline into the parent's `_create` list with a **parameter environment** folding `@Name` to the compile-time constant. An unresolvable component refuses `unresolved-component`. The `[Parameter]` carve-out is single-sourced in `Filament.Subset`.

**Tech Stack:** C# / Roslyn (`Filament.Subset` netstandard2.0; `Filament.Generator` net10.0), xUnit, Node (`canon.mjs`), Playwright/CDP harness, Blazor WASM baseline.

## Global Constraints

- **Slice:** static string param, leaf child (no own state/events), one level deep, single root element. Anything else (bound param `@x`, numeric/bool param, child state/events, nested composition, multi-root child) **refuses, loud and located** — never silently.
- **Resolution:** same-directory — `<Greeting …>` resolves `Greeting.razor` beside the input file (`Path.GetDirectoryName(_file)`). Missing → `unresolved-component` (FIL0003), located, no file written.
- **The `[Parameter]` carve-out** is forced (the Blazor baseline needs it) and **single-sourced** in `Filament.Subset` (a predicate consulted by `ClassifyMember` and by the generator's `CheckNoAttributes`), so the analyzer follows and one edit reddens both a generator and an analyzer test. General properties (#85) and all non-`[Parameter]` attributes (#77) stay refused.
- **Inline compile-time expansion:** the child becomes static DOM spliced into the parent. No new runtime primitive, no import, no runtime component instance.
- **The 181 generator gates are the parity net** — Counter/Rows/If/IfElse/Divide must stay byte-identical (the reentrant compilation must not perturb the top-level path).
- **Answer keys never edited to pass** (#21/#51). **Measured, not reasoned** — the oracle (#29/#30) is the authority on Blazor-faithfulness. **Harness edits bump `HARNESS_VERSION`** 1.4.0 → 1.5.0, disclosed (#31/#43/#59).

---

## Task 1: The `[Parameter]` carve-out (single-sourced)

Admit a `[Parameter] public <scalar> Name { get; set; }` property — the parameter role only. Three sites (the exploration confirmed): `CheckNoAttributes` refuses the attribute *first*, then `ClassifyMember` refuses the property kind, then `Member` has no case.

**Files:**
- Modify: `src/Filament.Subset/ConstructSubset.cs` (new `IsComponentParameter` predicate + `ClassifyMember` arm)
- Modify: `src/Filament.Generator/CSharpFrontEnd.cs` (`CheckNoAttributes` ~715, `Member` ~731, new `ParamDecl`)
- Test: `tests/Filament.Subset.Tests/ConstructSubsetTests.cs`
- Test: `tests/Filament.Analyzer.Tests/ConstructSubsetAnalyzerTests.cs`

**Interfaces:**
- Produces: `Filament.Subset.ConstructSubset.IsComponentParameter(PropertyDeclarationSyntax) : bool` — Task 2 (child param extraction) relies on it.

- [ ] **Step 1: Write the failing subset unit test**

In `ConstructSubsetTests.cs`, add a member helper if absent (mirror `FirstMember`) and:

```csharp
    [Fact]
    public void ComponentParameter_ScalarProperty_ClassifiesToNull()
        => Assert.Null(ConstructSubset.ClassifyMember(
            FirstMember("[Microsoft.AspNetCore.Components.Parameter] public string Name { get; set; }")));

    [Fact]
    public void PlainProperty_WithoutParameterAttribute_StaysRefused()
    {
        var r = ConstructSubset.ClassifyMember(FirstMember("public string Name { get; set; }"));
        Assert.NotNull(r);
        Assert.Equal("unsupported-member", r!.Value.Reason);
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Filament.Subset.Tests --filter Parameter`
Expected: `ComponentParameter_ScalarProperty_ClassifiesToNull` FAILS (a property currently classifies as `unsupported-member`).

- [ ] **Step 3: Add the predicate + `ClassifyMember` arm**

In `src/Filament.Subset/ConstructSubset.cs`, add:

```csharp
    /// <summary>A [Parameter]-attributed property is admitted ONLY in the component-parameter role —
    /// a narrow carve-out from §5's no-properties (#85) / no-attributes (#77) rules. Syntactic: the
    /// scalar-type and auto-property shape are checked semantically in the generator's ParamDecl.</summary>
    public static bool IsComponentParameter(PropertyDeclarationSyntax p) =>
        p.AttributeLists.SelectMany(l => l.Attributes)
            .Any(a => a.Name.ToString() is "Parameter"
                or "Microsoft.AspNetCore.Components.Parameter"
                or "Components.Parameter");
```

(Add `using System.Linq;` if absent.) In `ClassifyMember`'s switch, add the arm **before** the `_ =>` default:

```csharp
        PropertyDeclarationSyntax p when IsComponentParameter(p) => null,
```

- [ ] **Step 4: Run to verify the subset tests pass**

Run: `dotnet test tests/Filament.Subset.Tests --filter Parameter`
Expected: PASS — `[Parameter]` prop → null; plain prop → `unsupported-member`.

- [ ] **Step 5: Add the analyzer test**

In `ConstructSubsetAnalyzerTests.cs`, add (the `Component`/`Method` helpers already stub `ComponentBase`):

```csharp
    [Fact]
    public async Task ComponentParameter_ScalarProperty_IsNotFlagged()
        => await Component(
            "    [Microsoft.AspNetCore.Components.Parameter] public string Name { get; set; }").RunAsync();

    [Fact]
    public async Task PlainProperty_IsStillFlagged()
        => await Component("    {|FIL0001:public string Name { get; set; }|}").RunAsync();
```

Add a `ParameterAttribute` stub to the test preamble's `ComponentBase` const if the analyzer's semantic model needs it to resolve (mirror the existing `ComponentBase` stub): `namespace Microsoft.AspNetCore.Components { public class ParameterAttribute : System.Attribute {} }`.

Run: `dotnet test tests/Filament.Analyzer.Tests --filter Parameter` — Expected PASS (`[Parameter]` no diagnostic; plain property flagged).

- [ ] **Step 6: Admit the attribute + property in the generator**

In `src/Filament.Generator/CSharpFrontEnd.cs`, change `CheckNoAttributes` (~715) so a `[Parameter]` attribute on a property is allowed, other attributes still refused:

```csharp
    bool CheckNoAttributes(MemberDeclarationSyntax member)
    {
        // A [Parameter] attribute on a component-parameter property is the one admitted attribute
        // (single-sourced with ClassifyMember); every other attribute stays refused (#77).
        if (member is PropertyDeclarationSyntax p && Filament.Subset.ConstructSubset.IsComponentParameter(p)
            && member.DescendantNodesAndSelf().OfType<AttributeListSyntax>()
                .SelectMany(l => l.Attributes)
                .All(a => a.Name.ToString().EndsWith("Parameter")))
            return true;

        if (member.DescendantNodesAndSelf().OfType<AttributeListSyntax>().FirstOrDefault() is not { } list)
            return true;

        Refuse("unsupported-attribute", /* unchanged message */ , list.SpanStart);
        return false;
    }
```

Add a `case PropertyDeclarationSyntax p: ParamDecl(p); break;` to `Member`'s switch (~740), and a new `ParamDecl` method that validates the scalar auto-property shape (model on the record-property check at ~799-813: `{ get; set; }`, `CheckType` on the property type) and registers a `PropInfo`-like parameter entry for later binding. It emits no top-level code (the value comes from the parent). On a bad shape (computed, init-only, non-scalar) it refuses `unsupported-member` at the property's span.

- [ ] **Step 7: Generator test — a `[Parameter]` prop no longer refuses on the member**

In `GateSubsetTests.cs` `NegativeControls`, add a control that a component declaring a `[Parameter]` scalar property (unused) compiles clean:

```csharp
    [Fact]
    public void Section5_ComponentParameterProperty_CompilesClean()
        => Compiles(
            """
            <p><span id="a">@count</span></p>

            @code {
                [Parameter] public string Label { get; set; } = "";
                private int count = 0;
            }
            """);
```

Run: `dotnet build src/Filament.Generator -c Debug && dotnet test tests/Filament.Generator.Tests --filter ComponentParameter` — Expected PASS.

- [ ] **Step 8: Full suites green, then commit**

Run each of `tests/Filament.Subset.Tests`, `tests/Filament.Analyzer.Tests`, `tests/Filament.Generator.Tests` — all PASS.

```bash
git add src/Filament.Subset/ConstructSubset.cs src/Filament.Generator/CSharpFrontEnd.cs \
        tests/Filament.Subset.Tests/ConstructSubsetTests.cs \
        tests/Filament.Analyzer.Tests/ConstructSubsetAnalyzerTests.cs \
        tests/Filament.Generator.Tests/GateSubsetTests.cs
git commit -m "feat(subset): admit [Parameter] scalar property (component-parameter carve-out, single-sourced)"
```

---

## Task 2: Composition core — resolve + reentrant inline expansion

The hard task: `EmitElement` resolves a sibling `<Greeting>.razor`, compiles it with a parameter environment, and splices its static tree into the parent. Unresolvable → `unresolved-component`. Atomic — a reviewer can't accept resolution without compilation.

**Files:**
- Create: `baseline/Compose.Blazor/App.razor`, `baseline/Compose.Blazor/Greeting.razor` (the `.razor` files the generator compiles; the Blazor project scaffolding is Task 3)
- Modify: `src/Filament.Generator/TemplateCompiler.cs` (`EmitElement` ~788, a new `EmitComposition`, resolution off `_file`)
- Modify: `src/Filament.Generator/CSharpFrontEnd.cs` (parameter environment: a `_paramEnv` field + an `IPropertySymbol` case in `Identifier` ~1767; a public way to construct a child front end with bindings)
- Modify: `tests/Filament.Generator.Tests/RepoPaths.cs`, `GateTests.cs` (`ComposeRazor`, `Generate.ComposeToTemp`)
- Modify: `tests/Filament.Generator.Tests/DiagnosticTests.cs`, `GateSubsetTests.cs` (reconcile the refusal reason)

**Interfaces:**
- Consumes: `IsComponentParameter` (Task 1).
- Produces: `RepoPaths.ComposeRazor`, `Generate.ComposeToTemp()` — Task 3 relies on them.

- [ ] **Step 1: Create the parent + child `.razor` fixtures**

`baseline/Compose.Blazor/App.razor`:

```razor
@* Root component. The markup is the SHARED DOM CONTRACT. Composition: <Greeting Name="World" />
   resolves to the sibling Greeting.razor, which renders <span id="greeting">Hello, World</span>. *@

<div id="wrap"><Greeting Name="World" /></div>
```

`baseline/Compose.Blazor/Greeting.razor`:

```razor
<span id="greeting">Hello, @Name</span>

@code {
    [Parameter] public string Name { get; set; } = "";
}
```

- [ ] **Step 2: Write the failing positive gate + wire paths**

In `RepoPaths.cs`:

```csharp
    /// <summary>Parent + sibling child (Greeting.razor) — the file Blazor compiles. Static-leaf composition.</summary>
    public static string ComposeRazor => Path.Combine(Root, "baseline", "Compose.Blazor", "App.razor");
    public static string ComposeAnswerKey => Path.Combine(Root, "samples", "Compose", "compose.js");
```

In `GateTests.cs` (`Generate` class): `public static string ComposeToTemp() => ToTemp(RepoPaths.ComposeRazor, "Compose");`
(Note: `ToTemp` writes its temp `.gen-*.js` into `samples/Compose/`; create that dir in Task 3 Step for the answer key, or `Directory.CreateDirectory` in a test helper. For Task 2, the temp output goes outside the repo per `ToTemp`.)

Add a positive generator test in a new `tests/Filament.Generator.Tests/ComposeTests.cs`:

```csharp
using Xunit;

namespace Filament.Generator.Tests;

public class ComposeTests
{
    /// <summary>Static-leaf composition: <Greeting Name="World" /> resolves the sibling, folds the
    /// static param, and inlines the child's span. No unresolved <greeting> element, no import.</summary>
    [Fact]
    public void EmittedCompose_InlinesTheChildWithTheFoldedParam()
    {
        var js = File.ReadAllText(Generate.ComposeToTemp());
        Assert.Contains("document.createElement('span')", js);   // the child's root, inlined
        Assert.Contains("greeting", js);                          // its id survives
        Assert.Contains("World", js);                             // the param folded to a constant
        Assert.DoesNotContain("createElement('Greeting')", js);   // NOT emitted as an unknown element
        Assert.DoesNotContain("Hello, @Name", js);                // NOT the literal expression
    }
}
```

Run: `dotnet build src/Filament.Generator -c Debug && dotnet test tests/Filament.Generator.Tests --filter EmittedCompose_Inlines` — Expected FAIL (composition currently refused).

- [ ] **Step 3: Add the parameter environment to `CSharpFrontEnd`**

Add a field `Dictionary<string, string> _paramEnv = new();` (property name → JS constant, e.g. `"Name" → "'World'"`). In `Identifier` (~1767), add a case **before** the `default`:

```csharp
        case IPropertySymbol ps when _paramEnv.TryGetValue(ps.Name, out var constJs):
            return constJs;   // a bound [Parameter] read folds to the parent-supplied constant
```

Expose a way to compile a child with bindings — e.g. a public method `void BindParameters(IReadOnlyDictionary<string,string> bindings)` that sets `_paramEnv`, called before the child's `Compile`. (`IsReactive` at ~1243 already treats a constant as non-reactive — no effect emitted.)

- [ ] **Step 4: Add `EmitComposition` and the resolution in `EmitElement`**

Replace the `LooksLikeComponent` refusal block in `EmitElement` (~798) with a dispatch:

```csharp
        if (LooksLikeComponent(el.TagName))
            return EmitComposition(el);
```

Add `EmitComposition(MarkupElementIntermediateNode el)`:
1. `var childPath = Path.Combine(Path.GetDirectoryName(_file)!, el.TagName + ".razor");` — if `!File.Exists(childPath)`, `Diag("unresolved-component", "<{TagName}> resolves to a same-directory component {TagName}.razor, which does not exist (framework components such as <EditForm> are §3 non-goals and have no sibling). Refusing to emit.", el.Source); return null;`
2. Read the parent's static attribute bindings from `el.Children.OfType<HtmlAttributeIntermediateNode>()` (reuse `EmitAttribute`'s static-value extraction, ~1125). Build `{ "Name" → JsString("World") }`. A dynamic attribute value (`Name="@x"`) → `Diag("unsupported-directive"/"bound-parameter", …)` (out of slice).
3. `var child = RazorFrontEnd.Parse(childPath);` Run the child directive/document gates (reuse `AccountForDocument`).
4. Compile the child `@code` into a fresh `CSharpFrontEnd`, calling `BindParameters(bindings)` first; validate the child is in-slice: single root element, no events/handlers, no signals lifted (leaf), only `[Parameter]` scalar props + no nested composition. Any violation → a located refusal (`composition-out-of-subset`), no file.
5. Walk the child's single root element with `_code` **temporarily swapped** to the child front end (save/restore, exactly like `EmitBranchFn` swaps `_create`/`_bindings` at ~969-983): the child's `create` statements append into the parent's `_create`, and `@Name` folds to `'World'` via `_paramEnv`. Return the child root element's var so the parent inserts it.

Model the save/restore on `EmitBranchFn` (TemplateCompiler.cs:969-983). The child's root is found by taking the child IR's render method's single `MarkupElementIntermediateNode` child (refuse if not exactly one).

- [ ] **Step 5: Reconcile the existing refusal tests**

`<SomeWidget/>` (Component.razor) and `<EditForm>` (Forms.razor) have no sibling → now `unresolved-component`:

- `DiagnosticTests.ComponentComposition_IsRefused_AtItsExactLocation`: change the expected reason to `unresolved-component` and re-title (verify the exact `(line,col)` from the run — the refusal now fires in `EmitComposition`, likely the same span).
- `GateSubsetTests` `[InlineData("Gate/Forms.razor", …, "component-composition")]`: change reason to `unresolved-component` (verify line/col).
- `GateSubsetTests.ComponentComposition_IsADisclosedFalsePositive_ButIsLoudAndLocated`: rewrite like the division reconciliation — static-leaf composition now compiles (covered by `ComposeTests`); this fixture's `<MyWidget Count="3" />` (no sibling, int param) still refuses, now `unresolved-component`. Rename to `UnresolvedComponent_IsRefused_LoudAndLocated`, assert `[unresolved-component]`, keep no-file + located.

- [ ] **Step 6: Reconcile answer key / snapshot bytes against actual output**

Run: `dotnet run --project src/Filament.Generator -- baseline/Compose.Blazor/App.razor /tmp/compose.g.js` (from a repo-relative path so the runtime resolves) and read `/tmp/compose.g.js`. Confirm it inlines `<span id="greeting">` with the folded text and NO `Greeting` element. This output is the reference for Task 3's answer key + snapshot.

- [ ] **Step 7: Full generator suite green (the parity net), then commit**

Run: `dotnet test tests/Filament.Generator.Tests` — Expected PASS, **181 prior gates unchanged** (Counter/Rows/If/IfElse/Divide byte-identical) + `ComposeTests` + reconciled refusals green. If any prior gate reddened, the reentrant swap perturbed the top-level path — fix before proceeding (do not adjust the gate).

```bash
git add baseline/Compose.Blazor/App.razor baseline/Compose.Blazor/Greeting.razor \
        src/Filament.Generator/TemplateCompiler.cs src/Filament.Generator/CSharpFrontEnd.cs \
        tests/Filament.Generator.Tests/RepoPaths.cs tests/Filament.Generator.Tests/GateTests.cs \
        tests/Filament.Generator.Tests/ComposeTests.cs tests/Filament.Generator.Tests/DiagnosticTests.cs \
        tests/Filament.Generator.Tests/GateSubsetTests.cs
git commit -m "feat(compose): static-leaf component composition — same-dir resolve + inline expansion + param env"
```

---

## Task 3: The measured artifact — Blazor baseline, answer key, alpha-equivalence gate

**Files:**
- Create: `baseline/Compose.Blazor/_Imports.razor`, `Program.cs`, `Compose.Blazor.csproj`, `wwwroot/index.html`, `wwwroot/css/app.css`
- Create: `samples/Compose/compose.js` (answer key), `tests/Filament.Generator.Tests/Snapshots/Compose.approved.js`
- Modify: `tests/Filament.Generator.Tests/ComposeTests.cs` (add the alpha-equivalence + snapshot gates)

**Interfaces:** Consumes `ComposeRazor`/`ComposeToTemp` (Task 2); `ComposeAnswerKey` (Task 2 declared it).

- [ ] **Step 1: Blazor project scaffolding** (mirror `Divide.Blazor` — `_Imports.razor`, `Program.cs` with `using Compose.Blazor;` + `RootComponents.Add<App>("#app")`, `Compose.Blazor.csproj` with the identical size PropertyGroup incl. `InvariantGlobalization`, `wwwroot/index.html` titled "Compose", `wwwroot/css/app.css` copied from `Counter.Blazor`).

- [ ] **Step 2: Validate the Blazor app compiles**

Run: `dotnet build baseline/Compose.Blazor -c Release` — Expected 0 errors (proves `<Greeting Name="World" />` + `[Parameter] Name` are valid Blazor; the whole measurement rests on it).

- [ ] **Step 3: Add the alpha-equivalence + snapshot gates to `ComposeTests`**

```csharp
    [Fact]
    public void Gate_GeneratedCompose_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.ComposeToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.ComposeAnswerKey);
        Assert.True(exit == 0, "composition gate FAILED — not alpha-equivalent to samples/Compose/compose.js.\n" + stdout + stderr);
    }

    [Fact]
    public void Snapshot_EmittedComposeJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.ComposeToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "Compose.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
```

- [ ] **Step 4: Transcribe the answer key from the generator's actual output**

Create `samples/Compose/compose.js` by transcribing the generator's inline-expanded output (from Task 2 Step 6), readable local names, the folded `'World'` text, the `\n\n` whitespace nodes App.razor implies. It is the Blazor-faithful reference; canon confirms alpha-equivalence. If canon disagrees, fix `compose.js` to match the generator's faithful output (not the reverse).

- [ ] **Step 5: Approve the snapshot + verify the gate**

Run `dotnet test tests/Filament.Generator.Tests --filter ComposeTests` twice (first writes `Compose.approved.js`; review it — it must carry the generator banner, the inlined span, the folded `World`, and no `Greeting` element — then re-run). Expected: all `ComposeTests` PASS.

- [ ] **Step 6: Commit**

```bash
git add baseline/Compose.Blazor samples/Compose/compose.js \
        tests/Filament.Generator.Tests/ComposeTests.cs tests/Filament.Generator.Tests/Snapshots/Compose.approved.js
git commit -m "test(compose): Blazor baseline + answer key + alpha-equivalence gate for static-leaf composition"
```

---

## Task 4: Harness oracle + measure

**Files:**
- Modify: `bench/harness/bench.mjs` (`HARNESS_VERSION` 1.4.0→1.5.0, `APPS.compose`, `verifyContract` compose branch)
- Modify: `bench/build-filament.sh` (`filament-compose-gen` label across the dispatchers), `.gitignore`
- Create: `samples/filament-compose-gen/main.js`
- Modify/Create: `BENCH.md` (append-only entry n°10)

- [ ] **Step 1: Harness — version bump, app shape, oracle branch**

Bump `HARNESS_VERSION` to `'1.5.0'` (comment: `1.5.0: added the 'compose' contract (static-leaf composition)`). Add `compose: { readySelector: '#greeting', observeSelector: '#greeting', scenarios: [] }` to `APPS`. Add a `compose` branch to `verifyContract` after the `divide` branch:

```javascript
    if (app === 'compose') {
      return ctx.page.evaluate(() => {
        const out = { problems: [], observed: {} };
        const el = document.querySelector('#greeting');
        if (!el) { out.problems.push('missing required element: #greeting'); return out; }
        out.observed.text = el.textContent.trim();
        // Static-leaf composition: the child renders "Hello, World". A generator that dropped the
        // param or emitted the literal @Name renders something else, and this catches it.
        if (out.observed.text !== 'Hello, World')
          out.problems.push(`#greeting is "${out.observed.text}", expected "Hello, World"`);
        return out;
      });
    }
```

- [ ] **Step 2: build-filament.sh — the label**

Add `filament-compose-gen` to `ALL_LABELS` and a case to `project_for` (`samples/filament-compose-gen`), `mode_for` (`production`), `razor_for` (`baseline/Compose.Blazor/App.razor`), `generated_js_for` (`App.g.js` — note the input is `App.razor`, so the emitted file is `App.g.js`; confirm the entry `main.js` imports `./App.g.js`), `title_for` (`Compose`), `blazor_label_for` (`blazor-compose`), `css_for` (`baseline/Compose.Blazor/wwwroot/css/app.css`). Create `samples/filament-compose-gen/main.js` importing `./App.g.js` and mounting; add `samples/filament-compose-gen/App.g.js` to `.gitignore`.

- [ ] **Step 3: Syntax-check + label resolves**

Run: `node --check bench/harness/bench.mjs && bash -n bench/build-filament.sh && ./bench/build-filament.sh --list | grep filament-compose-gen` — Expected clean + label listed. Commit the harness wiring.

- [ ] **Step 4: Run the measurement (attempt; hand off if blocked)**

```
dotnet publish baseline/Compose.Blazor -c Release -o bench/publish/blazor-compose
./bench/build-filament.sh filament-compose-gen
node bench/harness/bench.mjs --dir bench/publish/blazor-compose/wwwroot --app compose --label blazor-compose        --headless --contract-only
node bench/harness/bench.mjs --dir bench/publish/filament-compose-gen  --app compose --label filament-compose-gen  --headless --contract-only
```

Expected: both print `DOM contract OK: {"text":"Hello, World"}`. A mismatch (e.g. Blazor renders `Hello, World` but Filament renders `Hello, ` or `Hello, @Name`) is the measurement catching a composition bug. If browser/WASM is blocked, hand off with these exact commands; the automated gates stand and #88's "measured" claim waits.

- [ ] **Step 5: BENCH.md entry n°10 (append-only)**

Append entry n°10 (French, house style): the `compose` correctness result (both render `#greeting` = "Hello, World"), the `HARNESS_VERSION` 1.4.0→1.5.0 bump, the honest note (correctness-only; static-leaf slice; the composed-DOM question this answers). If handed off, record once the owner reports. Commit.

---

## Task 5: Record the decision and update memory

- [ ] **Step 1: Append decision #88 to DECISIONS.md** (French, house style): static-leaf component composition enters §5 — same-directory resolution + `unresolved-component`; the `[Parameter]`-scalar carve-out (single-sourced across `ClassifyMember`/`CheckNoAttributes`, mutation-tested); inline compile-time expansion with a parameter environment folding `@Name` to a constant; the reentrant child compilation (`_code` swap, the `EmitBranchFn` idiom) with the 181 gates as parity net; measured against Blazor via the #29/#30 oracle (`#greeting` = "Hello, World"), `HARNESS_VERSION` 1.5.0 disclosed. Honest ceiling: only the static-leaf/string slice of composition; bound params, numeric/bool params, stateful children, nested composition remain OPEN; RADICAL still "ni éliminée ni établie". If handed off, say so.

- [ ] **Step 2: Update memory** — record composition (static-leaf) shipped in `double-division-widened-subset.md` or a new project memory; note the remaining composition sub-slices and root-level control flow as the next widenings. Update `MEMORY.md`.

- [ ] **Step 3: Commit** `git commit -m "docs: record decision #88 (static-leaf component composition enters §5)"`

---

## Self-review notes

- **Spec coverage:** carve-out (T1) · resolution + `unresolved-component` (T2) · reentrant inline expansion + param env (T2) · reconcile Component/Forms/disclosed-false-positive (T2) · Blazor baseline + alpha-equiv + snapshot (T3) · oracle + HARNESS_VERSION + measure (T4) · decision #88 (T5). All covered.
- **Green at every commit:** T1 admits the member additively; T2 is atomic (resolution + compilation + reconciliation land together, 181 gates verified); T3/T4 additive. No red commit.
- **Type consistency:** `IsComponentParameter(PropertyDeclarationSyntax)` (T1) consumed by `CheckNoAttributes` (T1) and child param extraction (T2); `_paramEnv` + the `Identifier` `IPropertySymbol` case (T2); `ComposeRazor`/`ComposeAnswerKey`/`ComposeToTemp` declared in T2, used in T3.
- **The reentrant compilation is the risk;** its verification is the 181-gate parity net (T2 Step 7) plus the measured oracle (T4). Honest framing preserved: compile-time expansion, not a runtime component unit.
- **Hand-off honesty:** T4 never lets the automated gates masquerade as the measurement; #88's "measured" claim waits for the oracle.
