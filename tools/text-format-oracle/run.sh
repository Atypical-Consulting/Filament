#!/usr/bin/env bash
#
# run.sh -- the S9 measurement (register A13/A15), both sides, no browser.
#
#   BLAZOR   (the authority): tools/text-format-oracle renders App.razor through the real
#            HtmlRenderer under InvariantGlobalization and prints the four spans' text as JSON.
#   FILAMENT: Filament.Generator compiles the SAME App.razor to a module, esbuild bundles it against
#            the real runtime, and it is mounted in happy-dom (observe-filament.mjs), same JSON shape.
#
# The two JSON blobs must be byte-identical. This is the repo's no-Playwright measurement path (the
# reserve BENCH n°69 / decision 164 disclosed): what C# renders a bool/float as in text is decided by
# the BCL's ToString walked by the real Renderer, the SAME code a WASM app runs.
#
#   bash tools/text-format-oracle/run.sh
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
BLAZOR="$(dotnet run --project "$HERE/TextFormatOracle.csproj" -c Debug -v q --nologo)"

printf 'BLAZOR  : %s\n' "$BLAZOR"
printf 'FILAMENT: %s\n' "$FILAMENT"

if [ "$BLAZOR" = "$FILAMENT" ]; then
  printf 'PASS -- byte-identical\n'
  exit 0
fi
printf 'FAIL -- the two shells DIVERGE\n' >&2
exit 1
