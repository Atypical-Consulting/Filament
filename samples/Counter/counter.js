/**
 * Counter — hand-written Filament app. Phase 1 reference.
 *
 * This file is the ANSWER KEY. Phase 2's generator consumes baseline's
 * Counter.Blazor/App.razor and its emitted JS is snapshot-tested against what is
 * written here, so every line below is written the way a COMPILER would emit it,
 * not the way a human would prefer it.
 *
 * The source it corresponds to (baseline/Counter.Blazor/App.razor):
 *
 *     <h1 id="title">Counter</h1>
 *     <p>Current count: <span id="counter-value">@currentCount</span></p>
 *     <button id="increment" @onclick="Increment">Click me</button>
 *
 *     @code {
 *         private int currentCount = 0;
 *         private void Increment() { currentCount++; }
 *     }
 *
 * THE SHAPE, and why it is this shape:
 *
 *   create()   builds the element tree ONCE, imperatively. Static structure is
 *              static code — there is no template string to parse at runtime, no
 *              vdom to allocate, and nothing to diff. A `<h1 id="title">Counter</h1>`
 *              whose content never varies compiles to createElement + a Text node
 *              and is then never looked at again.
 *
 *   effect()   one per BINDING POINT. There is exactly one here (@currentCount),
 *              so there is exactly one effect. It closes over `t` — the Text node
 *              inside #counter-value — at create time, which is what makes an
 *              increment cost one `t.data = v` and nothing else. (C3.)
 *
 * WHY THE INTERPOLATION GETS ITS OWN TEXT NODE.
 * `<p>Current count: <span id="counter-value">@currentCount</span></p>` has a
 * static prefix and a dynamic span. The span's child is a Text node created once
 * and handed to setText forever. Writing `span.textContent = v` instead would
 * destroy and rebuild the span's children on every increment — 2 DOM writes
 * (remove + add) where the contract allows 1, and C3 would fail on markup that
 * looks identical.
 *
 * WHAT IS DELIBERATELY ABSENT: no diffing, no reconciliation, no render tree, no
 * component instance, no lifecycle. An increment is: flag walk over one edge,
 * one queue pop, one character-data write. That is the entire thesis.
 */

import { signal, effect, setText, listen, insert } from '../../src/filament-runtime/src/index.ts';

/**
 * Compiled render for App.razor.
 *
 * @param {Node} target the mount point (#app), standing in for
 *   Program.cs's `builder.RootComponents.Add<App>("#app")`.
 */
export function mount(target) {
  // -- @code { private int currentCount = 0; } -------------------------------
  // A private field READ by the template is reactive state; the compiler lifts it
  // to a Signal. `currentCount++` in C# maps to `currentCount.value++` — one node
  // in, one node out, no syntactic desugaring. (See core.ts's header on why the
  // public surface is `.value` and not `currentCount()`.)
  const currentCount = signal(0);

  // -- create(): the static tree ---------------------------------------------

  // <h1 id="title">Counter</h1>
  const h1 = document.createElement('h1');
  h1.id = 'title';
  insert(h1, document.createTextNode('Counter'));

  // <p>Current count: <span id="counter-value">@currentCount</span></p>
  const p = document.createElement('p');
  insert(p, document.createTextNode('Current count: '));
  const span = document.createElement('span');
  span.id = 'counter-value';
  // THE binding point. Created empty and never replaced; the effect below owns
  // its `.data` from here on.
  const t = document.createTextNode('');
  insert(span, t);
  insert(p, span);

  // <button id="increment" @onclick="Increment">Click me</button>
  const button = document.createElement('button');
  button.id = 'increment';
  insert(button, document.createTextNode('Click me'));

  // -- the binding: @currentCount --------------------------------------------
  // Runs once immediately (writing "0", which is why the initial DOM already
  // satisfies the contract without a separate initialisation path), then exactly
  // once per real change to currentCount.
  effect(() => setText(t, currentCount.value));

  // -- the handler: @onclick="Increment" -------------------------------------
  // `private void Increment() { currentCount++; }`. No batch(): the body performs
  // exactly one write, so there is nothing to coalesce and a batch would only add
  // a try/finally. The write flushes synchronously inside dispatchEvent, so the
  // harness's MutationObserver observes a DOM that is already final.
  listen(button, 'click', () => {
    currentCount.value++;
  });

  // -- attach ----------------------------------------------------------------
  // Last, and on purpose: the effect's first run above wrote into a DETACHED
  // tree, so it produced no MutationRecord. Everything the observer sees at
  // startup is these three inserts, and every increment thereafter is the single
  // characterData write. Attaching first would work identically but would make
  // the create-time write and the update-time write indistinguishable in a trace.
  insert(target, h1);
  insert(target, p);
  insert(target, button);
}
