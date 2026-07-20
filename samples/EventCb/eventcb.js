/**
 * EventCb — hand-written Filament answer key for baseline/EventCb.Blazor/App.razor.
 *
 * THE POINT: the OTHER half of composition. #88/#90 pushed data DOWN into a child as a bound
 * parameter; this pushes an EVENT back UP. `<Bumper OnBump="@Inc" />` hands the child one of the
 * parent's own methods; the child's `@onclick="OnBump"` raises it, and the PARENT's state changes.
 * Decision 130 — the highest-value gap ADR 0002's Bucket B audit identified.
 *
 * Blazor DOM contract (same shape Blazor renders <App> into #app):
 *
 *   <div id="wrap">
 *     <span id="out">0</span>          <!-- the PARENT's count -->
 *     <button id="bump">bump</button>  <!-- lives inside the CHILD -->
 *   </div>
 *
 * Clicking #bump — a button the child owns — moves #out from "0" to "1" to "2". That upward
 * crossing of the composition boundary is the measurement (BENCH n°49).
 *
 * WHY THERE IS NO EventCallback IN THIS FILE. Look for it: there is no delegate, no subscription
 * list, no `.InvokeAsync`, no child instance to hold one. Because the child inlines into the
 * PARENT's mount() scope (#88), an EventCallback parameter is not a value that exists at runtime —
 * it is an ALIAS the compiler resolves and then erases. `OnBump` resolves, at the composition site,
 * to the parent's `Inc`, and what ships is the listener the parent would have written itself.
 *
 * That is the whole claim this slice tests: Blazor needs EventCallback to be a runtime object
 * because it discovers the binding at runtime. Filament knows it at build time, so the feature
 * costs zero bytes — the runtime is untouched (still 1,943 B) and this module gains nothing but
 * the one listener it would have had anyway.
 *
 * THE HANDLER. Because the alias resolves to the parent's `Inc` BEFORE the handler is recorded,
 * every downstream rule fires on the parent's own tables, exactly as in Counter and BoundCompose:
 * Inc performs one write (`count++`), so decision 68's batch rule gives it NO batch(); it is named
 * by exactly one @onclick and called nowhere else, so decision 68's single-use inlining folds its
 * body straight into the click handler. Composition changes none of that, which is the point.
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

  const bumpBtn = document.createElement('button');
  bumpBtn.id = 'bump';
  insert(bumpBtn, document.createTextNode('bump'));
  insert(wrap, bumpBtn);

  effect(() => setText(tx, count.value));

  listen(bumpBtn, 'click', () => {
    count.value++;
  });

  insert(target, wrap);
}
