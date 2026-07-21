#!/usr/bin/env bash
#
# stage-engine.sh — copy the PUBLISHED playground engine into the site's public/ tree, where
# website/src/pages/playground.astro expects it (${base}/playground-engine/).
#
#   playground/stage-engine.sh <published-wwwroot> [<website-public-dir>]
#
# main.js is staged from SOURCE (unfingerprinted): the page imports a stable name, and the file
# resolves ./_framework/dotnet.js and ./refpack/ relative to itself. filament.js is the runtime
# the PREVIEW mounts compiled components against — the same dist bundle every Filament app ships.
set -euo pipefail
cd "$(dirname "$0")/.."

SRC="${1:?usage: stage-engine.sh <published-wwwroot> [<website-public-dir>]}"
DST="${2:-website/public}/playground-engine"

[[ -d "$SRC/_framework" ]] || { echo "FAIL: no _framework under '$SRC' — publish first" >&2; exit 1; }
[[ -d "$SRC/refpack" ]] || { echo "FAIL: no refpack under '$SRC' — run make-refpack.sh into the app's wwwroot first" >&2; exit 1; }
[[ -f src/filament-runtime/dist/filament.js ]] || { echo "FAIL: runtime dist missing — npm run build in src/filament-runtime" >&2; exit 1; }

rm -rf "$DST"
mkdir -p "$DST"
cp -R "$SRC/_framework" "$DST/_framework"
cp -R "$SRC/refpack" "$DST/refpack"
cp playground/Filament.Playground/wwwroot/main.js "$DST/main.js"
cp src/filament-runtime/dist/filament.js "$DST/filament.js"

echo "engine staged -> $DST"
du -sh "$DST" | awk '{print "  on disk (with .br/.gz siblings): " $1}'
