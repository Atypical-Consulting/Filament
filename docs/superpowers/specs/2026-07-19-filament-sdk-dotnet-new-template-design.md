# `dotnet new filament` — a distributable template + `Filament.Sdk` package — design

**Goal:** `dotnet new filament -n MyApp`, run in **any folder** (not just this repo), scaffolds a
self-contained Filament app that opens in Rider and runs on F5, with **no repo-relative paths**. The
compile-`.razor`→JS step and the runtime are delivered by an **SDK-style NuGet package**
(`Filament.Sdk`) the scaffolded app references.

**Status:** design approved by the owner through three decisions (template reach = distributable; delivery
= SDK-style NuGet build-targets package; the section-8 design questions). Next step: implementation plan.

**This completes the arc `examples/FilamentApp` began.** The app (decision #91) proved the compile+run
mechanics in-repo with a `ProjectReference` + inline targets. This packages those exact mechanics so a
developer can scaffold the same app anywhere. It is **DX/packaging** — no generator emitted bytes, no
BENCH number, no subset change. The firewall is section 6.

---

## Context — why the app's wiring is not portable, and what "distributable" forces

`examples/FilamentApp` reaches the generator via `<ProjectReference Include="../../src/Filament.Generator">`
and copies `../../src/filament-runtime/dist/filament.js`. Both are **repo-relative** — a scaffolded app in
`~/anywhere` has no such paths. Distributable therefore forces two deliveries with **no repo dependency**:

- **The generator**, reachable without a project reference. Chosen form (owner): an **SDK-style NuGet
  package** whose `build/<PackageId>.targets` is auto-imported into the consuming project and runs the
  generator (bundled in the package). Rejected: a global/local `dotnet` tool (per-project version pinning
  and feed friction), and a repo `ProjectReference` (not portable).
- **The runtime `filament.js`**, without committing runnable JS into *this* repo (the repo rule: *nothing
  runnable is committed*). Chosen form: the package **bundles** the prebuilt `filament.js` (packed from the
  gitignored `dist/`), and the SDK targets `<Copy>` it into the scaffolded app's `wwwroot`.

### A clean consequence of the SDK-targets choice: the generator is untouched

Because the SDK's own targets perform the runtime `<Copy>` from the bundled file, the generator needs **no**
`--emit-runtime` option and **no** source change. It is bundled as-is and invoked via its existing CLI
(`<in> <out> --runtime ./filament.js`). The firewall holds completely: not one generator byte changes, and
the measured path (`bench/build-filament.sh` invoking `dotnet run --project src/Filament.Generator`) is
unaffected.

### Owner decisions locked before this spec

1. **Reach:** distributable — usable in any folder (over an in-repo-only scaffolder).
2. **Delivery:** an SDK-style **NuGet build-targets package** `Filament.Sdk` (over a global dotnet tool, or
   a local tool-manifest + local feed).
3. **Design questions (section 8 of the presented design):** bundle the *published* generator (its 6-DLL
   closure); resolve the local package via a **one-time global `dotnet nuget add source`** (scaffolded app
   carries no `nuget.config`); template at `templates/filament/`, local feed at `artifacts/nuget/`
   (gitignored).

**Verified facts (do not re-derive):**
- `dotnet new install` and `dotnet pack` both work here; SDK 10.0.301.
- The generator's dependency closure is **6 DLLs**: `Filament.Generator.dll`, `Filament.Subset.dll`,
  `Microsoft.AspNetCore.Razor.Language.dll`, `Microsoft.CodeAnalysis.CSharp.dll`,
  `Microsoft.CodeAnalysis.Razor.dll`, `Microsoft.CodeAnalysis.dll` (plus `.deps.json`/`.runtimeconfig.json`
  from `dotnet publish`).
- Emitted module for the Counter: `import { signal, effect, setText, listen, insert } from './filament.js';`
  + `export function mount(target)`. The scaffolded `index.html` imports `./App.g.js` and calls `mount`.

---

## The design

### 1. The `Filament.Sdk` package contents

```
Filament.Sdk.<version>.nupkg
  build/
    Filament.Sdk.props            (optional; property defaults)
    Filament.Sdk.targets          auto-imported by NuGet into the consuming project
  tools/
    Filament.Generator.dll        the published generator + its 6-DLL closure
    Filament.Subset.dll  Microsoft.*.dll  Filament.Generator.deps.json  …
  runtime/
    filament.js                   the prebuilt runtime (packed from src/filament-runtime/dist)
```

NuGet auto-imports `build/<PackageId>.targets` (and `.props`) into any project that references the package
— that is the whole mechanism by which a bare `<PackageReference>` makes Build compile `.razor`.

### 2. `build/Filament.Sdk.targets` — the compile + copy, sourced from inside the package

The same two `AfterTargets="Build"` targets proven in `examples/FilamentApp`, but with paths resolved
relative to the imported targets file via `$(MSBuildThisFileDirectory)` (so they point inside the restored
package, wherever NuGet put it):

```xml
<Project>
  <PropertyGroup>
    <FilamentGeneratorDll>$(MSBuildThisFileDirectory)../tools/Filament.Generator.dll</FilamentGeneratorDll>
    <FilamentRuntimeJs>$(MSBuildThisFileDirectory)../runtime/filament.js</FilamentRuntimeJs>
    <FilamentEntry Condition="'$(FilamentEntry)' == ''">$(MSBuildProjectDirectory)/App.razor</FilamentEntry>
    <FilamentAppJs>$(MSBuildProjectDirectory)/wwwroot/App.g.js</FilamentAppJs>
    <FilamentWwwrootRuntimeJs>$(MSBuildProjectDirectory)/wwwroot/filament.js</FilamentWwwrootRuntimeJs>
  </PropertyGroup>

  <Target Name="FilamentCompile" AfterTargets="Build"
          Inputs="$(FilamentEntry)" Outputs="$(FilamentAppJs)">
    <Message Importance="high" Text="Filament: $(FilamentEntry) -> wwwroot/App.g.js" />
    <Exec Command="dotnet &quot;$(FilamentGeneratorDll)&quot; &quot;$(FilamentEntry)&quot; &quot;$(FilamentAppJs)&quot; --runtime ./filament.js" />
  </Target>

  <Target Name="FilamentCopyRuntime" AfterTargets="Build"
          Inputs="$(FilamentRuntimeJs)" Outputs="$(FilamentWwwrootRuntimeJs)">
    <Copy SourceFiles="$(FilamentRuntimeJs)" DestinationFiles="$(FilamentWwwrootRuntimeJs)" />
  </Target>
</Project>
```

The generator is invoked out-of-process (`dotnet "<dll>"`), not as an in-process MSBuild Task — so its
Razor 6.0.36 / Roslyn 5.6.0 closure never loads into MSBuild and cannot conflict with it. `FilamentEntry`
defaults to `App.razor` and is overridable, leaving room for the deferred multi-component case.

### 3. The packaging project `src/Filament.Sdk/Filament.Sdk.csproj`

A packaging-only project (`<IsPackable>true</IsPackable>`, `<IncludeBuildOutput>false</IncludeBuildOutput>`
— it ships no assembly of its own). On `dotnet pack`:

- **Publishes** `Filament.Generator` (framework-dependent, same TFM) into a staging dir and packs that
  output as `tools/**`.
- **Bundles** `src/filament-runtime/dist/filament.js` as `runtime/filament.js` — with an `<Error>` guard
  naming the fix (`run 'npm ci && npm run build' in src/filament-runtime first`) if it is missing, exactly
  as `examples/FilamentApp` guards it.
- **Packs** `build/Filament.Sdk.targets` (+ optional `.props`).
- Sets package metadata: `PackageId=Filament.Sdk`, `Version=0.1.0`, license/description.
- `PackageOutputPath` → `artifacts/nuget/` (the local feed).

Added to `Filament.sln` under a `src` grouping. `artifacts/` is **gitignored** (nupkgs are build artifacts,
same rule as `bench/publish/`).

### 4. The template `templates/filament/`

```
templates/filament/
  .template.config/template.json
  FilamentApp.csproj            Microsoft.NET.Sdk.Web + <PackageReference Include="Filament.Sdk" Version="0.1.0"/>
                                + EnableDefaultRazorComponentItems=false + <None Include="App.razor"/>
  Program.cs                    minimal Kestrel static host (identical to examples/FilamentApp)
  App.razor                     the Counter (clean)
  wwwroot/index.html            inline module: import { mount } from './App.g.js'; mount(app)
  wwwroot/css/app.css
  Properties/launchSettings.json
  .gitignore                    ignores the scaffolded app's wwwroot/App.g.js + wwwroot/filament.js
```

`template.json`:

```json
{
  "$schema": "http://json.schemastore.org/template",
  "identity": "Filament.App",
  "name": "Filament App",
  "shortName": "filament",
  "sourceName": "FilamentApp",
  "tags": { "language": "C#", "type": "project" }
}
```

`sourceName: "FilamentApp"` means `-n MyApp` renames the project file, assembly, and any `FilamentApp`
token to `MyApp`. The scaffolded csproj has **no** `ProjectReference` and **no** inline targets — only the
`<PackageReference Include="Filament.Sdk">`, which brings the compile step by auto-import. No `nuget.config`
in the scaffolded output (portable).

### 5. Distribution / one-time setup

Not published to NuGet.org (POC). The SDK resolves from the local feed via a one-time **global** source add
— the honest parallel to installing a tool once:

```bash
# once, after building the repo (and building the runtime: npm run build in src/filament-runtime):
dotnet pack src/Filament.Sdk -c Release                        # -> artifacts/nuget/Filament.Sdk.0.1.0.nupkg
dotnet nuget add source "$PWD/artifacts/nuget" -n filament-local
dotnet new install ./templates/filament

# then, anywhere:
dotnet new filament -n MyApp && cd MyApp && dotnet run
```

Documented in a `templates/filament/README.md` and the app README. The `nuget.config`-in-template
alternative (a baked absolute feed path) is noted as the fallback if the global source add is unwanted.

---

## Firewall — what this must not touch

- **Generator source byte-untouched.** Bundled and invoked via its existing CLI; `--runtime` is already
  supported. Not one emitted module byte changes.
- **Runtime source untouched.** The package bundles its *built output*; `src/filament-runtime` is not
  edited.
- **No measurement impact.** No BENCH entry, no oracle run, no C1/C3/C4 artifact. Bundling/minification
  stay exclusively in the measured path. This is packaging.
- **No subset/DECISIONS-subset change.** The scaffolded app compiles the Counter, which compiles today.
- **`examples/FilamentApp` unchanged.** It remains the in-repo reference (repo-path wiring). Migrating it
  to consume the SDK is a deferred, optional follow-on.
- **All 270 tests stay green** (197 generator + 55 subset + 18 analyzer) — the count is the proof nothing in
  the compiler moved.

A DECISIONS.md entry (#92) records this as the distributable-template packaging; memory updated. No BENCH
entry (correct — nothing measured).

---

## Explicitly deferred (not this slice)

- Publishing `Filament.Sdk` / the template to **NuGet.org** (local feed only here).
- **Multi-`.razor` component discovery** — the SDK compiles `App.razor` (`FilamentEntry`) by default;
  compiling/mounting several components is a later slice.
- **`dotnet watch`** hot-reload on `.razor` edits.
- Migrating **`examples/FilamentApp`** to consume `Filament.Sdk` (it stays on the ProjectReference wiring).
- A **minified** production bundle for scaffolded apps (measured path only).

---

## Success criteria

1. `dotnet pack src/Filament.Sdk -c Release` produces `artifacts/nuget/Filament.Sdk.0.1.0.nupkg` containing
   `build/Filament.Sdk.targets`, `tools/Filament.Generator.dll` (+ its closure), and `runtime/filament.js`.
2. After `dotnet nuget add source` + `dotnet new install ./templates/filament`, `dotnet new filament -n MyApp`
   run **in a temp dir outside the repo** scaffolds a buildable project.
3. `dotnet build` of that scaffolded app compiles `App.razor` → `wwwroot/App.g.js` (imports `./filament.js`,
   exports `mount`) and drops `wwwroot/filament.js` — with no repo paths and no `App.razor.g.cs`.
4. `dotnet run` of the scaffolded app serves it; the Counter increments `#counter-value` 0→1 in a browser.
5. The missing-runtime guard fires at pack time with the actionable message if `dist/filament.js` is absent.
6. Generator source, runtime source, BENCH.md unchanged; all 270 tests green; `examples/FilamentApp` still
   builds and runs.
