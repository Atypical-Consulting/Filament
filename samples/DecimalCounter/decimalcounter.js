/**
 * DecimalCounter — hand-written Filament answer key for baseline/DecimalCounter.Blazor/App.razor.
 *
 * `decimal` -> a boxed { m: BigInt mantissa, s: scale } + the __dec helpers (decision 114). C#'s decimal is a
 * 128-bit base-10 type that TRACKS SCALE (1.10m keeps its trailing zero; it is mantissa 110, scale 2). JS has no
 * native decimal, so a decimal value is that pair, and arithmetic is exact base-10 on it: __decAdd aligns scales
 * and adds mantissas; __decStr renders the mantissa with the point at `scale`, preserving trailing zeros. These
 * match System.Decimal for +, -, *, comparison and display; division (28-digit rounding) is deferred, not emitted.
 *
 * `total` starts at 1.10m ({ m: 110n, s: 2 }) and each click adds 1.05m: 1.10 + 1.05 = 2.15, then 3.20 -- the
 * trailing zero PRESERVED and the base-10 sum exact (a double would give "1.1", then 3.2000000000000002).
 * `total` is read by @total AND assigned by the handler -> a signal. The __dec helpers are EMITTED (not runtime
 * exports), so a decimal stays generator-only: the runtime is untouched. DOM contract mirrors Counter.Blazor.
 *
 * NOTE ON NAMING: the mount() locals use the generator's own scheme (_el0/_tx0/…) rather than prettier names.
 * The canon gate's alpha-renaming has a documented limitation (L3) when object-literal keys appear inside helper
 * bodies (the __dec* returns), so aligning the local names lets canon verify the equivalence the snapshot and the
 * DOM-contract oracle also check. The helpers, the boxed { m, s } shapes and the arithmetic are written here
 * independently; only the throwaway local names match.
 */

import { signal, effect, setText, listen, insert } from '../../src/filament-runtime/src/index.ts';

// -- decimal: boxed { m, s } (BigInt mantissa + scale), exact base-10 -- C# System.Decimal --
function __decAdd(a, b) { const s = a.s > b.s ? a.s : b.s; return { m: a.m * 10n ** BigInt(s - a.s) + b.m * 10n ** BigInt(s - b.s), s: s }; }
function __decStr(d) {
  let neg = d.m < 0n, g = (neg ? -d.m : d.m).toString();
  if (d.s > 0) { while (g.length <= d.s) g = '0' + g; const i = g.length - d.s; g = g.slice(0, i) + '.' + g.slice(i); }
  return (neg ? '-' : '') + g;
}

export function mount(target) {
  const total = signal({ m: 110n, s: 2 });

  const _el0 = document.createElement('p');
  const _el1 = document.createElement('span');
  _el1.id = 'value';
  const _tx0 = document.createTextNode('');
  insert(_el1, _tx0);
  insert(_el0, _el1);
  const _el2 = document.createElement('button');
  _el2.id = 'add';
  insert(_el2, document.createTextNode('add'));

  effect(() => setText(_tx0, __decStr(total.value)));

  listen(_el2, 'click', () => {
    total.value = __decAdd(total.value, { m: 105n, s: 2 });
  });

  insert(target, _el0);
  insert(target, document.createTextNode('\n\n'));
  insert(target, _el2);
}
