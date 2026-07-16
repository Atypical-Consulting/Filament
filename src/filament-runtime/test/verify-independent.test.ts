/**
 * INDEPENDENT VERIFICATION SUITE — written by the skeptical verifier.
 * Deliberately uses DIFFERENT inputs/shapes than test/adversarial.test.ts.
 */
import { describe, it, expect, beforeEach } from 'vitest';
import { signal, computed, effect, batch, untrack, list } from '../src/index';
import { stats } from '../src/stats';

beforeEach(() => stats.reset());

function subCount(src: unknown): number {
  let n = 0;
  for (let l = (src as any).subs; l !== undefined; l = l.ns) n++;
  return n;
}

/* ---------- BUG 1: throwing computed, novel shapes ---------- */
describe('V-BUG1', () => {
  it('throw on a MIDDLE dep (multi-dep computed) does not truncate the dep set', () => {
    const a = signal(1);
    const b = signal(10);
    const c = signal(100);
    let boom = false;
    // reads a, then maybe throws, then reads b and c. On throw, b & c are unreached.
    const sum = computed(() => {
      const av = a.value;
      if (boom) throw new Error('mid');
      return av + b.value + c.value;
    });
    expect(sum.value).toBe(111);
    boom = true;
    a.value = 2;
    expect(() => sum.value).toThrow('mid');
    boom = false;
    // If prune() had dropped b/c edges, writing c would not invalidate sum.
    c.value = 500;
    expect(sum.value).toBe(2 + 10 + 500);
    b.value = 20;
    expect(sum.value).toBe(2 + 20 + 500);
  });

  it('throw recovers when the SIGNAL is untouched (recovery via flag alone, no new write)', () => {
    const a = signal(3);
    let boom = false;
    const c = computed(() => {
      if (boom) throw new Error('x');
      return a.value * 10;
    });
    expect(c.value).toBe(30);
    boom = true;
    a.value = 4;
    expect(() => c.value).toThrow('x');
    boom = false;
    // No write between the throw and this read. STALE alone must force re-run.
    expect(c.value).toBe(40);
  });

  it('a computed throwing every OTHER read stays consistent over many cycles', () => {
    const a = signal(0);
    let boom = false;
    const c = computed(() => {
      if (boom) throw new Error('flap');
      return a.value + 1;
    });
    for (let i = 1; i <= 50; i++) {
      boom = true;
      a.value = i;
      expect(() => c.value).toThrow('flap');
      boom = false;
      expect(c.value).toBe(i + 1);
    }
  });

  it('effect downstream of throwing computed at depth 4 recovers (different depth than suite)', () => {
    const a = signal(1);
    let boom = false;
    const base = computed(() => {
      if (boom) throw new Error('d');
      return a.value;
    });
    const r1 = computed(() => base.value + 1);
    const r2 = computed(() => r1.value + 1);
    const r3 = computed(() => r2.value + 1);
    const seen: number[] = [];
    effect(() => seen.push(r3.value));
    expect(seen).toEqual([4]);
    boom = true;
    expect(() => {
      a.value = 10;
    }).toThrow('d');
    boom = false;
    a.value = 20;
    expect(seen[seen.length - 1]).toBe(23);
  });
});

/* ---------- BUG 2: throwing effect, novel shapes ---------- */
describe('V-BUG2', () => {
  it('effect throwing in the MIDDLE of 5 siblings: all other 4 still run', () => {
    const s = signal(0);
    const ran: number[] = [];
    for (let i = 0; i < 5; i++) {
      effect(() => {
        s.value;
        if (i === 2 && s.value === 1) throw new Error('boom2');
        ran.push(i);
      });
    }
    ran.length = 0;
    expect(() => {
      s.value = 1;
    }).toThrow('boom2');
    expect(ran.sort()).toEqual([0, 1, 3, 4]);
  });

  it('effect that throws is NOT left deaf: it re-runs on the next write', () => {
    const s = signal(0);
    const seen: number[] = [];
    effect(() => {
      const v = s.value;
      if (v === 1) throw new Error('once');
      seen.push(v);
    });
    expect(() => {
      s.value = 1;
    }).toThrow('once');
    s.value = 2;
    expect(seen).toEqual([0, 2]);
  });

  it('effect throwing BEFORE reading its 2nd dep keeps that dep subscribed', () => {
    const a = signal(0);
    const b = signal(0);
    let boom = false;
    const seen: string[] = [];
    effect(() => {
      const av = a.value;
      if (boom) throw new Error('pre-b');
      seen.push(av + ':' + b.value);
    });
    expect(seen).toEqual(['0:0']);
    boom = true;
    expect(() => {
      a.value = 1;
    }).toThrow('pre-b');
    boom = false;
    // If b's edge was pruned by the partial run, this write reaches nobody.
    b.value = 9;
    expect(seen[seen.length - 1]).toBe('1:9');
  });

  it('two throwing effects: first error surfaces, both siblings still run', () => {
    const s = signal(0);
    const ran: string[] = [];
    effect(() => {
      s.value;
      ran.push('a');
      if (s.value === 1) throw new Error('E1');
    });
    effect(() => {
      s.value;
      ran.push('b');
      if (s.value === 1) throw new Error('E2');
    });
    effect(() => {
      s.value;
      ran.push('c');
    });
    ran.length = 0;
    expect(() => {
      s.value = 1;
    }).toThrow('E1');
    expect(ran).toEqual(['a', 'b', 'c']);
  });
});

/* ---------- BUG 3: duplicate keys, novel patterns ---------- */
function h() {
  const parent = document.createElement('div');
  const items = signal<{ id: number }[]>([]);
  list(
    parent,
    () => items.value,
    (i) => i.id,
    (i) => {
      const el = document.createElement('i');
      el.textContent = String(i.id);
      return el;
    },
  );
  return {
    parent,
    set: (ids: number[]) => (items.value = ids.map((id) => ({ id }))),
    dom: () => [...parent.children].map((c) => Number(c.textContent)),
  };
}

describe('V-BUG3', () => {
  const cases: [number[], number[]][] = [
    [[1, 2, 2, 3], [3, 2, 1]],
    [[5, 5, 5], [5]],
    [[5, 5, 5], [5, 5]],
    [[1, 1, 1, 1], [2, 1]],
    [[7, 8, 8, 9, 9, 9], [9, 8, 7]],
    [[1, 2, 3, 2, 1], [1, 2, 3]],
    [[4, 4], []],
    [[], [6, 6]],
    [[6, 6], [6, 6]],
    [[1, 1, 2, 2, 3, 3], [3, 3, 2, 2, 1, 1]],
    [[9, 1, 9, 2, 9], [9, 2, 1]],
  ];
  for (const [from, to] of cases) {
    it(`dup keys ${JSON.stringify(from)} -> ${JSON.stringify(to)} yields exactly the requested rows`, () => {
      const x = h();
      x.set(from);
      x.set(to);
      // The contract the report claims: never corrupt the document.
      // Child count must equal requested length; no orphans.
      expect(x.parent.children.length).toBe(to.length);
      expect(x.dom()).toEqual(to);
    });
  }

  it('FUZZ: random duplicate-heavy lists never corrupt the DOM', () => {
    let seed = 12345;
    const rnd = (n: number) => {
      seed = (seed * 1103515245 + 12345) & 0x7fffffff;
      return seed % n;
    };
    for (let iter = 0; iter < 400; iter++) {
      const x = h();
      for (let step = 0; step < 5; step++) {
        const len = rnd(8);
        const ids: number[] = [];
        for (let i = 0; i < len; i++) ids.push(rnd(3)); // tiny key space => many dups
        x.set(ids);
        expect(x.parent.children.length).toBe(ids.length);
        expect(x.dom()).toEqual(ids);
      }
    }
  });
});

/* ---------- BUG 4: cycle cap ---------- */
describe('V-BUG4', () => {
  it('a legitimate ~100-deep cascade is NOT rejected', () => {
    const s = signal(0);
    let runs = 0;
    expect(() => {
      effect(() => {
        runs++;
        const v = s.value;
        if (v < 100) s.value = v + 1;
      });
    }).not.toThrow();
    expect(s.value).toBe(100);
    expect(runs).toBe(101);
  });

  it('a legitimate 300k cascade (above the pinned 200k) is NOT rejected', () => {
    const s = signal(0);
    expect(() => {
      effect(() => {
        const v = s.value;
        if (v < 300_000) s.value = v + 1;
      });
    }).not.toThrow();
    expect(s.value).toBe(300_000);
  });

  it('an unconditional SELF-write throws cycle detected (no test-side breaker)', () => {
    const s = signal(0);
    expect(() => {
      effect(() => {
        s.value = s.value + 1;
      });
    }).toThrow(/cycle detected/i);
  });

  it('MUTUAL two-effect cycle throws cycle detected (no test-side breaker)', () => {
    const a = signal(0);
    const b = signal(0);
    expect(() => {
      effect(() => {
        b.value = a.value + 1;
      });
      effect(() => {
        a.value = b.value + 1;
      });
    }).toThrow(/cycle detected/i);
  });

  it('a THREE-effect ring throws cycle detected', () => {
    const a = signal(0);
    const b = signal(0);
    const c = signal(0);
    expect(() => {
      effect(() => {
        b.value = a.value + 1;
      });
      effect(() => {
        c.value = b.value + 1;
      });
      effect(() => {
        a.value = c.value + 1;
      });
    }).toThrow(/cycle detected/i);
  });

  it('BYSTANDER effect queued behind a cycle is not left permanently deaf', () => {
    const cyc = signal(0);
    const other = signal(0);
    const seen: number[] = [];
    effect(() => {
      seen.push(other.value);
    });
    expect(() => {
      effect(() => {
        cyc.value = cyc.value + 1;
      });
    }).toThrow(/cycle detected/i);
    seen.length = 0;
    other.value = 42;
    expect(seen).toEqual([42]);
  });

  it('graph still works after a cycle error', () => {
    const cyc = signal(0);
    expect(() => {
      effect(() => {
        cyc.value = cyc.value + 1;
      });
    }).toThrow(/cycle detected/i);
    const a = signal(1);
    const c = computed(() => a.value * 2);
    const seen: number[] = [];
    effect(() => seen.push(c.value));
    a.value = 5;
    expect(seen).toEqual([2, 10]);
  });
});

/* ---------- REGRESSIONS ---------- */
describe('V-REGRESSION', () => {
  it('glitch-freedom: diamond sink runs exactly once', () => {
    const a = signal(1);
    const b = computed(() => a.value + 1);
    const c = computed(() => a.value * 2);
    let runs = 0;
    effect(() => {
      b.value;
      c.value;
      runs++;
    });
    runs = 0;
    a.value = 5;
    expect(runs).toBe(1);
  });

  it('laziness: unobserved computed does not run until read', () => {
    const a = signal(1);
    let n = 0;
    const c = computed(() => {
      n++;
      return a.value;
    });
    expect(n).toBe(0);
    a.value = 2;
    expect(n).toBe(0);
    expect(c.value).toBe(2);
    expect(n).toBe(1);
  });

  it('conditional deps: dropped branch is unsubscribed', () => {
    const flag = signal(true);
    const a = signal(1);
    const b = signal(2);
    let runs = 0;
    effect(() => {
      runs++;
      flag.value ? a.value : b.value;
    });
    runs = 0;
    b.value = 99; // not a dep yet
    expect(runs).toBe(0);
    flag.value = false;
    expect(runs).toBe(1);
    a.value = 50; // no longer a dep
    expect(runs).toBe(1);
    b.value = 100;
    expect(runs).toBe(2);
  });

  it('disposal: disposed effect stops and drops edges', () => {
    const s = signal(0);
    let runs = 0;
    const d = effect(() => {
      s.value;
      runs++;
    });
    expect(subCount(s)).toBe(1);
    d.dispose();
    s.value = 1;
    expect(runs).toBe(1);
    expect(subCount(s)).toBe(0);
  });

  it('zero-alloc hot path: counter increment allocates no links', () => {
    const s = signal(0);
    effect(() => s.value);
    stats.reset();
    for (let i = 0; i < 1000; i++) s.value = i;
    expect(stats.links).toBe(0);
  });

  it('batch still coalesces', () => {
    const a = signal(1);
    const b = signal(2);
    let runs = 0;
    effect(() => {
      a.value;
      b.value;
      runs++;
    });
    runs = 0;
    batch(() => {
      a.value = 10;
      b.value = 20;
    });
    expect(runs).toBe(1);
  });

  it('untrack still does not subscribe', () => {
    const a = signal(1);
    let runs = 0;
    effect(() => {
      runs++;
      untrack(() => a.value);
    });
    runs = 0;
    a.value = 2;
    expect(runs).toBe(0);
  });
});
