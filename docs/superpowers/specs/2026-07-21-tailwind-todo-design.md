# Tailwind validation — the Todo app program

**Goal.** Prove "Filament works with Tailwind" the only way this repo proves anything: a
Blazor-authored todo-list styled entirely with Tailwind utility classes, compiled by the
unchanged pipeline, measured DOM-identical against real Blazor by the oracle — plus the
Tailwind *build* wired into a runnable example app.

**Why this is not trivial.** Tailwind's surface syntax is hostile to naive attribute
handling: variant colons (`hover:bg-amber-400`), fraction slashes (`w-1/2`,
`bg-cyan-500/75`), arbitrary-value brackets (`max-w-[42rem]`, `md:grid-cols-[1fr_2fr]`),
leading dashes (`-mt-2`), and *many tokens per attribute*. A todo list additionally needs a
class that *changes* on a persisting list row (`line-through` on toggle) — reactive
attribute machinery inside `list()` rows.

## Ground truth (probed 2026-07-21, before design)

Scratchpad probes against the built generator, not guesses:

| Probe | Result |
|---|---|
| Static exotic classes, top level (`w-1/2 hover:… max-w-[42rem] -mt-2`, 7 tokens) | **compiles but WRONG** — emitted `'w-1/2hover:bg-amber-400focus:ring-2…'`: inter-token whitespace DROPPED (defect **D1**) |
| Static exotic classes, 2 tokens (`bg-cyan-500/75 disabled:opacity-50`) | correct (space kept) — the split trigger must be characterized |
| Mixed literal+expr class (`rounded @cls px-4`), field-reactive | correct: `effect(() => setAttr(el,'class','rounded ' + cls.value + ' px-4'))` |
| Reactive class inside a `@foreach` row, from a FIELD | compiles; emitted once at create via `setAttr` |
| Reactive class inside a `@foreach` row, from the LOOP VARIABLE (`@(t.Done ? … : …)`) | **refused** — `[unsupported-expression] 't.Done' is not member access on a record declared in this component` (the attribute-expression path doesn't know the loop variable) (widening **W1**) |
| One field both mutated (`items.Add`) and reassigned (`items = items.Where(…).ToList()`) | **compiles to throwing JS** — `const items` then `items = items.filter(…)` → TypeError (defect **D2**; valid Blazor C#, so this is a §10 violation: neither faithful nor refused) |
| Row text bound to a mutated record property (the Duel) | already live: `effect(() => setText(_tx2, t.done.value ? 'done' : 'todo'))` — the per-record-signal machinery W1 must reuse |
| Composition (string params, EventCallback, ChildContent), children = leaf display | proven (#88/#90/#130/#131); children may carry their own static Tailwind classes |

## Deliverables (waves)

### Wave 1 — D1: whitespace-faithful static class values (defect fix)

The static-attribute concatenation (`TemplateCompiler` — the
`string.Concat(...OfType<HtmlAttributeValueIntermediateNode>()...)` shape, wherever it
occurs: static path, composition bindings at ~1698, and the mixed fold's literal parts if
affected) must reconstruct the attribute value *including each value node's whitespace
prefix*, so the emitted string equals the authored string byte-for-byte.

- Characterize first: a test fixture pinning WHICH shapes Razor splits into multiple
  value nodes (the 7-token exotic line from the probe is a witness).
- TDD: `Supported/Code/TailwindClasses.razor` (exotic multi-token static classes at top
  level, in a row, and on a composed child) + emission pins asserting the emitted class
  strings equal the source strings.
- Existing approved snapshots must stay byte-identical (no current witness carries a
  multi-token static class that hits the split — verify, don't assume).
- Runtime frozen (generator-only).

### Wave 2 — W1: reactive class on a list row from the loop variable (widening)

`<li @key="t.Id" class="flex gap-2 @(t.Done ? "line-through …" : "…")">` inside
`@foreach` compiles to the attribute analogue of the Duel's row-text effect:

```js
effect(() => setAttr(_el, 'class', 'flex gap-2 ' + (t.done.value ? 'line-through …' : '…')));
```

- Reuses: the mixed-fold ComposableValue (#96), the per-record signal lifting the Duel's
  row text already uses, and `setAttr` (runtime ships it — generator-only).
- The stale-row trap is the measurement: `list()` reuses a persisting keyed row, so a
  class computed at create time would freeze; the oracle must toggle a PERSISTING row and
  see the class flip (the #125 lesson, applied to attributes).
- Boundary held: a class expression using anything outside the admitted expression subset
  still refuses with a located diagnostic.
- Fixture flips from Unsupported → Supported only if a prior Unsupported witness covered
  it (none does — this is new surface, so a new Supported witness + gate boundary
  fixture).

### Wave 3 — D2: the mutate+reassign field (defect: refuse or make faithful)

One field that is both version-bumped (mutation) and reassigned currently emits
`const` + assignment — code that throws. Decide in-wave after reading the const/let
decision site:

- **Faithful** if cheap: such a field becomes `let` + its reassignment also bumps the
  version signal (or is folded into the signal model consistently).
- **Refuse** otherwise: a located diagnostic naming the mixed idiom and the two supported
  shapes (mutate-only + version, or reassign-only signal — the Duel's `tasks`/`visible`
  split). Refusal is honest; throwing JS is not.
- Either way: a gate/witness test pins the behavior; `dotnet build` the Blazor baseline
  of the shape FIRST to confirm it is valid authored C#.

### Wave 4 — the Todo app, measured (BENCH entry)

`baseline/Todo.Blazor` — a Blazor WASM todo-list, Tailwind classes throughout, three
Razor components:

- **App.razor** (all state; the Duel idiom: mutate `todos`, recompute `visible` by
  reassignment): plain `<input @bind="newText">` (#104 — NOT EditForm: Blazor's
  `InputText` injects validation classes that would poison the class-equality contract)
  + add button; keyed `@foreach` over `visible`; per-row toggle button and remove button
  (captured lambdas, #141); remaining/total counts (LINQ); the row carries
  `class="… @(t.Done ? "line-through text-…" : "text-…")"` — W1 live; exotic static
  classes from the D1 probe on the shell markup — D1 live.
- **TodoShell.razor** (leaf child): `[Parameter] string Title` +
  `[Parameter] RenderFragment? ChildContent` — the Tailwind-styled card the whole app
  renders inside (#131 live under Tailwind).
- **TodoFooter.razor** (leaf child): `[Parameter] string Left` (reactive bound string,
  #90) + `[Parameter] EventCallback OnClear` (#130) — remaining-count text + a
  clear-done button raising the parent's method.

Discipline: `dotnet build` the baseline FIRST; `samples/Todo/todo.js` is the
hand-written answer key; generator tests pin gate + emission (row class effect, child
inlining, class strings verbatim); `bench.mjs` gains the `todo` contract
(HARNESS 1.58.0): add ×2 → toggle the FIRST row (persisting key: class must gain
`line-through`, counts move) → clear-done via the child's button (EventCallback) →
remove → empty state; every `class` attribute asserted byte-identical vs Blazor.
`bench/build-filament.sh` gains the `todo` tables. Contract-only invocation (like
`httpjson`). Accessibility per the repo's authoring bar: semantic elements
(`<ul>/<li>/<button>`), labels on inputs.

### Wave 5 — the Tailwind build + the example app (DX)

- Root `package.json`: `tailwindcss` + `@tailwindcss/cli` (v4, pinned exact, like
  esbuild). CI's existing root `npm ci` covers installation on the 3 OSes.
- `examples/TodoTailwind/` — a runnable app modeled on `examples/FilamentApp` (csproj:
  generator ProjectReference + CompileFilament target; App.razor + TodoShell.razor +
  TodoFooter.razor siblings; `<Watch>` on all three): plus a Tailwind step —
  `tailwind.css` input with `@import "tailwindcss";` + `@source "./*.razor";`, an
  MSBuild target invoking `npx @tailwindcss/cli` to build `wwwroot/app.css`, and
  `index.html` linking it. `examples/FilamentApp` stays untouched (it mirrors the
  `dotnet new filament` template; the todo app is the showcase, the counter is the
  template mirror).
- **Scanner-coverage proof** (the honest part): because the generator emits class strings
  verbatim, scanning `.razor` covers the emitted JS. Prove it — a node script (in the
  example, run by a test or CI step) extracting every class token from the built
  `App.g.js` and asserting each appears in the built `app.css`. Conditional utilities
  written as full class names inside ternaries are literals in the `.razor`, so the
  scanner sees them; the proof would catch any future dynamic-composition regression
  (Tailwind's own "never construct class names dynamically" rule, enforced).
- Docs: `docs/REAL-APPS.md` gains "Styling with Tailwind" (the recipe + the
  full-class-names rule); example README; root README counts + example row; French
  `DECISIONS.md` entries (D1 fix, W1 widening, D2 verdict, the app, the build wiring)
  and append-only French `BENCH.md` n°65 (+ HARNESS 1.58.0 bump disclosed).

## Non-goals

- No website page for the todo app (the site's evidence pages are the Duel and the
  Playground; a follow-up could add it).
- No Tailwind theme/config surface (v4 zero-config defaults; the validation is the
  pipeline, not a design system).
- No `@apply`/component-classes story, no CSS isolation (ADR-covered non-goal).
- No template (`dotnet new`) change — the Tailwind recipe is documented, not scaffolded
  (a follow-up could add a `--tailwind` template option).

## Invariants (unchanged from every prior program)

Runtime firewall: `git diff -- src/filament-runtime` empty. Every slice measured vs
Blazor via the oracle before it ships. Baselines `dotnet build` FIRST. Witnesses that
flip refused → supported move to `Supported/`. Commits
`type(scope): … (DECISIONS #N, BENCH n°N)`. CI 3-OS green before push is declared done.
