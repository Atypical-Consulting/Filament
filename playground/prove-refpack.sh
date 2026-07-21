#!/usr/bin/env bash
#
# prove-refpack.sh — THE EQUIVALENCE PROOF (decision 144): the FULL generator test suite, green,
# with FILAMENT_DOTNET_ROOT pointed at the curated layout. A green suite means every supported
# fixture compiles to the same bytes (the canon gates + snapshots see to that) and every refusal
# still refuses -- the curated set is behaviourally indistinguishable from the SDK's 300+ assemblies
# across the entire measured surface. A red suite BLOCKS shipping; there is no cherry-picked subset.
set -euo pipefail
cd "$(dirname "$0")/.."

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

bash playground/make-refpack.sh "$TMP"

echo
echo "==> full suite under FILAMENT_DOTNET_ROOT=$TMP"
FILAMENT_DOTNET_ROOT="$TMP" dotnet test Filament.sln --nologo "$@"
