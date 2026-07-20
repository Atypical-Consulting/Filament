/**
 * ForeachArray — hand-written Filament answer key for baseline/ForeachArray.Blazor/App.razor.
 *
 * THE POINT: a @foreach over an ARRAY field (decision 124), not a List<T>. A List<T> reconciles off a
 * version signal (rows.js decision 1) -- a plain array plus a separate `xVersion` the mutating method
 * bumps. An array has no version. But a REASSIGNED array IS a signal (read by the @foreach AND assigned
 * outside construction, the decision-67 conjunction): its own signal is the subscribable thing. So
 * list()'s source is just `() => items.value` -- reading the signal both subscribes the list to it AND
 * yields the array. (For a List the source is a two-line block, `{ version.value; return array; }`,
 * because the thing read and the thing returned differ; here they are one, so it collapses to one line.)
 *
 * Blazor DOM contract (same shape Blazor renders <App> into #app):
 *
 *   <ul id="list"><li>1</li><li>2</li><li>3</li></ul>
 *   <button id="add">add</button>
 *
 * `items` starts [1,2,3] -> three <li>. #add REASSIGNS it wholesale to [3,4,1,5,2]; the signal write
 * fires list(), which reconciles by @key="n" (the int value, a stable non-reactive identity): keys 4
 * and 5 are inserted, keys 1/2/3 are MOVED into the new order, nothing is destroyed and rebuilt. So
 * #list text goes "123" -> "34152" and its <li> count 3 -> 5 -- exactly what Blazor's keyed diff renders
 * for the same reassignment. The click IS the measurement (BENCH n°43).
 *
 * THE ROW. createN(n) builds one <li> with a STATIC text node for n: n is the loop variable, constant
 * within a row, so it is not reactive (like Rows' row.Label). A reused key keeps its node and its text;
 * a new key builds a fresh <li>. keyOf is `(n) => n` and anchor is null (a list whose wrapper <ul> holds
 * nothing after the rows appends to it).
 *
 * THE HANDLER. Add() is one write (the reassignment), so decision 68's batch rule does NOT wrap it; Add
 * is named by exactly one @onclick and called nowhere else, so its body inlines into the click handler.
 * No runtime primitive is added -- signal/list/insert/listen are the runtime the flagship Rows path uses.
 */

import { signal, listen, insert, list } from '../../src/filament-runtime/src/index.ts';

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
    items.value = [3, 4, 1, 5, 2];
  });

  insert(target, ul);
  insert(target, document.createTextNode('\n\n'));
  insert(target, addBtn);
}
