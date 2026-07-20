/**
 * Inherits — hand-written Filament answer key for baseline/Inherits.Blazor/App.razor.
 *
 * THE POINT: @inherits. Decision 136.
 *
 * Blazor DOM contract (same shape Blazor renders <App> into #app):
 *
 *   <div id="wrap">
 *     <span id="out">0</span>          <!-- the BASE's field -->
 *     <button id="inc">inc</button>    <!-- calls the BASE's method -->
 *   </div>
 *
 * Clicking #inc moves #out "0" -> "1" -> "2". That is the measurement (BENCH n°55): the inherited
 * field must be a real signal and the inherited method must really write it.
 *
 * LOOK FOR THE BASE CLASS. There isn't one — no prototype, no vtable, no `this`, and nothing named
 * CounterBase. The base's members are merged into the derived component's compilation BEFORE state
 * lifting runs, so `count` is lifted exactly as though it had been written in this file. That is the
 * whole mapping: inheritance is a COMPILE-TIME question about where a member's text lives, and this
 * compiler answers it and then has nothing left to emit.
 *
 * WHY THE BASE'S MARKUP IS NOT HERE, AND WHY THAT IS FAITHFUL. A derived component overrides
 * BuildRenderTree, so Blazor renders the DERIVED markup and never the base's. Discarding the base's
 * markup is therefore what Blazor does, not a shortcut taken here.
 *
 * WHY THE BASE MUST BE A SIBLING .razor. It is the only C# this compiler ever reads. A base in a .cs
 * file would be invisible to it, and silently inheriting nothing would produce a module missing
 * exactly the state the author put in the base — so that case is refused rather than half-compiled.
 */

import { signal, effect, setText, listen, insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const count = signal(0);

  const wrap = document.createElement('div');
  wrap.id = 'wrap';

  const out = document.createElement('span');
  out.id = 'out';
  const tx = document.createTextNode('');
  insert(out, tx);
  insert(wrap, out);

  const incBtn = document.createElement('button');
  incBtn.id = 'inc';
  insert(incBtn, document.createTextNode('inc'));
  insert(wrap, incBtn);

  effect(() => setText(tx, count.value));

  listen(incBtn, 'click', () => {
    count.value++;
  });

  insert(target, wrap);
}
