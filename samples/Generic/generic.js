/**
 * Generic — hand-written Filament answer key for baseline/Generic.Blazor/App.razor.
 *
 * THE POINT: a generic component (@typeparam). Decision 135.
 *
 * Blazor DOM contract (same shape Blazor renders <App> into #app):
 *
 *   <div id="wrap">
 *     <span id="out">1</span>          <!-- the child, rendering a value it received as a T -->
 *     <button id="inc">inc</button>
 *   </div>
 *
 * Clicking #inc moves #out "1" -> "2" -> "3". That is the measurement (BENCH n°54), and it is the
 * part worth measuring: an erased generic must still be a LIVE binding, not a folded constant.
 *
 * LOOK FOR THE TYPE ARGUMENT. There isn't one, and there is no second copy of the child either.
 * Generics ERASE, and they erase for free — for two reasons that compound:
 *
 *   1. A type parameter is a COMPILE-TIME constraint on what may be substituted. This compiler
 *      resolves every composition at compile time, into a scope where the child's @Value is simply
 *      the parent's own translated expression (`count.value`). JavaScript has no type to carry, so
 *      after that substitution there is nothing left for T to have meant.
 *
 *   2. There is no monomorphisation to do. A generic in C# or Rust needs one instantiation per type
 *      argument; here the child is INLINED at each use site, so each site already has its own copy
 *      by construction. Filament pays the cost that makes monomorphisation unnecessary anyway.
 *
 * ADMITTED ONLY WHERE THE PARENT'S EXPRESSION IS ALREADY TYPE-CORRECT — i.e. a reactively bound
 * parameter, decision 90's exemption, for the same reason: `count.value` is a number because the
 * parent's C# said so. A statically folded T would splice a JS string wherever T was substituted.
 */

import { signal, effect, setText, listen, insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const count = signal(1);

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
