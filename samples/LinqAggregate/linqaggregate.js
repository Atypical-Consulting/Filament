/**
 * LinqAggregate — hand-written Filament answer key for baseline/LinqAggregate.Blazor/App.razor.
 *
 * LINQ number aggregates over a List (decision 121). C#'s aggregates map to JS array reductions: Sum ->
 * reduce((a,b)=>a+b, 0); Min/Max -> Math.min/max(...arr); Average -> sum/length; First/Last -> index. They are
 * admitted when the RESULT is int or double (a long or decimal aggregate would need a typed reduction, deferred).
 *
 * #go computes `_nums.Where(x => x > 3).Sum()` -> `_nums.filter(x => x > 3).reduce((a, b) => a + b, 0)` = 7+9+5
 * = 21, and `_nums.Max()` -> `Math.max(..._nums)` = 9. So #sum goes "0" -> "21" and #max "0" -> "9". `total`/`peak`
 * are read by @total/@peak and assigned by Go -> signals; `_nums` is never mutated -> a plain array. No runtime
 * primitive is added. DOM contract mirrors Counter.Blazor.
 */

import { signal, effect, batch, setText, listen, insert } from '../../src/filament-runtime/src/index.ts';

// _nums is an immutable literal list, hoisted to module scope (rows.js decision 4).
const _nums = [3, 7, 2, 9, 5];

export function mount(target) {
  const total = signal(0);
  const peak = signal(0);

  const p = document.createElement('p');
  insert(p, document.createTextNode('Sum: '));
  const sumSpan = document.createElement('span');
  sumSpan.id = 'sum';
  const sumT = document.createTextNode('');
  insert(sumSpan, sumT);
  insert(p, sumSpan);
  insert(p, document.createTextNode(', Max: '));
  const maxSpan = document.createElement('span');
  maxSpan.id = 'max';
  const maxT = document.createTextNode('');
  insert(maxSpan, maxT);
  insert(p, maxSpan);

  const button = document.createElement('button');
  button.id = 'go';
  insert(button, document.createTextNode('go'));

  effect(() => setText(sumT, total.value));
  effect(() => setText(maxT, peak.value));

  listen(button, 'click', () => batch(() => {
    total.value = _nums.filter(x => x > 3).reduce((a, b) => a + b, 0);
    peak.value = Math.max(..._nums);
  }));

  insert(target, p);
  insert(target, document.createTextNode('\n\n'));
  insert(target, button);
}
