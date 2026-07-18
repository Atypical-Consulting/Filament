/**
 * BoundCompose — hand-written Filament answer key for baseline/BoundCompose.Blazor/App.razor.
 *
 * THE POINT: a BOUND scalar component parameter. `<Display Value="@count" />` passes the parent's
 * reactive `count` to a composed child; the child's `@Value` is a LIVE binding on that signal. This
 * is the first deferred #88 sub-slice, closed by decision 90 — the "parent->child reactive plumbing"
 * #88 said was not implemented.
 *
 * Blazor DOM contract (same shape Blazor renders <App> into #app):
 *
 *   <div id="wrap">
 *     <button id="inc">inc</button>
 *     <span id="out">0</span>          <!-- tracks the parent's count -->
 *   </div>
 *
 * The child's <span id="out"> is INLINED into the parent's #wrap (compile-time expansion, #88): no
 * runtime component instance, no wrapper. Clicking #inc runs `count++`, and the child's #out updates
 * from "0" to "1" to ... — the reactive plumbing crossing the composition boundary. That update is
 * the measurement (BENCH n°12): a generator that folded the value at mount would leave #out at "0".
 *
 * WHY IT WORKS WITH NO PROP-PASSING. Because the child inlines into the PARENT's mount() scope, the
 * child's `@Value` compiles to `effect(() => setText(_tx, count.value))` — a live read of the PARENT's
 * own `count` signal, directly in scope. `count` lifts to a signal (decision 22's conjunction) because
 * it is BOTH assigned outside construction (`count++`) AND read by the template — and the bound
 * parameter expression IS that read (harvested into the parent's compilation, decision 90).
 *
 * THE HANDLER. Inc() performs exactly one write (`count++`), so per decision 68's batch rule (batch
 * iff more than one write to coalesce) it gets NO batch(), same as Counter's Increment(). Inc is named
 * by exactly one @onclick and called nowhere else, so decision 68's single-use inlining folds its body
 * straight into the click handler. This key mirrors the generator's actual emission.
 */

import { signal, effect, setText, listen, insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const count = signal(0);

  const wrap = document.createElement('div');
  wrap.id = 'wrap';

  const incBtn = document.createElement('button');
  incBtn.id = 'inc';
  insert(incBtn, document.createTextNode('inc'));
  insert(wrap, incBtn);

  const out = document.createElement('span');
  out.id = 'out';
  const tx = document.createTextNode('');
  insert(out, tx);
  insert(wrap, out);

  effect(() => setText(tx, count.value));

  listen(incBtn, 'click', () => {
    count.value++;
  });

  insert(target, wrap);
}
