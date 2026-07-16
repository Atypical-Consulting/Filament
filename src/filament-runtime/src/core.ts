import { stats } from './stats';

/* ===========================================================================
 * WHY THE PUBLIC SURFACE IS `.value` AND NOT `count()` / `count(v)`
 * ===========================================================================
 * Filament.Core declares, and Phase 2/3 must compile against:
 *
 *     Signal<T>   { public T Value { get; set; } }
 *     Computed<T> { public T Value { get; } }
 *
 * With a `.value` accessor the C#->JS mapping is a per-SYMBOL identifier rename.
 * The emitter never inspects the syntactic context of the access:
 *
 *     count.Value            ->  count.value            (read)
 *     count.Value = 5        ->  count.value = 5        (write)
 *     count.Value++          ->  count.value++          (read-modify-write, ONE node in, ONE node out)
 *     count.Value += 2       ->  count.value += 2
 *     total.Value            ->  total.value            (Computed; read-only by shape)
 *
 * With Solid-style call signatures those same five lines become count(), count(5),
 * count(count() + 1), count(count() + 2), total(). The emitter would then have to
 * ask "am I the left operand of an assignment?" and desugar `++`/`+=` into a
 * read-then-write pair by hand. That is a syntactic transform whose correctness
 * depends on getting every C# assignment form right — a bug surface we get to not
 * have, on a compiler that does not exist yet.
 *
 * Read-only-ness of Computed<T> also falls out for free: we simply never define a
 * setter, so `total.value = x` throws in strict mode (all ESM is strict) instead of
 * silently doing nothing. That mirrors the C# compile error rather than diverging
 * from it.
 *
 * COST, honestly stated: one accessor pair per class. It lives on the PROTOTYPE,
 * not the instance, so 1000 row-label signals share one hidden class and one pair
 * of accessor functions; V8 inlines a monomorphic prototype getter into a plain
 * field load. The alternative (a closure pair per signal) would allocate two
 * function objects per row. `.value` is both the better mapping and the cheaper one.
 * ===========================================================================
 */

/* --------------------------------------------------------------------------
 * Graph shape
 *
 * A Link is one dependency edge. It is doubly linked in BOTH directions:
 *   pd/nd  — prev/next inside the SUBSCRIBER's dependency list
 *   ps/ns  — prev/next inside the SOURCE's subscriber list
 * so unlinking an edge is O(1) from either end, which is what makes conditional
 * dependencies and disposal cheap.
 *
 * `v` is the source's `version` as observed when this edge was last read. It is
 * how a PENDING subscriber decides whether an upstream computed actually changed
 * value, rather than merely having been invalidated.
 * -------------------------------------------------------------------------- */
interface Link {
  dep: Source;
  sub: Sub;
  v: number;
  pd: Link | undefined;
  nd: Link | undefined;
  ps: Link | undefined;
  ns: Link | undefined;
}

/** Anything that can be depended upon: Signal, Computed. */
interface Source {
  version: number;
  flags: number;
  subs: Link | undefined;
  subsTail: Link | undefined;
}

/** Anything that can depend: Computed, Effect. */
interface Sub {
  flags: number;
  deps: Link | undefined;
  depsTail: Link | undefined;
}

// Flags. Plain consts, not a const enum: esbuild inlines single-assignment
// primitive consts during minification, so these cost zero bytes, and const enum
// has cross-module caveats we do not need to think about.
const DIRTY = 1; //    must recompute / re-run: a direct dependency changed value
const PENDING = 2; //  a TRANSITIVE dependency changed: must checkDirty before acting
const QUEUED = 4; //   sitting in the effect queue
const DISPOSED = 8;
const COMPUTED = 16; // discriminates Computed from Signal without instanceof
/**
 * A Computed whose fn THREW on its last run. Semantically "must re-run", exactly
 * like DIRTY — refresh() treats the two identically.
 *
 * It is a SEPARATE BIT from DIRTY for one reason, and it is subtle enough to be
 * worth the byte: propagate() prunes its walk at any node already carrying
 * DIRTY|PENDING, on the invariant "a marked node has already marked its own
 * subtree". A computed marked by recompute()'s failure path has marked NOTHING —
 * the throw unwound before it could. Reusing DIRTY there would silently poison
 * that invariant and propagate() would stop descending past the failed computed
 * FOREVER, which is precisely the "permanently deaf effect" this fix exists to
 * remove. STALE means "must re-run, and my subtree is NOT marked", so propagate()
 * walks straight through it and re-marks the subtree properly.
 */
const STALE = 32;

// --------------------------------------------------------------------------
// Scheduler state. All module-level scalars — nothing here allocates.
// --------------------------------------------------------------------------

/** The subscriber currently tracking reads. `null` = reads are untracked. */
let activeSub: Sub | null = null;
/** The scope adopting newly created effects, for cascade disposal. */
let owner: Owned | null = null;
/** The effect queue: an INTRUSIVE list threaded through Effect.nq. */
let qHead: Effect | null = null;
let qTail: Effect | null = null;
let batchDepth = 0;
let flushing = false;

/**
 * Anything a scope can adopt and tear down: Effect, Computed.
 *
 * Both already carry `deps`/`depsTail`, so "dispose" is the same two moves for
 * both — drop every edge, then refuse to act again — and the owner list does not
 * need to know which kind it is holding.
 */
export interface Disposable {
  /** Next sibling in the OWNER's list. */
  no: Disposable | null;
  dispose(): void;
}

/**
 * Owns disposables so they can be torn down together. Duck-typed on `owned`.
 *
 * This list holds Computeds as well as Effects. It has to: a Computed created in
 * a disposal scope (a list() row template, an effect body) that reads a
 * longer-lived signal would otherwise sit in that signal's subscriber list
 * FOREVER, retaining its fn, the captured row, and the row's DOM nodes — and
 * propagate() would walk the growing list on every write. See Computed.dispose.
 */
export interface Owned {
  owned: Disposable | null;
}

// --------------------------------------------------------------------------
// Edge maintenance
// --------------------------------------------------------------------------

/**
 * Record that `sub` read `dep`.
 *
 * THE ZERO-ALLOCATION TRICK, and the reason C3 holds: a subscriber almost always
 * reads the same dependencies in the same ORDER on every run. `sub.depsTail` is a
 * cursor into the edge list built by the previous run. If the edge at the cursor
 * already points at `dep`, we advance the cursor and refresh the observed version
 * — no object is created. A steady-state re-run therefore allocates NOTHING: it
 * walks a list that already exists and writes two fields.
 *
 * A new Link is built only when the dependency SET or ORDER actually changes, i.e.
 * on first run and on a real control-flow change. Those are not the hot path.
 *
 * Known and accepted: a subscriber that reads deps in a NON-consecutively-repeating
 * order (a, b, a) allocates a duplicate edge for `a`. That costs memory, never
 * correctness — propagate() dedupes via the DIRTY/QUEUED flags — and the duplicate
 * is stable across runs, so it is allocated once and then reused like any other.
 */
function link(dep: Source, sub: Sub): void {
  const t = sub.depsTail;
  // Same dep read twice in a row (e.g. `a.value + a.value`): cursor already there.
  if (t !== undefined && t.dep === dep) {
    t.v = dep.version;
    return;
  }
  const n = t !== undefined ? t.nd : sub.deps;
  if (n !== undefined && n.dep === dep) {
    n.v = dep.version; // REUSE — the hot path. No allocation.
    sub.depsTail = n;
    return;
  }
  const l: Link = {
    dep,
    sub,
    v: dep.version,
    pd: t,
    nd: n,
    ps: dep.subsTail,
    ns: undefined,
  };
  if (__FILAMENT_STATS__) stats.links++;
  if (n !== undefined) n.pd = l;
  if (t !== undefined) t.nd = l;
  else sub.deps = l;
  if (dep.subsTail !== undefined) dep.subsTail.ns = l;
  else dep.subs = l;
  dep.subsTail = l;
  sub.depsTail = l;
}

/** Detach one edge from its source's subscriber list. Returns the next dep edge. */
function unlink(l: Link): Link | undefined {
  const nd = l.nd;
  const dep = l.dep;
  const ps = l.ps;
  const ns = l.ns;
  if (ns !== undefined) ns.ps = ps;
  else dep.subsTail = ps;
  if (ps !== undefined) ps.ns = ns;
  else dep.subs = ns;
  return nd;
}

/**
 * Drop every edge the last run did NOT re-read (they sit past the cursor).
 *
 * This is what makes conditional dependencies correct: an effect that reads `a`
 * only while `b` is true stops re-reading `a` the moment `b` goes false, so the
 * `a` edge falls past the cursor here and is unlinked. Writes to `a` then reach
 * nobody. Passing depsTail === undefined drops ALL edges, which is disposal.
 */
function prune(sub: Sub): void {
  const t = sub.depsTail;
  let l = t !== undefined ? t.nd : sub.deps;
  if (l !== undefined) {
    while (l !== undefined) l = unlink(l);
    if (t !== undefined) t.nd = undefined;
    else sub.deps = undefined;
  }
}

// --------------------------------------------------------------------------
// Propagation
// --------------------------------------------------------------------------

/**
 * Mark subscribers of a changed source.
 *
 * GLITCH FREEDOM. Marking is a pure graph walk: it runs NO user code and reads NO
 * value. The whole marking pass therefore completes before any effect body runs,
 * so an effect can never observe a half-updated graph. Combined with lazy pull in
 * Computed.value, that is the entire mechanism.
 *
 * WHAT ACTUALLY PREVENTS THE DOUBLE-RUN, stated precisely because it is easy to
 * credit the wrong line here (mutation testing caught exactly that: deleting the
 * `continue` below fails NO test). For a -> b, a -> c, b+c -> d, writing `a`
 * reaches d twice, once via b and once via c. d runs once because:
 *   1. reaching d only MARKS and enqueues it — nothing executes during the walk;
 *   2. enqueue() is idempotent via the QUEUED flag, so the second arrival is a
 *      no-op;
 *   3. flush() pops each effect once and clears QUEUED before running it.
 * Replace step 1 with an eager `runEffect(sub)` and the diamond tests fail with
 * `seen = [12, 14, 24]` — 14 being the glitch, a sum of a fresh b and a stale c.
 *
 * The `already marked -> stop` branch below is a WALK-PRUNING OPTIMISATION, not
 * the correctness guard: a node that is already marked has already marked its own
 * subtree, so re-descending is redundant work. It keeps propagation linear in a
 * dense graph. Correctness would survive its removal; the cost would not.
 *
 * A DIRECT dependent of a changed Signal is DIRTY (a value it read definitely
 * changed). A dependent of an invalidated Computed is only PENDING, because that
 * computed may yet recompute to the same value — see checkDirty.
 */
function propagate(l: Link | undefined, mark: number): void {
  for (; l !== undefined; l = l.ns) {
    const sub = l.sub;
    const f = sub.flags;
    if (f & (DIRTY | PENDING)) {
      sub.flags = f | mark; // upgrade PENDING -> DIRTY; subtree already marked
      continue;
    }
    sub.flags = f | mark;
    if (f & COMPUTED) propagate((sub as Computed<unknown>).subs, PENDING);
    else enqueue(sub as Effect);
  }
}

/** Push an effect onto the intrusive queue. No array, therefore no allocation. */
function enqueue(e: Effect): void {
  if (e.flags & QUEUED) return;
  e.flags |= QUEUED;
  if (qTail !== null) qTail.nq = e;
  else qHead = e;
  qTail = e;
}

/**
 * Is a PENDING subscriber ACTUALLY stale? Refresh each upstream computed, then
 * compare the version we observed at read time against the version now. This is
 * what makes "re-runs only on a real change" hold THROUGH a computed: if
 * `computed(() => count.value > 5)` is invalidated by 1 -> 2, it recomputes to
 * `false` again, its version does not move, and the effect below it does not run.
 */
function checkDirty(sub: Sub): boolean {
  for (let l = sub.deps; l !== undefined; l = l.nd) {
    const d = l.dep;
    if (d.flags & COMPUTED) refresh(d as Computed<unknown>);
    if (l.v !== d.version) return true;
  }
  return false;
}

/**
 * Bring a computed up to date if — and only if — it is actually stale.
 *
 * THE POST-CONDITION, and why the guard lives HERE rather than in recompute().
 *
 * refresh() makes exactly one promise to every caller: when it returns, `c` is
 * up to date; if it CANNOT deliver that, `c` is left carrying a re-run marker.
 * There is no third outcome. A computed left CLEAN on a value its fn never
 * returned serves that wrong value forever, silently, with no error and no write
 * that can ever repair it — the graph just quietly disagrees with itself.
 *
 * Both branches below can throw, because both ultimately run user code: the
 * DIRTY|STALE branch through recompute() -> c.fn(), and the PENDING branch
 * through checkDirty() -> refresh() -> recompute() -> some UPSTREAM fn(). And
 * both branches clear a re-run marker before doing so (recompute() clears its
 * own; the PENDING branch clears PENDING on the line below). That is the whole
 * bug class: CLEAR A RE-RUN MARKER, THEN RUN CODE THAT CAN THROW.
 *
 * So the guard wraps the WHOLE decision, not one branch of it. An earlier fix put
 * a catch inside recompute() only — which is the depth a one-computed-deep test
 * happens to exercise — and left the PENDING branch two lines above it bare: a
 * chain `signal -> b -> c` where b throws left c CLEAN on its old value while b
 * recovered, so `c !== b + 1` permanently. Guarding the post-condition instead of
 * the branch makes that unreachable by construction, and recompute() no longer
 * needs a catch of its own: it is unreachable except through here.
 *
 * WHY STALE AND NOT PENDING. Restoring PENDING would be the tempting "put back
 * what I cleared", and it is wrong — it would re-open the permanently-deaf-effect
 * bug from the other side. propagate() prunes its walk at any node already
 * carrying DIRTY|PENDING, on the invariant "a marked node has already marked its
 * own subtree". A downstream effect may have been flushed and cleared its own
 * marks while c sat here, so c's subtree is NOT reliably marked any more; a c left
 * PENDING would make the next propagate() stop dead at c and never re-enqueue that
 * effect. STALE means "must re-run, and my subtree is NOT marked", which is
 * precisely the truth after an unwind, and propagate() walks straight through it.
 */
function refresh(c: Computed<unknown>): void {
  const f = c.flags;
  if (f & (DIRTY | STALE | PENDING)) {
    try {
      // STALE (last run threw) is as good a reason to re-run as DIRTY.
      if (f & (DIRTY | STALE)) recompute(c);
      else {
        c.flags = f & ~PENDING;
        if (checkDirty(c)) recompute(c);
      }
    } catch (e) {
      // Not `|= STALE` unconditionally: dispose() during our own fn cleared every
      // re-run marker on purpose ("a late read cannot restart the fn") and must win.
      if (!(c.flags & DISPOSED)) c.flags |= STALE;
      throw e;
    }
  }
}

/**
 * OWNERSHIP DURING RECOMPUTE, and why `owner = c` rather than leaving it alone.
 *
 * A Computed's fn runs lazily — at the first `.value` read, from whatever context
 * happens to get there first. If we left `owner` as the ambient scope, a computed
 * created INSIDE this fn would be adopted by whichever effect triggered the read,
 * and that effect's next re-run would dispose it: its edges would be pruned, the
 * upstream signal would no longer reach it, and THIS computed would never be
 * invalidated again. A leak traded for a stale value, which is the worse bug.
 *
 * Adopting onto `c` instead makes the nesting lexical: a computed's children live
 * exactly as long as the run that created them, torn down by the disposeOwned
 * below on the next recompute and by Computed.dispose when `c` itself dies. This
 * mirrors runEffect() exactly. `test/computed-dispose.test.ts` gates both halves.
 *
 * WHAT A THROWING fn MUST NOT COST YOU, and why prune() moved inside the try.
 *
 * fn() is arbitrary user code, so it can throw at any point — including HALFWAY
 * THROUGH its reads. Two invariants have to survive that:
 *
 *  1. THE COMPUTED MUST STILL KNOW IT IS STALE. The flags are cleared up-front
 *     (see below), so a run that unwinds must put a re-run marker back or the
 *     computed is left CLEAN sitting on a value its fn never returned — a wrong
 *     value served silently, forever. That restoration is refresh()'s catch, not
 *     ours: refresh() is this function's ONLY caller, and the same restoration is
 *     needed for the checkDirty() path it also owns. One guard over the whole
 *     post-condition beats two siblings that must be kept in sync — keeping them
 *     in sync is exactly what failed before.
 *
 *  2. ITS DEPENDENCY SET MUST NOT BE TRUNCATED. prune() drops every edge past the
 *     `depsTail` cursor, which is exactly right after a COMPLETE run and exactly
 *     wrong after a partial one: the edges fn never reached this time look
 *     identical to edges fn deliberately stopped reading. Pruning on the throw
 *     path would unsubscribe the computed from deps it still uses, and no later
 *     write to them would ever reach it. So prune() runs only on the success path.
 *     The cost of not pruning is an edge the computed may no longer need — it
 *     re-runs a little too eagerly, and the next SUCCESSFUL run prunes properly.
 *     Over-subscribing is a wasted run; under-subscribing is a wrong answer.
 *
 * The flags are still cleared BEFORE fn() rather than after, which is what makes
 * a self-referential computed (`c = computed(() => c.value)`) terminate: the
 * re-entrant refresh() finds no re-run marker and serves the in-progress value
 * instead of recursing into recompute() until the stack dies.
 */
function recompute(c: Computed<unknown>): void {
  const prevSub = activeSub;
  const prevOwner = owner;
  activeSub = c;
  owner = c;
  c.depsTail = undefined;
  c.flags &= ~(DIRTY | PENDING | STALE);
  disposeOwned(c); // tear down the previous run's nested computeds/effects
  try {
    if (__FILAMENT_STATS__) stats.computes++;
    const v = c.fn();
    // Version only moves on a REAL change. Everything downstream keys off this.
    if (!Object.is(v, c._v)) {
      c._v = v;
      c.version++;
    }
    prune(c); // success only — see (2) above
  } finally {
    activeSub = prevSub;
    owner = prevOwner;
  }
}

/**
 * The number of effect runs ONE drain may take before the runtime calls it a
 * cycle. Deliberately enormous, and that is a considered choice, not timidity:
 *
 * a self-writing effect that TERMINATES and one that never does are the same
 * shape — `s.value = f(s.value)` — and differ only in whether the recursion
 * bottoms out. No static property separates them, so the only available test is
 * "has it settled yet", and any cap is a guess about how long legitimate settling
 * can take. The suite pins a 200,001-run cascade as legitimate and correct
 * (`the self-write cascade is FLAT`), so anything at Solid's scale (100) would
 * reject working code. 1e6 is ~5x that pinned ceiling: it cannot fire on a
 * cascade this codebase considers valid, and it converts an infinite loop from
 * "the tab is gone" into a throw in under a second. That is the whole win — this
 * is a LIVENESS guard, not a correctness one, and correctness is why it errs high.
 */
const CYCLE_CAP = 1_000_000;

/**
 * Run the queue.
 *
 * Re-entrancy: an effect body that writes a signal lands back here with
 * `flushing` true; we return immediately and the running loop picks the newly
 * queued effect up. So there is exactly one flush loop, ever, and no recursion.
 *
 * ERROR ISOLATION, and why the throw is DEFERRED rather than swallowed or
 * propagated on the spot.
 *
 * Effects in this queue are INDEPENDENT subscribers. They are here because a
 * signal they each read changed; that one of them throws is not evidence about
 * any of the others. Letting the exception unwind the loop abandons every effect
 * still queued — and because the loop pops-and-clears each effect's flags BEFORE
 * running it, those effects are left marked CLEAN while their dependency has
 * moved on. They do not run late; they never run at all, and no later write can
 * fix them because nothing knows they are behind. A DOM that stops updating for
 * a reason no stack trace mentions.
 *
 * So each effect gets its own try/catch and the drain always completes. But the
 * error is REPORTED, not eaten: a silently swallowed exception is its own bug and
 * a worse one to debug. The first is rethrown once the queue is empty, so it
 * still surfaces synchronously at the write that caused it (`s.value = x` throws,
 * as it did before) — only now every effect has already run. Subsequent errors in
 * the same drain are dropped rather than aggregated: reporting the first cause
 * beats an AggregateError nobody reads, and it costs no bytes.
 */
function flush(): void {
  if (batchDepth !== 0 || flushing) return;
  flushing = true;
  let err: unknown;
  let failed = false;
  let n = 0;
  try {
    while (qHead !== null) {
      const e = qHead;
      qHead = e.nq;
      if (qHead === null) qTail = null;
      e.nq = null;
      e.flags &= ~QUEUED;
      if (e.flags & DISPOSED) continue;
      if (++n > CYCLE_CAP) {
        // Past the cap, treat the queue as a cycle and run nothing more. We keep
        // LOOPING rather than breaking: the pop above already clears `nq` and
        // QUEUED, so letting it drain empties the queue and unmarks the effects
        // with no extra code. Breaking here instead would strand a full queue of
        // QUEUED-flagged effects — enqueue() would then refuse to re-add them and
        // the next write would silently reach nobody. And since nothing runs, no
        // effect can write, so nothing re-enqueues: the drain terminates.
        //
        // The flag clear is NOT optional, and it is the whole reason this branch
        // is three lines instead of one. An effect drained here did not run, so
        // it is stale — but "stale" and "DIRTY" must not be confused. Leaving
        // DIRTY set produces the one state propagate() cannot recover from:
        // DIRTY-but-not-QUEUED. propagate() prunes at any node already carrying
        // DIRTY|PENDING and therefore never calls enqueue() on it again, so the
        // effect would be deaf to every future write, forever. That is not
        // hypothetical — a bystander effect merely sitting in the queue behind a
        // cycle it has nothing to do with inherits it. Clearing the marks leaves
        // it CLEAN and stale: the next write to a dep re-marks and re-enqueues it
        // through the normal path and it catches up. A cycle is a loud throw the
        // author must fix; it must not also silently kill the effects standing
        // next to it.
        e.flags &= ~(DIRTY | PENDING);
        if (!failed) {
          failed = true;
          err = new Error('Filament: cycle detected');
        }
        continue;
      }
      try {
        if (e.flags & DIRTY) {
          e.flags &= ~(DIRTY | PENDING);
          runEffect(e);
        } else if (e.flags & PENDING) {
          e.flags &= ~PENDING;
          if (checkDirty(e)) runEffect(e);
        }
      } catch (x) {
        if (!failed) {
          failed = true;
          err = x;
        }
      }
    }
  } finally {
    flushing = false;
  }
  if (failed) throw err;
}

/**
 * `prune()` sits INSIDE the try, after fn(), for the same reason it does in
 * recompute(): a body that throws halfway leaves `depsTail` mid-list, and pruning
 * against a partial cursor unsubscribes the effect from every dependency the run
 * had not reached yet. That effect is then permanently deaf to those signals —
 * the throw is loud and one-off, the deafness is silent and forever. Keeping the
 * un-re-read edges costs at most a spurious re-run, which the next completed run
 * prunes away.
 */
function runEffect(e: Effect): void {
  const prevSub = activeSub;
  const prevOwner = owner;
  activeSub = e;
  owner = e;
  e.depsTail = undefined; // reset the edge-reuse cursor
  disposeOwned(e); // tear down the previous run's nested effects
  try {
    if (__FILAMENT_STATS__) stats.runs++;
    e.fn();
    prune(e); // drop dependencies this run stopped reading — success path only
  } finally {
    activeSub = prevSub;
    owner = prevOwner;
  }
}

/** Dispose everything adopted by `o`. Cascades: children dispose their children. */
export function disposeOwned(o: Owned): void {
  let c = o.owned;
  o.owned = null;
  while (c !== null) {
    const n = c.no;
    c.no = null;
    c.dispose();
    c = n;
  }
}

// --------------------------------------------------------------------------
// Public API
// --------------------------------------------------------------------------

/** Observable value. Maps to C# `Signal<T> { public T Value { get; set; } }`. */
export class Signal<T> implements Source {
  _v: T;
  version = 0;
  flags = 0;
  subs: Link | undefined = undefined;
  subsTail: Link | undefined = undefined;

  constructor(v: T) {
    this._v = v;
  }

  get value(): T {
    if (activeSub !== null) link(this, activeSub);
    return this._v;
  }

  set value(v: T) {
    // Object.is, not !==: NaN !== NaN would make every write to a NaN signal look
    // like a change and re-run its effects forever. -0/+0 likewise.
    if (!Object.is(v, this._v)) {
      this._v = v;
      this.version++;
      propagate(this.subs, DIRTY);
      flush();
    }
  }
}

/** LAZY derivation. Maps to C# `Computed<T> { public T Value { get; } }`. */
export class Computed<T> implements Source, Sub, Owned, Disposable {
  _v: T = undefined as T;
  fn: () => T;
  version = 0;
  // Born DIRTY: fn has never run. This is the laziness — construction does not
  // evaluate, the first `.value` read does.
  flags = DIRTY | COMPUTED;
  subs: Link | undefined = undefined;
  subsTail: Link | undefined = undefined;
  deps: Link | undefined = undefined;
  depsTail: Link | undefined = undefined;
  /** Head of the intrusive list of disposables this computed's fn created. */
  owned: Disposable | null = null;
  /** Next sibling in the OWNER's list. */
  no: Disposable | null = null;

  constructor(fn: () => T) {
    this.fn = fn;
    // Adopt onto the enclosing scope, exactly as Effect does. Construction is
    // EAGER (unlike fn), so `owner` here is the lexical scope the author wrote
    // the computed in — a list() row, an effect body, or null at top level.
    // Without this a row-template computed outlives its row forever.
    if (owner !== null) {
      // O(1) prepend. Dispose order is reverse creation order.
      this.no = owner.owned;
      owner.owned = this;
    }
  }

  get value(): T {
    // Refresh BEFORE link: the edge must record the POST-refresh version, or a
    // subscriber would latch a stale version and miss the next real change.
    //
    // The link is in a `finally` because THE EDGE MUST EXIST EVEN IF THE REFRESH
    // THREW. Marking a failed computed STALE only helps if something is still
    // listening to it: propagate() reaches the computed and re-marks it correctly,
    // then walks `subs` — and a plain `refresh(); link()` leaves `subs` EMPTY,
    // because the throw jumps clean over the link. The reader never subscribed at
    // all, so it is deaf to every future write, silently and forever.
    //
    // That is not an exotic path. It is what an ordinary error boundary does:
    // `effect(() => { try { use(c.value) } catch { showError() } })`. The effect
    // handles the error, carries on, and — without this finally — never hears
    // about the recovery. It reproduces whenever the FIRST read throws, since the
    // "edge already exists from a previous good run" case is the only thing that
    // was ever hiding it.
    //
    // Linking with the PRE-refresh version is not a compromise, it is the point:
    // the computed is STALE, so the subscriber's next checkDirty() refreshes it,
    // observes the version move, and re-runs. A version that looks stale on an
    // edge to a computed that IS stale is simply the truth.
    //
    // Residual, and genuinely out of scope: a computed that throws before reading
    // any signal has no dependency anywhere, so no write can ever retrigger it. No
    // push-based runtime can recover that — there is nothing to push from.
    try {
      refresh(this as Computed<unknown>);
    } finally {
      if (activeSub !== null) link(this, activeSub);
    }
    return this._v;
  }

  dispose(): void {
    if (this.flags & DISPOSED) return;
    // COMPUTED is PRESERVED, not overwritten. propagate() and checkDirty()
    // discriminate on that flag to decide "recurse into subs" vs "enqueue as an
    // effect"; clearing it would make any surviving downstream edge treat this
    // object as an Effect and call enqueue() on something with no queue fields.
    // DIRTY/PENDING are cleared so a late read cannot restart the fn.
    this.flags = COMPUTED | DISPOSED;
    disposeOwned(this);
    this.depsTail = undefined;
    prune(this); // unsubscribes from every source
  }
}

/** Reaction. Re-runs when a signal it READ DURING EXECUTION changes. */
export class Effect implements Sub, Owned, Disposable {
  fn: () => void;
  flags = 0;
  deps: Link | undefined = undefined;
  depsTail: Link | undefined = undefined;
  /** Head of the intrusive list of disposables this effect owns. */
  owned: Disposable | null = null;
  /** Next sibling in the OWNER's list. */
  no: Disposable | null = null;
  /** Next in the flush queue. */
  nq: Effect | null = null;
  /** Optional teardown, used by list() to release its row scopes. */
  c: (() => void) | null = null;

  constructor(fn: () => void) {
    this.fn = fn;
    if (owner !== null) {
      // O(1) prepend. Dispose order is reverse creation order.
      this.no = owner.owned;
      owner.owned = this;
    }
    runEffect(this);
  }

  dispose(): void {
    if (this.flags & DISPOSED) return;
    this.flags = DISPOSED;
    disposeOwned(this);
    this.depsTail = undefined;
    prune(this); // unsubscribes from every source
    if (this.c !== null) this.c();
  }
}

export function signal<T>(v: T): Signal<T> {
  return new Signal(v);
}

export function computed<T>(fn: () => T): Computed<T> {
  return new Computed(fn);
}

export function effect(fn: () => void): Effect {
  return new Effect(fn);
}

/**
 * Coalesce writes into ONE logical change.
 *
 * Without this, `a.value = 1; b.value = 2` is two logical changes and an effect
 * reading both runs twice — which is correct, just not what the author meant.
 * batch() is where the author states the boundary. Effects run once, after the
 * outermost batch closes, and never observe an intermediate state.
 */
export function batch(fn: () => void): void {
  batchDepth++;
  try {
    fn();
  } finally {
    batchDepth--;
    flush();
  }
}

/** Read without subscribing. Used by list() so templates cannot capture edges. */
export function untrack<T>(fn: () => T): T {
  const prev = activeSub;
  activeSub = null;
  try {
    return fn();
  } finally {
    activeSub = prev;
  }
}

/** Run `fn` with `o` adopting any effect it creates. Internal to list(). */
export function scope<T>(o: Owned, fn: () => T): T {
  const prevOwner = owner;
  const prevSub = activeSub;
  owner = o;
  activeSub = null; // a template is built once; it must not become a dependency
  try {
    return fn();
  } finally {
    owner = prevOwner;
    activeSub = prevSub;
  }
}
