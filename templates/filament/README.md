# `dotnet new filament` — the Filament app template

Scaffolds a Filament app anywhere: a real .NET web project whose Build compiles `App.razor` → JS
(via the `Filament.Sdk` package) and whose F5 serves it on Kestrel. No .NET ships to the browser.

## One-time setup

From published packages (a GitHub Release, or nuget.org once the key is configured — see
`.github/workflows/release.yml`):

    dotnet new install Filament.Templates

Or from a local build of this repo:

    cd <this repo>
    (cd src/filament-runtime && npm ci && npm run build)   # builds filament.js
    dotnet pack src/Filament.Sdk -c Release                # -> artifacts/nuget/Filament.Sdk.<version>.nupkg
    dotnet pack src/Filament.Templates -c Release          # -> artifacts/nuget/Filament.Templates.<version>.nupkg
    dotnet nuget add source "$PWD/artifacts/nuget" -n filament-local
    dotnet new install Filament.Templates

## Use it (anywhere)

    dotnet new filament -n MyApp
    cd MyApp
    dotnet run        # or open in Rider and press F5

Edit `App.razor` and rebuild. Out-of-subset constructs make the generator refuse and the build fail
with a diagnostic — by design.

## Live reload

    dotnet watch --no-hot-reload run

Edit `App.razor` and save: the generator re-runs, `wwwroot/App.g.js` regenerates, and the browser
refreshes. (`--no-hot-reload` is required — otherwise a `.razor` change is treated as a no-op C# hot
reload and never rebuilds.) In Rider, add a `dotnet watch` run configuration with arguments
`--no-hot-reload run`.
