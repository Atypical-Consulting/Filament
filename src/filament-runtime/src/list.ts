import { Effect, disposeOwned, scope, type Owned, type Disposable } from './core';
import { insert, remove } from './dom';

/**
 * One live row. Also its own disposal scope: `owned` is the head of the intrusive
 * list of disposables the row's template created — effects AND computeds — so
 * unmounting a row unsubscribes everything inside it. Without this the Rows app
 * leaks 1000 subscribers per #run and every subsequent create-warm degrades — C4
 * would rot over iterations.
 *
 * COMPUTEDS COUNT, and the list is typed Disposable rather than Effect for that
 * reason. A row template containing a derived expression — which is exactly what
 * the Phase 2 generator emits for `@(row.Qty * Price)` — builds a Computed in this
 * scope. Left unowned it would sit in the upstream signal's subscriber list
 * forever, holding the row object and its DOM nodes, and propagate() would walk
 * the growing list on every write.
 */
interface Row extends Owned {
  /** The @key. */
  k: unknown;
  /** The row's root node. */
  n: Node;
  owned: Disposable | null;
}

const EMPTY: number[] = [];

function mount<T>(item: T, keyOf: (i: T) => unknown, create: (i: T) => Node): Row {
  const r: Row = { k: keyOf(item), n: null as unknown as Node, owned: null };
  // scope() adopts the template's effects onto `r` AND untracks the template
  // body, so a create() that reads a signal directly cannot silently make the
  // whole list depend on it (which would re-reconcile 1000 rows because one
  // label changed).
  r.n = scope(r, () => create(item));
  return r;
}

function unmount(r: Row): void {
  remove(r.n as ChildNode);
  disposeOwned(r);
}

/**
 * Longest increasing subsequence, returning INDICES into `a`.
 *
 * The point of the whole exercise: rows whose indices land in the LIS are already
 * in relative order and need no DOM move. Everything else moves. A 2-element swap
 * inside 1000 keyed rows yields an LIS of length 998, hence exactly 2 moves — not
 * 1000. O(n log n): patience sorting with a predecessor chain for reconstruction.
 *
 * Entries equal to 0 mark NEW items (see reconcile: old indices are stored +1) and
 * are skipped — a new item is mounted, never moved.
 */
function lis(a: number[]): number[] {
  const p = a.slice(); // predecessor chain
  const r = [0]; // indices of the current best subsequence's tails
  const n = a.length;
  let i: number, j: number, u: number, v: number, c: number;
  for (i = 0; i < n; i++) {
    const ai = a[i];
    if (ai !== 0) {
      j = r[r.length - 1];
      if (a[j] < ai) {
        p[i] = j;
        r.push(i);
        continue;
      }
      u = 0;
      v = r.length - 1;
      while (u < v) {
        c = (u + v) >> 1;
        if (a[r[c]] < ai) u = c + 1;
        else v = c;
      }
      if (ai < a[r[u]]) {
        if (u > 0) p[i] = r[u - 1];
        r[u] = i;
      }
    }
  }
  u = r.length;
  v = r[u - 1];
  while (u-- > 0) {
    r[u] = v;
    v = p[v];
  }
  return r;
}

/**
 * Reconcile `old` (in DOM order) against `items`. Returns the new row array.
 *
 * Structure is Vue 3's patchKeyedChildren, which is the "simple LIS" the spec
 * asks for and is the algorithm the row-benchmark scenarios were designed around:
 *
 *   1. sync a common PREFIX      -> #update (labels only) costs 0 list ops
 *   2. sync a common SUFFIX      -> together with (1), a swap reduces to the middle
 *   3. old exhausted -> mount    -> create 1000 = 1000 inserts, no map, no LIS
 *   4. new exhausted -> unmount  -> clear = 1000 removes, no map, no LIS
 *   5. otherwise: key map + LIS  -> swap = 2 moves
 *
 * Steps 3 and 4 allocate nothing beyond the result array. Step 5 allocates a Map
 * and two arrays — restructuring only, never on the counter path C3 measures.
 */
function reconcile<T>(
  parent: Node,
  old: Row[],
  items: readonly T[],
  keyOf: (i: T) => unknown,
  create: (i: T) => Node,
  anchor: Node | null,
): Row[] {
  const nl = items.length;
  const rows: Row[] = new Array(nl);
  let i = 0;
  let oe = old.length - 1;
  let ne = nl - 1;

  // 1. common prefix
  while (i <= oe && i <= ne && old[i].k === keyOf(items[i])) {
    rows[i] = old[i];
    i++;
  }
  // 2. common suffix
  while (i <= oe && i <= ne && old[oe].k === keyOf(items[ne])) {
    rows[ne] = old[oe];
    oe--;
    ne--;
  }

  if (i > oe) {
    // 3. only mounts left
    if (i <= ne) {
      const a = ne + 1 < nl ? rows[ne + 1].n : anchor;
      for (; i <= ne; i++) {
        rows[i] = mount(items[i], keyOf, create);
        insert(parent, rows[i].n, a);
      }
    }
  } else if (i > ne) {
    // 4. only unmounts left
    for (; i <= oe; i++) unmount(old[i]);
  } else {
    // 5. unknown middle
    const start = i;
    const toPatch = ne - start + 1;
    const keyToNew = new Map<unknown, number>();
    for (let j = start; j <= ne; j++) keyToNew.set(keyOf(items[j]), j);

    // newIndex -> oldIndex + 1. 0 means "new, must mount".
    const map: number[] = new Array(toPatch).fill(0);
    let patched = 0;
    let moved = false;
    let maxNew = 0;

    for (let j = start; j <= oe; j++) {
      const r = old[j];
      if (patched >= toPatch) {
        unmount(r);
        continue;
      }
      const ni = keyToNew.get(r.k);
      if (ni === undefined) {
        unmount(r);
      } else {
        // CONSUME the key. A new index may be claimed by exactly ONE old row.
        //
        // Without this, duplicate @keys corrupt the DOM rather than merely
        // rendering oddly. Two old rows sharing a key both resolve to the SAME
        // `ni`: the second overwrites `rows[ni]` and the first is never
        // unmounted — it is now unreachable from `rows` and stays in the
        // document forever. Worse, both increment `patched`, so `patched` counts
        // CLAIMS instead of FILLED SLOTS, overshoots `toPatch`, and the guard
        // above then unmounts a later row whose key is still in `items`. Measured
        // on [1,1,2] -> [2,1]: three children survive where two were asked for,
        // one of them an orphan and one of the requested rows gone.
        //
        // Deleting makes the claim exclusive, so `patched` counts slots again and
        // can no longer exceed `toPatch` — which retroactively makes the guard
        // above correct instead of merely lucky. Surplus duplicates find their key
        // already taken, fall into the `undefined` branch, and are unmounted:
        // first row wins the identity, the rest are treated as removed. Unique
        // keys never hit a second lookup for the same key, so this is invisible
        // to every well-formed list — no behaviour change, no measurable cost.
        //
        // WHY NOT THROW, given Blazor rejects duplicate @key outright? Two
        // reasons. A throw that is tree-shaken out of prod leaves prod doing the
        // corruption above, which is the one outcome that is worse than either
        // alternative; and a throw that ships is a hard runtime error on
        // something this function can simply get right. Note also that the check
        // Blazor-parity would want ("the new list has duplicate keys") does not
        // even fire here — [1,1,2] -> [2,1] has a duplicate-free NEW list, and
        // the corruption comes from the OLD one. Rejecting duplicate @key belongs
        // in the Phase 2 compiler, where it is a static property of the template
        // and can be reported against source. The runtime's contract is narrower
        // and absolute: never corrupt the document.
        keyToNew.delete(r.k);
        map[ni - start] = j + 1;
        if (ni >= maxNew) maxNew = ni;
        else moved = true; // an inversion exists, so at least one move is needed
        rows[ni] = r;
        patched++;
      }
    }

    // Skip the LIS entirely when nothing is out of order: pure mount/unmount
    // middles pay nothing for an algorithm they do not need.
    const seq = moved ? lis(map) : EMPTY;
    let s = seq.length - 1;
    for (let j = toPatch - 1; j >= 0; j--) {
      const ni = start + j;
      const a = ni + 1 < nl ? rows[ni + 1].n : anchor;
      if (map[j] === 0) {
        rows[ni] = mount(items[ni], keyOf, create);
        insert(parent, rows[ni].n, a);
      } else if (moved) {
        if (s < 0 || j !== seq[s]) insert(parent, rows[ni].n, a); // MOVE
        else s--; // already in order — the whole point
      }
    }
  }
  return rows;
}

/**
 * Keyed list. The runtime half of `@foreach`.
 *
 * @param parent  container the rows live in
 * @param source  reactive read of the current items — re-runs the reconcile
 * @param keyOf   `@key`
 * @param create  builds one row's DOM. Called ONCE per key, inside the row's own
 *                disposal scope, untracked.
 * @param anchor  rows are inserted before this node (null = append to parent)
 *
 * Returns the Effect; disposing it unmounts nothing but releases every row scope,
 * so no effect outlives the list.
 */
export function list<T>(
  parent: Node,
  source: () => readonly T[],
  keyOf: (i: T) => unknown,
  create: (i: T) => Node,
  anchor: Node | null = null,
): Effect {
  let rows: Row[] = [];
  const e = new Effect(() => {
    rows = reconcile(parent, rows, source(), keyOf, create, anchor);
  });
  e.c = () => {
    for (let i = 0; i < rows.length; i++) disposeOwned(rows[i]);
    rows = [];
  };
  return e;
}
