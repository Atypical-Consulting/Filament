import { describe, it, expect, beforeEach, vi } from 'vitest';
import { signal, computed, effect, batch, untrack } from '../src/index';
import { stats } from '../src/stats';

beforeEach(() => stats.reset());

/**
 * Walks a source's subscriber list. Reaching into the graph on purpose: "the
 * effect stopped re-running" is satisfied by a runtime that merely sets a flag
 * and keeps the edge forever. Only counting the edges distinguishes unsubscribed
 * from ignored, and that difference IS the leak.
 */
function subCount(src: unknown): number {
  let n = 0;
  let l = (src as { subs?: { ns?: unknown } }).subs;
  while (l) {
    n++;
    l = l.ns as { ns?: unknown } | undefined;
  }
  return n;
}

describe('signal: read/write', () => {
  it('reads back what was written', () => {
    const s = signal(1);
    expect(s.value).toBe(1);
    s.value = 2;
    expect(s.value).toBe(2);
  });

  it('holds reference values without copying', () => {
    const o = { a: 1 };
    const s = signal(o);
    expect(s.value).toBe(o);
  });

  it('C# MAPPING: .value supports read-modify-write in one expression', () => {
    // This is the whole reason for `.value` over `count()`/`count(v)`:
    // C# `count.Value++` transliterates, character for character.
    const count = signal(0);
    count.value++;
    count.value += 5;
    expect(count.value).toBe(6);
  });

  it('C# MAPPING: Computed has no setter, mirroring `{ get; }`', () => {
    const a = signal(1);
    const c = computed(() => a.value * 2);
    // ESM is strict mode, so assigning to an accessor without a setter throws
    // rather than silently no-op'ing. That mirrors the C# compile error.
    expect(() => {
      (c as unknown as { value: number }).value = 9;
    }).toThrow(TypeError);
  });
});

describe('effect: automatic dependency tracking', () => {
  it('runs once immediately', () => {
    const fn = vi.fn();
    effect(fn);
    expect(fn).toHaveBeenCalledTimes(1);
  });

  it('re-runs when a signal READ DURING EXECUTION changes', () => {
    const a = signal(1);
    const seen: number[] = [];
    effect(() => seen.push(a.value));
    a.value = 2;
    a.value = 3;
    expect(seen).toEqual([1, 2, 3]);
  });

  it('does NOT track a signal it never read', () => {
    const a = signal(1);
    const b = signal(1);
    const fn = vi.fn(() => void a.value);
    effect(fn);
    b.value = 99;
    expect(fn).toHaveBeenCalledTimes(1);
  });

  it('tracks a signal read behind a helper function (dynamic, not lexical)', () => {
    const a = signal(1);
    const read = () => a.value;
    const fn = vi.fn(() => void read());
    effect(fn);
    a.value = 2;
    expect(fn).toHaveBeenCalledTimes(2);
  });

  it('tracks multiple signals', () => {
    const a = signal(1);
    const b = signal(10);
    const fn = vi.fn(() => a.value + b.value);
    effect(fn);
    a.value = 2;
    b.value = 20;
    expect(fn).toHaveBeenCalledTimes(3);
  });

  it('handles the same signal read twice in one run without duplicate work', () => {
    const a = signal(1);
    const fn = vi.fn(() => a.value + a.value);
    effect(fn);
    stats.reset();
    a.value = 2;
    expect(fn).toHaveBeenCalledTimes(2);
    // Consecutive repeat reads hit the `t.dep === dep` cursor branch: no new edge.
    expect(stats.links).toBe(0);
  });

  it('effects run synchronously, inside the write', () => {
    // C3 depends on this: the DOM is correct by the time the click handler
    // returns. No microtask, no rAF, nothing for the harness to race.
    const a = signal(1);
    let seen = 0;
    effect(() => (seen = a.value));
    a.value = 42;
    expect(seen).toBe(42);
  });
});

describe('effect: re-runs only on a REAL change', () => {
  it('ignores a write of an equal value', () => {
    const a = signal(1);
    const fn = vi.fn(() => void a.value);
    effect(fn);
    a.value = 1;
    a.value = 1;
    expect(fn).toHaveBeenCalledTimes(1);
  });

  it('uses Object.is: NaN -> NaN is not a change', () => {
    // With `!==` this would re-run forever, because NaN !== NaN.
    const a = signal(NaN);
    const fn = vi.fn(() => void a.value);
    effect(fn);
    a.value = NaN;
    expect(fn).toHaveBeenCalledTimes(1);
  });

  it('uses Object.is: -0 -> +0 IS a change', () => {
    const a = signal(-0);
    const fn = vi.fn(() => void a.value);
    effect(fn);
    a.value = 0;
    expect(fn).toHaveBeenCalledTimes(2);
  });

  it('treats a new object with equal contents as a change (identity, not deep equality)', () => {
    const a = signal({ n: 1 });
    const fn = vi.fn(() => void a.value);
    effect(fn);
    a.value = { n: 1 };
    expect(fn).toHaveBeenCalledTimes(2);
  });
});

describe('effect: conditional dependencies', () => {
  it('does not re-run on a() when b() is false', () => {
    const a = signal('a1');
    const b = signal(true);
    const fn = vi.fn(() => (b.value ? a.value : 'off'));
    effect(fn);
    expect(fn).toHaveBeenCalledTimes(1);

    // While b is true, a is a dependency.
    a.value = 'a2';
    expect(fn).toHaveBeenCalledTimes(2);

    // Turn b off. The re-run stops reading a, so the a edge is pruned.
    b.value = false;
    expect(fn).toHaveBeenCalledTimes(3);

    // THE ASSERTION: a is no longer a dependency.
    a.value = 'a3';
    a.value = 'a4';
    expect(fn).toHaveBeenCalledTimes(3);

    // And it comes back when b does.
    b.value = true;
    expect(fn).toHaveBeenCalledTimes(4);
    a.value = 'a5';
    expect(fn).toHaveBeenCalledTimes(5);
  });

  it('prunes edges rather than leaking them (re-subscribing does not duplicate)', () => {
    const a = signal(1);
    const b = signal(true);
    let runs = 0;
    effect(() => {
      runs++;
      if (b.value) void a.value;
    });

    for (let i = 0; i < 50; i++) {
      b.value = false; // drops the a-edge
      b.value = true; // re-creates it
    }

    // THE ANTI-LEAK ASSERTION: after 50 subscribe/unsubscribe cycles `a` holds
    // exactly ONE subscriber, not 50. A runtime that pruned lazily (or not at
    // all) would pass every behavioural test above while growing this list
    // without bound — which is the shape of a real leak.
    expect(subCount(a)).toBe(1);
    expect(subCount(b)).toBe(1);
    expect(runs).toBe(101);

    b.value = false;
    expect(subCount(a)).toBe(0); // fully detached, not merely flagged
  });

  it('re-subscribing allocates one edge per SHAPE change — and only then', () => {
    // Honest accounting of the zero-allocation claim's boundary. A conditional
    // dependency that genuinely comes back MUST allocate an edge: the previous
    // one was unlinked, and keeping it around "just in case" is precisely the
    // leak the test above forbids. The claim is that a STABLE shape allocates
    // nothing, not that nothing ever allocates.
    const a = signal(1);
    const b = signal(true);
    effect(() => {
      if (b.value) void a.value;
    });

    stats.reset();
    b.value = false; // shape shrinks: prune, no allocation
    expect(stats.links).toBe(0);

    b.value = true; // shape grows back: exactly one new edge
    expect(stats.links).toBe(1);

    // Stable shape from here on: writes to `a` allocate nothing, which is the
    // property C3 actually rests on.
    stats.reset();
    for (let i = 0; i < 100; i++) a.value = i;
    expect(stats.links).toBe(0);
  });
});

describe('effect: nested effects', () => {
  it('disposes the previous run\'s children before re-running', () => {
    const outer = signal(0);
    const inner = signal(0);
    const innerFn = vi.fn(() => void inner.value);

    effect(() => {
      void outer.value;
      effect(innerFn);
    });
    expect(innerFn).toHaveBeenCalledTimes(1);

    inner.value = 1;
    expect(innerFn).toHaveBeenCalledTimes(2);

    // Re-running the outer must dispose the old inner and create a fresh one:
    // 1 call for the new inner's first run.
    outer.value = 1;
    expect(innerFn).toHaveBeenCalledTimes(3);

    // THE LEAK TEST: exactly ONE inner effect is alive, not two.
    inner.value = 2;
    expect(innerFn).toHaveBeenCalledTimes(4);
  });

  it('cascades disposal through three levels', () => {
    const s = signal(0);
    const deep = vi.fn(() => void s.value);
    const e = effect(() => {
      effect(() => {
        effect(deep);
      });
    });
    expect(deep).toHaveBeenCalledTimes(1);
    e.dispose();
    s.value = 1;
    expect(deep).toHaveBeenCalledTimes(1);
  });

  it('an inner effect does not become a dependency of the outer', () => {
    const a = signal(0);
    const outerFn = vi.fn(() => {
      effect(() => void a.value);
    });
    effect(outerFn);
    a.value = 1;
    // Only the inner re-runs; the outer never read `a` itself.
    expect(outerFn).toHaveBeenCalledTimes(1);
  });
});

describe('effect: cleanup / disposal', () => {
  it('unsubscribes on dispose', () => {
    const a = signal(1);
    const fn = vi.fn(() => void a.value);
    const e = effect(fn);
    a.value = 2;
    expect(fn).toHaveBeenCalledTimes(2);
    e.dispose();
    a.value = 3;
    a.value = 4;
    expect(fn).toHaveBeenCalledTimes(2);
  });

  it('dispose is idempotent', () => {
    const a = signal(1);
    const e = effect(() => void a.value);
    e.dispose();
    e.dispose();
    expect(() => (a.value = 2)).not.toThrow();
  });

  it('removes the effect from the source subscriber list (no leak)', () => {
    const a = signal(1);
    const effects = Array.from({ length: 100 }, () => effect(() => void a.value));
    // Reach into the graph: after disposing all, the source must hold NO edges.
    // A framework that only sets a flag would still hold 100 links here, and the
    // Rows app would accumulate them at 1000 per #run.
    for (const e of effects) e.dispose();
    expect((a as unknown as { subs: unknown }).subs).toBeUndefined();
    expect((a as unknown as { subsTail: unknown }).subsTail).toBeUndefined();
  });

  it('invokes the teardown hook', () => {
    const t = vi.fn();
    const e = effect(() => {});
    e.c = t;
    e.dispose();
    expect(t).toHaveBeenCalledTimes(1);
  });
});

describe('glitch freedom', () => {
  it('DIAMOND: a -> b, a -> c, b+c -> d runs d exactly ONCE', () => {
    const a = signal(1);
    const b = computed(() => a.value * 2);
    const c = computed(() => a.value * 10);
    const seen: number[] = [];
    const d = vi.fn(() => seen.push(b.value + c.value));

    effect(d);
    expect(d).toHaveBeenCalledTimes(1);
    expect(seen).toEqual([12]);

    a.value = 2;

    // THE ASSERTION. Naive push-propagation runs d twice (once via b, once via c)
    // and transiently shows 4 + 10 = 14 — a value that never existed.
    expect(d).toHaveBeenCalledTimes(2);
    expect(seen).toEqual([12, 24]);
  });

  it('DIAMOND is never observed half-updated', () => {
    const a = signal(1);
    const b = computed(() => a.value + 1);
    const c = computed(() => a.value + 2);
    const observed: Array<[number, number]> = [];
    effect(() => observed.push([b.value, c.value]));
    a.value = 10;
    // Every observation is internally consistent: c === b + 1, always.
    for (const [x, y] of observed) expect(y).toBe(x + 1);
    expect(observed).toEqual([
      [2, 3],
      [11, 12],
    ]);
  });

  it('wide diamond: 8 branches over one source still run the sink once', () => {
    const a = signal(0);
    const branches = Array.from({ length: 8 }, (_, i) => computed(() => a.value + i));
    const sink = vi.fn(() => branches.reduce((n, c) => n + c.value, 0));
    effect(sink);
    a.value = 1;
    expect(sink).toHaveBeenCalledTimes(2);
  });

  it('deep chain a -> b -> c -> d -> effect runs the effect once', () => {
    const a = signal(1);
    const b = computed(() => a.value + 1);
    const c = computed(() => b.value + 1);
    const d = computed(() => c.value + 1);
    const fn = vi.fn(() => void d.value);
    effect(fn);
    a.value = 2;
    expect(fn).toHaveBeenCalledTimes(2);
    expect(d.value).toBe(5);
  });

  it('an effect never sees a partially applied batch', () => {
    const x = signal(1);
    const y = signal(1);
    const seen: Array<[number, number]> = [];
    effect(() => seen.push([x.value, y.value]));
    batch(() => {
      x.value = 2;
      y.value = 2;
    });
    // Without batch these are two logical changes and [2,1] would be observed.
    expect(seen).toEqual([
      [1, 1],
      [2, 2],
    ]);
  });
});

describe('batch', () => {
  it('coalesces two writes into one effect run', () => {
    const a = signal(1);
    const b = signal(1);
    const fn = vi.fn(() => a.value + b.value);
    effect(fn);
    batch(() => {
      a.value = 2;
      b.value = 2;
    });
    expect(fn).toHaveBeenCalledTimes(2);
  });

  it('without batch, two writes are two logical changes (documented behaviour)', () => {
    const a = signal(1);
    const b = signal(1);
    const fn = vi.fn(() => a.value + b.value);
    effect(fn);
    a.value = 2;
    b.value = 2;
    expect(fn).toHaveBeenCalledTimes(3);
  });

  it('nests: only the outermost batch flushes', () => {
    const a = signal(1);
    const fn = vi.fn(() => void a.value);
    effect(fn);
    batch(() => {
      a.value = 2;
      batch(() => {
        a.value = 3;
      });
      expect(fn).toHaveBeenCalledTimes(1); // still not flushed
      a.value = 4;
    });
    expect(fn).toHaveBeenCalledTimes(2);
    expect(a.value).toBe(4);
  });

  it('flushes even if the body throws', () => {
    const a = signal(1);
    const fn = vi.fn(() => void a.value);
    effect(fn);
    expect(() =>
      batch(() => {
        a.value = 2;
        throw new Error('boom');
      }),
    ).toThrow('boom');
    expect(fn).toHaveBeenCalledTimes(2);
    // The graph is not wedged.
    a.value = 3;
    expect(fn).toHaveBeenCalledTimes(3);
  });

  it('a write reverted inside a batch still settles to no-op-equal value', () => {
    const a = signal(1);
    const fn = vi.fn(() => void a.value);
    effect(fn);
    batch(() => {
      a.value = 2;
      a.value = 1;
    });
    // Honest: the effect DOES re-run. Marking is by version, not by final value,
    // so a value that departs and returns within one batch still counts.
    expect(fn).toHaveBeenCalledTimes(2);
  });
});

describe('untrack', () => {
  it('reads without subscribing', () => {
    const a = signal(1);
    const b = signal(1);
    const fn = vi.fn(() => a.value + untrack(() => b.value));
    effect(fn);
    b.value = 2;
    expect(fn).toHaveBeenCalledTimes(1);
    a.value = 2;
    expect(fn).toHaveBeenCalledTimes(2);
  });

  it('restores tracking afterwards', () => {
    const a = signal(1);
    const b = signal(1);
    const fn = vi.fn(() => {
      untrack(() => b.value);
      return a.value;
    });
    effect(fn);
    a.value = 2;
    expect(fn).toHaveBeenCalledTimes(2);
  });
});

describe('robustness', () => {
  it('a throwing effect does not corrupt tracking for the next effect', () => {
    const a = signal(1);
    expect(() =>
      effect(() => {
        void a.value;
        throw new Error('boom');
      }),
    ).toThrow('boom');

    const b = signal(1);
    const fn = vi.fn(() => void b.value);
    effect(fn);
    b.value = 2;
    expect(fn).toHaveBeenCalledTimes(2);
  });

  it('an effect writing another signal cascades within the same flush', () => {
    const a = signal(1);
    const mirror = signal(0);
    effect(() => (mirror.value = a.value * 2));
    const fn = vi.fn(() => void mirror.value);
    effect(fn);
    a.value = 5;
    expect(mirror.value).toBe(10);
    expect(fn).toHaveBeenCalledTimes(2);
  });
});
