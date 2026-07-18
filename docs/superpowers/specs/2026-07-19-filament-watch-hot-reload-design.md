# `dotnet watch` hot-reload for Filament apps — design

**Goal:** Edit `App.razor` → the generator re-runs → the browser refreshes, without a manual rebuild.
A live-feeling dev loop for both `examples/FilamentApp` and any `dotnet new filament` app.

**Status:** design approved by the owner in chat. Small slice. Next: implementation plan.

**DX/packaging only** — no generator/runtime/BENCH change. The whole capability is one MSBuild `Watch`
item plus documentation; firewall is section 4.

---

## What the experiment established (measured, not assumed)

Running `dotnet watch` against a scaffolded app proved two things:

1. **`dotnet watch` ignores `App.razor` by default.** It is a `<None>` item (Filament owns it; the Razor
   SDK is switched off), so it is outside the default watch set. Editing it produced no event.
2. **With hot reload on, a `.razor` change no-ops.** Once `App.razor` is watched, a change is taken by the
   hot-reload engine, which reports *"No managed code changes to apply"* and **does not rebuild** — so the
   `AfterTargets="Build"` `FilamentCompile` target never runs and `App.g.js` is not regenerated.

The fix that worked end-to-end: watch the file **and** run with `--no-hot-reload`, so a change triggers a
rebuild+restart, which runs `FilamentCompile` (regenerating `App.g.js`) and — in Development — `dotnet
watch`'s browser-refresh reloads the page. Verified: an edit to `App.razor` regenerated `App.g.js` to
match.

---

## The design

### 1. Watch the entry `.razor`

`Filament.Sdk.targets` gains one item so any consuming app watches its entry component:

```xml
<ItemGroup>
  <Watch Include="$(FilamentEntry)" />
</ItemGroup>
```

`examples/FilamentApp/FilamentApp.csproj` (which uses inline targets, not the SDK) gets the parity line:

```xml
<ItemGroup>
  <Watch Include="App.razor" />
</ItemGroup>
```

### 2. Version bump (SDK contents changed)

`Filament.Sdk` `0.1.0 → 0.1.1`; the template's `<PackageReference Include="Filament.Sdk" Version="0.1.1" />`
follows. Re-pack to the local feed. (Bumping also sidesteps the NuGet-cache collision that re-packing the
same version would cause.)

### 3. Document the loop

Template README + `examples/FilamentApp` README gain a short section:

```
dotnet watch --no-hot-reload run
# edit App.razor → App.g.js regenerates → the browser refreshes
```

Plus a one-line Rider note: add a `dotnet watch` run configuration (arguments `--no-hot-reload run`).

### The caveat, disclosed

`--no-hot-reload` is required — a `.razor` change otherwise no-ops. There is no clean per-project MSBuild
property to force it, so it is **documented**, not worked around. Each change is a rebuild+restart of the
dev host (sub-second for this app), followed by a browser refresh.

---

## Firewall

- No generator source change, no runtime source change, no BENCH entry, no measurement, no subset change.
  The `Watch` item is pure build metadata consumed only by `dotnet watch`.
- Normal (non-watch) Build is unaffected — `Watch` items are inert outside `dotnet watch`.
- A DECISIONS.md entry (#93) records it; memory updated.

## Deferred

Making bare `dotnet watch` (no flag) work; watching multiple `.razor` files (pairs with the deferred
multi-component slice); CSS/`index.html` live-edit niceties.

## Success criteria

1. `dotnet watch --no-hot-reload run` on `examples/FilamentApp` and on a freshly scaffolded app: editing
   `App.razor` regenerates `wwwroot/App.g.js` to match, with no manual build.
2. The regenerated app renders the edit in the browser after refresh.
3. Plain `dotnet build` is unchanged; all 270 tests green; generator/runtime/BENCH untouched.
