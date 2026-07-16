#!/usr/bin/env node
/**
 * Measures the shipping runtime and PROVES the stats counters are gone.
 *
 * Two independent gates, both fatal:
 *
 *  1. STATS STRIPPED. Greps the minified production bundle for every stats
 *     identifier and for the `__filament` global. If any survives, the build
 *     fails. This is not hygiene: if instrumentation reached the C1 bundle, C1
 *     would be measuring bytes that do not ship, i.e. the wrong thing.
 *
 *  2. BUDGET. Runtime < 2048 B gzip (spec) — reported alongside brotli, because
 *     BENCH.md entry #2 makes brotli the C2 headline basis and gzip the C1 basis.
 *
 * gzip is level 9 and brotli is quality 11: a static host serves PRECOMPRESSED
 * siblings at max quality (which is what `dotnet publish` emits for the Blazor
 * baseline), so anything less would flatter Filament against a maximally
 * compressed opponent.
 */
import { gzipSync, brotliCompressSync, constants } from 'node:zlib';
import { readFileSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const prod = path.join(root, 'dist/filament.js');

if (!existsSync(prod)) {
  console.error('dist/filament.js missing — run `npm run build` first.');
  process.exit(1);
}

const buf = readFileSync(prod);
const src = buf.toString('utf8');

// ---------------------------------------------------------------------------
// Gate 1 — the stats counters must not exist in the shipping bundle.
// ---------------------------------------------------------------------------
// Every field of the `stats` object, plus the global it hangs off. Minification
// renames LOCALS, so a surviving counter would show up as a property access
// (`.text++`) or as the literal global name — both are matched here.
const FORBIDDEN = [
  '__filament',
  '__FILAMENT_STATS__',
  'computes',
  'reset',
  'stats',
];
const survivors = FORBIDDEN.filter((s) => src.includes(s));

// The five DOM counters are single common words (`text`, `insert`, ...) that also
// appear legitimately (e.g. `insertBefore`), so match them the way they would
// actually survive: as an increment on a property.
const INCREMENTS = /\.\s*(text|attr|listen|insert|remove|links|runs|computes)\s*(\+\+|\+=)/g;
const incSurvivors = [...src.matchAll(INCREMENTS)].map((m) => m[0]);

let failed = false;
if (survivors.length || incSurvivors.length) {
  failed = true;
  console.error('FAIL stats-stripped: instrumentation survived into dist/filament.js');
  for (const s of survivors) console.error(`  literal:   ${s}`);
  for (const s of incSurvivors) console.error(`  increment: ${s}`);
} else {
  console.log('PASS stats-stripped: no stats identifier, no counter increment, no __filament global');
}

// Sanity check the negative control: the DEV bundle MUST contain what the prod
// bundle must not. Without this, gate 1 would also "pass" on an empty file or a
// grep that never matches anything.
const dev = path.join(root, 'dist/filament.dev.js');
if (existsSync(dev)) {
  const devSrc = readFileSync(dev, 'utf8');
  const ok = devSrc.includes('__filament') && /\.\s*(text|links)\s*\+\+/.test(devSrc);
  if (!ok) {
    failed = true;
    console.error('FAIL control: dist/filament.dev.js does NOT contain the counters — the grep is broken, not the build');
  } else {
    console.log('PASS control:        dev bundle DOES contain them (so the grep can detect them)');
  }
}

// ---------------------------------------------------------------------------
// Gate 2 — budget.
// ---------------------------------------------------------------------------
const raw = buf.byteLength;
const gz = gzipSync(buf, { level: 9 }).byteLength;
const br = brotliCompressSync(buf, {
  params: {
    [constants.BROTLI_PARAM_QUALITY]: 11,
    [constants.BROTLI_PARAM_SIZE_HINT]: raw,
  },
}).byteLength;

const BUDGET = 2048;
console.log('');
console.log('filament-runtime — dist/filament.js');
console.log(`  raw      ${String(raw).padStart(6)} B`);
console.log(`  gzip -9  ${String(gz).padStart(6)} B    budget ${BUDGET} B (2 ko)  ${gz < BUDGET ? 'PASS' : 'FAIL'}  headroom ${BUDGET - gz} B`);
console.log(`  brotli   ${String(br).padStart(6)} B    (context: BENCH #2 makes brotli the C2 headline basis)`);
console.log('');

if (gz >= BUDGET) {
  failed = true;
  console.error(`FAIL budget: ${gz} B gzip >= ${BUDGET} B`);
}

process.exit(failed ? 1 : 0);
