# Playground (generator-in-WASM) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A live site page — Razor in, generated JS + running component (or verbatim FIL refusals) out — powered by the unchanged Filament.Generator compiled to WebAssembly, with the engine payload measured and displayed.

**Architecture:** One named seam (`FILAMENT_DOTNET_ROOT` consulted before the SDK probe in `ReferenceAssemblies`); a curated reference-pack whose equivalence is PROVEN by running the full generator test suite against it; a `wasmbrowser` host (`playground/Filament.Playground`) exposing `[JSExport] Compile(razor, runtimeSpecifier)` and hydrating MEMFS with the curated DLLs at startup; an Astro page (`website/src/pages/playground.astro`) with editor / output / diagnostics / iframe preview; deploy wired into `deploy-docs.yml`.

**Tech Stack:** net10.0 `Microsoft.NET.Sdk.WebAssembly` (wasm-tools/wasm-experimental 10.0.109), JSExport interop, Emscripten MEMFS, Astro 5 + Tailwind 4, Playwright smoke, GitHub Pages (gzip fallback via `DecompressionStream` if measured necessary).

## Global Constraints

- Spec: `docs/superpowers/specs/2026-07-21-playground-design.md`.
- The compile pipeline (`RazorFrontEnd`, `TemplateCompiler`, `CSharpFrontEnd`) stays byte-untouched; the ONLY generator change is the `ReferenceAssemblies` seam (+ its test).
- Runtime freeze unchanged: `git diff -- src/filament-runtime` empty.
- Trim proof = the FULL test suite green under the curated layout; a red suite blocks shipping, no cherry-picked subset of tests.
- Diagnostics in the page render with the same wording the CLI prints — refusals are the feature.
- No hardcoded payload/size numbers on the page: every displayed number is measured at run/build time.
- v1 non-goals (no permalinks, no multi-file, no `--router` in page, no editor library, no server fallback).
- Docs of record: DECISIONS **#141** (French), BENCH **n°59** (French) for the payload/latency numbers.

---

### Task 1: The seam (TDD)

**Files:**
- Modify: `src/Filament.Generator/ReferenceAssemblies.cs`
- Create: `tests/Filament.Generator.Tests/ReferenceAssembliesSeamTests.cs`

**Interfaces:**
- Produces: env var contract `FILAMENT_DOTNET_ROOT` = a directory containing `packs/Microsoft.NETCore.App.Ref/<ver>/ref/<tfm>/*.dll` and `packs/Microsoft.AspNetCore.App.Ref/<ver>/ref/<tfm>/*.dll`. Consumed by Tasks 2 and 3.

- [ ] **Step 1: Failing tests** — the seam is process-global (env var + static cache), so the test must run the GENERATOR SUBPROCESS, not the in-process statics (mirrors how `Run.Generator` tests already isolate): (a) `FILAMENT_DOTNET_ROOT` → valid mirror layout (symlink/copy the real packs) compiles `Supported/Gate` Counter fixture identically to a run without the var; (b) `FILAMENT_DOTNET_ROOT` → empty dir fails with `FIL-WIRING` mentioning the overridden path.
- [ ] **Step 2:** Run; expected FAIL (var ignored today).
- [ ] **Step 3: Implement** — in `Load()`:

```csharp
// FILAMENT_DOTNET_ROOT: the ONE seam for a host with no SDK on disk (the WASM playground).
// Same layout, same loud failures; nothing else about resolution changes.
var overrideRoot = Environment.GetEnvironmentVariable("FILAMENT_DOTNET_ROOT");
var dotnetRoot = overrideRoot is { Length: > 0 }
    ? Path.GetFullPath(overrideRoot)
    : Path.GetFullPath(Path.Combine(
        System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
```

- [ ] **Step 4:** Tests green; full suite green. **Commit** — `feat(playground): FILAMENT_DOTNET_ROOT seam in ReferenceAssemblies (DECISIONS #141)`

### Task 2: Curated ref-pack, proven by the suite

**Files:**
- Create: `playground/make-refpack.sh` — reads `playground/refpack.list` (one assembly simple-name per line, comments allowed), copies each from the installed SDK's newest net10.0 ref dirs into `<out>/packs/<pack>/<ver>/ref/net10.0/`, fails loud on a missing name.
- Create: `playground/refpack.list` — seeded with: `System.Runtime`, `System.Runtime.InteropServices`, `System.Collections`, `System.Linq`, `System.Console`, `netstandard`, `mscorlib`, `System.Threading.Tasks`, `Microsoft.AspNetCore.Components`, `Microsoft.AspNetCore.Components.Web`, `Microsoft.AspNetCore.Components.Forms` + whatever iteration demands — the list is DISCOVERED, not designed.
- Create: `playground/prove-refpack.sh` — `make-refpack.sh` into a temp dir, then `FILAMENT_DOTNET_ROOT=<temp> dotnet test Filament.sln`.

- [ ] **Step 1:** Iterate: run the proof, read the first failure, add the missing assembly, repeat until green. Record iterations briefly for DECISIONS.
- [ ] **Step 2:** Record the measurement: `N assemblies, X MB raw, Y MB gzip` (script prints it) → BENCH n°59 input.
- [ ] **Step 3:** CI: new ubuntu-only job `refpack-proof` in `.github/workflows/ci.yml` running `playground/prove-refpack.sh` (same setup steps as the dotnet job; runtime build included since the suite needs it).
- [ ] **Step 4: Commit** — `feat(playground): curated ref-pack + suite-green proof script + CI gate (BENCH n°59)`

### Task 3: WASM host

**Files:**
- Create: `playground/Filament.Playground/Filament.Playground.csproj` (`dotnet new wasmbrowser` shape; `ProjectReference` → `../../src/Filament.Generator/Filament.Generator.csproj`; `InvariantGlobalization=true`)
- Create: `playground/Filament.Playground/Program.cs` + `Compiler.cs` — `[JSExport] static string CompileRazor(string razor, string runtimeSpecifier)` returning JSON `{ok:bool, js?:string, diagnostics?:string[], ms:number}`; order of gates copied from `Program.cs` (Razor errors first, then TemplateCompiler diagnostics).
- Create: `playground/Filament.Playground/wwwroot/main.js` + `index.html` (dev harness only) — boot `dotnet.js`, fetch `refpack/manifest.json` + DLLs, `FS`-write them under `/refpack/packs/...` (use the runtime's exported FS or interop `File.WriteAllBytes` from C# — whichever the experiment favors), `Environment.SetEnvironmentVariable("FILAMENT_DOTNET_ROOT","/refpack")` before first compile, then expose `window.filamentCompile`.

**Interfaces:**
- Consumes: Task 1's env contract; Task 2's refpack output (copied to `wwwroot/refpack/` by a publish target + manifest generated by `make-refpack.sh --manifest`).
- Produces: `filamentCompile(razor, runtimeSpecifier) → Promise<{ok, js, diagnostics, ms}>` + `filamentReady → Promise<{downloadedBytes, assemblies}>` for Task 4's page.

- [ ] **Step 1: Experiments first** (throwaway console in the host): what `RuntimeEnvironment.GetRuntimeDirectory()` returns under WASM; whether `File.WriteAllBytes` + `Directory.CreateDirectory` land in MEMFS; whether `MetadataReference.CreateFromFile` reads them back. Adjust hydration mechanics to findings (the seam contract itself cannot change).
- [ ] **Step 2:** `dotnet publish -c Release` + serve; byte-parity smoke: node script compiles `tests/.../Supported/Gate/Counter-equivalent fixture` via the page's `filamentCompile` (Playwright) AND via the CLI with the same `--runtime` specifier; assert **byte-identical** JS; assert an `@onclick` fixture and a typed-`@code` fixture also match (tag-helper + semantic-model behavioural proof); assert a refused fixture surfaces its FIL code.
- [ ] **Step 3:** Record first-load wire bytes (DevTools/CDP transfer sum) → BENCH n°59.
- [ ] **Step 4: Commit** — `feat(playground): the generator compiled to WASM — byte-identical output in-browser (DECISIONS #141, BENCH n°59)`

### Task 4: The page

**Files:**
- Create: `website/src/pages/playground.astro` — layout: status strip (engine payload MB / compile ms / output bytes — live numbers), textarea editor prefilled with the Counter fixture, examples `<select>` (a build-time import of a handful of real `Supported/` + `Unsupported/` fixture sources), Compile button + Cmd/Ctrl-Enter, output `<pre>`, diagnostics panel, sandboxed preview iframe (Blob-URL module importing `filament.js` by absolute URL — the exact `runtimeSpecifier` passed to compile — then `mount(document.getElementById('app'))`).
- Create: `website/public/playground/filament.js` — built runtime copy step in the site build (never committed: extend `website/package.json` build script or a small prebuild script that runs `npm run build` in `src/filament-runtime` and copies `dist/filament.js`).
- Modify: `website/src/components/Nav.astro` — add `Playground` link (BASE_URL-aware, same treatment as Task 6 of the duel plan).

- [ ] **Step 1:** Page skeleton against the LOCAL dev host from Task 3 (engine assets under `website/public/playground-engine/` via a copy script, gitignored).
- [ ] **Step 2:** Playwright smoke (local, `astro preview`): engine loads; Counter compiles; output pane non-empty; preview button increments; refused fixture shows FIL code verbatim; payload number displayed equals measured transfer.
- [ ] **Step 3:** Accessibility pass: labels on all controls, keyboard path, `aria-live` on diagnostics/status.
- [ ] **Step 4: Commit** — `feat(site): /playground — live Razor→JS with the real generator (DECISIONS #141)`

### Task 5: Deploy

**Files:**
- Modify: `.github/workflows/deploy-docs.yml` — add setup-dotnet 10.0.x + `dotnet workload install wasm-tools` + runtime build + `dotnet publish playground/Filament.Playground` + copy into `website/public/playground-engine/` before `astro build`; widen the `paths:` trigger to `playground/**`, `src/**`.
- Possibly create: gzip-fallback loader (only if measurement demands — see Step 2).

- [ ] **Step 1:** Deploy; page must load on `https://atypical-consulting.github.io/Filament/playground`.
- [ ] **Step 2:** Measure the deployed response headers for `.wasm`/`.dll`: if content-encoding is identity, implement the documented fallback (fetch `.gz` siblings through `DecompressionStream('gzip')` via the runtime's resource loader hook) and re-measure. Decided by the artifact, not assumed.
- [ ] **Step 3: Commit** — `fix(site): serve the playground engine compressed on Pages` (only if Step 2 fired)

### Task 6: Docs of record

- [ ] **Step 1:** BENCH.md `Entrée n°59` (French): curated-pack numbers (N assemblies, raw/gzip MB), suite-green proof statement, first-load wire bytes, in-browser compile latency (median of 10 on the Counter fixture), byte-parity statement.
- [ ] **Step 2:** DECISIONS.md `## 141.` (French): the seam arbitration, why the proof is the whole suite, MEMFS findings, the irony framing (the playground ships Blazor-scale bytes to run the compiler whose output is bytes — displayed live).
- [ ] **Step 3:** README.md — playground link next to the demo link.
- [ ] **Step 4:** Full gates: `dotnet test`, runtime `npm test`, runtime diff empty, `prove-refpack.sh`, website build, Playwright smoke.
- [ ] **Step 5: Commit** — `docs(playground): BENCH n°59 + DECISIONS #141 + README pointer`
