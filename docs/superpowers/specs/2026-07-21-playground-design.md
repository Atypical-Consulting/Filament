# The Playground — the unchanged generator, running in your browser (design)

**Date:** 2026-07-21 · **Status:** approved (owner picked this feature explicitly)

## Purpose

A live page on the site: Razor on the left, the generated JS on the right, the running component
below — and, on refusal, the exact FIL diagnostics instead of output. It demonstrates the two
claims the whole project makes, interactively: the output is *tiny*, and the compiler is *honest*
(it refuses rather than emitting silently-wrong JS).

The engine is **Filament.Generator itself, compiled to WebAssembly** — not a port, not a subset
reimplementation. The page's banner states the irony as a measurement: *"you downloaded N MB of
.NET to run this compiler; the component it just produced is Y bytes."* The playground is itself
a Blazor-vs-Filament exhibit.

## Architecture

Three parts; the generator's compile pipeline is (almost) untouched.

### 1. Generator seam — one, named, fails loud

`ReferenceAssemblies.Load()` currently probes `<dotnet root>/packs` derived from the running
runtime. In the browser there is no SDK on disk — but there IS a filesystem (Emscripten MEMFS),
so `RazorProjectFileSystem`, `File.WriteAllText`, and path-based `Parse()` all work unchanged.
The ONE thing that cannot work is the SDK probe. Seam: an environment variable
**`FILAMENT_DOTNET_ROOT`** consulted before `RuntimeEnvironment.GetRuntimeDirectory()`; when set,
`packs/` is resolved under it, same loud `FIL-WIRING` failures. One place, one name, testable on
any machine — and it is exactly the "narrowing later is a change in ONE place with a name" hook
`ReferenceAssemblies` already reserved.

### 2. Trimmed reference pack — proven by the whole suite, not curated by hope

The full packs are 307 assemblies / ~84 MB raw — unshippable. The playground ships a **curated
subset** (BCL closure Roslyn needs + `Microsoft.AspNetCore.Components*` + their deps for
tag-helper discovery). The equivalence proof is the project's own oracle: **the full generator
test suite (466 tests) runs with `FILAMENT_DOTNET_ROOT` pointed at the curated layout and must be
green.** A green suite means every supported fixture compiles to the same bytes and every refusal
still refuses — the curated set is behaviourally indistinguishable across the entire measured
surface. The curation script (`playground/make-refpack.sh`) builds the layout from the installed
SDK from a committed list (`playground/refpack.list`); a CI job (ubuntu only) re-runs the proof so
an SDK bump that grows the closure fails loud instead of shipping a broken playground.

### 3. WASM host + page

- **`playground/Filament.Playground/`** — a `wasmbrowser` app (`Microsoft.NET.Sdk.WebAssembly`,
  net10.0, no Blazor UI): references `Filament.Generator`, exposes one `[JSExport]`:
  `Compile(string razor, string runtimeSpecifier) → JSON { ok, js|diagnostics[] }`.
  Startup: fetch the curated DLLs (manifest with per-file bytes), write them into MEMFS under a
  packs layout, set `FILAMENT_DOTNET_ROOT`, report readiness + total downloaded bytes to JS.
  Compile: write the source to a MEMFS `.razor`, run the existing `Parse` → Razor-error gate →
  `TemplateCompiler.Compile` → diagnostics-or-JS, exactly Program.cs's order (shared, not copied,
  if extraction is cheap; duplicated-with-a-test if not — decided at implementation).
- **`website/src/pages/playground.astro`** — plain textarea editor (no editor library; the site
  stays light), examples dropdown fed from real `Supported/` and `Unsupported/` fixtures at build
  time, output `<pre>`, diagnostics panel (FIL codes rendered as the compiler prints them),
  preview iframe. Preview: the compiled module is a Blob URL importing `filament.js` by absolute
  URL (the `runtimeSpecifier` we pass), `mount(#app)` inside a sandboxed iframe. Compile on
  button + Cmd/Ctrl-Enter; auto-compile only if measured latency permits.
  A status strip shows: engine payload downloaded (MB), compile time (ms), output size (bytes,
  gzip estimate) — the irony banner, from live numbers, never hardcoded.
- **Deploy:** publish output copied under `website/public/playground-engine/`;
  `deploy-docs.yml` gains setup-dotnet + `wasm-tools` + the publish/copy step. GitHub Pages does
  not negotiate precompressed Blazor/WASM assets: if measured response headers show identity
  encoding for `.wasm`/`.dll`, the host uses a custom resource loader fetching `.gz` siblings
  through `DecompressionStream('gzip')` (native; brotli has no native decoder). Decided by
  measuring the deployed headers, not assumed.

## Error handling

- Engine load failure (fetch, MEMFS, packs) → the page says so and keeps the editor + examples
  readable; it never half-works.
- Razor parse errors and FIL diagnostics render verbatim in the diagnostics panel — refusals are
  a feature, shown with the same wording the CLI prints.
- The `@code` semantic model and tag-helper chain must behave identically in WASM: the smoke test
  compiles a fixture whose correctness depends on each (an `@onclick` fixture; a typed `@code`
  fixture) and byte-compares against the CLI's output for the same input.

## Testing

- Seam: unit test — `FILAMENT_DOTNET_ROOT` honored, absent packs still fail `FIL-WIRING`.
- Trim proof: full suite green under the curated layout (locally + ubuntu CI job).
- WASM byte-parity smoke: Playwright drives the built site preview: load engine, compile
  Counter.razor, assert emitted JS is byte-identical to the CLI's emission for the same source and
  specifier; compile a known-refused fixture, assert the FIL code surfaces; assert the preview
  iframe increments. Runs locally (and is CI-eligible: functional, not a timing measurement).
- Payload: first-load wire bytes measured and recorded (BENCH entry, French) — the banner's N.

## Non-goals (v1)

- No sharing/permalinks, no multi-file input, no `--router` mode in the page, no editor
  intelligence (completion/highlight-as-you-type), no server fallback. Each is additive later.
