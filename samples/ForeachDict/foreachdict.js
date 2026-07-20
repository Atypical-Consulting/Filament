/**
 * ForeachDict — hand-written Filament answer key for baseline/ForeachDict.Blazor/App.razor.
 *
 * THE POINT: a @foreach over a Dictionary (decision 125), the KeyValuePair sibling of the array @foreach
 * (decision 124). A Dictionary is a JS Map (decision 118). Two things make it more than the array case:
 *
 *   1. THE SOURCE SPREADS. list() reconciles an ARRAY, but a Map is not one, so the source spreads it:
 *      `() => [...scores.value]` yields [[k,v], …]. Reading scores.value both subscribes the list AND
 *      materialises the entries. Each row's loop variable `kvp` is thus a [k, v] pair.
 *
 *   2. THE VALUE IS A REACTIVE LOOKUP, NOT kvp[1]. list() REUSES a persisting key's row -- it never re-runs
 *      create -- so a frozen kvp[1] would go STALE when that key's value changes, where Blazor re-renders the
 *      reused element. So @kvp.Value compiles to `scores.value.get(kvp[0])` inside an effect: reading the Dict
 *      signal makes the text reactive, and the effect re-runs on reassignment to fetch the CURRENT value for
 *      this row's stable key. @kvp.Key stays the plain kvp[0] -- it is the reconcile identity, and it never
 *      changes for a row that persists.
 *
 * Blazor DOM contract (same shape Blazor renders <App> into #app):
 *
 *   <ul id="list"><li>a=1</li><li>b=2</li></ul>
 *   <button id="bump">bump</button>
 *
 * `scores` starts { a=1, b=2 }. #bump REASSIGNS it to { b=20, c=3, a=1 }: key "b" PERSISTS but its value
 * changes 2 -> 20 (the reactive lookup refreshes it), "c" is inserted, "a" persists, and the keyed order
 * becomes b,c,a. So #list text goes "a=1b=2" -> "b=20c=3a=1" and its <li> count 2 -> 3 -- exactly what
 * Blazor renders. The click IS the measurement (BENCH n°44); it proves BOTH the keyed reorder AND the value
 * refresh, the second of which a static kvp[1] would get wrong.
 *
 * THE HANDLER. Bump() is one write (the reassignment), so decision 68's batch rule does NOT wrap it; Bump is
 * named by exactly one @onclick and called nowhere else, so its body inlines into the click handler. No runtime
 * primitive is added -- signal/effect/setText/list/insert/listen are the runtime the flagship Rows path uses.
 */

import { signal, effect, setText, listen, insert, list } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const scores = signal(new Map([['a', 1], ['b', 2]]));

  const ul = document.createElement('ul');
  ul.id = 'list';
  const bumpBtn = document.createElement('button');
  bumpBtn.id = 'bump';
  insert(bumpBtn, document.createTextNode('bump'));

  function createKvp(kvp) {
    const li = document.createElement('li');
    insert(li, document.createTextNode(kvp[0]));
    insert(li, document.createTextNode('='));
    const txValue = document.createTextNode('');
    insert(li, txValue);
    effect(() => setText(txValue, scores.value.get(kvp[0])));
    return li;
  }
  list(ul, () => [...scores.value], (kvp) => kvp[0], createKvp, null);

  listen(bumpBtn, 'click', () => {
    scores.value = new Map([['b', 20], ['c', 3], ['a', 1]]);
  });

  insert(target, ul);
  insert(target, document.createTextNode('\n\n'));
  insert(target, bumpBtn);
}
