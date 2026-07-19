/**
 * DictLookup — hand-written Filament answer key for baseline/DictLookup.Blazor/App.razor.
 *
 * `Dictionary<K,V>` -> a JS Map (decision 118), admitted READ-ONLY. A Dictionary's construction becomes
 * `new Map([[k, v], …])`, its indexer `d[key]` becomes `d.get(key)`, `.Count` becomes `.size`, and
 * `.ContainsKey(k)` becomes `.has(k)`. An entry write `d[key] = v` is refused (a Dictionary is read-only here --
 * no reactive version signal, unlike a List).
 *
 * `labels` is the constant map {1:"one", 2:"two", 3:"three"}; `key` is a signal, and #next advances it (1->2->3->1),
 * so `@labels[key]` walks the map. #value goes "one" -> "two" -> "three" -> "one". `labels` is read but never
 * mutated -> a plain Map; `key` is read AND assigned -> a signal. Map is a JS builtin, so no runtime primitive is
 * added. DOM contract mirrors Counter.Blazor.
 */

import { signal, effect, setText, listen, insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const labels = new Map([[1, 'one'], [2, 'two'], [3, 'three']]);
  const key = signal(1);

  const p = document.createElement('p');
  insert(p, document.createTextNode('Label: '));
  const span = document.createElement('span');
  span.id = 'value';
  const t = document.createTextNode('');
  insert(span, t);
  insert(p, span);

  const button = document.createElement('button');
  button.id = 'next';
  insert(button, document.createTextNode('next'));

  effect(() => setText(t, labels.get(key.value)));

  listen(button, 'click', () => {
    key.value = key.value % 3 + 1;
  });

  insert(target, p);
  insert(target, document.createTextNode('\n\n'));
  insert(target, button);
}
