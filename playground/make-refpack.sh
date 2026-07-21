#!/usr/bin/env bash
#
# make-refpack.sh — build the curated reference-pack layout (decision 144) from the INSTALLED SDK.
#
#   playground/make-refpack.sh <out-dir> [--manifest]
#
# Reads playground/refpack.list (comments name the pack a section draws from), copies each named
# assembly from the SDK's newest net10.0 ref dir of that pack into
#   <out>/packs/<pack>/<version>/ref/net10.0/<name>.dll
# and FAILS LOUD on a name the SDK does not have -- a silently missing reference is exactly the
# decision-53 mis-parse this layout exists to prevent. --manifest additionally writes
# <out>/refpack.manifest.json (path, bytes per file) for the playground's fetch-and-hydrate step.
set -euo pipefail
cd "$(dirname "$0")/.."

OUT="${1:?usage: make-refpack.sh <out-dir> [--manifest]}"
MANIFEST="${2:-}"

dotnet_root() {
  local d
  d="$(dirname "$(dirname "$(dotnet --info 2>/dev/null | awk -F': *' '/Base Path/ {print $2}')")")"
  [[ -d "$d/packs" ]] || { echo "FAIL: no packs/ under derived dotnet root '$d'" >&2; exit 1; }
  echo "$d"
}

newest_ref_dir() { # <packs-root>/<pack>
  local best
  best="$(find "$1" -maxdepth 1 -mindepth 1 -type d | sort -V | tail -1)"
  echo "$best/ref/net10.0"
}

ROOT="$(dotnet_root)"
NET_PACK="$ROOT/packs/Microsoft.NETCore.App.Ref"
ASP_PACK="$ROOT/packs/Microsoft.AspNetCore.App.Ref"
NET_REF="$(newest_ref_dir "$NET_PACK")"
ASP_REF="$(newest_ref_dir "$ASP_PACK")"
NET_VER="$(basename "$(dirname "$(dirname "$NET_REF")")")"
ASP_VER="$(basename "$(dirname "$(dirname "$ASP_REF")")")"

NET_OUT="$OUT/packs/Microsoft.NETCore.App.Ref/$NET_VER/ref/net10.0"
ASP_OUT="$OUT/packs/Microsoft.AspNetCore.App.Ref/$ASP_VER/ref/net10.0"
mkdir -p "$NET_OUT" "$ASP_OUT"

pack="net"
copied=0
missing=0
while IFS= read -r line; do
  line="${line%%[[:space:]]}"
  [[ -z "$line" ]] && continue
  if [[ "$line" == \#* ]]; then
    [[ "$line" == *"Microsoft.AspNetCore.App.Ref"* ]] && pack="asp"
    continue
  fi
  if [[ "$pack" == "net" ]]; then src="$NET_REF/$line.dll"; dst="$NET_OUT/$line.dll"
  else src="$ASP_REF/$line.dll"; dst="$ASP_OUT/$line.dll"; fi
  if [[ ! -f "$src" ]]; then
    echo "FAIL: refpack.list names '$line' but the SDK has no $src" >&2
    missing=$((missing+1)); continue
  fi
  cp "$src" "$dst"; copied=$((copied+1))
done < playground/refpack.list

[[ "$missing" -eq 0 ]] || exit 1

raw_bytes="$(find "$OUT/packs" -name '*.dll' -print0 | xargs -0 stat -f%z 2>/dev/null | awk '{s+=$1} END {print s}')" || true
if [[ -z "${raw_bytes:-}" ]]; then # Linux stat
  raw_bytes="$(find "$OUT/packs" -name '*.dll' -print0 | xargs -0 stat -c%s | awk '{s+=$1} END {print s}')"
fi
echo "refpack: $copied assemblies, $raw_bytes bytes raw -> $OUT/packs"

if [[ "$MANIFEST" == "--manifest" ]]; then
  node - "$OUT" <<'EOF'
const fs = require('fs'), path = require('path');
const out = process.argv[2];
const files = [];
(function walk(d) {
  for (const e of fs.readdirSync(d, { withFileTypes: true })) {
    const p = path.join(d, e.name);
    if (e.isDirectory()) walk(p);
    else if (e.name.endsWith('.dll')) files.push({ path: path.relative(out, p).split(path.sep).join('/'), bytes: fs.statSync(p).size });
  }
})(path.join(out, 'packs'));
files.sort((a, b) => a.path.localeCompare(b.path));
fs.writeFileSync(path.join(out, 'refpack.manifest.json'),
  JSON.stringify({ generated: 'make-refpack.sh', files, totalBytes: files.reduce((a, f) => a + f.bytes, 0) }, null, 2) + '\n');
console.log(`manifest: ${files.length} files -> ${path.join(out, 'refpack.manifest.json')}`);
EOF
fi
