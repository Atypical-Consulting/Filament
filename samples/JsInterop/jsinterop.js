/**
 * JsInterop — hand-written Filament answer key for baseline/JsInterop.Blazor/App.razor.
 *
 * THE POINT: JS interop, and the one honest form of @inject. Decision 133.
 *
 * Blazor DOM contract (same shape Blazor renders <App> into #app):
 *
 *   <div id="wrap">
 *     <p id="out"></p>                 <!-- empty until #go -->
 *     <button id="go">go</button>
 *   </div>
 *
 * Clicking #go writes a value into localStorage and reads it back, so #out becomes "hello".
 * That round trip THROUGH the browser is the measurement (BENCH n°52).
 *
 * LOOK FOR THE BRIDGE. There is no IJSRuntime, no InvokeVoidAsync, no marshalling, no identifier
 * resolution — and, more to the point, no injected anything. Blazor needs IJSRuntime to be a real
 * service because calling JavaScript from .NET means crossing a boundary: it resolves the dotted
 * identifier against the browser's global scope at RUNTIME and serialises the arguments across.
 * This module is already on the other side. So the identifier is resolved at COMPILE time — into
 * the very same dotted path, which is legal JS exactly as written — and the arguments are the
 * translated expressions themselves. The bridge is not implemented; it is ERASED.
 *
 * That erasure is why this closes two spec 3 items at once, and it is also why it closes them
 * NARROWLY. @inject is admitted for IJSRuntime alone, because IJSRuntime is the one service with a
 * compile-time meaning. A general DI container resolves an implementation at runtime and there is
 * nothing here to ask; a service of your own lives in a .cs file this compiler never reads. Those
 * are different questions, and they are refused rather than approximated.
 *
 * ONLY BUILT-IN BROWSER APIS. A hand-written helper in index.html would have to be added to both
 * shells, and the measurement would then be of the shells rather than of the interop.
 *
 * THE awaits ARE KEPT. localStorage is synchronous, so they resolve immediately; keeping them
 * preserves the ordering the C# states instead of betting on that.
 */

import { signal, effect, setText, listen, insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const val = signal('');

  const wrap = document.createElement('div');
  wrap.id = 'wrap';

  const out = document.createElement('p');
  out.id = 'out';
  const tx = document.createTextNode('');
  insert(out, tx);
  insert(wrap, out);

  const goBtn = document.createElement('button');
  goBtn.id = 'go';
  insert(goBtn, document.createTextNode('go'));
  insert(wrap, goBtn);

  effect(() => setText(tx, val.value));

  listen(goBtn, 'click', async () => {
    await localStorage.setItem('fil', 'hello');
    val.value = await localStorage.getItem('fil');
  });

  insert(target, wrap);
}
