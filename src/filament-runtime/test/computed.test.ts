import { describe, it, expect, beforeEach, vi } from 'vitest';
import { signal, computed, effect, batch } from '../src/index';
import { stats } from '../src/stats';

beforeEach(() => stats.reset());

describe('computed: LAZINESS', () => {
  it('does NOT run fn at construction', () => {
    const fn = vi.fn(() => 1);
    computed(fn);
    expect(fn).not.toHaveBeenCalled();
    expect(stats.computes).toBe(0);
  });

  it('does NOT run fn until the first .value read', () => {
    const a = signal(1);
    const fn = vi.fn(() => a.value * 2);
    const c = computed(fn);

    // Writing a dependency must not wake it either — nothing is observing it.
    a.value = 2;
    a.value = 3;
    expect(fn).not.toHaveBeenCalled();

    expect(c.value).toBe(6);
    expect(fn).toHaveBeenCalledTimes(1);
  });

  it('caches: repeated reads without a change do not re-run fn', () => {
    const a = signal(2);
    const fn = vi.fn(() => a.value * 2);
    const c = computed(fn);
    expect(c.value).toBe(4);
    expect(c.value).toBe(4);
    expect(c.value).toBe(4);
    expect(fn).toHaveBeenCalledTimes(1);
  });

  it('an UNOBSERVED computed stays lazy: N writes cost 1 recompute, at read time', () => {
    const a = signal(0);
    const fn = vi.fn(() => a.value * 2);
    const c = computed(fn);
    for (let i = 1; i <= 100; i++) a.value = i;
    expect(fn).not.toHaveBeenCalled();
    expect(c.value).toBe(200);
    expect(fn).toHaveBeenCalledTimes(1);
  });

  it('nested computeds stay lazy through the chain', () => {
    const a = signal(1);
    const f1 = vi.fn(() => a.value + 1);
    const b = computed(f1);
    const f2 = vi.fn(() => b.value + 1);
    const c = computed(f2);
    expect(f1).not.toHaveBeenCalled();
    expect(f2).not.toHaveBeenCalled();
    expect(c.value).toBe(3);
    expect(f1).toHaveBeenCalledTimes(1);
    expect(f2).toHaveBeenCalledTimes(1);
  });
});

describe('computed: INVALIDATION', () => {
  it('recomputes after a dependency changes', () => {
    const a = signal(1);
    const c = computed(() => a.value * 2);
    expect(c.value).toBe(2);
    a.value = 5;
    expect(c.value).toBe(10);
  });

  it('recomputes exactly once per change, on read', () => {
    const a = signal(1);
    const fn = vi.fn(() => a.value * 2);
    const c = computed(fn);
    void c.value;
    expect(fn).toHaveBeenCalledTimes(1);
    a.value = 2;
    void c.value;
    void c.value;
    expect(fn).toHaveBeenCalledTimes(2);
  });

  it('tracks conditionally, like an effect', () => {
    const a = signal('a1');
    const b = signal(true);
    const fn = vi.fn(() => (b.value ? a.value : 'off'));
    const c = computed(fn);
    expect(c.value).toBe('a1');

    b.value = false;
    expect(c.value).toBe('off');
    expect(fn).toHaveBeenCalledTimes(2);

    // `a` was pruned: changing it must not invalidate c.
    a.value = 'a2';
    expect(c.value).toBe('off');
    expect(fn).toHaveBeenCalledTimes(2);
  });

  it('an observed computed drives its effect', () => {
    const a = signal(1);
    const c = computed(() => a.value * 2);
    const seen: number[] = [];
    effect(() => seen.push(c.value));
    a.value = 2;
    a.value = 3;
    expect(seen).toEqual([2, 4, 6]);
  });
});

describe('computed: value-equality cutoff (re-runs only on a REAL change)', () => {
  it('an effect does NOT re-run when the computed recomputes to the same value', () => {
    // THE point of tracking versions rather than just a dirty bit. `big` is
    // invalidated by 1 -> 2, recomputes, and is still false. The effect below it
    // has no reason to run, and does not.
    const n = signal(1);
    const big = computed(() => n.value > 5);
    const fn = vi.fn(() => void big.value);
    effect(fn);
    expect(fn).toHaveBeenCalledTimes(1);

    n.value = 2;
    n.value = 3;
    n.value = 4;
    expect(fn).toHaveBeenCalledTimes(1);

    n.value = 6; // NOW it flips
    expect(fn).toHaveBeenCalledTimes(2);

    n.value = 7; // still true
    expect(fn).toHaveBeenCalledTimes(2);
  });

  it('the computed itself still recomputes (it must, to know)', () => {
    const n = signal(1);
    const fn = vi.fn(() => n.value > 5);
    const big = computed(fn);
    effect(() => void big.value);
    n.value = 2;
    expect(fn).toHaveBeenCalledTimes(2);
  });

  it('the cutoff propagates through a chain of computeds', () => {
    const n = signal(1);
    const a = computed(() => n.value > 5);
    const b = computed(() => (a.value ? 'yes' : 'no'));
    const fn = vi.fn(() => void b.value);
    effect(fn);
    for (let i = 2; i <= 5; i++) n.value = i;
    expect(fn).toHaveBeenCalledTimes(1);
    n.value = 6;
    expect(fn).toHaveBeenCalledTimes(2);
  });

  it('a half-cut diamond still runs the sink once', () => {
    // b changes, c does not. d must run exactly once, not twice, not zero times.
    const a = signal(1);
    const b = computed(() => a.value * 2);
    const c = computed(() => a.value > 100);
    const fn = vi.fn(() => `${b.value}:${c.value}`);
    effect(fn);
    a.value = 2;
    expect(fn).toHaveBeenCalledTimes(2);
  });
});

describe('computed: interaction with batch', () => {
  it('reads inside a batch see the pre-flush values', () => {
    const a = signal(1);
    const c = computed(() => a.value * 2);
    void c.value;
    batch(() => {
      a.value = 5;
      // Computeds are pull-based, so this read is up to date immediately even
      // though effects have not flushed.
      expect(c.value).toBe(10);
    });
    expect(c.value).toBe(10);
  });
});
