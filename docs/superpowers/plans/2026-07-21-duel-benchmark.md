# Duel Benchmark Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One non-trivial app (routed task board + about page) built from the same `.razor` sources by Blazor WASM and by Filament, measured on weight / time-to-interactive / memory, results committed and rendered on a data-driven site page.

**Architecture:** `baseline/Duel.Blazor` (Router host + 2 pages) is the source of truth; Filament compiles the SAME page files via `--router` into `samples/filament-duel-gen`. `bench/publish-baseline.sh` + `bench/build-filament.sh` gain duel labels; `bench/harness/bench.mjs` gains the duel DOM contract + a new memory instrument (`performance.measureUserAgentSpecificMemory`, COOP/COEP added to `server.mjs`); a mirrored-pass run commits `bench/results/duel/*.json`; `website/src/pages/benchmark.astro` renders those JSONs at build time.

**Tech Stack:** net10.0 Blazor WASM (`Microsoft.NET.Sdk.BlazorWebAssembly` 10.0.9), Filament.Generator `--router`, esbuild, Playwright 1.61.1 harness (HARNESS bump), Astro 5 + Tailwind 4.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-07-21-duel-benchmark-design.md`.
- The Blazor baseline builds FIRST; no Filament work before `dotnet publish` of the baseline succeeds (RZ9979 lesson).
- Runtime freeze: `git diff -- src/filament-runtime` must stay empty unless a defect is found (then it's a disclosed fix, not a widening).
- Bench never runs in CI; measurement runs happen on this machine, sequentially, results committed.
- The memory instrument fails loud if `measureUserAgentSpecificMemory` is unavailable — never JS-heap-only silently.
- Docs of record: DECISIONS entry **#140** (French), BENCH entry **n°58** (French, append-only), HARNESS_VERSION bump disclosed in the BENCH entry.
- Commit style: `type(scope): summary (DECISIONS #N, BENCH n°N)`.

---

### Task 1: Blazor baseline `Duel.Blazor`

**Files:**
- Create: `baseline/Duel.Blazor/Duel.Blazor.csproj` (copy `baseline/Routing.Blazor/Routing.Blazor.csproj`, rename RootNamespace/AssemblyName to `Duel`)
- Create: `baseline/Duel.Blazor/Program.cs`, `_Imports.razor`, `App.razor` (copy from `baseline/Routing.Blazor`, same Router host; `_Imports` must also import `Microsoft.AspNetCore.Components.Forms`)
- Create: `baseline/Duel.Blazor/Pages/Board.razor`, `Pages/About.razor`
- Create: `baseline/Duel.Blazor/wwwroot/index.html`, `wwwroot/css/app.css` (copy Routing.Blazor's, retitle)

**Interfaces:**
- Produces: the DOM contract consumed by Task 3's harness scenario and Task 2's Filament build:
  `#board`, `#task-input`, `#task-add`, `#task-list` (`<li>` per visible task, each with `.toggle` and `.remove` buttons and a `.label` span), `#filter-all/#filter-active/#filter-done`, `#stats` (`"N active / M total"`), `#to-about`; About: `#about`, `#about-count`, `#to-board`.

- [ ] **Step 1: Write `Pages/Board.razor`** — every construct individually proven; keep records positional (#111), list reassignment (#124), record replace via LINQ Select (#116) rather than untested mutations:

```razor
@page "/"

@* The Duel's working page: forms + keyed list + LINQ + control flow, in ONE component. *@

<div id="board">
<EditForm Model="@model" OnValidSubmit="Add"><InputText id="task-input" @bind-Value="model.Label" /><button id="task-add" type="submit">add</button></EditForm>

<p id="stats">@tasks.Where(t => !t.Done).Count() active / @tasks.Count total</p>

<button id="filter-all" @onclick="() => filter = 0">all</button><button id="filter-active" @onclick="() => filter = 1">active</button><button id="filter-done" @onclick="() => filter = 2">done</button>

@if (tasks.Count == 0)
{
    <p id="empty">no tasks</p>
}
else
{
    <ul id="task-list">
    @foreach (var t in Visible())
    {
        <li><span class="label">@t.Label</span><span class="state">@(t.Done ? "done" : "todo")</span><button class="toggle" @onclick="() => Toggle(t.Id)">toggle</button><button class="remove" @onclick="() => Remove(t.Id)">remove</button></li>
    }
    </ul>
}

<a id="to-about" href="/about">about</a>
</div>

@code {
    private Draft model = new Draft();
    private List<TaskItem> tasks = new List<TaskItem>();
    private int filter = 0;
    private int nextId = 1;

    void Add()
    {
        if (model.Label == "") { return; }
        tasks = tasks.Concat(new List<TaskItem> { new TaskItem(nextId, model.Label, false) }).ToList();
        nextId = nextId + 1;
        model = new Draft();
    }

    void Toggle(int id) { tasks = tasks.Select(t => t.Id == id ? new TaskItem(t.Id, t.Label, !t.Done) : t).ToList(); }
    void Remove(int id) { tasks = tasks.Where(t => t.Id != id).ToList(); }

    List<TaskItem> Visible()
    {
        if (filter == 1) { return tasks.Where(t => !t.Done).ToList(); }
        if (filter == 2) { return tasks.Where(t => t.Done).ToList(); }
        return tasks;
    }

    public record TaskItem(int Id, string Label, bool Done);
    public record Draft { public string Label { get; set; } = ""; }
}
```

- [ ] **Step 2: Write `Pages/About.razor`** (state resets on re-entry — the #139 mounted-afresh contract):

```razor
@page "/about"

<div id="about"><h2>about</h2><p>The same .razor, two compilers.</p><button id="about-count" @onclick="() => n++">clicked @n</button><a id="to-board" href="/">board</a></div>

@code {
    private int n = 0;
}
```

- [ ] **Step 3: Build and publish the baseline**

Run: `dotnet publish baseline/Duel.Blazor -c Release -o /tmp-scratch/duel-baseline` (use the session scratchpad).
Expected: SUCCESS. If Razor rejects any construct, fix the BASELINE first — the app may only contain what Blazor itself accepts.

- [ ] **Step 4: Manual contract check** — serve the publish output, drive add/toggle/filter/remove/navigate in a browser (or quick Playwright script), confirm every contract ID behaves.

- [ ] **Step 5: Commit** — `feat(duel): Blazor WASM baseline for the Duel app (task board + about, routed)`

### Task 2: Filament side — the same pages through `--router`

**Files:**
- Create: `samples/filament-duel-gen/main.js` (copy `samples/filament-routing-gen/main.js`, retarget comment)
- Modify: `bench/build-filament.sh` — add `filament-duel-gen` to: the label list, `src_dir_for`, mode switch (`production`), `router_pages_for` (both Duel pages), `entry_for` (`Router.g.js` — mirror routing-gen's entry), `baseline_label_for` (`blazor-duel`), `css_for`, shell/title switches (mirror `filament-routing-gen` in every switch that names it)
- Modify: `bench/publish-baseline.sh` — add `blazor-duel-nojit` / `blazor-duel-aot` labels pointing at `baseline/Duel.Blazor` (mirror the `blazor-routing` entries)

**Interfaces:**
- Consumes: Task 1's `.razor` pages — THE SAME FILES, by path.
- Produces: `filament-duel-gen` and `blazor-duel-{nojit,aot}` build labels for Task 3/5.

- [ ] **Step 1: Dry-run the generator on the pages**

Run: `dotnet run --project src/Filament.Generator -- --router /tmp-scratch/duel/Router.g.js baseline/Duel.Blazor/Pages/Board.razor baseline/Duel.Blazor/Pages/About.razor`
Expected: either clean emission, or FIL diagnostics. **If refused:** diagnose. A narrow generator gap (e.g. an expression shape) → mini-slice with TDD + DECISIONS entry; a structural gap → reshape the app in Task 1 (both sides, same source) and disclose in DECISIONS #140. Do not proceed with a divergent app.

- [ ] **Step 2: Wire the two build scripts** (labels above), then build both sides:

Run: `bash bench/build-filament.sh filament-duel-gen` and `bash bench/publish-baseline.sh blazor-duel-nojit blazor-duel-aot`
Expected: static roots under the scripts' PUBLISH_ROOT with gzip/brotli siblings.

- [ ] **Step 3: Manual parity check** — serve both roots, drive the full contract on each; any visible divergence is a bug to fix NOW (generator or app), not at measurement time.

- [ ] **Step 4: Commit** — `feat(duel): compile the Duel pages with --router + duel labels in both build scripts`

### Task 3: Harness — duel contract + HARNESS bump

**Files:**
- Modify: `bench/harness/bench.mjs` — new app scenario `duel`: contract driver (add "alpha", add "beta", toggle beta, filter active → expect only alpha, filter all, remove alpha, navigate about, click `#about-count` → "clicked 1", back, expect list intact) with every step's observed DOM asserted; register the app so `--app duel` works for C1 weight + cold/warm `msToMutation`; bump `HARNESS_VERSION`.
- Modify: `bench/harness/selftest.mjs` — duel-contract self-test against a deliberately-broken fixture (e.g. stats that don't update) proving the contract FAILS when it should.

**Interfaces:**
- Consumes: Task 1's DOM contract IDs verbatim.
- Produces: `--app duel` invocations used by Task 5's run script.

- [ ] **Step 1:** Read bench.mjs's existing per-app scenario wiring (counter/rows/forms/routing) and add `duel` following the same shape — same instrument functions, new driver only.
- [ ] **Step 2:** Extend selftest.mjs; run `node bench/harness/selftest.mjs`; expected: PASS including the new negative case.
- [ ] **Step 3:** Smoke the real thing: one `bench.mjs` invocation per side against Task 2's roots; expected: contract passes on both, numbers within sanity.
- [ ] **Step 4: Commit** — `bench(duel): harness contract for the Duel app (HARNESS x.y.z)`

### Task 4: Memory instrument

**Files:**
- Modify: `bench/harness/server.mjs` — add `Cross-Origin-Opener-Policy: same-origin` + `Cross-Origin-Embedder-Policy: require-corp` response headers.
- Modify: `bench/harness/bench.mjs` — `--memory` pass: load → contract-settle → `performance.measureUserAgentSpecificMemory()` → interaction burst (the Task 3 driver) → settle → measure again; 5 repetitions, medians; JSON records the full per-measurement `breakdown` (wasm vs js attribution kept). **Fails loud** (nonzero exit, no JSON) if the API rejects or is absent.

**Interfaces:**
- Produces: `--memory` mode consumed by Task 5; JSON shape `{ label, afterLoad: {bytes, breakdown}, afterBurst: {bytes, breakdown}, runs, harness }`.

- [ ] **Step 1:** Implement; verify COOP/COEP took effect (`crossOriginIsolated === true` asserted in-page before measuring — assert, don't assume).
- [ ] **Step 2:** Run against both duel roots; sanity: Blazor ≫ Filament, breakdown shows wasm memory on the Blazor side.
- [ ] **Step 3: Commit** — `bench(memory): measureUserAgentSpecificMemory instrument, COOP/COEP, fails loud (HARNESS x.y.z)`

### Task 5: The measurement run

**Files:**
- Create: `bench/run-duel.sh` — mirrored passes (decision 47 discipline): weight+timing over `{blazor-duel-nojit, filament-duel-gen, blazor-duel-aot}` × `{gzip, br}` with the order reversed in the second half; memory pass last (its GC/profiling must not sit inside timing passes — same reasoning as C3).
- Create: `bench/results/duel/*.json` (run output, committed).

- [ ] **Step 1:** Write the script mirroring `run-phase3-measure.sh`'s structure (sequential, nothing backgrounded, harness identity check).
- [ ] **Step 2:** Close other work on the machine; run it; eyeball IQRs for stability; re-run any pass whose IQR screams interference.
- [ ] **Step 3: Commit** — `bench(duel): the Duel measured — weight, TTI, memory, both compilers (BENCH n°58)`

### Task 6: Site page

**Files:**
- Create: `website/src/pages/benchmark.astro` — imports `bench/results/duel/*.json` (static import at build time), renders: header (what the app is, links to the `.razor` sources on GitHub), three axis sections with CSS bars (linear scale, labelled values; nojit + aot + filament side by side), memory breakdown note, methodology footnotes linking BENCH.md n°58.
- Modify: `website/src/components/Nav.astro` — links become page-aware: add `Benchmark` → `${import.meta.env.BASE_URL}benchmark` (keep anchor links working from the index).
- Modify: `website/src/pages/index.astro` — hero gains a "see the app-level benchmark" link to the page.

- [ ] **Step 1:** Build the page; `cd website && npm run build` — expected SUCCESS with the imported JSONs.
- [ ] **Step 2:** `npx astro preview` + eyeball; verify all links respect `BASE_URL` (`/Filament/`).
- [ ] **Step 3: Commit** — `feat(site): /benchmark — the Duel rendered from committed results (DECISIONS #140, BENCH n°58)`

### Task 7: Docs of record

- [ ] **Step 1:** BENCH.md — append `Entrée n°58` (French): app composition, labels, all numbers (weight gzip+br, TTI cold/warm, memory with breakdown), harness version + identity, machine, disclosure of the no-answer-key arbitration.
- [ ] **Step 2:** DECISIONS.md — append `## 140.` (French): the Duel's purpose, the no-canon-oracle arbitration, the memory-instrument choice (why `measureUserAgentSpecificMemory`, why fail-loud), any reshape taken in Task 2.
- [ ] **Step 3:** README.md — one line + link in the evidence section pointing at `/benchmark`.
- [ ] **Step 4:** Full gates: `dotnet test Filament.sln` + runtime `npm test` + `git diff -- src/filament-runtime` (must be empty) + website build.
- [ ] **Step 5: Commit** — `docs(duel): BENCH n°58 + DECISIONS #140 + README pointer`
