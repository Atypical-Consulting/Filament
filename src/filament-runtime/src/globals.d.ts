/**
 * Build flag. Replaced at bundle time by esbuild --define:
 *   production  -> `false`  => every `if (__FILAMENT_STATS__)` block is dead code and is
 *                             eliminated, along with ./stats.ts entirely.
 *   dev / test  -> `true`   => `globalThis.__filament.stats` exists.
 *
 * It is `declare const`, never a runtime binding, so there is no fallback that
 * could accidentally ship. A build that forgets --define fails loudly with
 * "__FILAMENT_STATS__ is not defined" rather than silently shipping the counters.
 */
declare const __FILAMENT_STATS__: boolean;
