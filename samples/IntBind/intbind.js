/**
 * IntBind — hand-written Filament answer key for baseline/IntBind.Blazor/App.razor.
 *
 * Integer two-way binding (@bind on an int, decision 108). Unlike string/bool, this PARSES: the change
 * handler mirrors int.TryParse (invariant, NumberStyles.Integer) -- a regex for the accepted shape, an
 * int32-range check, and revert-on-invalid so an unparseable/overflowing entry KEEPS the field and
 * re-renders the old value (exactly Blazor's BindConverter). Value formats via String(count.value); @count
 * (int) renders faithfully as text. `count` is an int SIGNAL (read by @count, assigned by @bind + Set).
 */

import { signal, effect, setText, listen, insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const count = signal(0);

  const box = document.createElement('input');
  box.id = 'box';

  const echo = document.createElement('span');
  echo.id = 'echo';
  const t = document.createTextNode('');
  insert(echo, t);

  const set = document.createElement('button');
  set.id = 'set';
  insert(set, document.createTextNode('Set'));

  effect(() => { box.value = String(count.value); });
  effect(() => setText(t, count.value));

  listen(box, 'change', (e) => {
    const _s = e.target.value;
    if (/^\s*[+-]?\d+\s*$/.test(_s)) {
      const _n = parseInt(_s, 10);
      if (_n >= -2147483648 && _n <= 2147483647) { count.value = _n; return; }
    }
    box.value = String(count.value);
  });
  listen(set, 'click', () => {
    count.value = 42;
  });

  insert(target, box);
  insert(target, document.createTextNode('\n\n'));
  insert(target, echo);
  insert(target, document.createTextNode('\n\n'));
  insert(target, set);
}
