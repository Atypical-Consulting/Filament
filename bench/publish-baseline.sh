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
# Idempotent: every run purges the config's obj/, bin/ and output directory
# first, so a second run produces the same bytes as the first. The purge is not
# hygiene theatre — `wasm-opt` rewrites dotnet.native.wasm IN PLACE and the
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

set -euo pipefail

REPO_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
PUBLISH_ROOT="$REPO_ROOT/bench/publish"

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

file_size() {
  # macOS stat and GNU stat disagree on flags; try BSD first.
  stat -f%z "$1" 2>/dev/null || stat -c%s "$1"
}

if [[ "${1:-}" == "--list" ]]; then
  printf '%s\n' "${ALL_LABELS[@]}"
  exit 0
fi

if [[ "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then
  sed -n '3,40p' "${BASH_SOURCE[0]}" | sed 's/^#\{1,2\} \{0,1\}//'
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

  log "$label  <-  $project_rel  (RunAOTCompilation=$aot)"

  # Idempotency + correctness. See the header: this purge is load-bearing.
  rm -rf "$project_dir/obj" "$project_dir/bin" "$out_dir"

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
done

log "Done. ${#REQUESTED[@]} config(s) published under bench/publish/"
printf '\nThe benchmark static root is bench/publish/<label>/wwwroot -- never bench/publish/<label>.\n'
printf 'Next: see README.md for the bench command lines.\n'
