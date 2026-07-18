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
