# A runnable Filament App — a real .NET project you open in Rider — design

**Goal:** Turn "a Filament app" from a shell-driven demo into **one real `.NET` web project** that opens
in JetBrains Rider, **compiles `App.razor` → JS on Build**, and **runs on F5** (Kestrel serves the static
output, the browser opens on the app). This is the seed for a `dotnet new filament` template later; this
slice delivers the runnable, openable project the template will be cut from.

**Status:** design approved by the owner through four decisions (below). Next step: implementation plan.

**This is DX/packaging, not subset/measurement.** `Program.cs` of the generator records the MSBuild
integration (spec §4.3) as *"a packaging concern that changes no emitted byte, so it is deferred, not
skipped."* This picks that up. It touches **zero** emitted generator bytes, **zero** BENCH numbers, the
runtime source not at all, and the measurement harness not at all. The firewall is section 8.

---

## Context — why "open the demo in Rider and compile" is not a thing today

- **The generator is a console app** (`Filament.Generator`, net10.0): `<in.razor> <out.js> [--runtime <spec>]`.
  Building a `.csproj` does **not** run it. It is invoked only by shell scripts.
- **The runtime is TypeScript** (`src/filament-runtime`) with a compile-time `__FILAMENT_STATS__` define.
  Browser-runnable `dist/filament.js` exists only after an esbuild/tsc build, and `dist/` is **gitignored**
  — only the `.ts` source is committed (the repo rule: *nothing runnable is committed*).
- **The "demo" is not a project.** `demo/build.sh` runs the generator per sample, esbuild-bundles a
  `main.js` harness, writes an `index.html`, and you serve it with `python3 -m http.server`. There is
  nothing a developer opens in Rider and presses Build/Run on.
- At runtime a Filament app is **just static files** (HTML + JS + CSS) — no .NET in the browser. So the
  .NET side is purely a **build-time compiler**; the "app project" is a build-time project whose product
  is a static web bundle, served in dev by a host (exactly Blazor WebAssembly's own dev model).

### Owner decisions locked before this spec

1. **App shape:** an **MSBuild-integrated `.csproj`** — Rider's Build compiles `.razor` → JS. (Over: an
   npm/script folder, or merely wiring the existing `demo/build.sh` into a Rider run config.)
2. **Compile output:** **native ES modules, pure-.NET build** — Build runs the generator + copies a
   prebuilt runtime `filament.js` + the committed `index.html`; **no Node/esbuild inside `dotnet build`**.
   The emitted module imports `./filament.js`; the browser loads both natively. (Over: an esbuild
   single-bundle, which would put a JS toolchain inside `dotnet build`.)
3. **Project feel:** an **ASP.NET Core dev host** (`Microsoft.NET.Sdk.Web`) — green Run arrow / F5 builds,
   launches Kestrel, opens the browser: the Blazor-WASM dev loop. Kestrel is **dev-only**; the shipped
   artifact stays pure static files. (Over: a plain SDK project + `dotnet-serve` launch profile, or a
   manual `http.server`. The owner's requirement was "it must feel like a real .NET project.")
4. **Demo component:** the **Counter** (proven, canonical, visibly reactive) — trivially swappable.

---

## The design

### 1. Project layout

```
examples/FilamentApp/                 ← new; added to Filament.sln
  FilamentApp.csproj                  Microsoft.NET.Sdk.Web + the Filament compile target
  Program.cs                          minimal Kestrel static host (dev-only)
  App.razor                           the Filament component you edit (Counter content, clean)
  Properties/launchSettings.json      Run profile: applicationUrl + launchBrowser
  wwwroot/
    index.html                        COMMITTED; <script type=module> imports App.g.js, calls mount
    css/app.css                       COMMITTED
    App.g.js                          GENERATED at build — gitignored
    filament.js                       COPIED prebuilt runtime at build — gitignored
```

The two generated files live physically under `wwwroot/` so the static host serves them from one web
root, and are **gitignored** — the same pattern the repo already uses for generated `.g.js`
(`.gitignore` lines 33–40). Committed `wwwroot/` therefore holds only hand-written static content.

### 2. `App.razor` — the component you edit

The Counter component, clean (no measurement-header essay), so it reads like a template a developer would
write:

```razor
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

The blank lines between siblings are part of the shared DOM contract (they become real `\n\n` text nodes)
— kept, matching `samples/Counter/Counter.razor`. The generator compiles this exact shape today; this
project does not exercise any new construct.

### 3. Build pipeline (the MSBuild target)

`FilamentApp.csproj` (`Microsoft.NET.Sdk.Web`):

- **Generator builds first as a dependency.** A `ProjectReference` to `Filament.Generator` with
  `ReferenceOutputAssembly="false"` (we want its build ordering and its output DLL path, not a managed
  reference into the web app). This gives fast **incremental** builds — no `dotnet run` rebuild churn.
- **Compile `.razor` → JS.** A target `CompileFilament` `BeforeTargets="Build"` that execs the generator
  DLL:
  `dotnet "<Filament.Generator.dll>" "$(MSBuildProjectDirectory)/App.razor" "$(MSBuildProjectDirectory)/wwwroot/App.g.js" --runtime ./filament.js`
  Passing `--runtime ./filament.js` sets the emitted import specifier verbatim **and** sidesteps the
  generator's FIL-WIRING specifier search (which would otherwise throw for an output path with no
  `src/filament-runtime` above it).
- **Copy the runtime.** A target `CopyFilamentRuntime` that copies the prebuilt
  `src/filament-runtime/dist/filament.js` → `wwwroot/filament.js`.
- **Incremental.** Both targets carry MSBuild `Inputs`/`Outputs` (`App.razor` → `wwwroot/App.g.js`;
  `dist/filament.js` → `wwwroot/filament.js`), so an unchanged input is skipped and Build feels instant.
- **A non-zero generator exit fails the build** (`Exec` default), surfacing a refusal diagnostic in
  Rider's build output rather than shipping wrong JS — the same contract `demo/build.sh` enforces.

### 4. Stop the Razor SDK from compiling `App.razor`

The Web SDK's Razor tooling would otherwise glob `.razor` as a `RazorComponent` and try to compile it as
a Blazor component. It must not: **Filament** is the only thing that compiles `.razor` here — into JS,
never a .NET component. In the csproj:

```xml
<PropertyGroup>
  <EnableDefaultRazorComponentItems>false</EnableDefaultRazorComponentItems>
</PropertyGroup>
<ItemGroup>
  <None Include="App.razor" />   <!-- visible in Rider, not compiled, not published -->
</ItemGroup>
```

The implementation verifies from a clean build that `dotnet build` produces no Razor-component output for
`App.razor` (no `App.razor.g.cs`, no component type), i.e. Filament owns it exclusively.

### 5. Runtime delivery — the one honest wrinkle (disclosed arbitrage)

`filament.js` exists only after the runtime's TypeScript is built, and `dist/` is gitignored — so **no
runnable JS is committed**, upholding the repo rule. Consequences, decided deliberately:

- The app's **`CopyFilamentRuntime` target errors clearly if the prebuilt runtime is missing**, naming the
  fix: *"filament.js not found at src/filament-runtime/dist/ — run `npm run build` in src/filament-runtime
  first."* No silent-empty, no Node shelled into `dotnet build`.
- Building the runtime once (`cd src/filament-runtime && npm ci && npm run build`) is a **documented
  prerequisite**, the same tier as `npm ci` for any frontend project. The **app's** build stays pure-.NET.
- The production `filament.js` folds `__FILAMENT_STATS__` out, so the app ships no `__filament` global —
  correct for a dev/demo app.

For the eventual `dotnet new` template (deferred), the runtime ships **prebuilt inside the template
package**, so an end user never runs npm. The in-repo demo copies from the repo's built runtime because it
lives right here.

### 6. Run / dev host

`Program.cs` — minimal ASP.NET Core static host:

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.UseDefaultFiles();   // "/" -> wwwroot/index.html
app.UseStaticFiles();    // serves App.g.js, filament.js, css with correct module MIME types
app.Run();
```

`Properties/launchSettings.json` — one profile with `applicationUrl` and `"launchBrowser": true`, so F5
launches Kestrel and opens the browser on the app. Kestrel is **dev-only**; the shipped artifact is the
static `wwwroot/` (no .NET in the browser). Serving via a real host (not `file://`) is also what makes the
native ES-module imports resolve with correct MIME types.

### 7. Serve / static wiring

`wwwroot/index.html` (committed) folds the old `main.js` mount harness into an inline module:

```html
<div id="app">Loading…</div>
<script type="module">
  import { mount } from './App.g.js';
  const app = document.getElementById('app');
  app.textContent = '';
  mount(app);
</script>
```

`App.g.js` imports `./filament.js`; both sit in `wwwroot/` at build time; the browser resolves the chain
natively. `css/app.css` reuses the Counter baseline stylesheet for parity of look.

### 8. Solution integration

`examples/FilamentApp/FilamentApp.csproj` is added to `Filament.sln` so the whole thing opens together in
Rider and the app appears beside the generator it depends on.

---

## Firewall — what this must not touch

- **No generator emission change.** The generator is invoked as-is via its existing CLI; not one emitted
  byte changes. `--runtime` is an already-supported flag.
- **No measurement impact.** No BENCH.md entry, no oracle run, no C1/C3/C4 artifact. Bundling and
  minification remain **exclusively** in the measured path (`bench/build-filament.sh` + `bench.mjs`); this
  project is a **viewer**, never weighed — the same firewall `demo/build.sh` already declares.
- **Runtime source byte-untouched.** We copy its built output; we do not edit `src/filament-runtime`.
- **No subset/DECISIONS widening.** Nothing here admits a new construct; the Counter compiles today.

A short DECISIONS.md entry records this as the §4.3 packaging concern being picked up (DX, not a
measurement), and memory is updated. No BENCH entry (correct — nothing was measured).

---

## Explicitly deferred (not this slice)

- The `dotnet new` **template** itself (`.template.config/template.json`) — step 2, made trivial by this
  layout (parameterize the project name, wrap, ship the prebuilt runtime in the package).
- Packaging the generator as a **dotnet tool / NuGet MSBuild targets** — this slice uses an in-repo
  `ProjectReference`. The tool/package form is what a standalone (out-of-repo) template needs.
- A **minified/bundled** production output for apps — stays out of scope on purpose (measured path only).
- **Watch / hot-reload** on `.razor` edits (`dotnet watch`) — a nice follow-on once the project builds.

---

## Success criteria

1. `examples/FilamentApp` opens in Rider from `Filament.sln`.
2. From a clean tree with the runtime prebuilt, **Build** compiles `App.razor` → `wwwroot/App.g.js` and
   copies `wwwroot/filament.js`, with no Razor-component compilation of `App.razor`.
3. **F5** launches Kestrel and opens the browser on the Counter; clicking `#increment` increments
   `#counter-value` (the emitted reactive JS runs).
4. A second Build with no `.razor` change is a no-op (incremental targets skip).
5. With the runtime **not** built, Build fails with the actionable "run `npm run build`" message — not a
   broken or empty app.
6. Generator emitted bytes, runtime source, BENCH.md, and the measurement harness are unchanged.
