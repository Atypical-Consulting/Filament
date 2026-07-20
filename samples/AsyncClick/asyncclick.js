/**
 * AsyncClick — hand-written Filament answer key for baseline/AsyncClick.Blazor/App.razor.
 *
 * async/await (decision 119). An `async Task` event handler AWAITS before it mutates state: LoadAsync awaits a
 * 1ms delay, then increments `count`. The mapping:
 *   - an `async Task` method -> an `async function` (a value-returning `async Task<T>` is deferred);
 *   - `await x` -> JS's own `await x` (valid only in an async context, which the generator guards);
 *   - `Task.Delay(ms)` -> `new Promise((resolve) => setTimeout(resolve, ms))`.
 * When inlined into a listen(), the handler arrow is `async () => { … }` so its await is legal JS.
 *
 * `count` is read by @count and assigned by LoadAsync -> a signal; the effect re-renders when the awaited
 * continuation writes it, so #count goes 0 -> 1 -> 2, each after the awaited delay. No runtime primitive is added:
 * Promise/setTimeout/await are JS builtins. DOM contract mirrors Counter.Blazor.
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
  button.id = 'go';
  insert(button, document.createTextNode('go'));

  effect(() => setText(t, count.value));

  listen(button, 'click', async () => {
    await new Promise((resolve) => setTimeout(resolve, 1));
    count.value++;
  });

  insert(target, p);
  insert(target, document.createTextNode('\n\n'));
  insert(target, button);
}
