/**
 * ForeachList — hand-written Filament answer key for baseline/ForeachList.Blazor/App.razor.
 *
 * THE POINT: a @foreach over a REASSIGNED List<T> (decision 140) — the List twin of ForeachArray
 * (decision 124). A MUTATED List reconciles off a version signal (rows.js decision 1) because the
 * array reference never changes; a reassigned-never-mutated List has no version to bump — the FIELD
 * is the subscribable thing, exactly as a reassigned T[] is. So list()'s source collapses to the one
 * self-subscribing read `() => items.value`: reading the signal both subscribes the list to it AND
 * yields the array. No new emission shape exists here — decision 124's — which is why the slice is
 * an ADMISSION change, not an emitter one.
 *
 * Blazor DOM contract (same shape Blazor renders <App> into #app):
 *
 *   <ul id="list"><li>1</li><li>2</li><li>3</li></ul>
 *   <button id="add">add</button>
 *
 * `items` starts [1,2,3] -> three <li>. #add reassigns it to `_pool.Where(x => x != 2).ToList()`,
 * i.e. [3,4,1,5]: the signal write fires list(), which reconciles by @key="n" — key 2 is REMOVED,
 * keys 4 and 5 are INSERTED, keys 1/3 are MOVED. One click exercises all three reconcile behaviours
 * and #list text goes "123" -> "3415", <li> count 3 -> 4 — exactly what Blazor's keyed diff renders
 * for the same reassignment. The click IS the measurement.
 *
 * `_pool` is read only inside Add and never written: not a signal, not rendered — a hoisted plain
 * array (decision 121's hoisted literal list). Where -> filter and ToList -> the array itself
 * (decision 116: filter already materialises), so the whole handler is ONE signal write; decision
 * 68's batch rule does not wrap a single write, and Add is named by exactly one @onclick so its
 * body inlines into the click handler.
 *
 * THE ROW. createN(n) builds one <li> with a STATIC text node for n: n is the loop variable,
 * constant within a row (like ForeachArray's). keyOf is `(n) => n`; anchor is null.
 */

import { signal, listen, insert, list } from '../../src/filament-runtime/src/index.ts';

// Immutable literal data hoists to module scope (rows.js mapping decision 4; the BENCH n°40 gotcha:
// the answer key must follow the generator's stated hoisting rule, not restate the C# layout).
const _pool = [3, 4, 1, 5, 2];

export function mount(target) {
  const items = signal([1, 2, 3]);

  const ul = document.createElement('ul');
  ul.id = 'list';
  const addBtn = document.createElement('button');
  addBtn.id = 'add';
  insert(addBtn, document.createTextNode('add'));

  function createN(n) {
    const li = document.createElement('li');
    insert(li, document.createTextNode(n));
    return li;
  }
  list(ul, () => items.value, (n) => n, createN, null);

  listen(addBtn, 'click', () => {
    items.value = _pool.filter(x => x !== 2);
  });

  insert(target, ul);
  insert(target, document.createTextNode('\n\n'));
  insert(target, addBtn);
}
