#!/usr/bin/env bash
#
# run-phase3-measure.sh — C1/C3/C4 on JS COMPILED FROM PURE .razor, both apps.
#
# WHAT THIS ANSWERS, AND WHY IT IS NOT run-phase2-gen.sh AGAIN.
# run-phase2-gen.sh measured the generator's output for COUNTER only, and Counter's
# @code block at that time still held hand-written JAVASCRIPT (`const currentCount =
# signal(0)`) -- so the state lifting, the one thing the thesis turns on, happened in
# the INPUT, by hand, before the compiler ran. Both holes are closed here:
#
#   - Counter compiles from samples/Counter/Counter.razor, template AND @code, whose
#     markup and @code are pinned to baseline/Counter.Blazor/App.razor by a test.
#   - Rows compiles from baseline/Rows.Blazor/RowsApp.razor -- THE VERY FILE BLAZOR
#     COMPILES. Not a copy. There is nothing to drift.
#
# AND WHY ROWS IS THE POINT. Decisions 13/15 fix C4's headline at create-warm against
# blazor-rows-aot (7.35 ms). Phase 2 measured the counter's increment and touched
# C4's actual target -- create / update / swap / clear on 1000 rows -- NOT AT ALL.
# Counter has no @foreach, no list(), no record, no LCG and no @key. This run hits it.
#
# THE ORDER IS THE POINT (decision 47). A confound had to be disclosed once when
# Blazor and Filament were measured in two sequential blocks: any thermal or scheduler
# drift over the window was perfectly confounded with framework identity. Every pass
# below is MIRRORED across encodings, so each config appears once in each half and the
# two Filament labels -- the comparison this run exists to make -- sit ADJACENT in
# both passes. Monotonic drift can no longer masquerade as a framework effect.
#
#   rows    gzip:  blazor-nojit  filament-hand  filament-gen  blazor-aot
#   rows    br:    blazor-aot    filament-gen   filament-hand blazor-nojit
#   counter gzip:  blazor-nojit  filament-hand  filament-gen  blazor-aot
#   counter br:    blazor-aot    filament-gen   filament-hand blazor-nojit
#
# STRICTLY SEQUENTIAL. One config, one browser, at a time. Nothing is backgrounded.
# Section 7 forbids concurrent benchmarks, and a build running next to a timing run
# would be measuring the build.
#
# C3 RUNS LAST AND SEPARATELY, and only on --app counter, because that is what the
# harness's DOM-write instrument scopes to and because "1 DOM write per increment" is
# a statement about an increment. It is a COUNT, so drift cannot distort it and it
# needs no slot in the interleave. It is kept out of the C1/C4 invocations because the
# allocation probe leaves V8's sampling profiler enabled and forces GCs -- a C4 median
# taken under it would be measuring the instrument.
#
# Usage:
#   bash bench/run-phase3-measure.sh            # everything, in order
#   bash bench/run-phase3-measure.sh rows       # the rows passes only
#   bash bench/run-phase3-measure.sh counter    # the counter passes only
#   bash bench/run-phase3-measure.sh c3         # the C3 pass only

set -euo pipefail

PHASE="${1:-all}"
case "$PHASE" in
  all|rows|counter|c3) ;;
  *) printf 'usage: %s [all|rows|counter|c3]\n' "$0" >&2; exit 2 ;;
esac

REPO_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$REPO_ROOT/bench/results/phase3-pure"
BENCH="$REPO_ROOT/bench/harness/bench.mjs"
RUNS=10
WEIGHT_RUNS=3

mkdir -p "$OUT"

# label -> served static root. THE BLAZOR ROOT HAS wwwroot/, THE FILAMENT ROOT DOES
# NOT. Pointing at the wrong one is ENOENT, not a wrong number, but it will waste an
# afternoon -- see build-filament.sh's header.
dir_for() {
  case "$1" in
    blazor-*) echo "$REPO_ROOT/bench/publish/$1/wwwroot" ;;
    *) echo "$REPO_ROOT/bench/publish/$1" ;;
  esac
}

app_for() {
  case "$1" in
    *rows*) echo "rows" ;;
    *) echo "counter" ;;
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
    --app "$(app_for "$label")" \
    --label "$label" \
    --runs "$RUNS" \
    --weight-runs "$WEIGHT_RUNS" \
    --max-encoding "$enc" \
    --headless \
    "$(aot_for "$label")" \
    --out "$out_file"
}

printf '\n########## started %s ##########\n' \
  "$(date --iso-8601=seconds 2>/dev/null || date +%Y-%m-%dT%H:%M:%S%z)"

# ---- ROWS: C4's REAL target -------------------------------------------------
if [[ "$PHASE" == all || "$PHASE" == rows ]]; then
  printf '\n########## ROWS — C4 (create-warm/update/swap/clear) + C1 ##########\n'
  run_one blazor-rows-nojit gzip
  run_one filament-rows     gzip
  run_one filament-rows-gen gzip
  run_one blazor-rows-aot   gzip

  run_one blazor-rows-aot   br
  run_one filament-rows-gen br
  run_one filament-rows     br
  run_one blazor-rows-nojit br
fi

# ---- COUNTER ----------------------------------------------------------------
if [[ "$PHASE" == all || "$PHASE" == counter ]]; then
  printf '\n########## COUNTER — C1 + increment ##########\n'
  run_one blazor-counter-nojit gzip
  run_one filament-counter     gzip
  run_one filament-counter-gen gzip
  run_one blazor-counter-aot   gzip

  run_one blazor-counter-aot   br
  run_one filament-counter-gen br
  run_one filament-counter     br
  run_one blazor-counter-nojit br
fi

if [[ "$PHASE" != all && "$PHASE" != c3 ]]; then
  printf '\nfinished %s (phase %s)\n' "$(date +%Y-%m-%dT%H:%M:%S%z)" "$PHASE"
  exit 0
fi

# ---- C3 ---------------------------------------------------------------------
printf '\n########## C3 — DOM writes per increment ##########\n'

# Blazor: the DOM-write instrument only. The allocation probe is deliberately NOT run
# here, and that is a decision, not an omission: decision 30 records that the probe
# samples the JS heap, so it is COMPLETE for Filament (whose runtime is JS) and
# STRUCTURALLY BLIND to Blazor's render tree (which lives in WASM linear memory, one
# ArrayBuffer to V8). A Blazor allocation number would be its interop-glue subset, and
# the only thing anyone would ever do with it is divide it by Filament's total -- the
# exact comparison decision 30 forbids. The DOM-write count IS framework-agnostic:
# identical code, same observer, same root.
printf '\n=== C3 blazor-counter-nojit (DOM writes only) %s ===\n' "$(date +%H:%M:%S)"
node "$BENCH" --dir "$(dir_for blazor-counter-nojit)" --app counter \
  --label blazor-counter-nojit-c3 --runs 1 --weight-runs 1 --headless --no-aot \
  --c3 --scenarios increment-warm \
  --out "$OUT/c3-blazor-counter-nojit.json"

# Filament: DOM writes AND allocation. The probe is complete for both of these labels
# and they are both JS flushing synchronously, so hand-vs-generated IS a like-for-like
# allocation comparison. It can refute a "0 tree allocation" claim for the generator's
# output, which is precisely why it is worth its runtime.
for label in filament-counter-stats filament-counter-gen-stats; do
  printf '\n=== C3 %s (DOM writes + allocation) %s ===\n' "$label" "$(date +%H:%M:%S)"
  node "$BENCH" --dir "$(dir_for "$label")" --app counter \
    --label "$label-c3" --runs 1 --weight-runs 1 --headless --no-aot \
    --c3-alloc --scenarios increment-warm \
    --out "$OUT/c3-$label.json"
done

printf '\nfinished %s\n' \
  "$(date --iso-8601=seconds 2>/dev/null || date +%Y-%m-%dT%H:%M:%S%z)"
printf 'results in %s\n' "$OUT"
