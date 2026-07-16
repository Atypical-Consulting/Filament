#!/usr/bin/env bash
#
# publish-baseline.sh — regenerate the Phase 0 Blazor baseline publish outputs.
#
# This script is the ONLY place the four baseline configs are defined. Before it
# existed, the difference between `blazor-counter-nojit` and `blazor-counter-aot`
# lived exclusively in an operator's shell history (BENCH.md, open reserve #3).
#
# Four configs from TWO source trees:
#
#   blazor-counter-nojit   baseline/Counter.Blazor   Release
#   blazor-counter-aot     baseline/Counter.Blazor   Release + RunAOTCompilation=true
#   blazor-rows-nojit      baseline/Rows.Blazor      Release
#   blazor-rows-aot        baseline/Rows.Blazor      Release + RunAOTCompilation=true
#
# RunAOTCompilation is passed on the COMMAND LINE and never written into a
# .csproj (DECISIONS.md #9): the same source tree must produce both the AOT and
# the non-AOT config, otherwise the two are not comparable and the `aot` label in
# the result JSON would be a lie.
#
# CONCURRENCY-SAFE, VIA A PER-PROJECT LOCK. Two labels share one source tree:
# counter-nojit and counter-aot are both baseline/Counter.Blazor, rows-nojit and
# rows-aot are both baseline/Rows.Blazor. The purge below (`rm -rf obj bin`)
# therefore operates on a directory the project's OTHER config also uses, so two
# concurrent invocations for the same project would delete each other's
# intermediates mid-build. An orchestrator ran exactly that -- all four configs at
# once -- and the rows-* and counter-* pairs raced. The artifacts happened to come
# out correct; that was luck, not design.
#
# Each config now takes an exclusive lock on its PROJECT for the whole
# purge+publish, so this is safe to run concurrently in any combination:
#
#   ./bench/publish-baseline.sh blazor-rows-aot &
#   ./bench/publish-baseline.sh blazor-counter-aot &   # different project: parallel
#   ./bench/publish-baseline.sh blazor-rows-nojit &    # same project as #1: waits
#
# Locking costs almost nothing here: the two SLOW configs (the AOT pair) live on
# DIFFERENT projects, so they never contend and still run fully in parallel. The
# lock only makes a project's cheap non-AOT build wait for its AOT build -- tens of
# seconds against a multi-minute AOT. Serialising the pair is nearly free.
#
# Giving each config its own obj/bin instead was tried and REJECTED, for a reason
# worth recording. The SDK derives DefaultItemExcludes from
# $(BaseIntermediateOutputPath), so redirecting obj to obj/<label>/ stops
# obj/Release/** being excluded from the default **/*.cs glob. Any checkout that
# has ever run this script has stale generated sources there
# (obj/Release/net10.0/*.AssemblyInfo.cs, wasm/for-publish/aot-instances.cs), and
# they get compiled as project source: ~11x CS0579 "Duplicate attribute" and
# CS0101. Making that approach work would mean ALSO purging the shared obj/ --
# which is the very race being fixed. The lock leaves the build paths exactly as
# the Phase 0 numbers were measured through, so it cannot perturb the bytes.
#
# Idempotent: every run purges the config's obj/, bin/ and output directory
# first, so a second run produces the same bytes as the first. The purge is not
# hygiene theatre -- `wasm-opt` rewrites dotnet.native.wasm IN PLACE and the
# static-web-assets cache is poisoned by toggling AOT on a shared project dir.
# Both failure modes were hit for real during the Phase 0 measurement and are
# written up in DECISIONS.md #9. A naive re-run without the purge WILL fail with
# MSB3073 (wasm-opt: "Fatal: error parsing wasm") or ~40x MSB3030 (missing
# compressed/publish/<hash>-{0}-<hash>.gz).
#
# Usage:
#   ./bench/publish-baseline.sh                     # all four configs
#   ./bench/publish-baseline.sh blazor-rows-nojit   # a subset, by label
#   ./bench/publish-baseline.sh --list              # show known labels
#
# Outputs land in bench/publish/<label>/. The benchmark's static root is
# bench/publish/<label>/wwwroot, NEVER bench/publish/<label>.
#
# Set PUBLISH_ROOT to publish somewhere else. Use this to exercise the script
# WITHOUT overwriting the measured output that BENCH.md's numbers were read from:
#
#   PUBLISH_ROOT=/tmp/scratch ./bench/publish-baseline.sh blazor-rows-aot
#
# obj/ and bin/ deliberately do NOT follow PUBLISH_ROOT: a scratch run builds
# through the exact same intermediate paths as a real run, and is therefore honest
# evidence about the real run rather than evidence about some other build.
#
# Verified 2026-07-16 on SDK 10.0.301 / wasm-tools 10.0.109: a scratch run of
# blazor-rows-aot reproduced the measured artifact BYTE-FOR-BYTE --
# dotnet.native.wasm = 11,380,806 B, sha256 ef7a3ba9a0dcff7e..., same fingerprint
# (nm0j57lo9u). The AOT path of this script is therefore known-good, not assumed.

set -euo pipefail

REPO_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
# Overridable so the script can be exercised without clobbering measured output.
PUBLISH_ROOT="${PUBLISH_ROOT:-$REPO_ROOT/bench/publish}"

# Non-AOT dotnet.native.wasm measured at 1_494_734 B; AOT at 11_362_554 B (~7.6x).
# 4 MiB sits far from both, so these thresholds catch a silent AOT fallback --
# the exact failure DECISIONS.md #10 flags as unguarded ("--aot is self-declared
# and never verified; the JSON would happily record aot: true for a non-AOT build").
AOT_MIN_NATIVE_WASM_BYTES=4194304
NOJIT_MAX_NATIVE_WASM_BYTES=4194304

ALL_LABELS=(
  blazor-counter-nojit
  blazor-counter-aot
  blazor-rows-nojit
  blazor-rows-aot
)

project_for() {
  case "$1" in
    blazor-counter-nojit|blazor-counter-aot) echo "baseline/Counter.Blazor" ;;
    blazor-rows-nojit|blazor-rows-aot)       echo "baseline/Rows.Blazor" ;;
    *) return 1 ;;
  esac
}

aot_for() {
  case "$1" in
    *-aot)   echo "true" ;;
    *-nojit) echo "false" ;;
    *) return 1 ;;
  esac
}

die() { printf '\nFAIL: %s\n' "$1" >&2; exit 1; }
log() { printf '\n==> %s\n' "$1"; }

# ---- per-project mutex ------------------------------------------------------
# `mkdir` is the portable atomic test-and-set: it is one syscall that fails if the
# directory exists. macOS has no flock(1), so this is the option that works
# everywhere without a dependency. The lock lives in TMPDIR keyed by a hash of the
# project's absolute path, so it survives the obj/+bin/ purge (a lock inside the
# project would delete itself) and two different checkouts never share one.

LOCK_WAIT_SECONDS=1800   # an AOT publish is minutes; allow a slow one to finish.
HELD_LOCK=""

release_project_lock() {
  # Plain `if`, not `[[ ... ]] && ...`: under `set -e` a false one-liner AND-list
  # is itself a failing statement, and this runs from the EXIT trap.
  if [[ -n "$HELD_LOCK" ]]; then
    rm -rf "$HELD_LOCK"
  fi
  HELD_LOCK=""
}
# Do not strand the lock if we die, get Ctrl-C'd, or get killed.
trap release_project_lock EXIT INT TERM

acquire_project_lock() {
  local project_dir="$1" label="$2"
  local key lock_dir waited=0 owner
  key="$(printf '%s' "$project_dir" | shasum | cut -c1-16)"
  lock_dir="${TMPDIR:-/tmp}/filament-publish-$key.lock"

  while ! mkdir "$lock_dir" 2>/dev/null; do
    owner="$(cat "$lock_dir/pid" 2>/dev/null || true)"

    # Reap a lock whose holder died (crash / SIGKILL), rather than hang forever.
    if [[ -n "$owner" ]] && ! kill -0 "$owner" 2>/dev/null; then
      printf '    stale lock from dead pid %s; reclaiming\n' "$owner"
      rm -rf "$lock_dir"
      continue
    fi

    if (( waited == 0 )); then
      printf '    waiting for lock on %s (held by pid %s)\n' \
        "$(basename "$project_dir")" "${owner:-?}"
    fi
    if (( waited >= LOCK_WAIT_SECONDS )); then
      die "timed out after ${LOCK_WAIT_SECONDS}s waiting for the lock on
     $project_dir (held by pid ${owner:-unknown}).
     Another publish-baseline.sh is building this project. If nothing is
     running, remove the stale lock: rm -rf '$lock_dir'"
    fi
    sleep 1
    waited=$((waited + 1))
  done

  printf '%s' "$$" > "$lock_dir/pid"
  HELD_LOCK="$lock_dir"
  if (( waited > 0 )); then
    printf '    acquired lock after %ss\n' "$waited"
  fi
}

file_size() {
  # macOS stat and GNU stat disagree on flags; try BSD first.
  stat -f%z "$1" 2>/dev/null || stat -c%s "$1"
}

if [[ "${1:-}" == "--list" ]]; then
  printf '%s\n' "${ALL_LABELS[@]}"
  exit 0
fi

if [[ "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then
  # Print the whole header block: from the title to the last comment line before
  # the code. A hardcoded line range (it was '3,40p') silently truncates the help
  # as soon as the header grows, which it has.
  awk 'NR < 3 { next } !/^#/ { exit } { sub(/^#{1,2} ?/, ""); print }' \
    "${BASH_SOURCE[0]}"
  exit 0
fi

# ---- preflight: fail loudly and early, never half-publish -------------------

command -v dotnet >/dev/null 2>&1 || die "dotnet not on PATH. Install the .NET 10 SDK."

SDK_VERSION="$(dotnet --version)"
EXPECTED_SDK="10.0.301"
if [[ "$SDK_VERSION" != "$EXPECTED_SDK" ]]; then
  printf '\nWARNING: .NET SDK is %s; the Phase 0 baseline was measured on %s.\n' \
    "$SDK_VERSION" "$EXPECTED_SDK" >&2
  printf 'Bytes and timings are pinned to the SDK (DECISIONS.md #1). Any result\n' >&2
  printf 'produced here is NOT comparable to BENCH.md entry #1 -- record a new entry.\n' >&2
fi

if ! dotnet workload list 2>/dev/null | grep -q '^wasm-tools'; then
  die "the 'wasm-tools' workload is not installed, so AOT cannot run.
     Install it with:  dotnet workload install wasm-tools"
fi

# Resolve requested labels.
if [[ $# -gt 0 ]]; then
  REQUESTED=("$@")
  for label in "${REQUESTED[@]}"; do
    project_for "$label" >/dev/null 2>&1 \
      || die "unknown config label '$label'. Known labels:
     $(printf '%s ' "${ALL_LABELS[@]}")"
  done
else
  REQUESTED=("${ALL_LABELS[@]}")
fi

log "Publishing ${#REQUESTED[@]} config(s) with .NET SDK $SDK_VERSION"

for label in "${REQUESTED[@]}"; do
  project_rel="$(project_for "$label")"
  project_dir="$REPO_ROOT/$project_rel"
  aot="$(aot_for "$label")"
  out_dir="$PUBLISH_ROOT/$label"

  [[ -d "$project_dir" ]] || die "project directory not found: $project_dir"

  # The purge below deletes paths SHARED with this project's other config, so it
  # must not overlap a concurrent invocation. See the header for why the lock is
  # a lock and not per-config obj/bin paths.
  acquire_project_lock "$project_dir" "$label"

  log "$label  <-  $project_rel  (RunAOTCompilation=$aot)"

  # Idempotency + correctness. See the header: this purge is load-bearing.
  # ${var:?} so an empty project_dir can never turn this into `rm -rf /obj /bin`.
  rm -rf "${project_dir:?}/obj" "${project_dir:?}/bin" "${out_dir:?}"

  publish_args=(
    publish "$project_dir"
    -c Release
    -o "$out_dir"
  )
  # AOT is a command-line-only concern, on purpose (DECISIONS.md #9).
  if [[ "$aot" == "true" ]]; then
    publish_args+=(-p:RunAOTCompilation=true)
  fi

  printf '    dotnet %s\n' "${publish_args[*]}"
  dotnet "${publish_args[@]}" \
    || die "publish failed for '$label'.
     If this is MSB3073 (wasm-opt) or MSB3030 (missing compressed/publish/*.gz),
     the obj/ purge above did not cover a stale cache -- see DECISIONS.md #9."

  # ---- post-publish assertions: prove the output is what the label claims ----

  wwwroot="$out_dir/wwwroot"
  [[ -d "$wwwroot" ]] || die "'$label' published but produced no wwwroot/ at $wwwroot"
  [[ -f "$wwwroot/index.html" ]] || die "'$label' produced no wwwroot/index.html"

  native_wasm="$(find "$wwwroot/_framework" -name 'dotnet.native*.wasm' -type f | head -n1)"
  [[ -n "$native_wasm" ]] || die "'$label' produced no _framework/dotnet.native*.wasm"

  native_size="$(file_size "$native_wasm")"

  # A self-declared flag is worth nothing; check the artifact (DECISIONS.md #10).
  if [[ "$aot" == "true" ]]; then
    if (( native_size < AOT_MIN_NATIVE_WASM_BYTES )); then
      die "'$label' claims AOT but dotnet.native.wasm is only $native_size B
     (expected > $AOT_MIN_NATIVE_WASM_BYTES B; the Phase 0 AOT build was 11362554 B).
     AOT silently did NOT engage. Do not measure this output."
    fi
  else
    if (( native_size > NOJIT_MAX_NATIVE_WASM_BYTES )); then
      die "'$label' claims interpreted but dotnet.native.wasm is $native_size B
     (expected < $NOJIT_MAX_NATIVE_WASM_BYTES B; the Phase 0 non-AOT build was 1494734 B).
     This looks like an AOT build wearing a non-AOT label. Do not measure this output."
    fi
  fi

  printf '    OK  %s  (dotnet.native.wasm = %s B)\n' "$label" "$native_size"

  # Hold the lock across purge+publish+assertions only; a sibling waiting on this
  # project can start as soon as we are done reading our own output.
  release_project_lock
done

# Report the real root: with PUBLISH_ROOT overridden, claiming "bench/publish/"
# would be a lie, and this script exists to make honest claims.
log "Done. ${#REQUESTED[@]} config(s) published under $PUBLISH_ROOT/"
printf '\nThe benchmark static root is %s/<label>/wwwroot -- never %s/<label>.\n' \
  "$PUBLISH_ROOT" "$PUBLISH_ROOT"
printf 'Next: see README.md for the bench command lines.\n'
