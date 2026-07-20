/**
 * LinqOrder — hand-written Filament answer key for baseline/LinqOrder.Blazor/App.razor.
 *
 * LINQ ordering + paging over a List (decision 126), the sequence-producing siblings of the aggregates in
 * decision 121. Each maps to a JS array method:
 *
 *   OrderBy(x => key)            -> [...arr].sort((__a, __b) => key(__a) - key(__b))   (ascending)
 *   OrderByDescending(x => key)  -> [...arr].sort((__a, __b) => key(__b) - key(__a))   (descending)
 *   Skip(n)                      -> .slice(n)
 *   Take(n)                      -> .slice(0, n)
 *   First()                      -> [0]           Last() -> .at(-1)   (the scalar terminals, decision 121)
 *
 * The sort works on a COPY (`[...arr]`) so the source list is never mutated, and JS Array.sort is STABLE (ES2019)
 * exactly like LINQ's OrderBy, so equal keys keep their order. The key selector must be NUMERIC -- the comparator
 * SUBTRACTS keys, and `"a" - "b"` is NaN, a silent mis-sort -- so a string/other key is refused (deferred). The
 * selector is applied inline as `(x => x)(__a)`; it passes through untouched, the same lambda the aggregates use.
 *
 * Blazor DOM contract (same shape Blazor renders <App> into #app):
 *
 *   <p>Lo: <span id="lo">0</span>, Hi: <span id="hi">0</span></p>
 *   <button id="go">go</button>
 *
 * Over _nums = [3, 7, 2, 9, 5], #go computes lo = OrderBy(x=>x).Skip(1).First() -- sorted [2,3,5,7,9], drop the
 * first -> [3,5,7,9], take first = 3 -- and hi = OrderByDescending(x=>x).Take(2).Last() -- sorted desc [9,7,5,3,2],
 * take two -> [9,7], last = 7. So #lo goes "0" -> "3" and #hi "0" -> "7". The click IS the measurement (BENCH n°45).
 *
 * TWO gotchas mirrored from the generator's real emission (decisions 68 / rows.js 4): Go writes TWO signals, so its
 * body is wrapped in batch(); and _nums is an immutable literal list, so it is HOISTED to module scope. No runtime
 * primitive is added -- sort/slice/spread are JS builtins; signal/effect/batch/setText/listen/insert are Rows' runtime.
 */

import { signal, effect, batch, setText, listen, insert } from '../../src/filament-runtime/src/index.ts';

const _nums = [3, 7, 2, 9, 5];

export function mount(target) {
  const lo = signal(0);
  const hi = signal(0);

  const p = document.createElement('p');
  insert(p, document.createTextNode('Lo: '));
  const loSpan = document.createElement('span');
  loSpan.id = 'lo';
  const txLo = document.createTextNode('');
  insert(loSpan, txLo);
  insert(p, loSpan);
  insert(p, document.createTextNode(', Hi: '));
  const hiSpan = document.createElement('span');
  hiSpan.id = 'hi';
  const txHi = document.createTextNode('');
  insert(hiSpan, txHi);
  insert(p, hiSpan);
  const goBtn = document.createElement('button');
  goBtn.id = 'go';
  insert(goBtn, document.createTextNode('go'));

  effect(() => setText(txLo, lo.value));
  effect(() => setText(txHi, hi.value));

  listen(goBtn, 'click', () => batch(() => {
    lo.value = [..._nums].sort((__a, __b) => (x => x)(__a) - (x => x)(__b)).slice(1)[0];
    hi.value = [..._nums].sort((__a, __b) => (x => x)(__b) - (x => x)(__a)).slice(0, 2).at(-1);
  }));

  insert(target, p);
  insert(target, document.createTextNode('\n\n'));
  insert(target, goBtn);
}
