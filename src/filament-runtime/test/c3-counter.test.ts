import { describe, it, expect, beforeEach } from 'vitest';
import { PerformanceObserver } from 'node:perf_hooks';
import { signal, computed, effect, setText, setAttr, listen } from '../src/index';
import { stats } from '../src/stats';

/**
 * C3: exactly 1 DOM write per counter increment, 0 render-tree allocation,
 * VERIFIED BY INSTRUMENTATION.
 *
 * `stats` is that instrumentation. It exists only in the dev build; size.mjs
 * proves it is absent from the bundle C1 measures.
 */

beforeEach(() => stats.reset());

/** The Counter app's DOM contract: #counter-value's text is the count. */
function mountCounter() {
  const el = document.createElement('span');
  el.id = 'counter-value';
  const t = document.createTextNode('');
  el.appendChild(t);
  document.body.appendChild(el);
  const count = signal(0);
  const e = effect(() => setText(t, count.value));
  return { count, el, t, e };
}

describe('C3: exactly 1 DOM write per increment', () => {
  it('one increment => exactly 1 DOM write, and it is the text write', () => {
    const { count, el } = mountCounter();
    stats.reset();

    count.value++;

    expect(stats.text).toBe(1);
    expect(stats.dom).toBe(1); // nothing else touched the DOM at all
    expect(stats.attr).toBe(0);
    expect(stats.insert).toBe(0);
    expect(stats.remove).toBe(0);
    expect(el.textContent).toBe('1');
  });

  it('one increment => exactly 1 effect run', () => {
    const { count } = mountCounter();
    stats.reset();
    count.value++;
    expect(stats.runs).toBe(1);
  });

  it('N increments => exactly N DOM writes, for N = 1000', () => {
    const { count, el } = mountCounter();
    stats.reset();
    for (let i = 0; i < 1000; i++) count.value++;
    expect(stats.text).toBe(1000);
    expect(stats.dom).toBe(1000);
    expect(el.textContent).toBe('1000');
  });

  it('a no-op write => 0 DOM writes', () => {
    const { count } = mountCounter();
    stats.reset();
    count.value = 0; // already 0
    expect(stats.dom).toBe(0);
    expect(stats.runs).toBe(0);
  });

  it('a counter behind a computed still costs exactly 1 DOM write', () => {
    const count = signal(0);
    const doubled = computed(() => count.value * 2);
    const t = document.createTextNode('');
    effect(() => setText(t, doubled.value));
    stats.reset();

    count.value++;

    expect(stats.text).toBe(1);
    expect(stats.computes).toBe(1);
    expect(t.data).toBe('2');
  });

  it('a DIAMOND-fed counter still costs exactly 1 DOM write', () => {
    const count = signal(1);
    const b = computed(() => count.value * 2);
    const c = computed(() => count.value * 10);
    const t = document.createTextNode('');
    effect(() => setText(t, b.value + c.value));
    stats.reset();

    count.value++;

    // The whole point: 2 paths reach the text node, 1 write happens.
    expect(stats.text).toBe(1);
    expect(t.data).toBe('24');
  });
});

describe('C3: 0 render-tree allocation on the update path', () => {
  it('steady-state increments allocate ZERO dependency edges', () => {
    // There is no render tree to allocate — that is the thesis. What COULD still
    // allocate is the reactive graph: a naive implementation rebuilds its
    // dependency list on every run. `stats.links` counts every edge object the
    // runtime creates. After warm-up it must never move again.
    const { count } = mountCounter();
    stats.reset();

    count.value++;
    expect(stats.links).toBe(0);

    for (let i = 0; i < 10_000; i++) count.value++;
    expect(stats.links).toBe(0);
    expect(stats.text).toBe(10_001);
  });

  it('the FIRST run allocates edges, and then never again (proves the probe works)', () => {
    // Negative control: if stats.links were simply never incremented, the test
    // above would pass on a broken runtime.
    const count = signal(0);
    const t = document.createTextNode('');
    stats.reset();
    effect(() => setText(t, count.value)); // first run: builds the edge
    expect(stats.links).toBe(1);

    stats.reset();
    count.value++;
    expect(stats.links).toBe(0);
  });

  it('a computed chain allocates no edges in steady state', () => {
    const count = signal(0);
    const b = computed(() => count.value * 2);
    const c = computed(() => b.value + 1);
    const t = document.createTextNode('');
    effect(() => setText(t, c.value));
    stats.reset();
    for (let i = 0; i < 1000; i++) count.value++;
    expect(stats.links).toBe(0);
  });

  it('conditional deps allocate only when the SHAPE changes, then reuse', () => {
    const a = signal(1);
    const b = signal(true);
    const t = document.createTextNode('');
    effect(() => setText(t, b.value ? a.value : 0));
    stats.reset();
    // Same shape every time => zero edges.
    for (let i = 0; i < 100; i++) a.value = i;
    expect(stats.links).toBe(0);
  });

  it('GC: 2 MILLION increments of a pure effect trigger ZERO garbage collections', async () => {
    // External evidence, independent of our own counters — stats.links could in
    // principle be wrong or incomplete, and this cannot.
    //
    // WHY GC EVENTS AND NOT heapUsed: a heapUsed delta around a forced gc()
    // measures RETENTION, not allocation. Transient garbage is reclaimed, so a
    // runtime allocating one edge per update shows the same flat heap as one
    // allocating none — verified: that mutant passed a heapUsed-based test.
    // V8's sampling heap profiler is no better here (it reported 27 KB vs 30 KB
    // for runtimes that differ by ~40 MB of churn). GC EVENTS work, because
    // garbage must eventually provoke a scavenge: this loop is synchronous, so
    // any collection during it was caused by this loop.
    //
    // Measured discriminator: real runtime 0 events, a reuse-disabled mutant 20.
    //
    // A pure (non-DOM) effect isolates the reactive core on purpose: setText must
    // allocate a string because the DOM stores strings, and that is app data, not
    // framework overhead.
    let gcs = 0;
    const obs = new PerformanceObserver((l) => {
      gcs += l.getEntries().length;
    });
    obs.observe({ entryTypes: ['gc'] });

    const count = signal(0);
    let sink = 0;
    effect(() => (sink = count.value));

    for (let i = 0; i < 50_000; i++) count.value++; // warm JIT + settle edge reuse
    const gc = (globalThis as { gc?: () => void }).gc;
    if (!gc) throw new Error('gc unavailable: vitest.config.ts must pass --expose-gc');
    gc();
    await new Promise((r) => setTimeout(r, 20));

    gcs = 0;
    const N = 2_000_000;
    for (let i = 0; i < N; i++) count.value++;
    await new Promise((r) => setTimeout(r, 20));
    obs.disconnect();

    expect(sink).toBe(2_050_000);
    expect(gcs).toBe(0);
  });
});

describe('DOM ops are dumb and counted', () => {
  it('setText writes character data directly', () => {
    const t = document.createTextNode('a');
    setText(t, 'b');
    expect(t.data).toBe('b');
    expect(stats.text).toBe(1);
  });

  it('setText coerces non-strings the way the DOM does', () => {
    const t = document.createTextNode('');
    setText(t, 42);
    expect(t.data).toBe('42');
  });

  it('setAttr sets, and null removes', () => {
    const el = document.createElement('div');
    setAttr(el, 'class', 'x');
    expect(el.getAttribute('class')).toBe('x');
    setAttr(el, 'class', null);
    expect(el.hasAttribute('class')).toBe(false);
    setAttr(el, 'class', undefined);
    expect(el.hasAttribute('class')).toBe(false);
    expect(stats.attr).toBe(3);
  });

  it('listen attaches a real listener', () => {
    const el = document.createElement('button');
    let clicks = 0;
    listen(el, 'click', () => clicks++);
    el.dispatchEvent(new Event('click'));
    expect(clicks).toBe(1);
    expect(stats.listen).toBe(1);
  });

  it('an effect + listen is the whole counter: click => 1 DOM write', () => {
    const { count, el } = mountCounter();
    const btn = document.createElement('button');
    btn.id = 'increment';
    listen(btn, 'click', () => count.value++);
    stats.reset();

    btn.dispatchEvent(new Event('click'));

    expect(stats.text).toBe(1);
    expect(stats.dom).toBe(1);
    expect(el.textContent).toBe('1');
    // And it is already correct synchronously, inside dispatchEvent — no
    // microtask, no frame. The harness's MutationObserver fires on a DOM that is
    // already final.
  });
});
