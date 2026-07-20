/**
 * SizedArray — hand-written Filament answer key for baseline/SizedArray.Blazor/App.razor.
 *
 * A SIZED array `new T[n]` (decision 122). It maps to `new Array(n).fill(default(T))` -- C#'s n-defaults array
 * (int -> 0, string -> null, bool -> false). `xs` starts as `new int[3]` = `new Array(3).fill(0)` = [0,0,0]
 * (Length 3, xs[0] = 0); #fill REASSIGNS it to the array literal `new int[]{7,8,9,10}` -> [7, 8, 9, 10]
 * (Length 4, xs[0] = 7). So #len goes "3" -> "4" and #first "0" -> "7".
 *
 * `xs` is read by @xs.Length / @xs[0] AND reassigned by Fill -> a signal; wholesale reassignment fires the
 * effects (.length and [0] are the array's own, on xs.value). An element write xs[i] = v would be refused (a
 * mutable collection is a List). No runtime primitive is added -- Array/.fill/.length/indexing are JS builtins.
 */

import { signal, effect, setText, listen, insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const xs = signal(new Array(3).fill(0));

  const p = document.createElement('p');
  insert(p, document.createTextNode('Len: '));
  const lenSpan = document.createElement('span');
  lenSpan.id = 'len';
  const lenT = document.createTextNode('');
  insert(lenSpan, lenT);
  insert(p, lenSpan);
  insert(p, document.createTextNode(', First: '));
  const firstSpan = document.createElement('span');
  firstSpan.id = 'first';
  const firstT = document.createTextNode('');
  insert(firstSpan, firstT);
  insert(p, firstSpan);

  const button = document.createElement('button');
  button.id = 'fill';
  insert(button, document.createTextNode('fill'));

  effect(() => setText(lenT, xs.value.length));
  effect(() => setText(firstT, xs.value[0]));

  listen(button, 'click', () => {
    xs.value = [7, 8, 9, 10];
  });

  insert(target, p);
  insert(target, document.createTextNode('\n\n'));
  insert(target, button);
}
