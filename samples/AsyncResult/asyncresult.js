/**
 * AsyncResult — hand-written Filament answer key for baseline/AsyncResult.Blazor/App.razor.
 *
 * A value-returning `async Task<T>` (decision 123, extending #119). Compute is an `async Task<int>` -> an
 * `async function` that RETURNS its value; `count = await Compute()` becomes `count.value = await compute()`,
 * where await unwraps the Promise. Go (an `async Task` handler) awaits Compute; Compute awaits a 1ms delay then
 * returns `count + 42`. So each click adds 42 AFTER the awaited delay: #count goes 0 -> 42 -> 84.
 *
 * `count` is read by @count and assigned by Go -> a signal; the effect re-renders when the awaited continuation
 * writes it. `compute` is a component method called from Go (not a single-use handler) -> kept as a named
 * `async function`, not inlined. No runtime primitive is added -- async/await/Promise/setTimeout are JS builtins.
 */

import { signal, effect, setText, listen, insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const count = signal(0);

  async function compute() {
    await new Promise((resolve) => setTimeout(resolve, 1));
    return count.value + 42;
  }

  const p = document.createElement('p');
  insert(p, document.createTextNode('Count: '));
  const span = document.createElement('span');
  span.id = 'count';
  const t = document.createTextNode('');
  insert(span, t);
  insert(p, span);

  const button = document.createElement('button');
  button.id = 'go';
  insert(button, document.createTextNode('go'));

  effect(() => setText(t, count.value));

  listen(button, 'click', async () => {
    count.value = await compute();
  });

  insert(target, p);
  insert(target, document.createTextNode('\n\n'));
  insert(target, button);
}
