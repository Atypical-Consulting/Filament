/**
 * TryLock — hand-written Filament answer key for baseline/TryLock.Blazor/App.razor.
 *
 * try/catch/throw/lock (decision 110). The #go handler THROWS inside a try, CATCHES it, then runs a
 * lock body, so each click adds 6 (catch +5, lock +1). The mapping:
 *   - try/catch/finally  -> the JS namesake (a bindingless `catch` -> a bindingless `catch {}`).
 *   - throw new Exception(msg) -> throw new Error(msg): C#'s Exception.Message and JS's Error.message
 *     carry the same string. A CAUGHT throw is faithful; an UNCAUGHT one is the disclosed edge.
 *   - lock (x) { … } -> a BARE block `{ … }`: JS is single-threaded, so a lock is never contended and
 *     the lock target (`this`) is dropped -- it has no meaning in the mount() closure.
 *
 * `count` is read by @count and assigned TWICE by Go, so it is a signal AND the handler batches (#68):
 * one MutationRecord for the pair, not two. DOM contract mirrors Counter.Blazor: p("Count: " + span#count),
 * button#go, blank line "\n\n" text node between them.
 */

import { signal, effect, batch, setText, listen, insert } from '../../src/filament-runtime/src/index.ts';

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

  listen(button, 'click', () => batch(() => {
    try {
      throw new Error('boom');
    } catch {
      count.value = count.value + 5;
    }
    {
      count.value = count.value + 1;
    }
  }));

  insert(target, p);
  insert(target, document.createTextNode('\n\n'));
  insert(target, button);
}
