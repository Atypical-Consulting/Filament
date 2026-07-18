# Runnable Filament App (Rider, Web SDK) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One real `.NET` web project — `examples/FilamentApp` — that opens in Rider, compiles `App.razor` → JS on Build via `Filament.Generator`, and runs on F5 (Kestrel serves the static output, browser opens on the Counter).

**Architecture:** A `Microsoft.NET.Sdk.Web` project. `Program.cs` is a minimal Kestrel static host (dev-only). Two MSBuild targets, hooked `AfterTargets="Build"` so the generator (a `ProjectReference`, `ReferenceOutputAssembly=false`) is built first: `CompileFilament` execs the generator DLL (`App.razor` → `wwwroot/App.g.js`, `--runtime ./filament.js`); `CopyFilamentRuntime` copies the prebuilt `src/filament-runtime/dist/filament.js` → `wwwroot/filament.js`. Both carry `Inputs`/`Outputs` for incremental skips. The Razor SDK is told **not** to compile `App.razor` (`EnableDefaultRazorComponentItems=false`) so Filament owns it. Generated JS is gitignored; the browser loads `index.html` → `App.g.js` → `filament.js` as native ES modules.

**Tech Stack:** .NET 10 (`Microsoft.NET.Sdk.Web`), Kestrel static-file host, `Filament.Generator` (existing console app), the existing `filament-runtime` (prebuilt `dist/filament.js`). No Node/esbuild inside `dotnet build`.

## Global Constraints

- **Firewall — do not touch:** the generator's emitted bytes, `src/filament-runtime` source, `BENCH.md`, any measurement artifact (`bench/**`, `demo/**`), or any subset/analyzer code. This is DX/packaging only. No BENCH entry (nothing is measured).
- **No runnable JS committed.** `wwwroot/App.g.js` and `wwwroot/filament.js` are build outputs and MUST be gitignored (repo rule; see `.gitignore` lines 33–40). Only hand-written static content (`index.html`, `css/app.css`) is committed under `wwwroot/`.
- **Runtime prerequisite (one-time):** `src/filament-runtime/dist/filament.js` must exist (built by `cd src/filament-runtime && npm ci && npm run build`). It is present on this machine (4.5k). The app build copies it and MUST fail with an actionable message if it is absent.
- **Generator invocation is via the existing CLI unchanged**, always with `--runtime ./filament.js` (sets the import specifier and bypasses the FIL-WIRING specifier search).
- **Branch:** trunk-based on `main` (repo convention: no remote, all prior specs/commits on `main`). Commit per task.
- **House style:** DECISIONS.md is append-only, English technical prose matching existing entries; next number is **#91**.

**Verified facts (do not re-derive):**
- Generator DLL: `src/Filament.Generator/bin/$(Configuration)/net10.0/Filament.Generator.dll`.
- Emitted module for Counter: `import { signal, effect, setText, listen, insert } from './filament.js';` and `export function mount(target) { … }`.
- Baseline stylesheet to reuse: `baseline/Counter.Blazor/wwwroot/css/app.css` (795 B).
- `dotnet --version` = 10.0.301; no `global.json`.

---

## Task 1: Project skeleton that builds and serves static files

Stand up the Web-SDK host with the Razor-SDK exclusion, committed static content, and a launch profile — **before** any Filament compile wiring, so the "host builds and does not compile `App.razor` as a component" concern is isolated and proven on its own.

**Files:**
- Create: `examples/FilamentApp/FilamentApp.csproj`
- Create: `examples/FilamentApp/Program.cs`
- Create: `examples/FilamentApp/App.razor`
- Create: `examples/FilamentApp/wwwroot/index.html`
- Create: `examples/FilamentApp/wwwroot/css/app.css`
- Create: `examples/FilamentApp/Properties/launchSettings.json`
- Modify: `Filament.sln` (via `dotnet sln add`)

**Interfaces:**
- Produces: a buildable `examples/FilamentApp/FilamentApp.csproj` whose `wwwroot` is the web root. Task 2 adds the compile/copy targets to this same csproj and writes `wwwroot/App.g.js` + `wwwroot/filament.js`. `index.html` already imports `./App.g.js` and calls `mount`, so Task 2 needs no HTML change.

- [ ] **Step 1: Create the csproj (host only, Razor exclusion in place)**

`examples/FilamentApp/FilamentApp.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>

    <!-- Filament compiles App.razor to JS. The Razor SDK must NOT compile it as a
         Blazor component: Filament owns .razor here, and its product is JS, not a
         .NET component type. -->
    <EnableDefaultRazorComponentItems>false</EnableDefaultRazorComponentItems>
  </PropertyGroup>

  <ItemGroup>
    <!-- Visible in Rider's tree, not compiled, not published as content. -->
    <None Include="App.razor" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create the dev host `Program.cs`**

`examples/FilamentApp/Program.cs`:

```csharp
// Dev-only static host: serves wwwroot (index.html + the JS Filament emitted at build).
// This Kestrel host never ships to the browser -- the deployed artifact is the static
// files under wwwroot. "No .NET in the browser" holds; this is the Blazor-WASM dev model.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseDefaultFiles();   // "/" -> wwwroot/index.html
app.UseStaticFiles();    // App.g.js, filament.js, css served with correct MIME types

app.Run();
```

- [ ] **Step 3: Create `App.razor` (the component you edit — clean Counter)**

`examples/FilamentApp/App.razor`:

```razor
@* The Filament component. Filament.Generator compiles this to wwwroot/App.g.js on build.
   Blank lines between siblings are part of the shared DOM contract (they become real
   text nodes) -- keep them. *@

<h1 id="title">Counter</h1>

<p>Current count: <span id="counter-value">@currentCount</span></p>

<button id="increment" @onclick="Increment">Click me</button>

@code {
    private int currentCount = 0;

    private void Increment()
    {
        currentCount++;
    }
}
```

- [ ] **Step 4: Create `wwwroot/index.html` (mount harness inlined)**

`examples/FilamentApp/wwwroot/index.html`:

```html
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Filament App — Counter</title>
    <link rel="stylesheet" href="css/app.css" />
    <link rel="icon" href="data:," />
</head>
<body>
    <div id="app">Loading…</div>
    <script type="module">
      import { mount } from './App.g.js';
      const app = document.getElementById('app');
      app.textContent = '';
      mount(app);
    </script>
</body>
</html>
```

- [ ] **Step 5: Create `wwwroot/css/app.css` (reuse the Counter baseline sheet)**

Run: `cp baseline/Counter.Blazor/wwwroot/css/app.css examples/FilamentApp/wwwroot/css/app.css`
Expected: file copied (795 B). This is committed static content the app owns (not a build copy).

- [ ] **Step 6: Create the launch profile**

`examples/FilamentApp/Properties/launchSettings.json`:

```json
{
  "profiles": {
    "FilamentApp": {
      "commandName": "Project",
      "launchBrowser": true,
      "applicationUrl": "http://localhost:5100",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

- [ ] **Step 7: Add the project to the solution**

Run: `dotnet sln Filament.sln add examples/FilamentApp/FilamentApp.csproj`
Expected: `Project 'examples/FilamentApp/FilamentApp.csproj' added to the solution.`

- [ ] **Step 8: Build the host and verify it compiles**

Run: `dotnet build examples/FilamentApp/FilamentApp.csproj -c Debug`
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 9: Verify the Razor SDK did NOT compile `App.razor` as a component**

Run: `find examples/FilamentApp/obj -name "App.razor.g.cs" ; echo "exit=$?"`
Expected: no `App.razor.g.cs` found (empty output). Filament owns `App.razor`, not the Razor SDK.

Also run: `dotnet build examples/FilamentApp/FilamentApp.csproj -c Debug 2>&1 | grep -i "razor" || echo "no razor-component build for App.razor"`
Expected: `no razor-component build for App.razor`.

- [ ] **Step 10: Verify the host serves static content**

First drop a placeholder so there is something to serve (Task 2 will generate the real files):
Run: `printf 'export function mount(t){t.textContent="ok";}\n' > examples/FilamentApp/wwwroot/App.g.js && printf '// placeholder\n' > examples/FilamentApp/wwwroot/filament.js`

Start the app in the background:
Run: `dotnet run --project examples/FilamentApp/FilamentApp.csproj -c Debug &` then wait ~3s.
Run: `curl -s -o /dev/null -w "%{http_code} %{content_type}\n" http://localhost:5100/` → expected `200 text/html`.
Run: `curl -s -o /dev/null -w "%{http_code} %{content_type}\n" http://localhost:5100/App.g.js` → expected `200 text/javascript` (or `application/javascript`).
Stop the app (kill the background `dotnet run`).
Then remove the placeholders: `rm -f examples/FilamentApp/wwwroot/App.g.js examples/FilamentApp/wwwroot/filament.js`.

- [ ] **Step 11: Commit**

```bash
git add examples/FilamentApp/FilamentApp.csproj examples/FilamentApp/Program.cs \
        examples/FilamentApp/App.razor examples/FilamentApp/wwwroot/index.html \
        examples/FilamentApp/wwwroot/css/app.css examples/FilamentApp/Properties/launchSettings.json \
        Filament.sln
git commit -m "feat(app): scaffold examples/FilamentApp Web-SDK host (Razor-SDK excluded from App.razor)"
```

---

## Task 2: MSBuild compile pipeline (generate JS, copy runtime, incremental, guarded)

Wire Build to compile `App.razor` → `wwwroot/App.g.js` and copy the prebuilt runtime, with incremental skips and an actionable missing-runtime error.

**Files:**
- Modify: `examples/FilamentApp/FilamentApp.csproj` (add `ProjectReference` + two targets)
- Modify: `.gitignore` (ignore the two generated files)

**Interfaces:**
- Consumes: `Filament.Generator.dll` at `src/Filament.Generator/bin/$(Configuration)/net10.0/`, and `src/filament-runtime/dist/filament.js`.
- Produces: `wwwroot/App.g.js` (imports `./filament.js`, exports `mount`) and `wwwroot/filament.js`, both at build time, both gitignored. Task 3 verifies the end-to-end run against these.

- [ ] **Step 1: Add the ProjectReference and MSBuild targets to the csproj**

Edit `examples/FilamentApp/FilamentApp.csproj` — add these two `ItemGroup`/`Target` blocks before `</Project>` (keep the existing PropertyGroup/ItemGroup from Task 1):

```xml
  <PropertyGroup>
    <FilamentGeneratorDll>$(MSBuildProjectDirectory)/../../src/Filament.Generator/bin/$(Configuration)/net10.0/Filament.Generator.dll</FilamentGeneratorDll>
    <FilamentRuntimeJs>$(MSBuildProjectDirectory)/../../src/filament-runtime/dist/filament.js</FilamentRuntimeJs>
    <FilamentAppRazor>$(MSBuildProjectDirectory)/App.razor</FilamentAppRazor>
    <FilamentAppJs>$(MSBuildProjectDirectory)/wwwroot/App.g.js</FilamentAppJs>
    <FilamentWwwrootRuntimeJs>$(MSBuildProjectDirectory)/wwwroot/filament.js</FilamentWwwrootRuntimeJs>
  </PropertyGroup>

  <ItemGroup>
    <!-- Build the generator first; we invoke its DLL (AfterTargets=Build), not link its assembly. -->
    <ProjectReference Include="../../src/Filament.Generator/Filament.Generator.csproj"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>

  <!-- Compile App.razor -> wwwroot/App.g.js via Filament.Generator. AfterTargets=Build so the
       generator ProjectReference is already built; Inputs/Outputs make an unchanged .razor a no-op. -->
  <Target Name="CompileFilament"
          AfterTargets="Build"
          Inputs="$(FilamentAppRazor)"
          Outputs="$(FilamentAppJs)">
    <Error Condition="!Exists('$(FilamentGeneratorDll)')"
           Text="Filament.Generator.dll not found at $(FilamentGeneratorDll). Build the solution so the generator is compiled first." />
    <Message Importance="high" Text="Filament: App.razor -> wwwroot/App.g.js" />
    <Exec Command="dotnet &quot;$(FilamentGeneratorDll)&quot; &quot;$(FilamentAppRazor)&quot; &quot;$(FilamentAppJs)&quot; --runtime ./filament.js" />
  </Target>

  <!-- Copy the prebuilt runtime next to the emitted module (native ES-module import). -->
  <Target Name="CopyFilamentRuntime"
          AfterTargets="Build"
          Inputs="$(FilamentRuntimeJs)"
          Outputs="$(FilamentWwwrootRuntimeJs)">
    <Error Condition="!Exists('$(FilamentRuntimeJs)')"
           Text="filament.js not found at $(FilamentRuntimeJs). Run 'npm ci &amp;&amp; npm run build' in src/filament-runtime first." />
    <Copy SourceFiles="$(FilamentRuntimeJs)" DestinationFiles="$(FilamentWwwrootRuntimeJs)" />
  </Target>
```

- [ ] **Step 2: Gitignore the generated files**

Append to `.gitignore` (near the existing generated `.g.js` block, lines 33–40):

```
# examples/FilamentApp: emitted/copied at build, never committed (same rule as samples/*.g.js).
examples/FilamentApp/wwwroot/App.g.js
examples/FilamentApp/wwwroot/filament.js
```

- [ ] **Step 3: Clean build and verify both files are produced**

Run: `rm -f examples/FilamentApp/wwwroot/App.g.js examples/FilamentApp/wwwroot/filament.js && dotnet build examples/FilamentApp/FilamentApp.csproj -c Debug`
Expected: `Build succeeded.`; build output includes `Filament: App.razor -> wwwroot/App.g.js`.

Run: `test -f examples/FilamentApp/wwwroot/App.g.js && test -f examples/FilamentApp/wwwroot/filament.js && echo "both present"`
Expected: `both present`.

- [ ] **Step 4: Verify the emitted module shape**

Run: `grep -nE "from './filament.js'|export function mount" examples/FilamentApp/wwwroot/App.g.js`
Expected: a line `import { signal, effect, setText, listen, insert } from './filament.js';` and `export function mount(target) {`.

Run: `head -1 examples/FilamentApp/wwwroot/App.g.js`
Expected: `// GENERATED by Filament.Generator from App.razor. DO NOT EDIT.`

- [ ] **Step 5: Verify incremental no-op (unchanged `.razor` skips generation)**

Run: `dotnet build examples/FilamentApp/FilamentApp.csproj -c Debug -v minimal 2>&1 | grep -c "App.razor -> wwwroot/App.g.js" || true`
Expected: `0` — the `CompileFilament` message does NOT appear the second time (Inputs/Outputs up-to-date).

- [ ] **Step 6: Verify the missing-runtime guard**

Run: `mv src/filament-runtime/dist/filament.js src/filament-runtime/dist/filament.js.bak && rm -f examples/FilamentApp/wwwroot/filament.js && dotnet build examples/FilamentApp/FilamentApp.csproj -c Debug 2>&1 | grep -o "Run 'npm ci .* in src/filament-runtime first." ; mv src/filament-runtime/dist/filament.js.bak src/filament-runtime/dist/filament.js`
Expected: the build fails and prints `Run 'npm ci && npm run build' in src/filament-runtime first.` Restore leaves `dist/filament.js` back in place.

Run (re-establish good state): `dotnet build examples/FilamentApp/FilamentApp.csproj -c Debug` → `Build succeeded.`

- [ ] **Step 7: Confirm generated files are ignored by git**

Run: `git status --porcelain examples/FilamentApp/wwwroot/`
Expected: only `index.html` and `css/app.css` may appear (already committed in Task 1) — NOT `App.g.js` or `filament.js`.

- [ ] **Step 8: Commit**

```bash
git add examples/FilamentApp/FilamentApp.csproj .gitignore
git commit -m "feat(app): compile App.razor->JS on build + copy runtime (incremental, guarded)"
```

---

## Task 3: End-to-end run verification, README, DECISIONS #91, memory

Prove F5 works (Kestrel serves, the emitted reactive JS runs and increments), then document and record.

**Files:**
- Create: `examples/FilamentApp/README.md`
- Modify: `DECISIONS.md` (append #91)
- Modify: memory index + a memory file (see Step 5)

**Interfaces:**
- Consumes: the built project from Tasks 1–2.

- [ ] **Step 1: Run the app and verify it serves the generated app**

Run (background): `dotnet run --project examples/FilamentApp/FilamentApp.csproj -c Debug &` then wait ~3s.
Run: `curl -s http://localhost:5100/ | grep -c "id=\"app\""` → expected `1`.
Run: `curl -s http://localhost:5100/App.g.js | grep -c "export function mount"` → expected `1`.
Run: `curl -s http://localhost:5100/filament.js | grep -c "signal"` → expected `≥1`.
Leave the app running for Step 2 (or restart it there).

- [ ] **Step 2: Browser end-to-end — the reactive counter actually increments**

Using the chrome-devtools MCP tools (load schemas via ToolSearch `select:mcp__chrome-devtools__navigate_page,mcp__chrome-devtools__click,mcp__chrome-devtools__evaluate_script,mcp__chrome-devtools__new_page` as needed):
1. `navigate_page` → `http://localhost:5100/`.
2. `evaluate_script` → return `document.getElementById('counter-value').textContent` → expected `"0"`.
3. `click` the `#increment` button (take a snapshot first to get its uid).
4. `evaluate_script` → return `document.getElementById('counter-value').textContent` → expected `"1"`.

Expected: count goes `0 → 1` — the generator's emitted reactive JS runs in the browser, served by the Kestrel dev host. Stop the background app afterward.

(Fallback if MCP browser is unavailable: `node -e` with happy-dom is out of scope; instead assert the emitted `App.g.js` contains `listen(_el` and `effect(() =>` — the click handler and reactive text binding — via `grep`, and rely on the Counter's existing generator tests for behavior. Prefer the real browser check.)

- [ ] **Step 3: Write the app README**

`examples/FilamentApp/README.md`:

```markdown
# FilamentApp — a runnable Filament app

A real .NET web project. Build compiles `App.razor` → JavaScript with `Filament.Generator`;
Run (F5) serves the static output on Kestrel and opens the browser. No .NET ships to the
browser — the built app is `wwwroot/index.html` + the emitted `App.g.js` + the tiny runtime
`filament.js`, loaded as native ES modules.

## One-time prerequisite

The runtime is TypeScript; build it once so `filament.js` exists:

    cd src/filament-runtime && npm ci && npm run build

## Open in Rider

Open `Filament.sln`, set **FilamentApp** as the startup project, press the green **Run** (F5):
Build compiles `App.razor` → `wwwroot/App.g.js`, copies `filament.js`, launches Kestrel, and
opens `http://localhost:5100` on the Counter. Click **Click me** — the count increments.

## Edit

Edit `App.razor` and rebuild. If a construct is outside the Filament subset, the generator
**refuses** and the build fails with a diagnostic (by design — it never emits wrong JS).

## What's generated (not committed)

`wwwroot/App.g.js` and `wwwroot/filament.js` are produced at build time and gitignored.
```

- [ ] **Step 4: Append DECISIONS #91**

First read the tail to match the exact house format:
Run: `tail -40 DECISIONS.md`

Then append a `## #91` entry (English, in-style) recording: the deferred spec §4.3 packaging concern is picked up as `examples/FilamentApp` — a `Microsoft.NET.Sdk.Web` project whose Build compiles `App.razor`→JS (generator `ProjectReference` + `AfterTargets=Build` targets, `--runtime ./filament.js`), whose F5 serves the static output via a dev-only Kestrel host (Blazor-WASM model; thesis intact), with the Razor SDK excluded from `App.razor` (`EnableDefaultRazorComponentItems=false`) so Filament owns it. State the four owner decisions (MSBuild-integrated csproj; native ES modules / pure-.NET build; ASP.NET Core dev host; Counter). State the firewall explicitly: **no generator emitted bytes changed, no BENCH entry (nothing measured), runtime source untouched, no subset widening.** State the disclosed arbitrage: runnable JS is never committed, so the app copies the prebuilt runtime and requires a one-time `npm run build`; the app's own build stays pure-.NET. Note deferred follow-ons: the `dotnet new` template, generator-as-tool/NuGet packaging, `dotnet watch`.

- [ ] **Step 5: Update memory**

Create `/Users/phmatray/.claude/projects/-Users-phmatray-Repositories-dotnet-Filament/memory/filament-app-rider-project.md` (type `project`) capturing: `examples/FilamentApp` is the first runnable Filament app — a Web-SDK project, Build compiles `App.razor`→JS (generator ProjectReference + AfterTargets=Build, `--runtime ./filament.js`), F5 = dev-only Kestrel static host, Razor SDK excluded from `.razor`; generated JS gitignored; one-time `npm run build` of the runtime is the prereq; DX/packaging (spec §4.3), **not** a measurement (no BENCH). Link `[[double-division-widened-subset]]`. Next step: the `dotnet new` template wraps this. Then add a one-line pointer to `MEMORY.md`.

- [ ] **Step 6: Final verification and commit**

Run: `dotnet build Filament.sln -c Debug 2>&1 | tail -3` → expected `Build succeeded.` (whole solution still builds).
Run: `git diff --stat -- src/filament-runtime src/Filament.Generator BENCH.md` → expected **empty** (firewall holds; no generator/runtime/BENCH change).
Run: `git status --porcelain` → only the new/edited files of this task (README, DECISIONS.md, and note memory lives outside the repo).

```bash
git add examples/FilamentApp/README.md DECISIONS.md
git commit -m "docs(app): DECISIONS #91 + README for examples/FilamentApp (runnable Rider project)"
```

---

## Self-Review (completed by author)

- **Spec coverage:** every success criterion in the design maps to a step — (1) sln add → T1S7; (2) compile+copy, no component build → T2S3/T2S4 + T1S9; (3) F5 counter increments → T3S1/T3S2; (4) incremental no-op → T2S5; (5) missing-runtime error → T2S6; (6) firewall → T3S6.
- **Placeholder scan:** none — all files have complete content; all commands have expected output.
- **Type/name consistency:** csproj property names (`FilamentGeneratorDll`, `FilamentRuntimeJs`, `FilamentAppJs`, `FilamentWwwrootRuntimeJs`), target names (`CompileFilament`, `CopyFilamentRuntime`), port `5100`, and paths are identical across tasks.
- **Ordering risk noted:** targets are `AfterTargets="Build"` (not `BeforeBuild`) specifically so the generator `ProjectReference` is built before the DLL is invoked — if a clean build ever reports the DLL missing, that is the symptom to check first.
