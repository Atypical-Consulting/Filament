/**
 * LongCounter — hand-written Filament answer key for baseline/LongCounter.Blazor/App.razor.
 *
 * `long` -> BigInt (decision 112). `long`'s JS home is BigInt, not number: its integer display is EXACT
 * past 2^53 where a JS double loses precision, and BigInt division truncates toward zero exactly as C#'s
 * long/long. `total` starts at 9007199254740990n (just under 2^53 = 9007199254740992) and each click adds
 * 3n, so it reaches 9007199254740993n -- a value a double cannot hold (it would round to ...992). The DOM
 * coerces a BigInt to its exact decimal string when setText assigns node.data, so no runtime change is
 * needed: setText already ships.
 *
 * `total` is read by @total AND assigned by the handler -> a signal (of BigInt). The `3` literal is in a
 * long context, so it is the BigInt literal `3n` -- BigInt and number cannot be mixed, so both operands
 * must be BigInt. DOM contract mirrors Counter.Blazor: p(span#value), button#add, blank line "\n\n".
 */

import { signal, effect, setText, listen, insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const total = signal(9007199254740990n);

  const p = document.createElement('p');
  const span = document.createElement('span');
  span.id = 'value';
  const t = document.createTextNode('');
  insert(span, t);
  insert(p, span);

  const button = document.createElement('button');
  button.id = 'add';
  insert(button, document.createTextNode('add'));

  effect(() => setText(t, total.value));

  listen(button, 'click', () => {
    total.value = total.value + 3n;
  });

  insert(target, p);
  insert(target, document.createTextNode('\n\n'));
  insert(target, button);
}
