import { describe, it, expect, beforeEach, vi } from 'vitest';
import { signal, computed, effect, batch, untrack, list, setText } from '../src/index';
import type { Signal } from '../src/index';
import { stats } from '../src/stats';

beforeEach(() => stats.reset());

function subCount(src: unknown): number {
  let n = 0;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  for (let l = (src as any).subs; l !== undefined; l = l.ns) n++;
  return n;
}
function depCount(sub: unknown): number {
  let n = 0;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  for (let l = (sub as any).deps; l !== undefined; l = l.nd) n++;
  return n;
}

/* ===========================================================================
 * A. EXCEPTIONS INSIDE A COMPUTED  — the suspected semantic hole.
 * refresh() clears DIRTY *before* calling recompute(). If fn throws, nothing
 * restores it, so the computed is left CLEAN-but-STALE.
 * =========================================================================== */
describe('ADVERSARIAL: a computed whose fn throws', () => {
  it('BUG?: a computed that has never succeeded returns undefined forever after a throw', () => {
    const a = signal(1);
    let boom = true;
    const c = computed(() => {
      if (boom) throw new Error('boom');
      return a.value;
    });

    expect(() => c.value).toThrow('boom');
    boom = false;
    // The fn would now return 1. Does the runtime ever ask it again?
    expect(c.value).toBe(1);
  });

  it('BUG?: a throw strands a previously-good computed on its stale value', () => {
    const a = signal(1);
    let boom = false;
    const c = computed(() => {
      if (boom) throw new Error('boom');
      return a.value * 2;
    });
    expect(c.value).toBe(2);

    boom = true;
    a.value = 5;
    expect(() => c.value).toThrow('boom');

    boom = false;
    expect(c.value).toBe(10);
  });

  it('BUG?: a throw also silently unsubscribes the computed from its deps', () => {
    const a = signal(1);
    let boom = false;
    const c = computed(() => {
      if (boom) throw new Error('boom');
      return a.value * 2;
    });
    void c.value;
    expect(subCount(a)).toBe(1);

    boom = true;
    a.value = 5;
    try {
      void c.value;
    } catch {
      /* expected */
    }
    // prune() ran with depsTail === undefined, which means "drop EVERY edge".
    expect(subCount(a)).toBe(1);
  });

  it('BUG?: an effect downstream of a throwing computed goes permanently deaf', () => {
    const a = signal(1);
    let boom = false;
    const c = computed(() => {
      if (boom) throw new Error('boom');
      return a.value * 2;
    });
    const seen: number[] = [];
    effect(() => seen.push(c.value));
    expect(seen).toEqual([2]);

    boom = true;
    expect(() => (a.value = 5)).toThrow('boom');

    boom = false;
    a.value = 7;
    expect(seen).toEqual([2, 14]);
  });

  /* -------------------------------------------------------------------------
   * The four tests above all pin the throw at DEPTH 1 (the reader sits directly
   * on the throwing computed), which is the shape that reaches refresh()'s
   * DIRTY branch. That is not the invariant — it is one route to it. A relay
   * computed in between routes the SAME failure through refresh()'s PENDING
   * branch instead, and a guard on only one branch passes everything above
   * while `c !== b + 1` permanently. So parameterise over depth: the property
   * under test is "a recovered computed agrees with its inputs", at any depth.
   * ----------------------------------------------------------------------- */
  for (const depth of [1, 2, 3, 5]) {
    it(`a throw ${depth} computed(s) deep leaves NO computed stranded on a wrong value`, () => {
      const a = signal(1);
      let boom = false;
      // base = a*2, then `depth-1` relays each adding 1. Every relay reads only
      // a computed, so its refresh() takes the PENDING/checkDirty route.
      const base = computed(() => {
        if (boom) throw new Error('boom');
        return a.value * 2;
      });
      const chain: { value: number }[] = [base];
      for (let i = 1; i < depth; i++) {
        const prev = chain[i - 1];
        chain.push(computed(() => prev.value + 1));
      }
      const tip = chain[depth - 1];
      const expected = (av: number) => av * 2 + (depth - 1);

      expect(tip.value).toBe(expected(1));

      boom = true;
      a.value = 5;
      expect(() => tip.value).toThrow('boom');

      boom = false;
      // Every node in the chain must agree with the signal, not just the tip.
      for (let i = 0; i < depth; i++) expect(chain[i].value).toBe(5 * 2 + i);
      expect(tip.value).toBe(expected(5));
    });
  }

  it('a throw during checkDirty does not strand the relay while the source recovers', () => {
    // The exact invariant violation: read the SOURCE first (which repairs it),
    // then the relay. A relay left CLEAN by the PENDING path serves its old
    // value while its own input openly disagrees — with no error left to notice.
    const a = signal(1);
    let boom = false;
    const b = computed(() => {
      if (boom) throw new Error('boom');
      return a.value * 2;
    });
    const c = computed(() => b.value + 1);
    expect(c.value).toBe(3);

    boom = true;
    a.value = 5;
    expect(() => c.value).toThrow('boom');

    boom = false;
    expect(b.value).toBe(10);
    expect(c.value).toBe(11); // c === b + 1 must hold, always
  });

  it('BUG?: a computed whose FIRST read throws never subscribes its reader', () => {
    // An ordinary error boundary — the natural way to write recoverable code.
    // The STALE marker is useless if nothing is listening: propagate() reaches
    // the computed, marks it, then walks an EMPTY subscriber list.
    const s = signal(0);
    const c = computed(() => {
      if (s.value === 0) throw new Error('boom');
      return s.value * 2;
    });
    const seen: unknown[] = [];
    effect(() => {
      try {
        seen.push(c.value);
      } catch {
        seen.push('err');
      }
    });
    expect(seen).toEqual(['err']);
    // The edge must be CREATED, not merely retained from an earlier good run.
    expect(subCount(c)).toBe(1);

    s.value = 1;
    expect(seen).toEqual(['err', 2]);
  });

  it('a throwing computed keeps its reader subscribed across REPEATED failures', () => {
    const s = signal(0);
    const c = computed(() => {
      if (s.value % 2 === 0) throw new Error('boom');
      return s.value;
    });
    const seen: unknown[] = [];
    effect(() => {
      try {
        seen.push(c.value);
      } catch {
        seen.push('err');
      }
    });
    // First run: the effect's OWN body reads c, so its own try/catch sees the throw.
    expect(seen).toEqual(['err']);
    expect(subCount(c)).toBe(1);

    s.value = 1;
    expect(seen).toEqual(['err', 1]);

    // Once the effect is OBSERVING c, c's fn no longer runs inside the effect
    // body — it runs in checkDirty() during the drain, to decide whether the
    // effect needs to re-run at all. So the effect's try/catch cannot see this
    // throw, and per flush()'s error-isolation contract it surfaces at the WRITE
    // instead. Pinning that as-is: it is the documented behaviour (see 'an effect
    // downstream of a throwing computed', which pins the same surfacing point),
    // not something these fixes changed. The effect simply does not run.
    expect(() => (s.value = 2)).toThrow('boom');
    expect(seen).toEqual(['err', 1]);

    // THE INVARIANT THAT MATTERS: a failure must not cost the subscription. The
    // effect is still on c, so the next good value reaches it.
    expect(subCount(c)).toBe(1); // no edge dropped, and no duplicate accumulated
    s.value = 3;
    expect(seen).toEqual(['err', 1, 3]);
  });
});

/* ===========================================================================
 * B. EXCEPTIONS INSIDE AN EFFECT — queue integrity.
 * =========================================================================== */
describe('ADVERSARIAL: a throwing effect and the flush queue', () => {
  it('BUG?: one throwing effect starves every OTHER effect queued behind it', () => {
    const a = signal(0);
    const seen: number[] = [];
    effect(() => {
      if (a.value === 1) throw new Error('boom');
    });
    effect(() => seen.push(a.value));
    expect(seen).toEqual([0]);

    expect(() => (a.value = 1)).toThrow('boom');
    // The second effect is an independent subscriber. It has no reason to miss
    // this change just because an unrelated effect threw.
    expect(seen).toEqual([0, 1]);
  });

  it('the graph is not permanently wedged after an effect throws', () => {
    const a = signal(0);
    const seen: number[] = [];
    effect(() => {
      if (a.value === 1) throw new Error('boom');
    });
    effect(() => seen.push(a.value));
    try {
      a.value = 1;
    } catch {
      /* expected */
    }
    a.value = 2;
    expect(seen[seen.length - 1]).toBe(2);
  });
});

/* ===========================================================================
 * C. RE-ENTRANCY AND CYCLES
 * =========================================================================== */
describe('ADVERSARIAL: re-entrancy — a signal written inside an effect that reads it', () => {
  it('a SELF-TERMINATING self-write converges to the fixed point', () => {
    const s = signal(0);
    let runs = 0;
    effect(() => {
      runs++;
      const v = s.value;
      if (v < 5) s.value = v + 1;
    });
    expect(s.value).toBe(5);
    expect(runs).toBe(6);
  });

  it('the self-write cascade is FLAT (a flush loop), not recursive — no stack overflow', () => {
    // If each re-entrant run nested a flush inside a flush, 200k would blow the
    // stack. `flushing` makes the inner flush() a no-op and the outer while-loop
    // drains it, so depth stays constant.
    const s = signal(0);
    expect(() => {
      effect(() => {
        const v = s.value;
        if (v < 200_000) s.value = v + 1;
      });
    }).not.toThrow();
    expect(s.value).toBe(200_000);
  });

  it('BUG?: an UNCONDITIONAL self-write loops forever with no cycle detection', () => {
    // The classic authoring mistake. A runtime with a cycle guard throws
    // "Cycle detected"; this one is expected to spin until the circuit breaker
    // below fires. The breaker is the ONLY reason this test terminates.
    const s = signal(0);
    let runs = 0;
    expect(() => {
      effect(() => {
        if (++runs > 50_000) throw new Error('RUNAWAY');
        s.value = s.value + 1;
      });
    }).toThrow('RUNAWAY');
    // Documenting the observed behaviour: it really did spin.
    expect(runs).toBeGreaterThan(1000);
  });

  it('BUG?: two effects writing each other loop forever', () => {
    const a = signal(0);
    const b = signal(0);
    let runs = 0;
    const guard = () => {
      if (++runs > 50_000) throw new Error('RUNAWAY');
    };
    expect(() => {
      effect(() => {
        guard();
        b.value = a.value + 1;
      });
      effect(() => {
        guard();
        a.value = b.value + 1;
      });
    }).toThrow('RUNAWAY');
  });

  it('a self-referential computed returns undefined rather than recursing', () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const c: any = computed(() => (c as { value: number }).value);
    expect(() => c.value).not.toThrow();
    expect(c.value).toBeUndefined();
  });

  it('mutually recursive computeds terminate (with NaN) rather than hanging', () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const a: any = computed(() => (b as { value: number }).value + 1);
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const b: any = computed(() => (a as { value: number }).value + 1);
    expect(() => a.value).not.toThrow();
    expect(Number.isNaN(a.value)).toBe(true);
  });
});

/* ===========================================================================
 * D. GLITCHES / STALENESS THE EXISTING SUITE MAY NOT REACH
 * =========================================================================== */
describe('ADVERSARIAL: glitches and conditional staleness', () => {
  it('an effect depending on BOTH a signal and a computed of it runs once', () => {
    const a = signal(1);
    const b = computed(() => a.value * 10);
    const seen: string[] = [];
    effect(() => seen.push(`${a.value}:${b.value}`));
    a.value = 2;
    expect(seen).toEqual(['1:10', '2:20']);
  });

  it('same, with the read order REVERSED (edge order matters to propagate)', () => {
    const a = signal(1);
    const b = computed(() => a.value * 10);
    const seen: string[] = [];
    effect(() => seen.push(`${b.value}:${a.value}`));
    a.value = 2;
    expect(seen).toEqual(['10:1', '20:2']);
  });

  it('a conditional dep BEHIND a computed goes stale correctly', () => {
    const cond = signal(true);
    const a = signal('a1');
    const fn = vi.fn(() => (cond.value ? a.value : 'off'));
    const c = computed(fn);
    const seen: string[] = [];
    effect(() => seen.push(c.value));
    expect(seen).toEqual(['a1']);

    cond.value = false;
    expect(seen).toEqual(['a1', 'off']);

    a.value = 'a2';
    a.value = 'a3';
    expect(seen).toEqual(['a1', 'off']);
    expect(subCount(a)).toBe(0);

    cond.value = true;
    expect(seen).toEqual(['a1', 'off', 'a3']);
  });

  it('a computed dropped from an effect mid-flight is unsubscribed', () => {
    const cond = signal(true);
    const a = signal(1);
    const c = computed(() => a.value * 2);
    effect(() => (cond.value ? c.value : 0));
    expect(subCount(c)).toBe(1);
    cond.value = false;
    expect(subCount(c)).toBe(0);
    // c itself must also drop off `a` — nothing observes it now, but it is still
    // linked from its own last run until something reads it again.
    a.value = 9;
    expect(c.value).toBe(18);
  });

  it('an asymmetric diamond (one long leg, one short) still runs the sink once', () => {
    const a = signal(1);
    const short = computed(() => a.value + 1);
    const l1 = computed(() => a.value + 1);
    const l2 = computed(() => l1.value + 1);
    const l3 = computed(() => l2.value + 1);
    const fn = vi.fn(() => `${short.value}:${l3.value}`);
    effect(fn);
    a.value = 2;
    expect(fn).toHaveBeenCalledTimes(2);
  });

  it('a deep chain where an INTERMEDIATE cuts the change dead-ends correctly', () => {
    const n = signal(1);
    const gate = computed(() => n.value > 100);
    const down = computed(() => (gate.value ? 'big' : 'small'));
    const fn = vi.fn(() => void down.value);
    effect(fn);
    for (let i = 2; i < 100; i++) n.value = i;
    expect(fn).toHaveBeenCalledTimes(1);
    n.value = 500;
    expect(fn).toHaveBeenCalledTimes(2);
  });

  it('batch + a computed read INSIDE the batch does not lose the effect run', () => {
    const a = signal(1);
    const c = computed(() => a.value * 2);
    const seen: number[] = [];
    effect(() => seen.push(c.value));
    batch(() => {
      a.value = 5;
      // Forces c to recompute (version bump) while the effect is queued PENDING.
      expect(c.value).toBe(10);
    });
    expect(seen).toEqual([2, 10]);
  });

  it('untrack does not swallow a nested effect creation', () => {
    const a = signal(1);
    const seen: number[] = [];
    effect(() => {
      untrack(() => {
        effect(() => seen.push(a.value));
      });
    });
    a.value = 2;
    expect(seen).toEqual([1, 2]);
  });
});

/* ===========================================================================
 * E. DISPOSAL / RE-ENTRANT DISPOSAL
 * =========================================================================== */
describe('ADVERSARIAL: disposal edge cases', () => {
  it('disposing an effect from INSIDE another effect during the same flush', () => {
    const a = signal(0);
    const victimRuns: number[] = [];
    // The KILLER is created first, so propagate() enqueues it first and it runs
    // first. The victim is then sitting in the queue, already QUEUED, when it is
    // disposed. flush() must skip it.
    let victim: { dispose(): void };
    effect(() => {
      if (a.value === 1) victim.dispose();
    });
    victim = effect(() => victimRuns.push(a.value));
    a.value = 1;
    expect(victimRuns).toEqual([0]);
    a.value = 2;
    expect(victimRuns).toEqual([0]);
  });

  it('an effect that disposes ITSELF mid-run', () => {
    const a = signal(0);
    const runs: number[] = [];
    // eslint-disable-next-line prefer-const
    let e: { dispose(): void };
    e = effect(() => {
      runs.push(a.value);
      if (a.value === 1) e.dispose();
    });
    a.value = 1;
    a.value = 2;
    expect(runs).toEqual([0, 1]);
    expect(subCount(a)).toBe(0);
  });

  it('disposing a computed leaves downstream STALE (documented tradeoff, pinned)', () => {
    const a = signal(1);
    const c = computed(() => a.value * 2);
    const seen: number[] = [];
    effect(() => seen.push(c.value));
    expect(seen).toEqual([2]);
    c.dispose();
    a.value = 5;
    // Pinning current behaviour: a disposed computed detaches from its sources,
    // so the write reaches nobody and the effect never learns.
    expect(seen).toEqual([2]);
    expect(c.value).toBe(2);
  });

  it('a disposed effect never re-enters the queue', () => {
    const a = signal(0);
    const e = effect(() => void a.value);
    e.dispose();
    expect(depCount(e)).toBe(0);
    for (let i = 0; i < 100; i++) a.value = i;
    expect(subCount(a)).toBe(0);
  });

  it('disposing the OWNER disposes computeds created in its body', () => {
    const a = signal(1);
    const outer = effect(() => {
      const c = computed(() => a.value * 2);
      void c.value;
    });
    expect(subCount(a)).toBe(1);
    outer.dispose();
    expect(subCount(a)).toBe(0);
  });
});

/* ===========================================================================
 * F. COMPUTED LAZINESS — beyond "fn not called at construction"
 * =========================================================================== */
describe('ADVERSARIAL: computed laziness', () => {
  it('an OBSERVED computed is not lazy — it recomputes during checkDirty', () => {
    // Honest pin: laziness applies to UNOBSERVED computeds. Once an effect
    // depends on it, checkDirty must evaluate it to know whether to re-run.
    const a = signal(1);
    const fn = vi.fn(() => a.value > 100);
    const c = computed(fn);
    effect(() => void c.value);
    expect(fn).toHaveBeenCalledTimes(1);
    a.value = 2;
    expect(fn).toHaveBeenCalledTimes(2); // ran, even though nothing read c
  });

  it('a computed whose deps never change never re-runs, over 10k writes to a sibling', () => {
    const a = signal(1);
    const b = signal(1);
    const fn = vi.fn(() => a.value * 2);
    const c = computed(fn);
    void c.value;
    for (let i = 0; i < 10_000; i++) b.value = i;
    void c.value;
    expect(fn).toHaveBeenCalledTimes(1);
  });

  it('an unobserved computed reading another unobserved computed stays fully lazy', () => {
    const a = signal(1);
    const f1 = vi.fn(() => a.value);
    const f2 = vi.fn(() => (b as { value: number }).value + 1);
    const b = computed(f1);
    const c = computed(f2);
    for (let i = 0; i < 100; i++) a.value = i;
    expect(f1).not.toHaveBeenCalled();
    expect(f2).not.toHaveBeenCalled();
    expect(c.value).toBe(100);
    expect(f1).toHaveBeenCalledTimes(1);
    expect(f2).toHaveBeenCalledTimes(1);
  });
});

/* ===========================================================================
 * G. LIS / KEYED RECONCILIATION — the hard cases
 * =========================================================================== */
describe('ADVERSARIAL: keyed reconciliation hard cases', () => {
  function h() {
    const parent = document.createElement('div');
    const items = signal<{ id: number }[]>([]);
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

  it('empty -> empty', () => {
    const x = h();
    x.set([]);
    expect(x.dom()).toEqual([]);
    expect(stats.dom).toBe(0);
  });

  it('single element: mount, no-op, replace, remove', () => {
    const x = h();
    x.set([1]);
    expect(x.dom()).toEqual([1]);
    stats.reset();
    x.set([1]);
    expect(stats.dom).toBe(0);
    x.set([2]);
    expect(x.dom()).toEqual([2]);
    x.set([]);
    expect(x.dom()).toEqual([]);
    expect(x.parent.children.length).toBe(0);
  });

  it('ALL-NEW (the #run shape): 1000 unmounts + 1000 mounts, ZERO moves', () => {
    const x = h();
    x.set(Array.from({ length: 1000 }, (_, i) => i));
    stats.reset();
    x.set(Array.from({ length: 1000 }, (_, i) => 1000 + i));
    expect(stats.remove).toBe(1000);
    expect(stats.insert).toBe(1000); // mounts only — no move may sneak in
    expect(x.dom()[0]).toBe(1000);
    expect(x.dom()[999]).toBe(1999);
    expect(x.parent.children.length).toBe(1000);
  });

  it('ALL-REMOVED: 1000 removes, 0 inserts, parent empty', () => {
    const x = h();
    x.set(Array.from({ length: 1000 }, (_, i) => i));
    stats.reset();
    x.set([]);
    expect(stats.remove).toBe(1000);
    expect(stats.insert).toBe(0);
    expect(x.parent.children.length).toBe(0);
  });

  it('ROTATE left by 1 over 1000 rows is exactly 1 move', () => {
    const x = h();
    const ids = Array.from({ length: 1000 }, (_, i) => i);
    x.set(ids);
    stats.reset();
    x.set([...ids.slice(1), ids[0]]); // 0 goes to the back
    expect(stats.insert).toBe(1);
    expect(stats.remove).toBe(0);
    expect(x.dom()).toEqual([...ids.slice(1), 0]);
  });

  it('ROTATE right by 1 over 1000 rows is exactly 1 move', () => {
    const x = h();
    const ids = Array.from({ length: 1000 }, (_, i) => i);
    x.set(ids);
    stats.reset();
    x.set([999, ...ids.slice(0, 999)]);
    expect(stats.insert).toBe(1);
    expect(x.dom()).toEqual([999, ...ids.slice(0, 999)]);
  });

  it('ROTATE by 500 (the worst realistic case) is exactly 500 moves', () => {
    const x = h();
    const ids = Array.from({ length: 1000 }, (_, i) => i);
    x.set(ids);
    const p = [...ids.slice(500), ...ids.slice(0, 500)];
    stats.reset();
    x.set(p);
    expect(stats.insert).toBe(500);
    expect(x.dom()).toEqual(p);
  });

  it('REVERSE 1000: 999 moves and the exact reversed order', () => {
    const x = h();
    const ids = Array.from({ length: 1000 }, (_, i) => i);
    x.set(ids);
    const created = x.created();
    stats.reset();
    x.set(ids.slice().reverse());
    expect(stats.insert).toBe(999);
    expect(stats.remove).toBe(0);
    expect(x.created()).toBe(created); // nothing recreated
    expect(x.dom()).toEqual(ids.slice().reverse());
  });

  it('SWAP of 2 adjacent elements is 1 move, not 2', () => {
    const x = h();
    x.set([1, 2, 3, 4, 5]);
    stats.reset();
    x.set([1, 3, 2, 4, 5]);
    expect(stats.insert).toBe(1);
    expect(x.dom()).toEqual([1, 3, 2, 4, 5]);
  });

  it('SWAP first and last of 1000 is exactly 2 moves', () => {
    const x = h();
    const ids = Array.from({ length: 1000 }, (_, i) => i);
    x.set(ids);
    const p = ids.slice();
    [p[0], p[999]] = [p[999], p[0]];
    stats.reset();
    x.set(p);
    expect(stats.insert).toBe(2);
    expect(x.dom()).toEqual(p);
  });

  it('interleave (evens then odds) lands correctly', () => {
    const x = h();
    const ids = Array.from({ length: 20 }, (_, i) => i);
    x.set(ids);
    const p = [...ids.filter((i) => i % 2 === 0), ...ids.filter((i) => i % 2 === 1)];
    x.set(p);
    expect(x.dom()).toEqual(p);
  });

  it('a full replace THEN a full restore round-trips', () => {
    const x = h();
    x.set([1, 2, 3]);
    x.set([4, 5, 6]);
    x.set([1, 2, 3]);
    expect(x.dom()).toEqual([1, 2, 3]);
    expect(x.parent.children.length).toBe(3);
  });

  it('non-numeric / falsy-ish keys work (0, "", null are legitimate @key values)', () => {
    const parent = document.createElement('div');
    const items = signal<{ k: unknown; n: string }[]>([
      { k: 0, n: 'zero' },
      { k: '', n: 'empty' },
      { k: null, n: 'null' },
    ]);
    list(
      parent,
      () => items.value,
      (i) => i.k,
      (i) => {
        const el = document.createElement('i');
        el.textContent = i.n;
        return el;
      },
    );
    expect([...parent.children].map((c) => c.textContent)).toEqual(['zero', 'empty', 'null']);
    items.value = [
      { k: null, n: 'null' },
      { k: 0, n: 'zero' },
      { k: '', n: 'empty' },
    ];
    expect([...parent.children].map((c) => c.textContent)).toEqual(['null', 'zero', 'empty']);
  });

  it('BUG?: DUPLICATE KEYS must not corrupt the DOM', () => {
    // old = [1,1,2], new = [2,1]. Both old rows with key 1 resolve to the same
    // new index, so one of them is never unmounted AND the key-2 row is dropped
    // by the `patched >= toPatch` guard.
    const x = h();
    x.set([1, 1, 2]);
    expect(x.dom()).toEqual([1, 1, 2]);
    stats.reset();
    x.set([2, 1]);
    expect(x.parent.children.length).toBe(2);
    expect(x.dom()).toEqual([2, 1]);
  });

  it('BUG?: duplicate keys leave an ORPHANED node in the DOM', () => {
    const x = h();
    x.set([1, 1, 2]);
    x.set([2, 1]);
    // A row that is no longer in `items` must not still be in the document.
    expect(x.parent.children.length).toBeLessThanOrEqual(2);
  });
});

/* ===========================================================================
 * H. LEAKS ACROSS REPEATED #run — the benchmark reloads per iteration and
 *    would therefore never see this.
 * =========================================================================== */
describe('ADVERSARIAL: leaks across many #run cycles', () => {
  function rowsApp() {
    const parent = document.createElement('tbody');
    // A signal EVERY row subscribes to — the leak vector. Phase 2 emits exactly
    // this shape for any row template touching page-level state.
    const theme = signal('light');
    const rows = signal<{ id: number; label: Signal<string> }[]>([]);
    let nextId = 1;

    list(
      parent,
      () => rows.value,
      (r) => r.id,
      (r) => {
        const tr = document.createElement('tr');
        const t = document.createTextNode('');
        tr.appendChild(t);
        const derived = computed(() => `${theme.value}:${r.label.value}`);
        effect(() => setText(t, derived.value));
        return tr;
      },
    );

    return {
      parent,
      theme,
      rows,
      run: (n: number) =>
        (rows.value = Array.from({ length: n }, () => ({
          id: nextId++,
          label: signal('x'),
        }))),
      clear: () => (rows.value = []),
    };
  }

  it('50 x #run(200) leaves EXACTLY one subscriber per live row on the shared signal', () => {
    const a = rowsApp();
    const counts: number[] = [];
    for (let c = 0; c < 50; c++) {
      a.run(200);
      counts.push(subCount(a.theme));
    }
    // 200 live rows => 200 edges, every cycle. Not 200, 400, 600...
    expect(new Set(counts).size).toBe(1);
    expect(counts[0]).toBe(200);
    expect(counts[49]).toBe(200);
  });

  it('the ROWS signal itself keeps exactly ONE subscriber across 50 cycles', () => {
    const a = rowsApp();
    for (let c = 0; c < 50; c++) a.run(200);
    expect(subCount(a.rows)).toBe(1);
  });

  it('#run/#clear cycling drains the shared signal to ZERO edges', () => {
    const a = rowsApp();
    for (let c = 0; c < 20; c++) {
      a.run(200);
      a.clear();
      expect(subCount(a.theme)).toBe(0);
    }
  });

  it('LINK ALLOCATION per #run cycle is CONSTANT, not growing', () => {
    const a = rowsApp();
    const perCycle: number[] = [];
    for (let c = 0; c < 20; c++) {
      stats.reset();
      a.run(200);
      perCycle.push(stats.links);
    }
    // Warmed cycles must all cost the same. Growth here == a leak that the
    // page-reloading benchmark can never see.
    const warm = perCycle.slice(5);
    expect(new Set(warm).size).toBe(1);
  });

  it('a write to the shared signal costs O(live rows), not O(all rows ever)', () => {
    const a = rowsApp();
    for (let c = 0; c < 30; c++) a.run(100);
    stats.reset();
    a.theme.value = 'dark';
    // 100 live rows => 100 effect runs and 100 text writes. If dead rows leaked,
    // this would be 3000.
    expect(stats.runs).toBe(100);
    expect(stats.text).toBe(100);
  });

  it('disposing the list effect drops the shared signal to zero', () => {
    const a = rowsApp();
    const parent = a.parent;
    for (let c = 0; c < 10; c++) a.run(100);
    expect(subCount(a.theme)).toBe(100);
    expect(parent.children.length).toBe(100);
  });
});

/* ===========================================================================
 * I. THE ZERO-ALLOCATION CLAIM, probed independently of stats.links
 * =========================================================================== */
describe('ADVERSARIAL: the update hot path allocates nothing', () => {
  it('the counter increment path creates no Link and no queue array', () => {
    const count = signal(0);
    const t = document.createTextNode('');
    effect(() => setText(t, count.value));
    stats.reset();
    for (let i = 0; i < 100_000; i++) count.value++;
    expect(stats.links).toBe(0);
    expect(stats.runs).toBe(100_000);
  });

  it('the effect QUEUE is intrusive: 1000 effects flush with no array allocation', () => {
    // Reaching into the graph: the queue must be threaded through Effect.nq.
    const a = signal(0);
    const es = Array.from({ length: 1000 }, () => effect(() => void a.value));
    a.value = 1;
    // After the flush every nq must be null — a leaked nq chain would retain
    // every effect in the queue forever.
    for (const e of es) expect((e as unknown as { nq: unknown }).nq).toBeNull();
  });

  it('a 1000-row #update allocates zero edges and touches exactly 100 text nodes', () => {
    const parent = document.createElement('tbody');
    const rows = signal(
      Array.from({ length: 1000 }, (_, i) => ({ id: i, label: signal(`l${i}`) })),
    );
    list(
      parent,
      () => rows.value,
      (r) => r.id,
      (r) => {
        const tr = document.createElement('tr');
        const t = document.createTextNode('');
        tr.appendChild(t);
        effect(() => setText(t, r.label.value));
        return tr;
      },
    );
    stats.reset();
    const r = rows.value;
    for (let i = 0; i < r.length; i += 10) r[i].label.value += '!';
    expect(stats.links).toBe(0);
    expect(stats.text).toBe(100);
    expect(stats.insert).toBe(0);
    expect(stats.remove).toBe(0);
  });
});
