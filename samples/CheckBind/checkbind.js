/**
 * CheckBind — hand-written Filament answer key for baseline/CheckBind.Blazor/App.razor.
 *
 * Checkbox two-way binding (@bind on a bool, decision 107). Razor lowers `@bind="on"` on a checkbox to a
 * synthesised `checked`=BindConverter.FormatValue(on) + onchange=CreateBinder(...). For a BOOL the
 * converter is the identity `.checked` property, so it compiles to a checked-PROPERTY effect + a change
 * listener that writes the signal from e.target.checked -- no parsing, no parse-failure edge.
 *
 * `on` is a bool SIGNAL: read by the #status class ternary (a faithful string; a raw @on would render
 * "false" where C# renders "False") AND assigned by @bind + Set. Set writes once -> no batch (#68).
 */

import { signal, effect, setAttr, listen, insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const on = signal(false);

  const box = document.createElement('input');
  box.id = 'box';
  setAttr(box, 'type', 'checkbox');

  const status = document.createElement('span');
  status.id = 'status';
  insert(status, document.createTextNode('status'));

  const set = document.createElement('button');
  set.id = 'set';
  insert(set, document.createTextNode('Set'));

  effect(() => { box.checked = on.value; });
  effect(() => setAttr(status, 'class', on.value ? 'on' : 'off'));

  listen(box, 'change', (e) => { on.value = e.target.checked; });
  listen(set, 'click', () => {
    on.value = true;
  });

  insert(target, box);
  insert(target, document.createTextNode('\n\n'));
  insert(target, status);
  insert(target, document.createTextNode('\n\n'));
  insert(target, set);
}
