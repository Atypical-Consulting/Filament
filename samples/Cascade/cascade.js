/**
 * Cascade — hand-written Filament answer key for baseline/Cascade.Blazor/App.razor.
 *
 * THE POINT: [CascadingParameter]. Decision 134.
 *
 * Blazor DOM contract (same shape Blazor renders <App> into #app):
 *
 *   <div id="wrap">
 *     <span id="depth">1</span>        <!-- the CHILD, rendering a value nobody passed it -->
 *     <button id="inc">inc</button>
 *   </div>
 *
 * Clicking #inc moves #depth "1" -> "2" -> "3". A cascade that resolved once at mount would sit at
 * "1" forever, so that update is the measurement (BENCH n°53).
 *
 * LOOK FOR THE CASCADE. There is no context object, no dictionary keyed by type, no subscription,
 * and no <CascadingValue> wrapper element — the component emits no DOM of its own, and neither does
 * anything standing in for it. Blazor needs a cascading VALUE object because a descendant may be
 * arbitrarily deep and is discovered at render time; here the whole composition inlines into ONE
 * mount(), so an ancestor's expression is quite literally in scope at the point the descendant is
 * emitted. A cascade IS lexical scope, and it costs nothing.
 *
 * MATCHED BY TYPE, exactly as Blazor matches it: <CascadingValue Value="@level"> puts an `int` in
 * scope, and `[CascadingParameter] public int Level` asks for an `int`. The child declares no
 * [Parameter] and the parent passes no attribute — that is what makes it a cascade rather than the
 * bound-parameter plumbing of #90.
 *
 * REACTIVITY CARRIES ACROSS. `level` is a signal (read by the template, assigned by Inc), and the
 * cascaded expression is that same signal read, so the child's binding is a live effect on it.
 */

import { signal, effect, setText, listen, insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const level = signal(1);

  const wrap = document.createElement('div');
  wrap.id = 'wrap';

  const depth = document.createElement('span');
  depth.id = 'depth';
  const tx = document.createTextNode('');
  insert(depth, tx);
  insert(wrap, depth);

  const incBtn = document.createElement('button');
  incBtn.id = 'inc';
  insert(incBtn, document.createTextNode('inc'));
  insert(wrap, incBtn);

  effect(() => setText(tx, level.value));

  listen(incBtn, 'click', () => {
    level.value++;
  });

  insert(target, wrap);
}
