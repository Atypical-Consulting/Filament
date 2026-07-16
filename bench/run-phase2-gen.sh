#!/usr/bin/env bash
#
# run-phase2-gen.sh — the Phase 2 measurement: C1/C3/C4 on the GENERATOR'S OUTPUT.
#
# WHAT THIS ANSWERS. Every weight and every timing this repo has published so far
# describes samples/Counter/counter.js -- the hand-written ANSWER KEY (decision 21).
# The proposition that actually carries the thesis -- "a C# generator emits this,
# under 10 ko, at these times" -- has never been measured (decisions 34, 50). This
# script measures it, by putting the generated app, the hand-written app and Blazor
# through the SAME harness in ONE run.
#
# THE ORDER IS THE POINT. Decision 47 had to disclose an order confound: Blazor was
# measured 17:50-18:01 and Filament 18:01-18:07, sequential but NOT interleaved, so
# any thermal or scheduler drift over the window was perfectly confounded with
# framework identity. This run refuses to repeat that. The 8 configs run in a
# MIRRORED (counterbalanced) order:
#
#   gzip pass:  blazor-nojit  filament-hand  filament-gen  blazor-aot
#   br   pass:  blazor-aot    filament-gen   filament-hand  blazor-nojit
#
# Every config therefore appears once in the first half and once in the second, and
# the two Filament labels -- the comparison this run exists to make -- sit ADJACENT
# in both passes, so drift between them is bounded by one config's duration rather
# than by the length of the run. A monotonic drift over the window can no longer
# masquerade as a framework effect, in either direction.
#
# STRICTLY SEQUENTIAL. One config, one browser, at a time. Nothing is backgrounded.
# Section 7 forbids concurrent benchmarks, and a build running next to a timing run
# would be measuring the build.
#
# C3 RUNS LAST AND SEPARATELY. C3 is a COUNT, not a timing, so it cannot be
# distorted by drift and does not need a slot in the interleave. It is kept out of
# the C1/C4 invocations because the allocation probe leaves V8's sampling profiler
# enabled and forces GCs -- a C4 median taken under it would be measuring the
# instrument. It runs on the -stats labels, which are the only bundles with
# instrumentation compiled in (and which must NEVER be weighed).
#
# Usage:
#   bash bench/run-phase2-gen.sh          # everything, in order
#   bash bench/run-phase2-gen.sh gzip     # the gzip pass only
#   bash bench/run-phase2-gen.sh br       # the brotli pass only
#   bash bench/run-phase2-gen.sh c3       # the C3 pass only
#
# THE PHASE ARGUMENT DOES NOT CHANGE THE ORDER, and that is the only reason it is
# allowed to exist: the passes are emitted in the same sequence whether they run as
# one invocation or three, so splitting them to fit a caller's time budget cannot
# perturb the counterbalancing above. Nothing else may branch on it.

set -euo pipefail

PHASE="${1:-all}"
case "$PHASE" in
  all|gzip|br|c3) ;;
  *) printf 'usage: %s [all|gzip|br|c3]\n' "$0" >&2; exit 2 ;;
esac

REPO_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$REPO_ROOT/bench/results/phase2-gen"
BENCH="$REPO_ROOT/bench/harness/bench.mjs"
RUNS=10
WEIGHT_RUNS=3

mkdir -p "$OUT"

# label -> served static root. THE BLAZOR ROOT HAS wwwroot/, THE FILAMENT ROOT DOES
# NOT. Pointing at the wrong one is ENOENT, not a wrong number, but it will waste an
# afternoon -- see build-filament.sh's header.
dir_for() {
  case "$1" in
    blazor-counter-nojit|blazor-counter-aot) echo "$REPO_ROOT/bench/publish/$1/wwwroot" ;;
    *) echo "$REPO_ROOT/bench/publish/$1" ;;
  esac
}

# --aot is a CLAIM the harness independently checks against the served artifacts
# (environment.aotObserved). Passing it wrong does not corrupt a number quietly; it
# raises a warning. It is still passed correctly.
aot_for() {
  case "$1" in
    *-aot) echo "--aot" ;;
    *) echo "--no-aot" ;;
  esac
}

run_one() {
  local label="$1" enc="$2"
  local out_file="$OUT/$label.$enc.json"
  printf '\n=== %s [%s] %s ===\n' "$label" "$enc" "$(date +%H:%M:%S)"
  node "$BENCH" \
    --dir "$(dir_for "$label")" \
    --app counter \
    --label "$label" \
    --runs "$RUNS" \
    --weight-runs "$WEIGHT_RUNS" \
    --max-encoding "$enc" \
    --headless \
    "$(aot_for "$label")" \
    --out "$out_file"
}

printf '\n########## C1 + C4 — mirrored interleave, 8 configs ##########\n'
printf 'started %s\n' "$(date --iso-8601=seconds 2>/dev/null || date +%Y-%m-%dT%H:%M:%S%z)"

# gzip pass — C1's basis (decision 16: C1 = < 10 ko GZIP).
if [[ "$PHASE" == all || "$PHASE" == gzip ]]; then
  run_one blazor-counter-nojit gzip
  run_one filament-counter     gzip
  run_one filament-counter-gen gzip
  run_one blazor-counter-aot   gzip
fi

# brotli pass — the headline basis for C2 (decision 14), MIRRORED order.
if [[ "$PHASE" == all || "$PHASE" == br ]]; then
  run_one blazor-counter-aot   br
  run_one filament-counter-gen br
  run_one filament-counter     br
  run_one blazor-counter-nojit br
fi

if [[ "$PHASE" != all && "$PHASE" != c3 ]]; then
  printf '\nfinished %s (phase %s)\n' "$(date +%Y-%m-%dT%H:%M:%S%z)" "$PHASE"
  printf 'results in %s\n' "$OUT"
  exit 0
fi

printf '\n########## C3 — DOM writes per increment ##########\n'

# Blazor: the DOM-write instrument only. The allocation probe is deliberately NOT
# run here, and that is a decision, not an omission: decision 30 records that the
# probe samples the JS heap, so it is COMPLETE for Filament (whose runtime is JS)
# and STRUCTURALLY BLIND to Blazor's render tree (which lives in WASM linear memory,
# one ArrayBuffer to V8). A Blazor allocation number would be its interop-glue
# subset, and the only thing anyone would ever do with it is divide it by Filament's
# total -- the exact comparison decision 30 forbids. The DOM-write count IS
# framework-agnostic: identical code, same observer, same root.
printf '\n=== C3 blazor-counter-nojit (DOM writes only) %s ===\n' "$(date +%H:%M:%S)"
node "$BENCH" --dir "$(dir_for blazor-counter-nojit)" --app counter \
  --label blazor-counter-nojit-c3 --runs 1 --weight-runs 1 --headless --no-aot \
  --c3 --scenarios increment-warm \
  --out "$OUT/c3-blazor-counter-nojit.json"

# Filament: DOM writes AND allocation. The probe is complete for both of these
# labels and they are both JS flushing synchronously, so hand-vs-generated IS a
# like-for-like allocation comparison -- it is the same question C1 asks, in bytes
# per increment instead of bytes on the wire. It can refute a "0 allocation" claim
# for the generator's output, which is precisely why it is worth its runtime.
for label in filament-counter-stats filament-counter-gen-stats; do
  printf '\n=== C3 %s (DOM writes + allocation) %s ===\n' "$label" "$(date +%H:%M:%S)"
  node "$BENCH" --dir "$(dir_for "$label")" --app counter \
    --label "$label-c3" --runs 1 --weight-runs 1 --headless --no-aot \
    --c3-alloc --scenarios increment-warm \
    --out "$OUT/c3-$label.json"
done

printf '\nfinished %s\n' "$(date --iso-8601=seconds 2>/dev/null || date +%Y-%m-%dT%H:%M:%S%z)"
printf 'results in %s\n' "$OUT"
