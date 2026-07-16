import { describe, it, expect, beforeEach } from 'vitest';
import { signal, effect, list, setText } from '../src/index';
import type { Signal } from '../src/index';
import { stats } from '../src/stats';

beforeEach(() => stats.reset());

// ---------------------------------------------------------------------------
// A minimal keyed list harness: items are plain {id} and each row is <i>{id}</i>.
// ---------------------------------------------------------------------------
interface Item {
  id: number;
}
const it_ = (id: number): Item => ({ id });

function setup(initial: number[]) {
  const parent = document.createElement('div');
  const items = signal<Item[]>(initial.map(it_));
  const e = list(
    parent,
    () => items.value,
    (i) => i.id,
    (i) => {
      const el = document.createElement('i');
      el.appendChild(document.createTextNode(String(i.id)));
      return el;
    },
  );
  return { parent, items, e, ids: () => [...parent.children].map((c) => Number(c.textContent)) };
}

describe('list: basics', () => {
  it('mounts the initial items in order', () => {
    const { ids } = setup([1, 2, 3]);
    expect(ids()).toEqual([1, 2, 3]);
  });

  it('mounts into an empty parent with 1 insert per row and no removes', () => {
    stats.reset();
    const { ids } = setup([1, 2, 3, 4, 5]);
    expect(stats.insert).toBe(5);
    expect(stats.remove).toBe(0);
    expect(ids()).toEqual([1, 2, 3, 4, 5]);
  });

  it('appends', () => {
    const { items, ids } = setup([1, 2]);
    stats.reset();
    items.value = [1, 2, 3].map(it_);
    expect(ids()).toEqual([1, 2, 3]);
    expect(stats.insert).toBe(1); // prefix sync covers 1 and 2
    expect(stats.remove).toBe(0);
  });

  it('prepends with 1 insert (suffix sync covers the rest)', () => {
    const { items, ids } = setup([1, 2]);
    stats.reset();
    items.value = [0, 1, 2].map(it_);
    expect(ids()).toEqual([0, 1, 2]);
    expect(stats.insert).toBe(1);
    expect(stats.remove).toBe(0);
  });

  it('removes from the middle with 1 remove and 0 inserts', () => {
    const { items, ids } = setup([1, 2, 3]);
    stats.reset();
    items.value = [1, 3].map(it_);
    expect(ids()).toEqual([1, 3]);
    expect(stats.remove).toBe(1);
    expect(stats.insert).toBe(0);
  });

  it('IDENTICAL keys => ZERO DOM operations (no churn)', () => {
    // A new array with the same keys must cost nothing. Assigning a fresh list
    // is exactly what a C# `@foreach` over a rebuilt List<T> produces.
    const { items, ids } = setup([1, 2, 3, 4, 5]);
    stats.reset();
    items.value = [1, 2, 3, 4, 5].map(it_);
    expect(stats.dom).toBe(0);
    expect(ids()).toEqual([1, 2, 3, 4, 5]);
  });

  it('reverse of 5 => 4 moves, not 5', () => {
    // Reversal's LIS has length 1 (one element is already "in place").
    const { items, ids } = setup([1, 2, 3, 4, 5]);
    stats.reset();
    items.value = [5, 4, 3, 2, 1].map(it_);
    expect(ids()).toEqual([5, 4, 3, 2, 1]);
    expect(stats.insert).toBe(4);
    expect(stats.remove).toBe(0);
  });

  it('preserves node identity across a move (does not recreate)', () => {
    const { parent, items } = setup([1, 2, 3]);
    const node1 = parent.children[0];
    items.value = [3, 2, 1].map(it_);
    // Keyed: the SAME element object moved. This is what @key buys.
    expect(parent.children[2]).toBe(node1);
  });

  it('mixed add/remove/move lands in the right order', () => {
    const { items, ids } = setup([1, 2, 3, 4, 5]);
    items.value = [4, 1, 6, 3].map(it_);
    expect(ids()).toEqual([4, 1, 6, 3]);
  });

  it('empty -> empty is a no-op', () => {
    const { items } = setup([]);
    stats.reset();
    items.value = [];
    expect(stats.dom).toBe(0);
  });

  it('respects the anchor: rows stay inside their slot', () => {
    const parent = document.createElement('div');
    const before = document.createElement('header');
    const after = document.createElement('footer');
    parent.appendChild(before);
    parent.appendChild(after);
    const items = signal<Item[]>([1, 2].map(it_));
    list(
      parent,
      () => items.value,
      (i) => i.id,
      (i) => {
        const el = document.createElement('i');
        el.textContent = String(i.id);
        return el;
      },
      after,
    );
    expect([...parent.children].map((c) => c.tagName.toLowerCase())).toEqual([
      'header',
      'i',
      'i',
      'footer',
    ]);
    items.value = [2, 1].map(it_);
    expect([...parent.children].map((c) => c.tagName.toLowerCase())).toEqual([
      'header',
      'i',
      'i',
      'footer',
    ]);
    expect([...parent.children].slice(1, 3).map((c) => c.textContent)).toEqual(['2', '1']);
  });
});

describe('list: row scope cleanup', () => {
  it('disposes a removed row\'s effects', () => {
    const parent = document.createElement('div');
    const labels = new Map<number, Signal<string>>();
    const runs = new Map<number, number>();
    const items = signal<Item[]>([1, 2, 3].map(it_));
    list(
      parent,
      () => items.value,
      (i) => i.id,
      (i) => {
        const el = document.createElement('i');
        const t = document.createTextNode('');
        el.appendChild(t);
        const s = signal('v0');
        labels.set(i.id, s);
        runs.set(i.id, 0);
        effect(() => {
          runs.set(i.id, runs.get(i.id)! + 1);
          setText(t, s.value);
        });
        return el;
      },
    );
    expect(runs.get(2)).toBe(1);

    // Alive: the row's effect responds.
    labels.get(2)!.value = 'v1';
    expect(runs.get(2)).toBe(2);

    // Remove row 2.
    items.value = [1, 3].map(it_);

    // THE LEAK TEST. Writing the dead row's signal must reach nobody.
    stats.reset();
    labels.get(2)!.value = 'v2';
    expect(runs.get(2)).toBe(2);
    expect(stats.dom).toBe(0);

    // Survivors are untouched.
    labels.get(1)!.value = 'x';
    expect(runs.get(1)).toBe(2);
  });

  it('clearing 1000 rows unsubscribes all 1000 (no accumulation across #run)', () => {
    const parent = document.createElement('div');
    const sigs: Array<Signal<number>> = [];
    let totalRuns = 0;
    const items = signal<Item[]>([]);
    list(
      parent,
      () => items.value,
      (i) => i.id,
      (i) => {
        const el = document.createElement('i');
        const t = document.createTextNode('');
        el.appendChild(t);
        const s = signal(i.id);
        sigs.push(s);
        effect(() => {
          totalRuns++;
          setText(t, s.value);
        });
        return el;
      },
    );

    items.value = Array.from({ length: 1000 }, (_, i) => it_(i));
    expect(totalRuns).toBe(1000);
    items.value = [];

    // Every one of the 1000 effects must be dead. If disposal only unset a flag,
    // these writes would still walk 1000 subscriber edges and re-run.
    totalRuns = 0;
    stats.reset();
    for (const s of sigs) s.value = -1;
    expect(totalRuns).toBe(0);
    expect(stats.dom).toBe(0);
    // And the sources hold no edges at all.
    for (const s of sigs) expect((s as unknown as { subs: unknown }).subs).toBeUndefined();
  });

  it('disposing the list effect releases every row scope', () => {
    const parent = document.createElement('div');
    const sigs: Array<Signal<number>> = [];
    let runs = 0;
    const items = signal<Item[]>([1, 2, 3].map(it_));
    const e = list(
      parent,
      () => items.value,
      (i) => i.id,
      (i) => {
        const el = document.createElement('i');
        const s = signal(i.id);
        sigs.push(s);
        effect(() => {
          runs++;
          void s.value;
        });
        return el;
      },
    );
    runs = 0;
    e.dispose();
    for (const s of sigs) s.value = 99;
    expect(runs).toBe(0);
  });

  it('the row template does NOT become a dependency of the list', () => {
    // If create() read a signal tracked, one label change would re-reconcile all
    // 1000 rows. scope() untracks the template body to make that impossible.
    const parent = document.createElement('div');
    const label = signal('a');
    let reconciles = 0;
    const items = signal<Item[]>([1].map(it_));
    list(
      parent,
      () => {
        reconciles++;
        return items.value;
      },
      (i) => i.id,
      () => {
        const el = document.createElement('i');
        el.textContent = label.value; // read WITHOUT an effect, on purpose
        return el;
      },
    );
    expect(reconciles).toBe(1);
    label.value = 'b';
    expect(reconciles).toBe(1);
  });
});

// ---------------------------------------------------------------------------
// THE ROWS SCENARIOS — the exact four the harness drives, at the exact sizes.
//
// The app model mirrors baseline/Rows.Blazor/RowsApp.razor: a keyed list of rows
// with a MUTABLE label (here: a Signal<string>, which is what the Phase 2/3
// compiler will emit for `public string Label { get; set; }`), a monotonic id
// that never resets, and the Park-Miller LCG label stream.
// ---------------------------------------------------------------------------
describe('ROWS: the four benchmark scenarios, at 1000 rows', () => {
  const ADJ = 'pretty large big small tall short long handsome plain quaint clean elegant easy angry crazy helpful mushy odd unsightly adorable important inexpensive cheap expensive fancy'.split(' ');
  const COL = 'red yellow blue green pink brown purple brown white black orange'.split(' ');
  const NOUN = 'table chair house bbq desk car pony cookie sandwich burger pizza mouse keyboard'.split(' ');

  interface Row {
    id: number;
    label: Signal<string>;
  }

  function app() {
    const parent = document.createElement('tbody');
    // Park-Miller LCG in double arithmetic, seeded once per "page load".
    let seed = 42;
    const next = () => (seed = (seed * 16807) % 2147483647);
    const nextLabel = () =>
      `${ADJ[next() % 25]} ${COL[next() % 11]} ${NOUN[next() % 13]}`;

    let nextId = 1;
    const rows = signal<Row[]>([]);
    const textNodes = new Map<number, Text>();

    list(
      parent,
      () => rows.value,
      (r) => r.id,
      (r) => {
        const tr = document.createElement('tr');
        const td1 = document.createElement('td');
        td1.appendChild(document.createTextNode(String(r.id)));
        const td2 = document.createElement('td');
        const t = document.createTextNode('');
        textNodes.set(r.id, t);
        td2.appendChild(t);
        // The label is reactive; the id is not (it never changes).
        effect(() => setText(t, r.label.value));
        tr.appendChild(td1);
        tr.appendChild(td2);
        return tr;
      },
    );

    const build = () => {
      const out: Row[] = [];
      for (let i = 0; i < 1000; i++) out.push({ id: nextId++, label: signal(nextLabel()) });
      return out;
    };

    return {
      parent,
      rows,
      textNodes,
      run: () => (rows.value = build()), // #run: Clear() + 1000 AddRow, one write
      update: () => {
        const r = rows.value;
        for (let i = 0; i < r.length; i += 10) r[i].label.value += ' !!!';
      },
      swap: () => {
        const r = rows.value;
        if (r.length > 998) {
          const copy = r.slice();
          const tmp = copy[1];
          copy[1] = copy[998];
          copy[998] = tmp;
          rows.value = copy;
        }
      },
      clear: () => (rows.value = []),
    };
  }

  it('create 1000 (cold): exactly 1000 inserts, 0 removes, 0 moves', () => {
    const a = app();
    stats.reset();
    a.run();
    expect(stats.insert).toBe(1000);
    expect(stats.remove).toBe(0);
    expect(a.parent.children.length).toBe(1000);
  });

  it('create 1000 (WARM, the C4 headline): 1000 removes + 1000 inserts, and nothing more', () => {
    // #run calls Clear() first, so the timed second #run replaces 1000 rows whose
    // ids (1..1000) share no key with the new ones (1001..2000). A full
    // replacement is 1000 unmounts + 1000 mounts; there is nothing to reuse and
    // nothing to move. Crucially it must not ALSO emit moves.
    const a = app();
    a.run();
    stats.reset();
    a.run();
    expect(stats.remove).toBe(1000);
    expect(stats.insert).toBe(1000);
    expect(a.parent.children.length).toBe(1000);
    expect(a.parent.children[0].children[0].textContent).toBe('1001');
    expect(a.parent.children[999].children[0].textContent).toBe('2000');
  });

  it('update every 10th: exactly 100 text writes and ZERO list operations', () => {
    // The headline result for the signal model. Labels are signals, so the list
    // never re-reconciles: 100 rows change, 100 text nodes are written, and the
    // reconciler is not even invoked. Blazor must diff to discover the same thing.
    const a = app();
    a.run();
    stats.reset();
    a.update();

    expect(stats.text).toBe(100);
    expect(stats.insert).toBe(0);
    expect(stats.remove).toBe(0);
    expect(stats.dom).toBe(100);
    expect(stats.links).toBe(0); // and it allocates no edges

    const cells = [...a.parent.children].map((tr) => tr.children[1].textContent!);
    expect(cells[0].endsWith(' !!!')).toBe(true);
    expect(cells[10].endsWith(' !!!')).toBe(true);
    expect(cells[1].endsWith(' !!!')).toBe(false);
    expect(cells.filter((c) => c.endsWith(' !!!')).length).toBe(100);
  });

  it('update is cumulative, like the baseline', () => {
    const a = app();
    a.run();
    a.update();
    a.update();
    expect(a.parent.children[0].children[1].textContent!.endsWith(' !!! !!!')).toBe(true);
  });

  it('SWAP 1 <-> 998 in 1000 keyed rows: exactly 2 moves, 0 mounts, 0 unmounts', () => {
    // THE assertion the whole LIS exists for. A reconciler without LIS emits up
    // to 998 moves here and still produces the correct DOM, so correctness alone
    // does not catch it — only counting does.
    const a = app();
    a.run();
    const before = [...a.parent.children].map((c) => c.children[0].textContent);
    stats.reset();

    a.swap();

    expect(stats.insert).toBe(2); // a move IS an insertBefore
    expect(stats.remove).toBe(0);
    expect(stats.text).toBe(0); // no text was rewritten: nodes moved, not rebuilt
    expect(stats.dom).toBe(2);

    const after = [...a.parent.children].map((c) => c.children[0].textContent);
    // Reciprocal swap, verified in BOTH directions — the harness's own guard
    // against `rows[1] = rows[998]` passing as a swap.
    expect(after[1]).toBe(before[998]);
    expect(after[998]).toBe(before[1]);
    expect(after[0]).toBe(before[0]);
    expect(after[999]).toBe(before[999]);
    expect(after[500]).toBe(before[500]);
    expect(after.length).toBe(1000);
    expect(new Set(after).size).toBe(1000); // no duplicate, no loss
  });

  it('swap preserves node identity (moves the real nodes)', () => {
    const a = app();
    a.run();
    const n1 = a.parent.children[1];
    const n998 = a.parent.children[998];
    a.swap();
    expect(a.parent.children[1]).toBe(n998);
    expect(a.parent.children[998]).toBe(n1);
  });

  it('swap twice returns to the original order, still 2 moves each', () => {
    const a = app();
    a.run();
    const orig = [...a.parent.children].map((c) => c.children[0].textContent);
    a.swap();
    stats.reset();
    a.swap();
    expect(stats.insert).toBe(2);
    expect([...a.parent.children].map((c) => c.children[0].textContent)).toEqual(orig);
  });

  it('clear: exactly 1000 removes, 0 inserts', () => {
    const a = app();
    a.run();
    stats.reset();
    a.clear();
    expect(stats.remove).toBe(1000);
    expect(stats.insert).toBe(0);
    expect(a.parent.children.length).toBe(0);
  });

  it('the label stream matches bench/harness/expected-labels.json byte for byte', () => {
    // Guards the fairness contract from DECISIONS.md #5: an app emitting a
    // constant label does drastically less string work and posts a faster
    // create. These values are COPIED FROM THE HARNESS FIXTURE, not from a run
    // of this code — the point is to catch this model drifting away from the
    // stream Blazor emits, so they must come from the other side.
    const a = app();
    a.run();
    const labels = [...a.parent.children].map((tr) => tr.children[1].textContent);
    expect(labels.slice(0, 5)).toEqual([
      'adorable pink desk',
      'unsightly purple sandwich',
      'large brown sandwich',
      'important brown house',
      'mushy red desk',
    ]);
    expect(labels[999]).toBe('important white pizza');
    expect(a.parent.children[0].children[0].textContent).toBe('1'); // firstRunFirstId
    expect(a.parent.children[999].children[0].textContent).toBe('1000');
    expect(new Set(labels).size).toBe(863); // a real stream, not a constant
  });

  it('the SECOND run emits a different stream — the one create-warm times', () => {
    // The LCG is seeded once per page load and never reset, so run 2 draws
    // 3001..6000. create-warm times run 2, so an app that cached run 1's strings
    // would do zero of the work C4's headline is supposed to measure.
    const a = app();
    a.run();
    a.run();
    const labels = [...a.parent.children].map((tr) => tr.children[1].textContent);
    expect(labels.slice(0, 5)).toEqual([
      'mushy blue mouse',
      'expensive yellow keyboard',
      'large blue car',
      'large white pony',
      'unsightly black house',
    ]);
    expect(labels[999]).toBe('odd pink pizza');
    expect(a.parent.children[0].children[0].textContent).toBe('1001'); // secondRunFirstId
  });

  it('ids are monotonic and never reset across runs', () => {
    const a = app();
    a.run();
    a.clear();
    a.run();
    expect(a.parent.children[0].children[0].textContent).toBe('1001');
  });

  it('full scenario sequence leaves a consistent DOM', () => {
    // The harness's exact order: #run -> #update -> #swaprows -> #clear -> #run.
    const a = app();
    a.run();
    a.update();
    a.swap();
    a.clear();
    expect(a.parent.children.length).toBe(0);
    a.run();
    expect(a.parent.children.length).toBe(1000);
    expect(new Set([...a.parent.children].map((c) => c.children[0].textContent)).size).toBe(1000);
  });
});
