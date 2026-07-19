/**
 * FloatCounter — hand-written Filament answer key for baseline/FloatCounter.Blazor/App.razor.
 *
 * `float` -> Math.fround + shortest-round-trip display (decision 113). A float's JS home is a Math.fround'd
 * number: C# computes float arithmetic in single precision, rounding at EVERY operation, so each op is wrapped
 * in Math.fround (a literal is stored frounded too). And a float DISPLAY cannot be the bare double coercion --
 * `0.1f` is the double 0.10000000149…, which would print in full -- so it goes through __f32, which finds the
 * SHORTEST decimal that round-trips through float32, exactly what C#'s float.ToString prints ("0.1").
 *
 * `total` starts at 0.1f and each click adds 0.2f. In C# float that is 0.1f + 0.2f = 0.3f ("0.3"), where a JS
 * double would give 0.30000000000000004. Filament matches C#: #value goes "0.1" -> "0.3" -> "0.5". `total` is
 * read by @total AND assigned by the handler -> a signal. __f32 is emitted (not a runtime export), so a float
 * display stays generator-only: the runtime is untouched. DOM contract mirrors Counter.Blazor.
 */

import { signal, effect, setText, listen, insert } from '../../src/filament-runtime/src/index.ts';

// -- float display: shortest decimal that round-trips through float32 (C# float.ToString) --
function __f32(x) {
  x = Math.fround(x);
  if (!isFinite(x) || Number.isInteger(x)) return String(x);
  for (let p = 1; p <= 9; p++) {
    const s = x.toPrecision(p);
    if (Math.fround(Number(s)) === x) return Number(s).toString();
  }
  return String(x);
}

export function mount(target) {
  const total = signal(Math.fround(0.1));

  const p = document.createElement('p');
  const span = document.createElement('span');
  span.id = 'value';
  const t = document.createTextNode('');
  insert(span, t);
  insert(p, span);

  const button = document.createElement('button');
  button.id = 'add';
  insert(button, document.createTextNode('add'));

  effect(() => setText(t, __f32(total.value)));

  listen(button, 'click', () => {
    total.value = Math.fround(total.value + Math.fround(0.2));
  });

  insert(target, p);
  insert(target, document.createTextNode('\n\n'));
  insert(target, button);
}
