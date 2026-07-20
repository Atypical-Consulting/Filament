/**
 * GroupBy — hand-written Filament answer key for baseline/GroupBy.Blazor/App.razor.
 *
 * LINQ GroupBy over a List (decision 128), the grouping sibling of the ordering operators (#126). GroupBy(x => key)
 * yields IGrouping<K,T> groups, and an IGrouping is BOTH a key holder (.Key) AND a sequence of its elements. The
 * faithful JS model: a group is an ARRAY of its elements WITH a `.key` property. Then:
 *
 *   GroupBy(x => key)  ->  a reduce building Map<K, group>, then [...map.values()]  -- an array of groups
 *   g.Key              ->  g.key
 *   g.Count() / g.Sum() / First() on a group  ->  the normal array/LINQ mappings, because a group IS an array
 *
 * The reduce inserts a fresh keyed array on the FIRST-seen key and pushes each element; a Map preserves insertion
 * order, so .values() yields the groups in first-key-appearance order -- exactly LINQ's GroupBy order. The key must
 * be a SCALAR (int/string/bool): a JS Map compares keys by value, so a boxed key (a decimal object, a record) would
 * group by reference and silently split equal keys.
 *
 * Blazor DOM contract (same shape Blazor renders <App> into #app):
 *
 *   <p>G:<span id="g">0</span> K:<span id="k">-1</span> S:<span id="s">0</span></p>
 *   <button id="go">go</button>
 *
 * Over _nums = [3,7,2,9,5] grouped by x % 2: 3,7,9,5 are odd (key 1, seen first), 2 is even (key 0). So there are
 * 2 groups; the first group (key 1) is [3,7,9,5], 4 elements. #go sets groups = ...Count() (2), firstKey =
 * ...First().Key (1), firstSize = ...First().Count() (4). So #g "0" -> "2", #k "-1" -> "1", #s "0" -> "4". The
 * click IS the measurement (BENCH n°47).
 *
 * TWO gotchas mirrored from the generator (decisions 68 / rows.js 4): Go writes THREE signals, so its body is
 * wrapped in batch(); and _nums is an immutable literal list, HOISTED to module scope. No runtime primitive is
 * added -- reduce/Map/spread are JS builtins; signal/effect/batch/setText/listen/insert are Rows' runtime.
 */

import { signal, effect, batch, setText, listen, insert } from '../../src/filament-runtime/src/index.ts';

const _nums = [3, 7, 2, 9, 5];

export function mount(target) {
  const groups = signal(0);
  const firstKey = signal(-1);
  const firstSize = signal(0);

  const p = document.createElement('p');
  insert(p, document.createTextNode('G:'));
  const gSpan = document.createElement('span');
  gSpan.id = 'g';
  const txG = document.createTextNode('');
  insert(gSpan, txG);
  insert(p, gSpan);
  insert(p, document.createTextNode(' K:'));
  const kSpan = document.createElement('span');
  kSpan.id = 'k';
  const txK = document.createTextNode('');
  insert(kSpan, txK);
  insert(p, kSpan);
  insert(p, document.createTextNode(' S:'));
  const sSpan = document.createElement('span');
  sSpan.id = 's';
  const txS = document.createTextNode('');
  insert(sSpan, txS);
  insert(p, sSpan);
  const goBtn = document.createElement('button');
  goBtn.id = 'go';
  insert(goBtn, document.createTextNode('go'));

  effect(() => setText(txG, groups.value));
  effect(() => setText(txK, firstKey.value));
  effect(() => setText(txS, firstSize.value));

  listen(goBtn, 'click', () => batch(() => {
    groups.value = [..._nums.reduce((__m, __x) => { const __k = (x => x % 2)(__x); let __g = __m.get(__k); if (!__g) { __g = []; __g.key = __k; __m.set(__k, __g); } __g.push(__x); return __m; }, new Map()).values()].length;
    firstKey.value = [..._nums.reduce((__m, __x) => { const __k = (x => x % 2)(__x); let __g = __m.get(__k); if (!__g) { __g = []; __g.key = __k; __m.set(__k, __g); } __g.push(__x); return __m; }, new Map()).values()][0].key;
    firstSize.value = [..._nums.reduce((__m, __x) => { const __k = (x => x % 2)(__x); let __g = __m.get(__k); if (!__g) { __g = []; __g.key = __k; __m.set(__k, __g); } __g.push(__x); return __m; }, new Map()).values()][0].length;
  }));

  insert(target, p);
  insert(target, document.createTextNode('\n\n'));
  insert(target, goBtn);
}
