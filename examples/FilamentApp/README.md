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
