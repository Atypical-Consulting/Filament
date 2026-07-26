#!/usr/bin/env bash
#
# run.sh -- the S8 measurement (register A4), both sides, no browser.
#
#   BLAZOR   (the authority): tools/inherit-chain-oracle renders App.razor (App : Mid : Grand) through
#            bUnit's real ComponentBase + Renderer, reads #out, clicks #inc twice, re-reading as JSON.
#   FILAMENT: Filament.Generator compiles the SAME App.razor to a module (walking the @inherits chain,
#            decision 173), esbuild bundles it against the real runtime, and it is mounted in happy-dom
#            (observe-filament.mjs), same JSON shape.
#
# The two JSON blobs must be byte-identical. This is the repo's no-Playwright measurement path (the
# reserve BENCH n°69 / decision 164 disclosed): whether an inherited field+method advances across a
# chain is decided by C# member inheritance + HandleEventAsync -> StateHasChanged, the SAME code a WASM
# app runs, hosted headless by bUnit exactly as method-read-oracle hosts it for reactivity.
#
#   bash tools/inherit-chain-oracle/run.sh
#
set -euo pipefail

HERE="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd -- "$HERE/../.." && pwd)"
GEN_DLL="$REPO/src/Filament.Generator/bin/Debug/net8.0/Filament.Generator.dll"
RUNTIME="$REPO/src/filament-runtime/src/index.ts"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# --- FILAMENT side ---------------------------------------------------------------------------------
dotnet "$GEN_DLL" "$HERE/App.razor" "$WORK/app.js" --runtime "$RUNTIME" >/dev/null
"$REPO/node_modules/.bin/esbuild" "$WORK/app.js" --bundle --format=esm \
  '--define:__FILAMENT_STATS__=false' --outfile="$WORK/app.bundle.mjs" >/dev/null 2>&1
FILAMENT="$(node "$HERE/observe-filament.mjs" "$WORK/app.bundle.mjs")"

# --- BLAZOR side (the authority) -------------------------------------------------------------------
BLAZOR="$(dotnet run --project "$HERE/InheritChainOracle.csproj" -c Debug -v q --nologo 2>/dev/null | tail -1)"

printf 'BLAZOR  : %s\n' "$BLAZOR"
printf 'FILAMENT: %s\n' "$FILAMENT"

if [ "$BLAZOR" = "$FILAMENT" ]; then
  printf 'PASS -- byte-identical\n'
  exit 0
fi
printf 'FAIL -- the two shells DIVERGE\n' >&2
exit 1
