/**
 * Linq — hand-written Filament answer key for baseline/Linq.Blazor/App.razor.
 *
 * LINQ over a List (decision 116). C#'s common LINQ operators map to JS array methods: Where -> filter,
 * Select -> map, Count -> length, Any -> some, All -> every, ToList -> the array. A List is already a
 * materialised JS array (rows.js decision 1), so the eager array methods are faithful to a LINQ chain that
 * ends in a scalar. The predicate lambda `x => x > 0` translates through the same machinery as the rest of
 * @code (its parameter is an ordinary local), so it stays `x => x > 0`.
 *
 * `_nums.Where(x => x > 0).Count()` -> `_nums.filter(x => x > 0).length`. #go counts the positives in
 * [-2, 3, -1, 5, 0] -> 2. `count` is read by @count and assigned by the handler -> a signal; `_nums` is never
 * mutated -> a plain array. No runtime primitive is added (pure array methods). DOM contract mirrors Counter.Blazor.
 */

import { signal, effect, setText, listen, insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const count = signal(0);
  const _nums = [-2, 3, -1, 5, 0];

  const p = document.createElement('p');
  insert(p, document.createTextNode('Positives: '));
  const span = document.createElement('span');
  span.id = 'value';
  const t = document.createTextNode('');
  insert(span, t);
  insert(p, span);

  const button = document.createElement('button');
  button.id = 'go';
  insert(button, document.createTextNode('go'));

  effect(() => setText(t, count.value));

  listen(button, 'click', () => {
    count.value = _nums.filter(x => x > 0).length;
  });

  insert(target, p);
  insert(target, document.createTextNode('\n\n'));
  insert(target, button);
}
