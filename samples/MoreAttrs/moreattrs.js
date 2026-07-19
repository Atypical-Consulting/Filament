/**
 * MoreAttrs — hand-written Filament answer key for baseline/MoreAttrs.Blazor/App.razor.
 *
 * The attribute-allowlist widening (decision #103): a boolean `hidden` (present/absent, like `disabled`)
 * plus reactive string `role`, `style`, and `data-count` (admitted by the `data-*` prefix). Blazor treats
 * `hidden` (a bool) as a present/absent boolean attribute and role/style/data-count (strings) as string
 * attributes -- read from App_razor.g.cs (decision 64): AddAttribute("hidden", hid) with hid:bool, and
 * AddAttribute("role"/"style"/"data-count", <string>).
 *
 * `hidden` -> `setAttr(el, 'hidden', hid.value ? '' : null)` (the boolean present/absent ternary, #95).
 * role/style/data-count -> `setAttr(el, name, x.value)` (the composed string emission, #94/#97). All four
 * are reactive (read by the template, assigned in Toggle) -> effects. Toggle writes four fields -> batch().
 */

import { signal, effect, batch, setAttr, listen, insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const hid = signal(true);
  const r = signal('alert');
  const st = signal('color: red');
  const d = signal('1');

  const s = document.createElement('span');
  s.id = 's';
  insert(s, document.createTextNode('x'));

  const button = document.createElement('button');
  button.id = 'toggle';
  insert(button, document.createTextNode('Toggle'));

  effect(() => setAttr(s, 'hidden', hid.value ? '' : null));
  effect(() => setAttr(s, 'role', r.value));
  effect(() => setAttr(s, 'style', st.value));
  effect(() => setAttr(s, 'data-count', d.value));

  listen(button, 'click', () => batch(() => {
    hid.value = false;
    r.value = 'status';
    st.value = 'color: blue';
    d.value = '2';
  }));

  insert(target, s);
  insert(target, document.createTextNode('\n\n'));
  insert(target, button);
}
