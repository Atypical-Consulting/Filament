#!/usr/bin/env bash
#
# run-duel.sh — THE DUEL measured: weight + time-to-interactive + memory, three labels, both encodings.
#
# Mirrored pass order (decision 47's discipline): each label appears once in each half and the
# Filament label sits adjacent to a Blazor one in both passes, so monotonic thermal/scheduler drift
# cannot masquerade as a framework effect:
#
#   gzip:  blazor-nojit   filament-gen   blazor-aot
#   br:    blazor-aot     filament-gen   blazor-nojit
#
# The MEMORY pass runs LAST and separately: measureUserAgentSpecificMemory waits on GC-ish
# quiescence, which must never sit inside a weight/timing window (the C3 rule, applied again).
#
# STRICTLY SEQUENTIAL. One config, one browser, at a time. Nothing is backgrounded (section 7).
#
# Prerequisites (each verified by the harness itself, never assumed):
#   bash bench/publish-baseline.sh blazor-duel-nojit blazor-duel-aot
#   bash bench/build-filament.sh filament-duel-gen
set -euo pipefail
cd "$(dirname "$0")/.."

OUT=bench/results/duel
mkdir -p "$OUT"

run() { # label dir encoding aotflag
  node bench/harness/bench.mjs --dir "$2" --app duel --label "$1" \
    --headless --weight-runs 10 --max-encoding "$3" $4 \
    --out "$OUT/$1-$3.json"
}

run blazor-duel-nojit "bench/publish/blazor-duel-nojit/wwwroot" gzip --no-aot
run filament-duel-gen "bench/publish/filament-duel-gen"         gzip ""
run blazor-duel-aot   "bench/publish/blazor-duel-aot/wwwroot"   gzip --aot

run blazor-duel-aot   "bench/publish/blazor-duel-aot/wwwroot"   br --aot
run filament-duel-gen "bench/publish/filament-duel-gen"         br ""
run blazor-duel-nojit "bench/publish/blazor-duel-nojit/wwwroot" br --no-aot

mem() { # label dir
  node bench/harness/bench.mjs --dir "$2" --app duel --label "$1" --memory --headless \
    --out "$OUT/$1-memory.json"
}

mem blazor-duel-nojit "bench/publish/blazor-duel-nojit/wwwroot"
mem filament-duel-gen "bench/publish/filament-duel-gen"
mem blazor-duel-aot   "bench/publish/blazor-duel-aot/wwwroot"

echo
echo "Duel results in $OUT/ — commit them; the site's /benchmark page renders from these files."
