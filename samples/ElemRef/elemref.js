/**
 * ElemRef — hand-written Filament answer key for baseline/ElemRef.Blazor/App.razor.
 *
 * THE POINT: @ref. Decision 132.
 *
 * Blazor DOM contract (same shape Blazor renders <App> into #app):
 *
 *   <div id="wrap">
 *     <input id="box">
 *     <button id="go">go</button>
 *   </div>
 *
 * Clicking #go focuses #box, so document.activeElement.id becomes "box". That is the measurement
 * (BENCH n°51).
 *
 * LOOK FOR THE ElementReference IN THIS FILE. There isn't one, and there is no line that assigns
 * anything to `box` either. Blazor needs ElementReference to be a real object because it carries an
 * opaque id across the .NET/JS boundary so that a JS call can find the node again. This module never
 * crosses that boundary: it IS JS, and it already holds the node. So `@ref="box"` reduces to a
 * NAMING decision — the element is emitted into `const box` instead of `const _el1` — and the
 * reference costs nothing at all.
 *
 * WHAT IS DELIBERATELY NOT HERE. `ElementReference.Id` is not mapped. In Blazor it is a
 * framework-internal GUID with no DOM meaning, so any value emitted for it would be invented rather
 * than translated. FocusAsync() is the reference's only faithful surface, and it is `.focus()`.
 *
 * THE await IS KEPT. `await box.FocusAsync()` compiles to `await box.focus()` rather than a bare
 * call: awaiting a non-promise is the same program, and keeping it preserves the ordering the C#
 * states instead of relying on focus() happening to be synchronous.
 */

import { listen, insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const wrap = document.createElement('div');
  wrap.id = 'wrap';

  const box = document.createElement('input');
  box.id = 'box';
  insert(wrap, box);

  const goBtn = document.createElement('button');
  goBtn.id = 'go';
  insert(goBtn, document.createTextNode('go'));
  insert(wrap, goBtn);

  listen(goBtn, 'click', async () => {
    await box.focus();
  });

  insert(target, wrap);
}
