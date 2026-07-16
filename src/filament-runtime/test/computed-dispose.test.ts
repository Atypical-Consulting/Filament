import { describe, it, expect, beforeEach } from 'vitest';
import { signal, computed, effect, list } from '../src/index';
import { stats } from '../src/stats';

beforeEach(() => stats.reset());

/**
 * Count the edges hanging off a source's subscriber list.
 *
 * This walks the SOURCE side (`subs`/`ns`), which is the side a leak shows up on:
 * a Computed that is never disposed stays in its dependency's subscriber list
 * forever, and propagate() walks that list on every single write. The count is
 * therefore both the leak detector and the reason the leak degrades performance.
 */
function subCount(src: unknown): number {
  let n = 0;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  for (let l = (src as any).subs; l !== undefined; l = l.ns) n++;
  return n;
}

/* ===========================================================================
 * THE ROW-TEMPLATE LEAK — the case that matters for Phase 2.
 *
 * The generator maps a C# derived property onto computed(). The first `@foreach`
 * row containing `@(row.Qty * Price)` therefore constructs a Computed inside a
 * row scope that reads a longer-lived signal. If Computed has no disposal path,
 * every one of those rows is retained by that signal forever.
 * =========================================================================== */
describe('computed: DISPOSAL inside a row scope', () => {
  it('a computed in a row template is released when the row unmounts', () => {
    const globalMult = signal(2);
    const src = signal<{ id: number }[]>([]);
    const parent = document.createElement('tbody');

    list(
      parent,
      () => src.value,
      (r) => r.id,
      (r) => {
        const td = document.createElement('td');
        // A derived expression inside the row template: exactly what the Phase 2
        // generator emits for `@(row.Qty * Price)`.
        const derived = computed(() => r.id * globalMult.value);
        effect(() => {
          td.textContent = String(derived.value);
        });
        return td;
      },
    );

    const cycle = (base: number) =>
      Array.from({ length: 100 }, (_, i) => ({ id: base + i }));

    // 10 full replacements. Every row from every previous cycle is unmounted.
    const counts: number[] = [];
    for (let c = 0; c < 10; c++) {
      src.value = cycle(c * 1000);
      counts.push(subCount(globalMult));
    }

    // The live set is 100 rows, so 100 edges. Before the fix this read
    // [100, 200, 300, ... 1000]: unbounded growth, one leaked Link per row per
    // cycle, each retaining the row's closure and its DOM node.
    expect(counts).toEqual([100, 100, 100, 100, 100, 100, 100, 100, 100, 100]);
  });

  it('CONTROL: the same shape with only effect() was already correct', () => {
    // This control is what makes the assertion above meaningful. Effects ARE
    // disposed today; if this control ever fails, the harness itself is broken
    // and the computed assertion proves nothing.
    const globalMult = signal(2);
    const src = signal<{ id: number }[]>([]);
    const parent = document.createElement('tbody');

    list(
      parent,
      () => src.value,
      (r) => r.id,
      (r) => {
        const td = document.createElement('td');
        effect(() => {
          td.textContent = String(r.id * globalMult.value);
        });
        return td;
      },
    );

    const counts: number[] = [];
    for (let c = 0; c < 10; c++) {
      src.value = Array.from({ length: 100 }, (_, i) => ({ id: c * 1000 + i }));
      counts.push(subCount(globalMult));
    }
    expect(counts).toEqual([100, 100, 100, 100, 100, 100, 100, 100, 100, 100]);
  });

  it('RETENTION: unmounted rows carrying a computed become garbage', async () => {
    // The subscriber count is a proxy; this is the thing itself. A leaked Link
    // retains fn -> the captured row object -> the row's DOM nodes. WeakRef +
    // --expose-gc (vitest.config.ts) asks the GC directly.
    const globalMult = signal(2);
    const src = signal<{ id: number; tag: string }[]>([]);
    const parent = document.createElement('tbody');

    const refs: WeakRef<object>[] = [];

    list(
      parent,
      () => src.value,
      (r) => r.id,
      (r) => {
        const td = document.createElement('td');
        const derived = computed(() => `${r.tag}:${globalMult.value}`);
        effect(() => {
          td.textContent = derived.value;
        });
        return td;
      },
    );

    // The cycle-1 rows are built and released inside their own frame, and the
    // WeakRefs are taken with an INDEXED loop. Both details are load-bearing:
    // a `for (const r of ...)` leaves the last element in a stack slot that V8
    // keeps alive for the enclosing frame's lifetime, which shows up here as
    // exactly one survivor (index 49) and looks indistinguishable from a real
    // leak. Verified: with the iterator form this read [49] even after the fix.
    const seed = () => {
      const cycle1 = Array.from({ length: 50 }, (_, i) => ({ id: i, tag: 'c1' }));
      src.value = cycle1;
      for (let i = 0; i < cycle1.length; i++) refs.push(new WeakRef(cycle1[i]));
      // Replace every row. Cycle-1's rows are now unmounted and unreachable from
      // the app; only a leaked subscriber edge could still be holding them.
      src.value = Array.from({ length: 50 }, (_, j) => ({ id: 1000 + j, tag: 'c2' }));
      cycle1.length = 0;
    };
    seed();

    await new Promise((r) => setTimeout(r, 0));
    (globalThis as { gc?: () => void }).gc?.();
    await new Promise((r) => setTimeout(r, 0));
    (globalThis as { gc?: () => void }).gc?.();

    const alive = refs.filter((w) => w.deref() !== undefined).length;
    // Before the fix: 50/50 still reachable. The effect-only control collected 0.
    expect(alive).toBe(0);
  });

  it('RETENTION CONTROL: the leak is detectable by this harness', async () => {
    // Guards the test above against passing vacuously. A computed deliberately
    // held OUTSIDE any row scope keeps its captured rows alive; if this ever
    // reports 0, the harness has stopped being able to see retention at all and
    // the assertion above proves nothing.
    const globalMult = signal(2);
    const refs: WeakRef<object>[] = [];
    const keep: unknown[] = [];

    const seed = () => {
      const rows = Array.from({ length: 50 }, (_, i) => ({ id: i, tag: 'c1' }));
      for (let i = 0; i < rows.length; i++) {
        refs.push(new WeakRef(rows[i]));
        const row = rows[i];
        const c = computed(() => `${row.tag}:${globalMult.value}`);
        c.value; // link it to globalMult
        keep.push(c);
      }
      rows.length = 0;
    };
    seed();

    await new Promise((r) => setTimeout(r, 0));
    (globalThis as { gc?: () => void }).gc?.();
    await new Promise((r) => setTimeout(r, 0));
    (globalThis as { gc?: () => void }).gc?.();

    expect(refs.filter((w) => w.deref() !== undefined).length).toBe(50);
  });
});

/* ===========================================================================
 * THE EFFECT-BODY LEAK — same root cause, different scope.
 * =========================================================================== */
describe('computed: DISPOSAL inside an effect body', () => {
  it('a computed created in an effect body does not leak an edge per re-run', () => {
    const a = signal(1);
    const trigger = signal(0);

    effect(() => {
      trigger.value; // re-run driver
      const c = computed(() => a.value * 2);
      c.value; // read it, so it links to `a`
    });

    const before = subCount(a);
    for (let i = 1; i <= 10; i++) trigger.value = i;
    const after = subCount(a);

    // runEffect() disposes the previous run's owned children before re-running.
    // Once Computed participates in ownership, the previous run's computed is
    // torn down and `a` keeps exactly one live edge.
    // Before the fix: before=1, after=11 — one leaked Link per re-run.
    expect(before).toBe(1);
    expect(after).toBe(1);
  });

  it('DEGRADATION: propagate() over the shared signal stays bounded', () => {
    // The leak is not just memory: propagate() walks the subscriber list on
    // every write, so an unbounded list makes every write progressively slower.
    const a = signal(0);
    const trigger = signal(0);

    effect(() => {
      trigger.value;
      const c = computed(() => a.value + 1);
      c.value;
    });

    for (let i = 1; i <= 3000; i++) trigger.value = i;

    // Before the fix this was ~3001 edges and writes to `a` degraded ~10x.
    expect(subCount(a)).toBe(1);
  });
});

/* ===========================================================================
 * NESTED COMPUTEDS — the hazard that ownership must not introduce.
 *
 * A Computed's fn runs LAZILY, at the first .value read, which can happen from
 * an arbitrary context. If a computed created inside another computed's body
 * were adopted by whatever ambient effect happened to trigger that first read,
 * then that effect re-running would dispose it, prune its edges, and leave the
 * OUTER computed permanently stale — trading a leak for a wrong answer.
 *
 * These tests passed BEFORE the disposal fix (the nested computed leaked, but
 * leaking kept it correct). They must still pass after it. That is what forces
 * Computed to be an owner in its own right rather than merely an ownee.
 * =========================================================================== */
describe('computed: nested computeds stay correct under disposal', () => {
  it('an outer computed survives the re-run of the effect that first read it', () => {
    const a = signal(1);
    const trigger = signal(0);

    const outer = computed(() => {
      // Created during outer's lazy first run — ambient owner is the effect below.
      const inner = computed(() => a.value * 10);
      return inner.value;
    });

    let seen = 0;
    effect(() => {
      trigger.value;
      seen = outer.value;
    });

    expect(seen).toBe(10);

    trigger.value = 1; // the effect re-runs and disposes what it owns
    a.value = 5; // must still reach `outer` through `inner`

    expect(outer.value).toBe(50);
    expect(seen).toBe(50);
  });

  it('a nested computed is re-owned per recompute and does not accumulate edges', () => {
    const a = signal(1);
    const drive = signal(0);

    const outer = computed(() => {
      drive.value;
      const inner = computed(() => a.value * 2);
      return inner.value;
    });

    outer.value;
    // Force 20 recomputes of `outer`, each building a fresh `inner`.
    for (let i = 1; i <= 20; i++) {
      drive.value = i;
      outer.value;
    }

    // Each recompute tears down the previous run's inner, so `a` keeps exactly
    // one live edge rather than 21.
    expect(subCount(a)).toBe(1);
    a.value = 7;
    expect(outer.value).toBe(14);
  });
});

/* ===========================================================================
 * A top-level computed must NOT be swept up by ownership.
 * =========================================================================== */
describe('computed: ownership does not over-reach', () => {
  it('a computed created outside any scope is owned by nobody and stays live', () => {
    const a = signal(1);
    const c = computed(() => a.value * 2);
    expect(c.value).toBe(2);
    a.value = 5;
    expect(c.value).toBe(10);
  });

  it('an owning effect re-running does not break a computed created OUTSIDE it', () => {
    const a = signal(1);
    const trigger = signal(0);
    const outer = computed(() => a.value * 2); // created at top level

    let seen = 0;
    effect(() => {
      trigger.value;
      seen = outer.value; // merely READING must not adopt it
    });

    trigger.value = 1;
    trigger.value = 2;
    a.value = 4;
    expect(seen).toBe(8);
    expect(outer.value).toBe(8);
  });
});
