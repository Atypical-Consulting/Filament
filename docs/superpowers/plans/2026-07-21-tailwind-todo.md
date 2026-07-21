# Tailwind Validation Program — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove Filament works with Tailwind: fix the two probe-found defects (D1 whitespace
loss, D2 mutate+reassign mis-compile), widen reactive row classes (W1), ship a measured
Tailwind todo-list (baseline + answer key + oracle contract), and wire the Tailwind v4 build
into a runnable example app.

**Architecture:** Generator-only changes (runtime frozen). The todo app is the single
measurement vehicle for D1+W1 (Duel precedent: one app-level contract). The example app
mirrors `examples/FilamentApp` plus a Tailwind CSS build step and a scanner-coverage proof.

**Tech Stack:** Existing repo toolchain + `tailwindcss`/`@tailwindcss/cli` v4 (pinned exact,
root package.json, like esbuild).

## Global Constraints

- Runtime firewall: `git diff -- src/filament-runtime` stays EMPTY.
- Every slice measured vs Blazor via `bench/harness/bench.mjs` before it ships.
- Blazor baselines `dotnet build` FIRST (before generator work against them).
- French `DECISIONS.md` entries (next: **#151**), append-only French `BENCH.md` (next:
  **n°65**), HARNESS bump **1.57.0 → 1.58.0** disclosed in the changelog string.
- Commits: `type(scope): summary (DECISIONS #N, BENCH n°N)`.
- Existing approved snapshots stay byte-identical unless a test PINS the change on purpose.
- Semantic HTML + ARIA where interactive (repo authoring bar).

---

### Task 1 — D1: whitespace-faithful static attribute values

**Files:**
- Modify: `src/Filament.Generator/TemplateCompiler.cs:2231` (static path) and `:1698`
  (composition binding path)
- Create: `tests/Filament.Generator.Tests/Supported/Code/TailwindClasses.razor`,
  `Supported/Code/TwCompose.razor` + `Supported/Code/TwCard.razor` (sibling child)
- Test: `tests/Filament.Generator.Tests/TailwindTests.cs` (new file)

**Interfaces:** none new — the emitted class string simply equals the authored string.

- [ ] **Step 1: fixtures.** `TailwindClasses.razor`: the probe's 7-token exotic line on a
  static `<h1 id="title">`, a 2-token line on a `<button>`, and a keyed `@foreach` row
  carrying a multi-token static class (`class="flex w-1/2 hover:bg-amber-400 max-w-[42rem]"`)
  — Duel idiom state so it compiles today. `TwCompose.razor`:
  `<TwCard Label="alpha beta" Cls="rounded px-4 hover:bg-amber-400" />` with `TwCard.razor`
  a leaf child `<span id="card" class="@Cls">@Label</span>` (+ `[Parameter] string Label/Cls`).
- [ ] **Step 2: failing tests.** `TailwindTests.cs`:
  - `StaticExoticClasses_KeepTheirSpaces`: Run.Generator on TailwindClasses.razor, assert
    emitted contains `'w-1/2 hover:bg-amber-400 focus:ring-2 max-w-[42rem] sm:px-4 -mt-2 md:grid-cols-[1fr_2fr]'`
    and does NOT contain `'w-1/2hover:`.
  - `ComposedChildClassParam_KeepsItsSpaces`: TwCompose emitted contains
    `'rounded px-4 hover:bg-amber-400'`.
  Run → both FAIL (concatenated string, no spaces).
- [ ] **Step 3: fix.** Both sites become prefix-aware, mirroring `ComposeAttributeValue`:
  ```csharp
  var value = string.Concat(html.Select(h =>
      h.Prefix + string.Concat(h.Children.OfType<IntermediateToken>().Select(t => t.Content))));
  ```
  (at 1698 the same shape over `attr.Children.OfType<HtmlAttributeValueIntermediateNode>()`).
- [ ] **Step 4: green + no-regression.** `dotnet test tests/Filament.Generator.Tests` —
  new tests pass, every approved snapshot byte-identical (no existing witness hits the
  multi-node split; the run proves it).
- [ ] **Step 5: commit** `fix(attr): static attribute values keep their whitespace -- Razor splits multi-token values into prefixed nodes (DECISIONS #151)`.

### Task 2 — W1: reactive class on a list row from the loop variable

**Files:**
- Modify: `src/Filament.Generator/TemplateCompiler.cs` (`EmitAttribute` ~2182: route the
  reactive fold into the ROW create context instead of `_bindings` when inside a row;
  the slot for an attribute expression inside a `@foreach` body must compile with the loop
  variable bound — find the seam the row TEXT slots use, `effect(() => setText(_tx, t.done.value …))`
  proves it exists)
- Possibly modify: `src/Filament.Generator/CSharpFrontEnd.cs` (slot harvest/scope)
- Create: `tests/Filament.Generator.Tests/Supported/Code/RowClass.razor`;
  `Unsupported/Code/RowClassCall.razor` (boundary)
- Test: `TailwindTests.cs` additions

- [ ] **Step 1: characterize the second trap.** Probe (scratchpad): `class="x @cls"` inside
  a row where `cls` is a SIGNAL field — today the reactive branch lands in `_bindings`
  referencing a row-local element (broken scope). Pin whatever it does in the task notes;
  the routing fix must cover it.
- [ ] **Step 2: fixture.** `RowClass.razor` — Duel idiom (`tasks` mutated + `visible`
  reassigned), keyed row:
  `<li @key="t.Id" class="flex gap-2 @(t.Done ? "line-through text-slate-400" : "text-slate-900")">`
  plus a toggle button per row. `RowClassCall.razor` — same shape but
  `@(t.Label.Trim())` in the class value → must refuse (call not in subset) at its span.
- [ ] **Step 3: failing tests.**
  - `RowClass_LoopVarTernary_CompilesToARowEffect`: emitted createT contains
    `effect(() => setAttr(` and `t.done.value ? 'line-through text-slate-400' : 'text-slate-900'`,
    and the effect is INSIDE the row create function (assert it appears after
    `function createT` and before the function's closing brace).
  - `RowClass_FieldSignal_EffectLivesInTheRow` (from Step 1's probe shape).
  - `RowClassCall_RefusesAtItsSpan`: exit 1 + `unsupported-` diagnostic.
- [ ] **Step 4: implement.** Bind the loop variable for attribute-value slots (same
  compilation context as row text slots); in `EmitAttribute`, reactive fold → emit into the
  current create list when walking a row (the routing `EmitBinding` uses), `_bindings`
  otherwise. Runtime untouched (`setAttr`/`effect` ship).
- [ ] **Step 5: green + snapshots byte-identical** (top-level attr emission unchanged).
- [ ] **Step 6: commit** `feat(attr): reactive class on a list row -- the loop variable reaches attribute expressions, the effect lives in the row (DECISIONS #152)`.

### Task 3 — D2: one field both mutated and reassigned → refuse

**Files:**
- Modify: `src/Filament.Generator/CSharpFrontEnd.cs` (List classification, the
  `f.List is { } li && !f.IsSignal` seam at ~2121 — the refusal belongs where mutation and
  reassignment are both known)
- Create: `tests/Filament.Generator.Tests/Unsupported/Code/ListMutateReassign.razor`
- Test: `TailwindTests.cs` addition

- [ ] **Step 1: baseline validity.** Scratchpad Blazor project with the shape
  (`items.Add(t)` in one method, `items = items.Where(x => true).ToList()` in another) —
  `dotnet build` MUST succeed (valid authored C#; the lesson).
- [ ] **Step 2: fixture + failing test.** `ListMutateReassign.razor` with that shape;
  `ListMutateReassign_RefusesTheMixedIdiom`: exit 1, diagnostic names BOTH idioms and the
  split fix; today it exits 0 emitting `const items` + `items = …` (the throwing module) —
  the test fails before the fix.
- [ ] **Step 3: implement.** Where the List's `Mutated` and the field's reassignment are
  both visible, refuse at the reassignment's span:
  `"field '{name}' is both structurally mutated (Add/RemoveAt/Clear/element write) and reassigned. One field supports one idiom: mutate + version (rows.js) or reassign-as-signal (decision 140) -- split it into two fields (the Duel's tasks/visible). Refusing to emit: the mixed emission declared 'const' and assigned to it, a module that throws."`
  (If inspection shows the FAITHFUL fix is ≤ the refusal's complexity — `let` + version
  bump on reassign — prefer it and adjust the test to pin the emission instead; decide
  in-task, record in DECISIONS.)
- [ ] **Step 4: green + full suite.**
- [ ] **Step 5: commit** `fix(state): a List both mutated and reassigned refused -- was emitted as const+assignment, a module that throws (DECISIONS #153)`.

### Task 4 — the Todo app, measured (BENCH n°65, HARNESS 1.58.0)

**Files:**
- Create: `baseline/Todo.Blazor/` (`Todo.Blazor.csproj`, `Program.cs`, `_Imports.razor`,
  `App.razor`, `TodoShell.razor`, `TodoFooter.razor`, `wwwroot/index.html`) — copy
  `baseline/ForeachList.Blazor` scaffolding (`rm -rf obj bin` after copy), rename project.
- Create: `samples/Todo/todo.js` (answer key), `samples/filament-todo-gen/main.js`
- Modify: `tests/Filament.Generator.Tests/RepoPaths.cs` (+Todo paths),
  `bench/harness/bench.mjs` (registry + driver + HARNESS 1.58.0),
  `bench/build-filament.sh` (8 tables)
- Test: `tests/Filament.Generator.Tests/TodoTests.cs`

**App shape (the DOM contract, both sides):**
- `App.razor`: all state. `<TodoShell Title="todos">…</TodoShell>` wraps everything.
  Inside: `<input id="new" aria-label="New todo" @bind="newText" class="w-1/2 rounded border px-3 py-2 focus:ring-2" />`
  `<button id="add" @onclick="Add" class="rounded bg-amber-500 px-4 py-2 hover:bg-amber-400 disabled:opacity-50">add</button>`;
  `<ul id="list">` keyed `@foreach (Item t in visible)`; row
  `<li @key="t.Id" class="flex gap-2 max-w-[42rem] @(t.Done ? "line-through text-slate-400" : "text-slate-900")"><span class="grow">@t.Label</span><button class="toggle rounded px-2 hover:bg-slate-200" @onclick="() => Toggle(t.Id)">toggle</button><button class="remove rounded px-2 hover:bg-red-200" @onclick="() => Remove(t.Id)">remove</button></li>`
  (single-line rows, no inter-sibling whitespace — Rows contract);
  `<TodoFooter Left="@leftText" OnClear="@ClearDone" />`.
  `@code`: mutable record `Item { Id, Label, Done }`; `tasks` mutated, `visible` + counts
  recomputed by reassignment in `Refresh()` (Duel idiom); `leftText = active + " left"`;
  `ClearDone` removes done items (reverse for + RemoveAt); plain-input `@bind` (#104), NOT
  EditForm (InputText injects validation classes → would poison class equality).
- `TodoShell.razor` (leaf): `[Parameter] string Title`, `[Parameter] RenderFragment? ChildContent`;
  `<section id="shell" class="mx-auto max-w-[42rem] rounded-xl p-6 shadow-lg sm:px-4 md:grid-cols-[1fr_2fr]"><h1 id="title" class="text-2xl font-bold -mt-2">@Title</h1>@ChildContent</section>`
  (carries the D1 probe's exotic tokens).
- `TodoFooter.razor` (leaf): `[Parameter] string Left`, `[Parameter] EventCallback OnClear`;
  `<footer id="footer" class="flex justify-between border-t pt-2"><span id="left" class="text-sm text-slate-500">@Left</span><button id="clear" class="text-sm hover:underline" @onclick="OnClear">clear done</button></footer>`.

- [ ] **Step 1: baseline builds.** Author the three .razor + scaffolding;
  `dotnet build baseline/Todo.Blazor` → success BEFORE any generator run.
- [ ] **Step 2: generator compiles it.** `Run.Generator`-equivalent CLI on App.razor →
  exit 0 (Tasks 1–2 make it so). Inspect emitted module.
- [ ] **Step 3: answer key.** Write `samples/Todo/todo.js` mirroring the emission
  (module shape review = the human gate); snapshot test `TodoTests.Todo_MatchesAnswerKey`
  via the repo's canon alpha-equivalence pattern (copy an existing snapshot test's shape,
  e.g. HttpJsonTests'). Pins additionally:
  `Todo_RowClassEffect_InsideCreate`, `Todo_ChildrenInlined_NoComponentBoundary`
  (`shell`/`footer` markup present in the ONE module), `Todo_ExoticClasses_Verbatim`.
- [ ] **Step 4: bench contract.** `bench.mjs`: registry entry
  `todo: { readySelector: '#shell', observeSelector: '#list', scenarios: [] }` + driver:
  add "alpha" (fill `#new`, dispatch change, click `#add`), add "beta"; assert `#list`
  rows' text and EVERY row's `className` byte-equal; toggle row 1 (persisting key) →
  className gains `line-through text-slate-400` and `#left` says "1 left"; click `#clear`
  (child EventCallback) → row gone; remove remaining row → empty list, "0 left"; assert
  `#shell`/`#title`/`#footer` classNames byte-equal (D1 live). HARNESS 1.58.0 + prepended
  changelog sentence.
- [ ] **Step 5: build-filament.sh** — `todo` in the app list, outdir, razor path
  (`baseline/Todo.Blazor/App.razor`), out file `App.g.js`, component `App`, blazor label
  `blazor-todo`, css none; `samples/filament-todo-gen/main.js` standard entry.
- [ ] **Step 6: measure.**
  `dotnet publish baseline/Todo.Blazor -c Release -o bench/publish/blazor-todo`,
  `./bench/build-filament.sh filament-todo-gen`, then bench `--app todo --contract-only`
  on BOTH shells → `problems: []`, observed equal.
- [ ] **Step 7: commit** `feat(todo): the Tailwind todo-list measured vs Blazor -- classes byte-identical, row class flips on a persisting key (DECISIONS #154, BENCH n°65)`.

### Task 5 — the Tailwind build + examples/TodoTailwind (DX)

**Files:**
- Modify: root `package.json` + `package-lock.json` (add `tailwindcss` + `@tailwindcss/cli`,
  exact pin), `.github/workflows/ci.yml` (example-build step), `README.md`,
  `docs/REAL-APPS.md`
- Create: `examples/TodoTailwind/` (`TodoTailwind.csproj`, `Program.cs`, `Properties/launchSettings.json`,
  `App.razor` + `TodoShell.razor` + `TodoFooter.razor` (same sources as baseline),
  `tailwind.css`, `wwwroot/index.html`, `tools/check-css-coverage.mjs`, `README.md`)

- [ ] **Step 1: dependency.** `npm install --save-dev --save-exact tailwindcss @tailwindcss/cli`
  at root; note versions; `npm ci` still clean.
- [ ] **Step 2: app.** Copy `examples/FilamentApp` scaffolding; csproj: `<Watch>` all three
  .razor; `CompileFilament` target unchanged (App.razor → wwwroot/App.g.js; children are
  sibling-resolved by the generator); NEW target `BuildTailwind` (AfterTargets=Build,
  Inputs=the three .razor + tailwind.css, Outputs=wwwroot/app.css):
  `npx @tailwindcss/cli -i tailwind.css -o wwwroot/app.css --minify` (WorkingDirectory =
  project dir). `tailwind.css`:
  ```css
  @import "tailwindcss";
  @source "./*.razor";
  ```
  `index.html` links `css/…` → `app.css` + mounts `App.g.js`.
- [ ] **Step 3: coverage proof.** `tools/check-css-coverage.mjs`: extract every string
  literal passed as a class (regex over `App.g.js`: `setAttr\(.*'class', …` literals and
  fold string parts), split into utility tokens, assert each token appears in
  `wwwroot/app.css` (escaped per Tailwind's selector escaping: `/`→`\/`, `:`→`\:`,
  `[`→`\[` …); exit 1 listing missing tokens. Run it from the csproj after BuildTailwind
  (`<Exec Command="node tools/check-css-coverage.mjs" />`).
- [ ] **Step 4: local proof.** `dotnet build examples/TodoTailwind` → App.g.js + app.css
  produced + coverage check green; `dotnet run` smoke (Kestrel serves, manual note).
- [ ] **Step 5: CI.** ci.yml: after the runtime build + solution test steps, add
  `dotnet build examples/TodoTailwind -c Release` (3 OSes — proves the Tailwind CLI + the
  coverage gate everywhere).
- [ ] **Step 6: docs.** `docs/REAL-APPS.md` "Styling with Tailwind" (recipe: the input css,
  the MSBuild target, the full-class-names rule + why the coverage proof holds — verbatim
  emission); `examples/TodoTailwind/README.md`; root README: counts + a Tailwind line
  (CSS-isolation row already says "style with plain CSS or Tailwind" — link the example).
- [ ] **Step 7: DECISIONS #151–#155 (FR) + BENCH n°65 (FR, append-only) + memory.**
- [ ] **Step 8: full suite + firewall.** `dotnet test` (all projects), runtime vitest
  untouched, `git diff -- src/filament-runtime` EMPTY; commit
  `feat(dx): Tailwind v4 wired -- examples/TodoTailwind, scanner-coverage proof, docs (DECISIONS #155)`;
  push; watch CI 3-OS green.

## Self-review

- Spec coverage: D1→T1, W1→T2, D2→T3, app+measurement→T4, build+example+docs→T5. ✓
- No placeholders; exact paths; message texts written out. ✓
- Consistency: fixture names (`TailwindClasses`, `TwCompose`/`TwCard`, `RowClass`,
  `RowClassCall`, `ListMutateReassign`), ids (`#shell #title #new #add #list #left #clear #footer`),
  class strings match between Task 4's contract and the app shape. ✓
- Open in-task decisions are explicitly scoped (D2 refuse-vs-faithful; W1 exact seam). ✓
