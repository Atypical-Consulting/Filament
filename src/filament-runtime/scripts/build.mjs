#!/usr/bin/env node
/**
 * Builds two bundles from one source tree:
 *
 *   dist/filament.js      __FILAMENT_STATS__=false  — WHAT SHIPS. The C1 artefact.
 *   dist/filament.dev.js  __FILAMENT_STATS__=true   — carries __filament.stats for C3.
 *
 * The only difference is the --define. Nothing is #if'd by hand, no separate
 * "instrumented" source exists to drift out of sync with the real one.
 */
import { build } from 'esbuild';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));

/** @param {{stats: boolean, outfile: string}} o */
async function one(o) {
  const r = await build({
    entryPoints: [path.join(root, 'src/index.ts')],
    outfile: path.join(root, o.outfile),
    bundle: true,
    format: 'esm',
    target: 'es2022',
    platform: 'browser',
    minify: true,
    // Drop the legal-comment banner; there is no third-party code in here.
    legalComments: 'none',
    define: { __FILAMENT_STATS__: String(o.stats) },
    // Tell esbuild the module graph is side-effect free so it may drop
    // ./stats.ts once the last guarded reference folds away.
    metafile: true,
    write: true,
  });
  return r;
}

await one({ stats: false, outfile: 'dist/filament.js' });
await one({ stats: true, outfile: 'dist/filament.dev.js' });
console.log('built dist/filament.js (prod) + dist/filament.dev.js (stats)');
