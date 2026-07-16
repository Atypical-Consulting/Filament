/**
 * DOM-write / allocation instrumentation for C3.
 *
 * EVERY reference to `stats` in this package is wrapped in `if (__FILAMENT_STATS__)`.
 * `__FILAMENT_STATS__` is an esbuild --define constant, so the production build folds the
 * condition to `false`, drops the block, and then drops this module entirely
 * because nothing references it any more. See scripts/build.mjs + the
 * `stats-stripped` assertion in scripts/size.mjs, which greps the minified
 * output and fails the build if a single counter survives.
 *
 * Size does not matter here: these bytes never reach the C1 bundle. Being
 * generous with counters is therefore free, and precision is what C3 needs.
 */
export const stats = {
  /** setText calls that reached the DOM. C3 counts THIS for the counter app. */
  text: 0,
  /** setAttr calls that reached the DOM. */
  attr: 0,
  /** listen calls. */
  listen: 0,
  /** insert calls (mount AND move — an LIS move is an insertBefore). */
  insert: 0,
  /** remove calls. */
  remove: 0,

  /** Link objects allocated. THE allocation probe: steady-state must add 0. */
  links: 0,
  /** Effect bodies executed. */
  runs: 0,
  /** computed(fn) bodies executed. Laziness is asserted against this. */
  computes: 0,

  /** Sum of the five DOM counters. */
  get dom(): number {
    return stats.text + stats.attr + stats.listen + stats.insert + stats.remove;
  },

  /**
   * The same number as `dom`, under the name bench.mjs's C3 cross-check looks
   * for. Its readFilamentStats() reads `__filament.stats.domWrites` and, when it
   * is absent, reports the cross-check as INCONCLUSIVE rather than failing —
   * i.e. the self-report would silently stop corroborating the MutationObserver
   * while C3 still appeared to pass on the observer alone. An alias is cheap;
   * a cross-check that quietly went dark is not.
   *
   * Not a rename of `dom`: that name is what the runtime's own tests assert on,
   * and it reads better in them.
   */
  get domWrites(): number {
    return stats.dom;
  },

  reset(): void {
    stats.text = stats.attr = stats.listen = stats.insert = stats.remove = 0;
    stats.links = stats.runs = stats.computes = 0;
  },
};
