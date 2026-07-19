/**
 * ArrayIndex — hand-written Filament answer key for baseline/ArrayIndex.Blazor/App.razor.
 *
 * A `T[]` array (decision 117). A T[] maps to the SAME JS array a List<T> does; the difference is only
 * mutability -- an array is fixed-size, so it is admitted READ-ONLY (indexing, .Length, iteration; element
 * assignment is refused). `items` is the array literal [10, 20, 30]; `i` is a signal, and #next advances it
 * (mod items.length) so `@items[i]` steps through the array. `.Length` is the array's own `.length`.
 *
 * `items` is read by @items[i] but never mutated -> a plain array; `i` is read AND assigned -> a signal. So
 * `@items[i]` is `setText(t, items[i.value])`, reactive on `i`. #value goes "10" -> "20" -> "30" -> "10". No
 * runtime primitive is added (indexing and .length are the array's own). DOM contract mirrors Counter.Blazor.
 */

import { signal, effect, setText, listen, insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const items = [10, 20, 30];
  const i = signal(0);

  const p = document.createElement('p');
  insert(p, document.createTextNode('Item: '));
  const span = document.createElement('span');
  span.id = 'value';
  const t = document.createTextNode('');
  insert(span, t);
  insert(p, span);

  const button = document.createElement('button');
  button.id = 'next';
  insert(button, document.createTextNode('next'));

  effect(() => setText(t, items[i.value]));

  listen(button, 'click', () => {
    i.value = (i.value + 1) % items.length;
  });

  insert(target, p);
  insert(target, document.createTextNode('\n\n'));
  insert(target, button);
}
