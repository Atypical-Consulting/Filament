import { stats } from './stats';

export { signal, computed, effect, batch, untrack, Signal, Computed, Effect } from './core';
export type { Owned, Disposable } from './core';
export { setText, setAttr, listen, insert, remove } from './dom';
export { list } from './list';

/**
 * C3's instrument, and the ONLY thing in this package that touches a global.
 *
 * Guarded by `if (__FILAMENT_STATS__)` exactly like every other stats reference, so the
 * production build folds this to nothing, drops the assignment (its only side
 * effect), and then drops ./stats itself because nothing imports it any more.
 * scripts/size.mjs greps the minified bundle for the counter names and fails the
 * build if any survive: if stats reached the C1 bundle, C1 would be measuring an
 * artefact that does not ship.
 *
 * THE MARKER. bench/build-filament.sh verifies the stats state of a bundle from
 * the ARTEFACT rather than from the --define it passed, by grepping for the
 * literal string 'filament:stats': absent from the production bundle proves the
 * branch was eliminated; present in the instrumented bundle proves the
 * instrumentation is really compiled in and a C3 run is not measuring a no-op.
 * That marker is required to live inside this gate, and nothing else may emit
 * it. See build-filament.sh's header ("THE RUNTIME CONTRACT THIS SCRIPT
 * ENFORCES FROM THE ARTIFACT").
 */
if (__FILAMENT_STATS__) {
  (globalThis as Record<string, unknown>).__filament = { marker: 'filament:stats', stats };
}
