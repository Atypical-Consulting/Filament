/**
 * LambdaHandler — hand-written Filament answer key for baseline/LambdaHandler.Blazor/App.razor.
 *
 * An inline lambda event handler (decision 105): `@onclick="() => count++"`. The generator wraps the
 * lambda body as a synthetic method, TRANSLATES it through the semantic model (count is a signal, so
 * `count++` -> `count.value++`), and emits `listen(el, 'click', () => { … })` -- an emitted arrow, NOT a
 * verbatim splice (decision 57's reason: "is this a signal?" is answered by the compiler, not spelling).
 *
 * DOM contract mirrors Counter.Blazor: p("Count: " + span#count), button#inc, blank line "\n\n" text node.
 * `count` is read by @count and assigned by the lambda -> a signal. The lambda writes once -> no batch (#68).
 */

import { signal, effect, setText, listen, insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const count = signal(0);

  const p = document.createElement('p');
  insert(p, document.createTextNode('Count: '));
  const span = document.createElement('span');
  span.id = 'count';
  const t = document.createTextNode('');
  insert(span, t);
  insert(p, span);

  const button = document.createElement('button');
  button.id = 'inc';
  insert(button, document.createTextNode('Increment'));

  effect(() => setText(t, count.value));

  listen(button, 'click', () => {
    count.value++;
  });

  insert(target, p);
  insert(target, document.createTextNode('\n\n'));
  insert(target, button);
}
