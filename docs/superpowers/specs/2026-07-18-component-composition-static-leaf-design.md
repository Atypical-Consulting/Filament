# Component composition (static leaf) enters §5 — design

**Goal:** Admit **one-level-deep component composition with a static scalar parameter** into the
compiled subset — a leaf display child, resolved and inline-expanded at compile time, and gated
against Blazor's actual rendered DOM through the Playwright oracle.

**Status:** design approved by the owner through two decisions (slice + compilation model). Next step:
implementation plan.

---

## Context

Decision #77 named three open §5 false positives; #87 closed the first (double division). This closes
the `double`-division-sized core of the second: **component composition**. Today the generator refuses
*all* composition (`FIL0003: [component-composition]`) because it "has no compilation, so it cannot
resolve sibling `.razor` files into components" (`TemplateCompiler.cs:791`) — `<Greeting>` stays an
unknown markup element and is refused heuristically (upper-case initial).

§5 (recorded in the tests, since the spec is not on disk — DECISIONS #12/#17) admits *"component
composition with scalar parameters (one level deep is enough)."* This design admits the **minimal
faithful slice** of that: a **static** scalar parameter, a **leaf** child (no own state/events), **one
level** deep.

### Owner decisions locked before this spec

1. **Slice:** static leaf child — a leaf display child, static scalar param, one level deep. (Over:
   bound-param / stateful-child slices, which are deferred follow-ons.)
2. **Compilation model:** **inline compile-time expansion** — resolve the child, fold the static param
   to a compile-time constant, emit the child's element tree inline at the composition site. No child
   function, no import, no runtime instance. Honest framing: this is compile-time expansion, not a
   runtime component unit; the DECISIONS entry says so. The later slices (bound/stateful) will
   introduce the child-as-unit model when they need it.

---

## The IR the generator actually sees (verified)

`<div id="wrap"><Greeting Name="World" /></div>` lowers to (verified via `--dump-ir`):

```
MarkupElementIntermediateNode <div>
  HtmlAttributeIntermediateNode attr 'id' -> "wrap"
  MarkupElementIntermediateNode <Greeting>
    HtmlAttributeIntermediateNode attr 'Name'
      HtmlAttributeValueIntermediateNode htmlValue prefix=""
        LazyIntermediateToken [HTML] "World"
```

So `<Greeting>` is a **`MarkupElementIntermediateNode` named "Greeting"** carrying its attributes as
plain `HtmlAttributeIntermediateNode`s. The generator reads the tag name and the attribute values
directly — no tag-helper resolution, no compiled child type. This is the interception point:
`EmitElement`'s `LooksLikeComponent(el.TagName)` branch (`TemplateCompiler.cs:798`), which currently
refuses.

---

## The design

### 1. Resolution — same-directory convention

When `EmitElement` sees a component reference `<Greeting …>`, it resolves `Greeting.razor` as a
sibling of the **input `.razor` file** (the generator already knows the input's directory). If absent,
refuse with a new, located reason `unresolved-component` (a clear error, not the old blanket
`component-composition`). This is the lightest resolution that fits the single-file invocation
(`dotnet Filament.Generator <in.razor> <out.js>`) — no project or manifest.

### 2. The `[Parameter]` carve-out — single-sourced

For the **Blazor baseline itself to compile**, the child must declare `[Parameter] public string Name
{ get; set; }` — a property carrying an attribute. §5's `@code` refuses properties (#85) and any
member attribute (#77, `unsupported-attribute`). So composition **forces** a narrow carve-out:

- A **`[Parameter]`-attributed public auto-property of scalar type** (int/double/bool/string) is
  admitted, *only* in the parameter role. General properties and all other attributes stay refused.
- The carve-out lives in `Filament.Subset` (a `ConstructSubset.IsComponentParameter` predicate,
  consulted by `ClassifyMember` and by the generator's `CheckNoAttributes`), so the analyzer stops
  flagging `[Parameter]` props in the same edit the generator admits them — mutation-tested across the
  seam, as with division.

### 3. Compilation — inline expansion with a parameter environment

At the `<Greeting Name="World" />` site, the generator:

1. Reads the parent's attribute bindings: `{ Name: "World" }` (the literal HTML value).
2. Resolves and parses `Greeting.razor` (template + `@code`).
3. Extracts the child's `[Parameter]` props and their types; binds each to the parent value. For the
   **string** MVP the value is used verbatim; int/double/bool coercion is a disclosed follow-on
   (loud refusal until then — see Non-goals).
4. Compiles the child's template through the existing template machinery **with a parameter
   environment**: an identifier that resolves to a `[Parameter]` prop emits the **compile-time
   constant** the parent supplied (`Name` → the JS string literal `'World'`), not a signal read. A
   static param on a leaf child means the child's subtree is **fully static**.
5. Emits the child's element tree **inline** into the parent's current element (the child's `create`
   statements are spliced at the composition point).

**Reentrancy is the one real architectural addition.** Compiling the child means running the
frontend (`RazorFrontEnd` parse → `@code` via `CSharpFrontEnd` → template via `TemplateCompiler`) as a
nested, scoped compilation carrying the parameter environment. The top-level compile passes an empty
environment; a composition site passes the resolved bindings. This is bounded for a static leaf: no
new runtime primitive, no import, the child's output is static DOM.

### 4. The FIL-WIRING backstop still guards drift

The `component-composition` / `unresolved-component` refusals remain the fallback for anything outside
this slice (a child with state/events, a bound param, a nested composition, a non-scalar param). The
validate-then-translate discipline holds: only the admitted shape reaches the inline-expansion path.

---

## The measured artifact — a differential composition app

A **new isolated** Blazor app (division stays untouched; composition is the only variable).

- `baseline/Compose.Blazor/App.razor` (parent, DOM-contract): a wrapper containing
  `<Greeting Name="World" />`.
- `baseline/Compose.Blazor/Greeting.razor` (child, leaf): `<span id="greeting">Hello, @Name</span>`
  with `@code { [Parameter] public string Name { get; set; } }`.
- Both Blazor (`blazor-compose`) and Filament (`filament-compose-gen`) must render **`#greeting` =
  "Hello, World"** through the same DOM-contract oracle (correctness-only, no C1/C3/C4).

**Why this is a real measurement, not a tautology.** A leaf static composition has no interaction, so
the oracle checks the *initial composed DOM*. The question it answers: does Filament's **compile-time**
composition produce the same DOM Blazor produces at **runtime**? A generator that rendered
`Hello, @Name` literally, dropped the name, or emitted an empty `<greeting>` element renders a
different `#greeting` and the oracle catches it. `verifyContract` gains a `compose` branch asserting
`#greeting` reads "Hello, World"; `HARNESS_VERSION` 1.4.0 → 1.5.0 (disclosed, #31/#43/#59).

### Automated gates (in `dotnet test`)

| Test | Asserts |
|---|---|
| `ConstructSubsetTests` | a `[Parameter] public string Name { get; set; }` classifies as in-subset; a plain property and a non-`[Parameter]` attribute stay refused |
| `ConstructSubsetAnalyzerTests` | `[Parameter]` scalar prop → no diagnostic; plain property still flagged |
| `ComposeTests.Gate_GeneratedCompose_IsAlphaEquivalentToAnswerKey` | generator output ≡ `samples/Compose/compose.js` via `canon` |
| `ComposeTests.Snapshot_EmittedComposeJs_MatchesApprovedBytes` | byte-stable emission |
| `ComposeTests.EmittedCompose_InlinesTheChildWithTheFoldedParam` | emitted JS contains the child's span + the folded `'World'`, no unresolved `<greeting>` element |
| `GateSubsetTests` negative controls | static-leaf composition compiles clean; a child with state/events, a bound param (`Name="@x"`), a missing child, and a non-scalar param each **refuse, loud and located** |
| `DiagnosticTests` / `GateSubsetTests` | the old blanket `ComponentComposition_IsADisclosedFalsePositive` is reconciled — its in-subset case now compiles; genuinely-out cases (`<EditForm>`, cascading, generics) still refuse |

---

## What stays refused (non-goals of this slice)

- **Bound params** (`Name="@field"`) — deferred (needs parent→child reactive plumbing). Loud refusal.
- **Stateful/eventful child** (own signals, `@onclick`) — deferred. Loud refusal.
- **Nesting beyond one level** (the child itself composes) — deferred. Loud refusal.
- **Non-string scalar params** (`Count="3"` int/double/bool) — the coercion is a disclosed immediate
  follow-on *within* the composition family; refused loud until then.
- **`<EditForm>` / `RenderFragment` / cascading / generic components** — §3 non-goals; still refused,
  now by a rule that names them rather than the old blanket (the #77 quality point).

---

## Risks and open points

- **Reentrant compilation** is the main new surface. The child runs the full frontend with a parameter
  environment; the top-level path must be unaffected (the 181 generator gates are the parity net —
  Counter/Rows/If/IfElse must stay byte-identical).
- **DOM parity of the composed subtree.** Blazor may split `Hello, ` and `@Name` into separate text
  nodes and may wrap the component output with markers. The answer key is transcribed from the
  generator's actual output and validated Blazor-faithful by the oracle; the measurement is the
  authority, exactly as for division.
- **`LooksLikeComponent` heuristic vs resolution.** An upper-case tag with no sibling `.razor` must
  refuse `unresolved-component` (not silently emit `document.createElement('Greeting')`) — the old
  refusal's whole reason for existing.
- **In-session measurement** (playwright + WASM) worked for divide; expected to work here. If blocked,
  hand off with exact commands; automated gates stand regardless, and #88's "measured" claim waits.

---

## Decision journal

On completion, append **decision #88** to `DECISIONS.md` (French, house style): static-leaf component
composition enters §5 — same-directory resolution, the narrow `[Parameter]`-scalar carve-out
(single-sourced, mutation-tested), inline compile-time expansion with a parameter environment, the
`unresolved-component` reason, and the widening **measured** against Blazor through the #29/#30 oracle
(`#greeting` = "Hello, World"), `HARNESS_VERSION` bump disclosed. Honest ceiling: §5 widened by the
static-leaf slice of composition; bound/stateful/multi-scalar/nested remain open; RADICAL still "ni
éliminée ni établie".
