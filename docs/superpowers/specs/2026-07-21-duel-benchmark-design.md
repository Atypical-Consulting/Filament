# The Duel — one non-trivial app, both compilers, three numbers (design)

**Date:** 2026-07-21 · **Status:** approved (owner picked this feature explicitly)

## Purpose

Every measurement so far compares single-feature witnesses (Counter, Rows, Forms, Routing…).
The README's headline (1.88 MB vs 4.4 KB) is real but comes from the *Counter* pair — a reader can
object "sure, for a toy". The Duel closes that objection: **one non-trivial app** — routed pages,
a form, a keyed list with writes, LINQ — built from the same `.razor` sources by Blazor WASM and by
Filament, measured on the three axes a shipping team actually asks about:

1. **Weight** — cold-cache transferred bytes (the existing C1 instrument).
2. **Time to interactive** — ms from navigation to first rendered mutation (the existing
   `msToMutation` instrument, cold and warm).
3. **Memory** — a **new** instrument: `performance.measureUserAgentSpecificMemory()` after load,
   forced-quiet, and again after a scripted interaction burst. New because the harness measures no
   heap today, and JS-heap-only metrics would silently exclude Blazor's WASM linear memory — the
   honest instrument includes it.

Results are committed as JSON (bench never runs in CI — thermal discipline stands) and rendered by
a **new data-driven page on the Astro site**, replacing nothing: the existing per-feature evidence
stays; the Duel is the app-level headline above it.

## The app: “Duel”, a task board

Two routed pages, compiled by `--router` on the Filament side and hosted by `<Router>` on the
Blazor side — structurally `baseline/Routing.Blazor` with a rich home page.

- **`Board.razor`** (`@page "/"`) — the working page:
  - `EditForm` + `InputText` `@bind-Value` on a model property + submit → **add task** (forms, #137/#138).
  - Task list: `List<TaskItem>` of **positional records** (#111), rendered by `@foreach` → keyed
    `list()` (#124), with `@if` empty-state.
  - Per-row **toggle** (record replace via element write, #127) and **remove** (LINQ `Where`
    reassignment, #116/#124).
  - **Filter** (all / active / done) via `@onclick` handlers + `@if`/ternary classes (#94/#96).
  - Stats line via LINQ `Count` (#116/#121).
- **`About.razor`** (`@page "/about"`) — a static page with one interactive element (a counter
  button), so route re-entry has state to reset (the #139 mounted-afresh contract).

**Rule inherited from RZ9979:** the Blazor baseline (`baseline/Duel.Blazor`) is written FIRST and
`dotnet build` + `dotnet publish` must pass before any Filament work. Every construct above is
individually proven; their **intersection** is not — if the generator refuses a combination, the
choice is: fix it as a mini-slice if the cause is narrow, otherwise reshape the app to the honest
subset and disclose. The app must never be shaped by *undisclosed* generator limits.

## Measurement design

- **Labels:** `blazor-duel-nojit`, `blazor-duel-aot` (published by `publish-baseline.sh`),
  `filament-duel-gen` (built by `build-filament.sh` via the `--router` path, production esbuild).
  No hand-written answer key: the Duel is an *assembly* of already-keyed features; its oracle is
  the DOM contract below, not canon-equivalence. (Disclosed departure from the per-slice pattern.)
- **DOM contract (shared IDs, both apps):** `#board` page marker, `#task-input`, `#task-add`,
  `#task-list` (one `<li>` per task, `#task-list li .toggle/.remove`), `#filter-all/-active/-done`,
  `#stats`, `#to-about`, `#about-count`. The harness drives: add ×2 → toggle ×1 → filter → remove
  ×1 → navigate `/about` → click counter → back. Divergence in any observed value fails the run —
  this is the behavioural oracle.
- **Weight & timing:** existing instruments, mirrored-pass ordering (decision 47) across the three
  labels × {gzip, br}, 10-run medians + IQR, cold + warm.
- **Memory (new, HARNESS bump):** `server.mjs` gains COOP/COEP headers (required for
  `measureUserAgentSpecificMemory`); `bench.mjs` gains a `--memory` pass: load → settle → measure
  (bytes, with the wasm/js breakdown kept in the JSON) → interaction burst → settle → measure.
  Median of 5. The breakdown is stored so nobody can accuse the number of hiding WASM memory.
- **Results:** `bench/results/duel/*.json`, committed. BENCH entry (French, append-only) with the
  numbers; DECISIONS entry for the arbitrations (no answer key; memory instrument choice; app shape).

## Site page

`website/src/pages/benchmark.astro` (linked from nav + hero):

- Reads the committed `bench/results/duel/*.json` at **build time** (static import — no client JS).
- Three sections, one per axis: CSS bar comparisons (linear scale, values labelled — no chart
  library; the site's own weight is part of its message), Blazor AOT and no-JIT both shown.
- The app's source shown both ways (the `.razor` pages are the SAME files — that is the point),
  with links to the repo paths.
- Methodology footnotes: cold cache, mirrored passes, machine identity, links to BENCH.md entry.
- Numbers must render from data, not prose: a future re-run only re-commits JSON.

## Error handling & risks

- Intersection refusals during app authoring → mini-slice or disclosed reshape (above).
- `measureUserAgentSpecificMemory` unavailable/cross-origin-blocked → fail loud in the harness
  (refuse to write a memory JSON), never fall back silently to JS-heap-only.
- Blazor publish size varies with SDK — the JSON records SDK/tool versions like existing results do.

## Testing

- Baseline first: `Duel.Blazor` builds & publishes; contract IDs verified manually in-browser once.
- Generator: the two pages compile via `--router`; a `DuelTests.cs` gate pins that the pages stay
  compiled (Supported fixtures if new constructs surface; otherwise the bench build script is the gate).
- Harness: `selftest.mjs` extended for the duel contract; one full mirrored run on the bench machine
  produces the committed JSONs.
- Site: `astro check` + `astro build` (existing website.yml gate) — the page builds from the JSONs.
