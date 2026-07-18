# `dotnet new filament` template + `Filament.Sdk` package — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `dotnet new filament -n MyApp`, run in any folder, scaffolds a self-contained Filament app (a `<PackageReference Include="Filament.Sdk">`, no repo paths) that builds `App.razor`→JS and runs on F5.

**Architecture:** A packaging project `src/Filament.Sdk` produces `Filament.Sdk.0.1.0.nupkg` containing `build/Filament.Sdk.targets` (auto-imported; the compile+copy targets, sourced via `$(MSBuildThisFileDirectory)`), `tools/` (the framework-dependent published generator, ~6 DLLs), and `runtime/filament.js` (bundled from the built `dist/`). The template `templates/filament` scaffolds an app that references the package. The nupkg lands in a gitignored local feed `artifacts/nuget/`, resolved via a one-time global `dotnet nuget add source`. The generator is invoked out-of-process via its existing CLI — **zero generator source change**.

**Tech Stack:** .NET 10 SDK (`dotnet pack`, `dotnet new` engine — both verified present), NuGet build/targets package, the existing `Filament.Generator` (published into the package) and `filament-runtime` (prebuilt `dist/filament.js`).

## Global Constraints

- **Firewall — do not touch:** `src/Filament.Generator` source (bundled + invoked via existing CLI, not one byte changed), `src/filament-runtime` source, `BENCH.md`, any measurement artifact (`bench/**`, `demo/**`), any subset/analyzer code. No BENCH entry (nothing measured). `examples/FilamentApp` stays unchanged.
- **No runnable JS committed.** `filament.js` is bundled into the nupkg at pack time (from the gitignored `dist/`); it is never committed as repo source. `artifacts/` (the nupkg feed) is gitignored. The scaffolded app's generated JS is gitignored by the template's own `.gitignore`.
- **Runtime prerequisite (one-time):** `src/filament-runtime/dist/filament.js` must exist (built by `cd src/filament-runtime && npm ci && npm run build`); present on this machine (4.5k). Pack MUST fail with an actionable message if absent.
- **Version:** `Filament.Sdk` = `0.1.0`; the template's `<PackageReference>` pins `0.1.0`.
- **Branch:** trunk-based on `main` (repo convention). Commit per task.
- **House style:** DECISIONS.md is append-only, French, matching existing entries; next number is **#92**.

**Verified facts (do not re-derive):**
- Generator closure = 6 DLLs (`Filament.Generator`, `Filament.Subset`, `Microsoft.AspNetCore.Razor.Language`, `Microsoft.CodeAnalysis.CSharp`, `Microsoft.CodeAnalysis.Razor`, `Microsoft.CodeAnalysis`) + `.deps.json`/`.runtimeconfig.json` from publish.
- Emitted Counter module: `import { signal, effect, setText, listen, insert } from './filament.js';` + `export function mount(target)`.
- `examples/FilamentApp` content (`Program.cs`, `App.razor`, `wwwroot/index.html`, `wwwroot/css/app.css`, `Properties/launchSettings.json`) is proven and reused verbatim as template content.
- Scratchpad (outside the repo) for the end-to-end scaffold: `/private/tmp/claude-501/-Users-phmatray-Repositories-dotnet-Filament/37c09993-f15e-49cf-8dc6-4dbe10955234/scratchpad` — use `$SCRATCH` below.

---

## Task 1: `Filament.Sdk` package (generator + runtime + targets → nupkg)

**Files:**
- Create: `src/Filament.Sdk/Filament.Sdk.csproj`
- Create: `src/Filament.Sdk/build/Filament.Sdk.targets`
- Create: `src/Filament.Sdk/AssemblyMarker.cs` (empty-ish; only so the compiler has an input — avoids CS2008)
- Create: `src/Filament.Sdk/_._` (empty; NuGet compatibility placeholder)
- Modify: `.gitignore` (ignore `artifacts/`)
- Modify: `Filament.sln` (`dotnet sln add`)

**Interfaces:**
- Consumes: `src/Filament.Generator/Filament.Generator.csproj` (published into `tools/`), `src/filament-runtime/dist/filament.js` (bundled into `runtime/`).
- Produces: `artifacts/nuget/Filament.Sdk.0.1.0.nupkg` with `build/Filament.Sdk.targets` + `tools/Filament.Generator.dll` (+closure) + `runtime/filament.js`. Task 2's template references package `Filament.Sdk` `0.1.0`; the targets expect the consuming project to have `App.razor` and a `wwwroot/`.

- [ ] **Step 1: Create the auto-imported targets**

`src/Filament.Sdk/build/Filament.Sdk.targets`:

```xml
<Project>

  <!-- Auto-imported by NuGet into any project referencing Filament.Sdk. Paths resolve
       inside the restored package via $(MSBuildThisFileDirectory) = <pkg>/build/. -->
  <PropertyGroup>
    <FilamentGeneratorDll>$(MSBuildThisFileDirectory)../tools/Filament.Generator.dll</FilamentGeneratorDll>
    <FilamentRuntimeJs>$(MSBuildThisFileDirectory)../runtime/filament.js</FilamentRuntimeJs>
    <FilamentEntry Condition="'$(FilamentEntry)' == ''">$(MSBuildProjectDirectory)/App.razor</FilamentEntry>
    <FilamentAppJs>$(MSBuildProjectDirectory)/wwwroot/App.g.js</FilamentAppJs>
    <FilamentWwwrootRuntimeJs>$(MSBuildProjectDirectory)/wwwroot/filament.js</FilamentWwwrootRuntimeJs>
  </PropertyGroup>

  <Target Name="FilamentCompile"
          AfterTargets="Build"
          Inputs="$(FilamentEntry)"
          Outputs="$(FilamentAppJs)">
    <Error Condition="!Exists('$(FilamentGeneratorDll)')"
           Text="Filament.Sdk: generator not found at $(FilamentGeneratorDll) (package restore incomplete?)." />
    <Message Importance="high" Text="Filament: $(FilamentEntry) -&gt; wwwroot/App.g.js" />
    <Exec Command="dotnet &quot;$(FilamentGeneratorDll)&quot; &quot;$(FilamentEntry)&quot; &quot;$(FilamentAppJs)&quot; --runtime ./filament.js" />
  </Target>

  <Target Name="FilamentCopyRuntime"
          AfterTargets="Build"
          Inputs="$(FilamentRuntimeJs)"
          Outputs="$(FilamentWwwrootRuntimeJs)">
    <Copy SourceFiles="$(FilamentRuntimeJs)" DestinationFiles="$(FilamentWwwrootRuntimeJs)" />
  </Target>

</Project>
```

- [ ] **Step 2: Create the compiler-input marker and the compat placeholder**

`src/Filament.Sdk/AssemblyMarker.cs`:

```csharp
// Filament.Sdk is a packaging project (build/targets + tools + runtime). This file
// exists only so the C# compiler has an input; the built assembly is NOT packed
// (IncludeBuildOutput=false).
```

`src/Filament.Sdk/_._` — create an empty file:

Run: `: > src/Filament.Sdk/_._`
Expected: a zero-byte `_._` (packed to `lib/netstandard2.0/_._` so NuGet sees a compatible asset group).

- [ ] **Step 3: Create the packaging project**

`src/Filament.Sdk/Filament.Sdk.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking>
    <IsPackable>true</IsPackable>
    <Nullable>enable</Nullable>

    <PackageId>Filament.Sdk</PackageId>
    <Version>0.1.0</Version>
    <Authors>Filament</Authors>
    <Description>SDK-style package: compiles Filament .razor to JS on build and ships the runtime.</Description>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageOutputPath>$(MSBuildProjectDirectory)/../../artifacts/nuget</PackageOutputPath>
    <!-- NU5100: files outside lib/ (tools, runtime) are intentional. NU5128: no deps + no lib is intentional. -->
    <NoWarn>$(NoWarn);NU5100;NU5128</NoWarn>

    <GeneratorProject>$(MSBuildProjectDirectory)/../Filament.Generator/Filament.Generator.csproj</GeneratorProject>
    <RuntimeJs>$(MSBuildProjectDirectory)/../filament-runtime/dist/filament.js</RuntimeJs>
    <!-- ABSOLUTE publish dir: PublishDir passed to another project resolves relative to THAT project, so it must be absolute. -->
    <GenPublishDir>$(MSBuildProjectDirectory)/obj/gen-publish/</GenPublishDir>

    <TargetsForTfmSpecificContentInPackage>$(TargetsForTfmSpecificContentInPackage);FilamentPackContent</TargetsForTfmSpecificContentInPackage>
  </PropertyGroup>

  <!-- Static package files present at pack time. -->
  <ItemGroup>
    <None Include="build/Filament.Sdk.targets" Pack="true" PackagePath="build/" />
    <None Include="_._" Pack="true" PackagePath="lib/netstandard2.0/_._" />
  </ItemGroup>

  <!-- Guard: the runtime must be built before packing. -->
  <Target Name="FilamentCheckRuntime" BeforeTargets="Pack;FilamentPackContent">
    <Error Condition="!Exists('$(RuntimeJs)')"
           Text="filament.js not found at $(RuntimeJs). Run 'npm ci &amp;&amp; npm run build' in src/filament-runtime first." />
  </Target>

  <!-- Publish the generator (framework-dependent, no apphost) into the staging dir. -->
  <Target Name="FilamentPublishGenerator" BeforeTargets="FilamentPackContent">
    <RemoveDir Directories="$(GenPublishDir)" />
    <MSBuild Projects="$(GeneratorProject)"
             Targets="Publish"
             Properties="Configuration=$(Configuration);TargetFramework=net10.0;PublishDir=$(GenPublishDir);SelfContained=false;UseAppHost=false" />
  </Target>

  <!-- Add the generator publish output + runtime to the package. -->
  <Target Name="FilamentPackContent" DependsOnTargets="FilamentPublishGenerator">
    <ItemGroup>
      <TfmSpecificPackageFile Include="$(GenPublishDir)**/*">
        <PackagePath>tools/</PackagePath>
      </TfmSpecificPackageFile>
      <TfmSpecificPackageFile Include="$(RuntimeJs)">
        <PackagePath>runtime/filament.js</PackagePath>
      </TfmSpecificPackageFile>
    </ItemGroup>
  </Target>

</Project>
```

- [ ] **Step 4: Ignore the artifacts feed; add project to the solution**

Append to `.gitignore`:

```
# Local NuGet feed (Filament.Sdk nupkgs): build artifacts, regenerable via `dotnet pack src/Filament.Sdk`.
artifacts/
```

Run: `dotnet sln Filament.sln add src/Filament.Sdk/Filament.Sdk.csproj`
Expected: `Project ... added to the solution.`

- [ ] **Step 5: Pack and verify the nupkg contents**

Run: `dotnet pack src/Filament.Sdk -c Release`
Expected: `Successfully created package '.../artifacts/nuget/Filament.Sdk.0.1.0.nupkg'.`

Run: `unzip -l artifacts/nuget/Filament.Sdk.0.1.0.nupkg | grep -E "build/Filament.Sdk.targets|tools/Filament.Generator.dll|runtime/filament.js"`
Expected: three matching lines (targets, generator DLL, runtime).

Run: `unzip -l artifacts/nuget/Filament.Sdk.0.1.0.nupkg | grep -cE "tools/.*\.dll"`
Expected: `6` (the full generator closure is bundled).

- [ ] **Step 6: Verify the missing-runtime guard at pack time**

Run: `mv src/filament-runtime/dist/filament.js src/filament-runtime/dist/filament.js.bak && dotnet pack src/Filament.Sdk -c Release 2>&1 | grep -o "Run 'npm ci .* in src/filament-runtime first." | head -1 ; mv src/filament-runtime/dist/filament.js.bak src/filament-runtime/dist/filament.js`
Expected: prints `Run 'npm ci && npm run build' in src/filament-runtime first.` and pack fails.

Run (restore good state): `dotnet pack src/Filament.Sdk -c Release 2>&1 | grep -c "Successfully created package"` → expected `1`.

- [ ] **Step 7: Confirm firewall (generator/runtime source untouched)**

Run: `git diff --stat -- src/Filament.Generator src/filament-runtime/src`
Expected: **empty**.

- [ ] **Step 8: Commit**

```bash
git add src/Filament.Sdk/Filament.Sdk.csproj src/Filament.Sdk/build/Filament.Sdk.targets \
        src/Filament.Sdk/AssemblyMarker.cs src/Filament.Sdk/_._ .gitignore Filament.sln
git commit -m "feat(sdk): Filament.Sdk package — bundles generator + runtime + build targets"
```

---

## Task 2: The template + end-to-end scaffold, build, and run OUTSIDE the repo

**Files:**
- Create: `templates/filament/.template.config/template.json`
- Create: `templates/filament/FilamentApp.csproj`
- Create: `templates/filament/Program.cs`
- Create: `templates/filament/App.razor`
- Create: `templates/filament/wwwroot/index.html`
- Create: `templates/filament/wwwroot/css/app.css`
- Create: `templates/filament/Properties/launchSettings.json`
- Create: `templates/filament/.gitignore`

**Interfaces:**
- Consumes: package `Filament.Sdk` `0.1.0` from `artifacts/nuget/` (Task 1).
- Produces: an installed `dotnet new` template `filament`; a scaffolded app whose Build compiles `App.razor`→JS via the package.

- [ ] **Step 1: Copy the proven app content into the template**

Run:
```bash
mkdir -p templates/filament/.template.config templates/filament/wwwroot/css templates/filament/Properties
cp examples/FilamentApp/Program.cs                    templates/filament/Program.cs
cp examples/FilamentApp/App.razor                     templates/filament/App.razor
cp examples/FilamentApp/wwwroot/index.html            templates/filament/wwwroot/index.html
cp examples/FilamentApp/wwwroot/css/app.css           templates/filament/wwwroot/css/app.css
cp examples/FilamentApp/Properties/launchSettings.json templates/filament/Properties/launchSettings.json
```
Expected: five files copied (identical, proven content).

- [ ] **Step 2: Create the template's csproj (PackageReference, no repo wiring)**

`templates/filament/FilamentApp.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>

    <!-- Filament compiles App.razor to JS; the Razor SDK must not compile it as a component. -->
    <EnableDefaultRazorComponentItems>false</EnableDefaultRazorComponentItems>
  </PropertyGroup>

  <ItemGroup>
    <None Include="App.razor" />
  </ItemGroup>

  <ItemGroup>
    <!-- Brings the compile-App.razor->JS + copy-runtime targets by auto-import. -->
    <PackageReference Include="Filament.Sdk" Version="0.1.0" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Create `template.json`**

`templates/filament/.template.config/template.json`:

```json
{
  "$schema": "http://json.schemastore.org/template",
  "author": "Filament",
  "classifications": [ "Web", "Filament" ],
  "identity": "Filament.App",
  "name": "Filament App",
  "shortName": "filament",
  "sourceName": "FilamentApp",
  "tags": { "language": "C#", "type": "project" },
  "preferNameDirectory": true
}
```

- [ ] **Step 4: Create the scaffolded app's `.gitignore`**

`templates/filament/.gitignore`:

```
bin/
obj/
wwwroot/App.g.js
wwwroot/filament.js
```

- [ ] **Step 5: One-time setup — register the local feed and install the template**

Run:
```bash
dotnet nuget remove source filament-local 2>/dev/null
dotnet nuget add source "$PWD/artifacts/nuget" -n filament-local
dotnet new install ./templates/filament --force
```
Expected: the source is added; install reports `filament` among installed templates (`Template Name: Filament App, Short Name: filament`).

- [ ] **Step 6: Scaffold OUTSIDE the repo**

Run:
```bash
SCRATCH="/private/tmp/claude-501/-Users-phmatray-Repositories-dotnet-Filament/37c09993-f15e-49cf-8dc6-4dbe10955234/scratchpad"
rm -rf "$SCRATCH/MyApp" ; dotnet new filament -n MyApp -o "$SCRATCH/MyApp"
ls "$SCRATCH/MyApp"
```
Expected: creates `MyApp.csproj` (renamed from `FilamentApp.csproj` via `sourceName`), `Program.cs`, `App.razor`, `wwwroot/`, `Properties/`.

Run: `test -f "$SCRATCH/MyApp/MyApp.csproj" && grep -q 'Filament.Sdk' "$SCRATCH/MyApp/MyApp.csproj" && echo "renamed + references SDK"`
Expected: `renamed + references SDK`.

- [ ] **Step 7: Build the scaffolded app (fresh restore from the local feed)**

Clear any cached copy so the just-packed 0.1.0 is used, then build:
```bash
rm -rf ~/.nuget/packages/filament.sdk
dotnet build "$SCRATCH/MyApp/MyApp.csproj" -c Debug 2>&1 | grep -E "Filament: .*-> wwwroot/App.g.js|Build succeeded|error" | head
```
Expected: `Filament: .../App.razor -> wwwroot/App.g.js` and `Build succeeded.`

Run:
```bash
test -f "$SCRATCH/MyApp/wwwroot/App.g.js" && test -f "$SCRATCH/MyApp/wwwroot/filament.js" && echo "both present"
grep -nE "from './filament.js'|export function mount" "$SCRATCH/MyApp/wwwroot/App.g.js"
find "$SCRATCH/MyApp/obj" -name "App.razor.g.cs" | grep -q . && echo "BAD: razor-compiled" || echo "no App.razor.g.cs (Filament owns it)"
```
Expected: `both present`; the import + `export function mount` lines; `no App.razor.g.cs`.

- [ ] **Step 8: Run the scaffolded app and verify the counter increments in a browser**

Start (background, distinct port, no launch profile so no auto-browser):
`ASPNETCORE_URLS=http://localhost:5101 dotnet run --project "$SCRATCH/MyApp/MyApp.csproj" -c Debug --no-build --no-launch-profile` (run_in_background).

Then:
- `curl -s --retry-connrefused --retry 20 --retry-delay 1 http://localhost:5101/App.g.js | grep -c "export function mount"` → expected `1`.
- chrome-devtools MCP (tools already loaded this session): `navigate_page` → `http://localhost:5101/`; `evaluate_script` return `document.getElementById('counter-value').textContent` → `"0"`; `take_snapshot`, `click` the `#increment` button; `evaluate_script` again → `"1"`.

Expected: `0 → 1` — a template-scaffolded app, built from a NuGet package with no repo paths, runs the emitted reactive JS. Stop the background app.

- [ ] **Step 9: Commit the template**

```bash
git add templates/filament/
git commit -m "feat(template): dotnet new filament — SDK-referencing app, scaffolds + builds + runs anywhere"
```

---

## Task 3: Docs, DECISIONS #92, memory, firewall

**Files:**
- Create: `templates/filament/README.md`
- Modify: `README.md` (repo root — a short pointer to the template, optional but recommended)
- Modify: `DECISIONS.md` (append #92)
- Modify: memory index + a memory file

- [ ] **Step 1: Template README (the one-time setup + usage)**

`templates/filament/README.md`:

```markdown
# `dotnet new filament` — the Filament app template

Scaffolds a Filament app anywhere: a real .NET web project whose Build compiles `App.razor` → JS
(via the `Filament.Sdk` package) and whose F5 serves it on Kestrel. No .NET ships to the browser.

## One-time setup (local feed — not on NuGet.org)

    cd <this repo>
    (cd src/filament-runtime && npm ci && npm run build)   # builds filament.js
    dotnet pack src/Filament.Sdk -c Release                # -> artifacts/nuget/Filament.Sdk.0.1.0.nupkg
    dotnet nuget add source "$PWD/artifacts/nuget" -n filament-local
    dotnet new install ./templates/filament

## Use it (anywhere)

    dotnet new filament -n MyApp
    cd MyApp
    dotnet run        # or open in Rider and press F5

Edit `App.razor` and rebuild. Out-of-subset constructs make the generator refuse and the build fail
with a diagnostic — by design.
```

- [ ] **Step 2: Append DECISIONS #92**

Read the tail to match format: `tail -30 DECISIONS.md`. Then append a French, in-style `## 92.` entry recording: `dotnet new filament` + `Filament.Sdk` — the distributable form of #91. Cover the two owner decisions (distributable reach; SDK-style NuGet build-targets package). Mechanism: `build/Filament.Sdk.targets` auto-imported, sourced via `$(MSBuildThisFileDirectory)`, runs the bundled generator (`tools/`, the published 6-DLL closure) via its existing CLI and `<Copy>`s the bundled `runtime/filament.js`; the packaging project publishes the generator (absolute `PublishDir`, `UseAppHost=false`) and packs it + runtime + targets to the gitignored `artifacts/nuget/` feed. **The clean consequence: the generator has ZERO source change** (the SDK targets copy the runtime, so no `--emit-runtime`), so the firewall is tighter than the tool route. Distribution: one-time global `dotnet nuget add source` + `dotnet new install`. Firewall: no generator bytes, no BENCH, runtime source untouched, no subset widening, `examples/FilamentApp` unchanged, 270 tests green. End-to-end proof: a scaffold OUTSIDE the repo built and ran (`#counter-value` 0→1). Deferred: NuGet.org publish, multi-`.razor` discovery, `dotnet watch`, migrating `examples/FilamentApp` onto the SDK.

- [ ] **Step 3: Update memory**

Create `/Users/phmatray/.claude/projects/-Users-phmatray-Repositories-dotnet-Filament/memory/filament-dotnet-new-template.md` (type `project`): `dotnet new filament` is the distributable template (#92) built on [[filament-app-rider-project]]; `Filament.Sdk` NuGet (build/targets auto-import + tools/ published generator + runtime/filament.js), scaffolded app references it (no repo paths); local feed `artifacts/nuget/` (gitignored) + one-time global `dotnet nuget add source` + `dotnet new install ./templates/filament`; generator ZERO source change (SDK targets copy runtime); one-time `npm run build` of runtime still the prereq; NOT a measurement (no BENCH). Note the pack gotchas (absolute PublishDir, UseAppHost=false, `_._` compat placeholder, clear `~/.nuget/packages/filament.sdk` between re-tests). Then add a one-line pointer to `MEMORY.md`.

- [ ] **Step 4: Final verification**

Run: `dotnet test Filament.sln -c Debug 2>&1 | grep -E "Passed!|Failed!" ` → expected three `Passed!` lines (197 + 55 + 18 = 270).
Run: `git diff --stat -- src/Filament.Generator src/filament-runtime BENCH.md` → expected **empty**.
Run: `dotnet build examples/FilamentApp/FilamentApp.csproj -c Debug 2>&1 | grep -c "Build succeeded"` → expected `1` (the in-repo reference still works).
Run: `git status --porcelain` → only this task's files (README, DECISIONS.md; memory is outside the repo).

- [ ] **Step 5: Commit**

```bash
git add templates/filament/README.md DECISIONS.md README.md
git commit -m "docs(sdk): DECISIONS #92 + template README for dotnet new filament"
```

---

## Self-Review (completed by author)

- **Spec coverage:** every success criterion maps to a step — (1) nupkg contents → T1S5; (2) scaffold outside repo → T2S6; (3) build compiles + no razor.g.cs → T2S7; (4) run increments → T2S8; (5) missing-runtime guard → T1S6; (6) firewall + 270 tests + FilamentApp still builds → T1S7 & T3S4.
- **Placeholder scan:** none — every file has full content; every command has expected output.
- **Type/name consistency:** `Filament.Sdk` / `0.1.0` / `sourceName "FilamentApp"` / `filament` shortName / property names (`FilamentGeneratorDll`, `FilamentRuntimeJs`, `FilamentEntry`, `GenPublishDir`) identical across the package, targets, and template.
- **Known-gotcha coverage (baked in, not discovered at runtime):** absolute `PublishDir`; `UseAppHost=false`; `AssemblyMarker.cs` for CS2008; `_._` for NuGet compat; `NoWarn` NU5100/NU5128; clear `~/.nuget/packages/filament.sdk` before the scaffold build; `--no-launch-profile` + distinct port 5101 for the run. If NU1202 (package incompatible) still appears at T2S7, the `_._` placeholder is the fix already in place — re-pack and clear the cache.
```
