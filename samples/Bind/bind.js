/**
 * Bind — hand-written Filament answer key for baseline/Bind.Blazor/App.razor.
 *
 * Two-way binding (@bind, decision #104). Razor lowers `@bind="text"` on <input> to a synthesised
 * value=/onchange pair (BindConverter.FormatValue + CreateBinder). For a STRING field the converter is
 * identity, so it compiles to:
 *   - a reactive value-PROPERTY effect: effect(() => { input.value = text.value; })  (the .value property,
 *     not the attribute, is what an input displays after user interaction);
 *   - a change listener that writes the signal: listen(input, 'change', e => text.value = e.target.value).
 * `text` is a string SIGNAL (read by @text AND @bind, assigned in Set). Both directions are exercised:
 * Set() -> text.value -> the effect updates input.value; a change event -> text.value -> #echo updates.
 *
 * Set() writes text once -> no batch (decision 68); single-use -> inlined.
 */

import { signal, effect, setText, listen, insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const text = signal('hi');

  const input = document.createElement('input');
  input.id = 'box';

  const echo = document.createElement('span');
  echo.id = 'echo';
  const echoTx = document.createTextNode('');
  insert(echo, echoTx);

  const setBtn = document.createElement('button');
  setBtn.id = 'set';
  insert(setBtn, document.createTextNode('Set'));

  effect(() => { input.value = text.value; });
  effect(() => setText(echoTx, text.value));

  listen(input, 'change', (e) => { text.value = e.target.value; });
  listen(setBtn, 'click', () => { text.value = 'world'; });

  insert(target, input);
  insert(target, document.createTextNode('\n\n'));
  insert(target, echo);
  insert(target, document.createTextNode('\n\n'));
  insert(target, setBtn);
}
