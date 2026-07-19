/**
 * DateTimeCounter — hand-written Filament answer key for baseline/DateTimeCounter.Blazor/App.razor.
 *
 * `DateTime` -> a BigInt count of ticks (100ns since 0001-01-01) + the __dtStr formatter (decision 115). C#'s
 * DateTime IS a 64-bit tick count, and a BigInt holds it exactly. Construction is computed from the CONSTANT
 * arguments at generate-time (new DateTime(2026,7,20) = 639201024000000000 ticks); .AddDays(5) adds 5·TicksPerDay
 * (5·864000000000 = 4320000000000); comparison is a plain BigInt compare; and the default display renders C#'s
 * "MM/dd/yyyy HH:mm:ss" (invariant) by converting ticks -> ms since the Unix epoch and formatting a UTC Date.
 *
 * `when` starts at new DateTime(2026,7,20) and each click adds 5 days: 07/20 -> 07/25 -> 07/30, calendar-correct.
 * `when` is read by @when AND assigned by the handler -> a signal. __dtStr is EMITTED (not a runtime export), so
 * a DateTime stays generator-only: the runtime is untouched. DOM contract mirrors Counter.Blazor.
 *
 * NOTE ON NAMING: mount()'s locals use the generator's scheme (_el0/_tx0/…) rather than prettier names, for the
 * same canon-L3 reason the decimal answer key does -- BigInt literals interact with canon's alpha-renaming.
 */

import { signal, effect, setText, listen, insert } from '../../src/filament-runtime/src/index.ts';

// -- DateTime display: ticks (BigInt) -> C#'s default "MM/dd/yyyy HH:mm:ss" (invariant) --
function __dtStr(t) {
  const d = new Date(Number((t - 621355968000000000n) / 10000n));
  const p = (n, w = 2) => String(n).padStart(w, '0');
  return p(d.getUTCMonth() + 1) + '/' + p(d.getUTCDate()) + '/' + p(d.getUTCFullYear(), 4) +
    ' ' + p(d.getUTCHours()) + ':' + p(d.getUTCMinutes()) + ':' + p(d.getUTCSeconds());
}

export function mount(target) {
  const when = signal(639201024000000000n);

  const _el0 = document.createElement('p');
  const _el1 = document.createElement('span');
  _el1.id = 'value';
  const _tx0 = document.createTextNode('');
  insert(_el1, _tx0);
  insert(_el0, _el1);
  const _el2 = document.createElement('button');
  _el2.id = 'add';
  insert(_el2, document.createTextNode('add'));

  effect(() => setText(_tx0, __dtStr(when.value)));

  listen(_el2, 'click', () => {
    when.value = (when.value + 4320000000000n);
  });

  insert(target, _el0);
  insert(target, document.createTextNode('\n\n'));
  insert(target, _el2);
}
