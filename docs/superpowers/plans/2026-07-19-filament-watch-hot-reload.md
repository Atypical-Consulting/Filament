# `dotnet watch` hot-reload — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Edit `App.razor` under `dotnet watch --no-hot-reload run` → `FilamentCompile` regenerates `App.g.js` → the browser refreshes, for both `examples/FilamentApp` and scaffolded `dotnet new filament` apps.

**Architecture:** One MSBuild `Watch` item makes `dotnet watch` monitor the entry `.razor` (proven necessary by experiment). Ships in `Filament.Sdk.targets` (all scaffolded apps) and in `examples/FilamentApp.csproj` (parity). SDK bumps `0.1.0 → 0.1.1`; the template's `<PackageReference>` follows. `--no-hot-reload` (documented) forces rebuild-on-change so the generator re-runs.

## Global Constraints

- **Firewall — do not touch:** `src/Filament.Generator` source, `src/filament-runtime` source, `BENCH.md`, `bench/**`, `demo/**`, subset/analyzer code. `Watch` is inert build metadata; normal Build is unaffected. No BENCH entry.
- **Branch:** trunk-based on `main`. Commit per task. **House style:** DECISIONS.md append-only, French; next number **#93**.
- **Verified by experiment (do not re-derive):** default watch set excludes the `<None>` `App.razor`; a `Watch` item makes watch see it; with hot reload on, the change no-ops (`No managed code changes to apply`); `--no-hot-reload` makes the change rebuild+restart, which runs `FilamentCompile` and regenerates `App.g.js` (confirmed end-to-end).
- **Scratch (outside repo):** `/private/tmp/claude-501/-Users-phmatray-Repositories-dotnet-Filament/37c09993-f15e-49cf-8dc6-4dbe10955234/scratchpad` = `$SCRATCH`.

---

## Task 1: Watch item, version bump, docs, and end-to-end proof

**Files:**
- Modify: `src/Filament.Sdk/build/Filament.Sdk.targets` (add `Watch` item)
- Modify: `src/Filament.Sdk/Filament.Sdk.csproj` (`Version` → `0.1.1`)
- Modify: `templates/filament/FilamentApp.csproj` (`<PackageReference>` → `0.1.1`)
- Modify: `examples/FilamentApp/FilamentApp.csproj` (add `Watch` item)
- Modify: `templates/filament/README.md`, `examples/FilamentApp/README.md` (watch section)

- [ ] **Step 1: Add the Watch item to the SDK targets**

In `src/Filament.Sdk/build/Filament.Sdk.targets`, add before `</Project>`:

```xml
  <!-- Make `dotnet watch` monitor the entry .razor (it is a <None> item, otherwise unwatched).
       Use `dotnet watch --no-hot-reload run` so a change rebuilds (running FilamentCompile). -->
  <ItemGroup>
    <Watch Include="$(FilamentEntry)" />
  </ItemGroup>
```

- [ ] **Step 2: Bump the SDK version**

In `src/Filament.Sdk/Filament.Sdk.csproj`, change `<Version>0.1.0</Version>` → `<Version>0.1.1</Version>`.

- [ ] **Step 3: Bump the template's PackageReference**

In `templates/filament/FilamentApp.csproj`, change
`<PackageReference Include="Filament.Sdk" Version="0.1.0" />` → `Version="0.1.1"`.

- [ ] **Step 4: Add the parity Watch item to examples/FilamentApp**

In `examples/FilamentApp/FilamentApp.csproj`, add (near the `<None Include="App.razor" />` item group):

```xml
  <ItemGroup>
    <!-- `dotnet watch --no-hot-reload run` monitors App.razor and recompiles it to JS on change. -->
    <Watch Include="App.razor" />
  </ItemGroup>
```

- [ ] **Step 5: Document the loop in both READMEs**

Append to `examples/FilamentApp/README.md` and `templates/filament/README.md`:

```markdown
## Live reload

    dotnet watch --no-hot-reload run

Edit `App.razor` and save: the generator re-runs, `wwwroot/App.g.js` regenerates, and the browser
refreshes. (`--no-hot-reload` is required — otherwise a `.razor` change is treated as a no-op C# hot
reload and never rebuilds.) In Rider, add a `dotnet watch` run configuration with arguments
`--no-hot-reload run`.
```

- [ ] **Step 6: Re-pack the SDK (0.1.1) and re-install the template**

Run: `dotnet pack src/Filament.Sdk -c Release 2>&1 | grep -c "Successfully created package"` → expected `1`.
Run: `test -f artifacts/nuget/Filament.Sdk.0.1.1.nupkg && echo "0.1.1 packed"` → expected `0.1.1 packed`.
Run: `dotnet new install ./templates/filament --force 2>&1 | grep -c "Filament App"` → expected `≥1`.

- [ ] **Step 7: In-repo end-to-end — examples/FilamentApp watch loop**

Start (background): `cd examples/FilamentApp && ASPNETCORE_URLS=http://localhost:5103 dotnet watch --non-interactive --no-hot-reload run` (run_in_background). Wait until `http://localhost:5103/App.g.js` serves (curl retry).

Edit the component and confirm regeneration:
```bash
sed -i '' 's/>Click me</>Tick</' examples/FilamentApp/App.razor
```
Poll (Monitor / retry with real spacing, ~60s timeout) until `curl -s http://localhost:5103/App.g.js | grep -q "Tick"`.
Expected: `App.g.js` served content contains `Tick` — the edit was recompiled with no manual build.

Then **revert** the experimental edit so the committed source is clean:
```bash
sed -i '' 's/>Tick</>Click me</' examples/FilamentApp/App.razor
```
Stop the background watch.

- [ ] **Step 8: Distributable end-to-end — a freshly scaffolded 0.1.1 app watches too**

```bash
rm -rf "$SCRATCH/MyAppW" ~/.nuget/packages/filament.sdk
dotnet new filament -n MyAppW -o "$SCRATCH/MyAppW"
grep -q '0.1.1' "$SCRATCH/MyAppW/MyAppW.csproj" && echo "scaffold references 0.1.1"
dotnet build "$SCRATCH/MyAppW/MyAppW.csproj" -c Debug 2>&1 | grep -c "Build succeeded"
```
Start (background): `cd "$SCRATCH/MyAppW" && ASPNETCORE_URLS=http://localhost:5104 dotnet watch --non-interactive --no-hot-reload run --no-launch-profile`. Wait until serving.
Edit and poll:
```bash
sed -i '' 's/>Click me</>Zap</' "$SCRATCH/MyAppW/App.razor"
```
Poll (~60s) until `curl -s http://localhost:5104/App.g.js | grep -q "Zap"`.
Expected: `Zap` appears — the SDK-provided `Watch` item works end-to-end in a scaffolded app. Stop the background watch.

- [ ] **Step 9: Confirm normal build + firewall unaffected**

Run: `dotnet build examples/FilamentApp/FilamentApp.csproj -c Debug 2>&1 | grep -c "Build succeeded"` → `1`.
Run: `git diff --stat -- src/Filament.Generator src/filament-runtime BENCH.md` → **empty**.
Run: `git status --porcelain examples/FilamentApp/App.razor` → **empty** (the experimental edit was reverted).

- [ ] **Step 10: Commit**

```bash
git add src/Filament.Sdk/build/Filament.Sdk.targets src/Filament.Sdk/Filament.Sdk.csproj \
        templates/filament/FilamentApp.csproj templates/filament/README.md \
        examples/FilamentApp/FilamentApp.csproj examples/FilamentApp/README.md
git commit -m "feat(watch): dotnet watch --no-hot-reload recompiles App.razro on save (SDK 0.1.1 + FilamentApp)"
```
(Fix the commit message typo `App.razro` → `App.razor` before committing.)

---

## Task 2: DECISIONS #93 + memory + final verification

**Files:**
- Modify: `DECISIONS.md` (append #93)
- Modify: memory (`filament-dotnet-new-template.md` note + `MEMORY.md`)

- [ ] **Step 1: Append DECISIONS #93**

Read the tail (`tail -20 DECISIONS.md`), then append a French, in-style `## 93.` entry: `dotnet watch`
hot-reload for Filament apps. Record: the experiment findings (default watch set excludes the `<None>`
`App.razor`; a `Watch` item makes watch see it; hot reload no-ops a `.razor` change —
`No managed code changes to apply`; `--no-hot-reload` forces rebuild → `FilamentCompile` → regenerate,
proven end-to-end). The fix: `<Watch Include="$(FilamentEntry)"/>` in `Filament.Sdk.targets` (all
scaffolded apps) + parity in `examples/FilamentApp`; SDK bumped `0.1.0 → 0.1.1` (also sidesteps the
NuGet-cache collision); `--no-hot-reload` documented (no clean per-project property to force it).
Firewall: no generator/runtime/BENCH change, `Watch` is inert outside `dotnet watch`, normal Build
unaffected, 270 tests green. Deferred: bare `dotnet watch`; multi-`.razor` watching.

- [ ] **Step 2: Update memory**

In `filament-dotnet-new-template.md`, add a short line: SDK is now `0.1.1`; ships a `<Watch Include="$(FilamentEntry)"/>`
item so `dotnet watch --no-hot-reload run` recompiles `App.razor`→JS on save + browser-refresh (decision
#93); `--no-hot-reload` required (hot reload no-ops a `.razor` change). Reflect the `0.1.1` version in the
memory's "Distribution" note. Keep `MEMORY.md`'s pointer line accurate.

- [ ] **Step 3: Final verification**

Run: `dotnet test Filament.sln -c Debug 2>&1 | grep -E "Passed!|Failed!"` → three `Passed!` (270).
Run: `git status --porcelain` → only `DECISIONS.md` (memory is outside the repo).

- [ ] **Step 4: Commit**

```bash
git add DECISIONS.md
git commit -m "docs(watch): DECISIONS #93 — dotnet watch hot-reload"
```

---

## Self-Review (completed by author)

- **Spec coverage:** criteria (1) edit→regenerate on both apps → T1S7 & T1S8; (2) renders after refresh → the served `App.g.js` reflects the edit (browser refresh is `dotnet watch`'s built-in, verified content-side); (3) normal build + tests + firewall → T1S9 & T2S3.
- **Placeholder scan:** none — exact edits, exact commands, expected outputs. Commit-message typo flagged inline for correction.
- **Consistency:** version `0.1.1` is changed in the SDK csproj AND the template PackageReference AND asserted in T1S8; ports 5103/5104 distinct; experimental edits reverted before commit (T1S7) so committed sources are clean.
- **Wait mechanism:** watch-loop polls use Monitor / real-spaced retries (foreground `sleep` is unavailable), not a tight no-delay loop.
