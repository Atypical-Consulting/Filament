import { describe, it, expect } from 'vitest';
import { signal, list } from '../src/index';
import { stats } from '../src/stats';

/**
 * The move COUNTS asserted in list.test.ts are only meaningful if the resulting
 * DOM is actually correct — a reconciler that emits 2 operations and the wrong
 * order would sail through them. This file fuzzes correctness independently, then
 * checks minimality against a brute-force LIS reference.
 *
 * Deterministic PRNG: a failure is reproducible from its seed, not a ghost.
 */
function rng(seed: number) {
  let s = seed >>> 0 || 1;
  return () => {
    s ^= s << 13;
    s >>>= 0;
    s ^= s >>> 17;
    s ^= s << 5;
    s >>>= 0;
    return s / 4294967296;
  };
}

interface Item {
  id: number;
}

function harness() {
  const parent = document.createElement('div');
  const items = signal<Item[]>([]);
  let created = 0;
  list(
    parent,
    () => items.value,
    (i) => i.id,
    (i) => {
      created++;
      const el = document.createElement('i');
      el.textContent = String(i.id);
      return el;
    },
  );
  return {
    parent,
    set: (ids: number[]) => (items.value = ids.map((id) => ({ id }))),
    dom: () => [...parent.children].map((c) => Number(c.textContent)),
    created: () => created,
  };
}

/** O(n^2) longest-increasing-subsequence LENGTH. Slow, obviously right. */
function lisLen(a: number[]): number {
  if (!a.length) return 0;
  const d = new Array(a.length).fill(1);
  let best = 1;
  for (let i = 1; i < a.length; i++) {
    for (let j = 0; j < i; j++) if (a[j] < a[i] && d[j] + 1 > d[i]) d[i] = d[j] + 1;
    if (d[i] > best) best = d[i];
  }
  return best;
}

describe('list: randomized correctness', () => {
  it('1000 random transitions all land in exactly the requested order', () => {
    const rand = rng(0xf11a);
    const h = harness();
    let cur: number[] = [];
    for (let step = 0; step < 1000; step++) {
      const size = Math.floor(rand() * 12);
      const pool = Array.from({ length: 15 }, (_, i) => i);
      // Random subset, random order, no duplicate keys.
      for (let i = pool.length - 1; i > 0; i--) {
        const j = Math.floor(rand() * (i + 1));
        [pool[i], pool[j]] = [pool[j], pool[i]];
      }
      const next = pool.slice(0, size);
      h.set(next);
      expect(h.dom(), `step ${step}: ${cur} -> ${next}`).toEqual(next);
      cur = next;
    }
  });

  it('random permutations of 200 keys stay correct and never recreate a node', () => {
    const rand = rng(12345);
    const h = harness();
    const ids = Array.from({ length: 200 }, (_, i) => i);
    h.set(ids);
    const afterMount = h.created();
    for (let step = 0; step < 50; step++) {
      const p = ids.slice();
      for (let i = p.length - 1; i > 0; i--) {
        const j = Math.floor(rand() * (i + 1));
        [p[i], p[j]] = [p[j], p[i]];
      }
      h.set(p);
      expect(h.dom()).toEqual(p);
    }
    // Keys never left, so create() must never have been called again.
    expect(h.created()).toBe(afterMount);
  });

  it('duplicate-free grow/shrink cycles do not leak nodes', () => {
    const rand = rng(999);
    const h = harness();
    for (let step = 0; step < 200; step++) {
      const n = Math.floor(rand() * 30);
      const ids = Array.from({ length: n }, (_, i) => i);
      h.set(ids);
      expect(h.parent.children.length).toBe(n);
    }
  });
});

describe('list: MINIMALITY vs a brute-force LIS reference', () => {
  it('a permutation costs exactly (n - LIS) moves, for 200 random cases', () => {
    // The theoretical minimum number of moves for a pure permutation is
    // n - |LIS|. Anything more means the reconciler is doing avoidable DOM work;
    // this is the property the swap test asserts at one specific point.
    const rand = rng(0xbeef);
    for (let c = 0; c < 200; c++) {
      const n = 2 + Math.floor(rand() * 14);
      const ids = Array.from({ length: n }, (_, i) => i);
      const h = harness();
      h.set(ids);

      const p = ids.slice();
      for (let i = p.length - 1; i > 0; i--) {
        const j = Math.floor(rand() * (i + 1));
        [p[i], p[j]] = [p[j], p[i]];
      }

      stats.reset();
      h.set(p);
      expect(h.dom(), `case ${c}`).toEqual(p);

      // Reference: strip the prefix/suffix the reconciler syncs for free, then
      // the middle's minimum move count is (middleLen - LIS(middle)).
      let s = 0;
      while (s < n && ids[s] === p[s]) s++;
      let e = 0;
      while (e < n - s && ids[n - 1 - e] === p[n - 1 - e]) e++;
      const mid = p.slice(s, n - e).map((id) => ids.indexOf(id));
      const expected = mid.length - lisLen(mid);

      expect(stats.insert, `case ${c}: ${ids} -> ${p}`).toBe(expected);
      expect(stats.remove).toBe(0);
    }
  });

  it('a 2-element swap in 1000 rows is 2 moves — n - LIS = 1000 - 998', () => {
    const h = harness();
    const ids = Array.from({ length: 1000 }, (_, i) => i);
    h.set(ids);
    const p = ids.slice();
    [p[1], p[998]] = [p[998], p[1]];
    stats.reset();
    h.set(p);
    expect(stats.insert).toBe(2);
    expect(h.dom()).toEqual(p);
  });

  it('moving 1 row of 1000 to the front is 1 move', () => {
    const h = harness();
    const ids = Array.from({ length: 1000 }, (_, i) => i);
    h.set(ids);
    const p = [999, ...ids.slice(0, 999)];
    stats.reset();
    h.set(p);
    expect(stats.insert).toBe(1);
    expect(h.dom()).toEqual(p);
  });

  it('reversing 1000 rows is 999 moves (LIS of a reversal is 1)', () => {
    const h = harness();
    const ids = Array.from({ length: 1000 }, (_, i) => i);
    h.set(ids);
    stats.reset();
    h.set(ids.slice().reverse());
    expect(stats.insert).toBe(999);
  });
});
