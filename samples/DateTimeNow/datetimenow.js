/**
 * DateTimeNow — hand-written Filament answer key for baseline/DateTimeNow.Blazor/App.razor.
 *
 * DateTime.UtcNow -> the wall clock as BigInt ticks (decision 145), the FIRST admitted
 * non-deterministic value. The ticks model (decision 115) already represents every DateTime as a
 * BigInt of 100ns intervals since 0001-01-01; what was refused was the SOURCE. The clock is the
 * same clock on both sides: __dtUtcNow() = epoch offset (ticks at 1970-01-01) + Date.now() scaled
 * ms->ticks -- faithful to C#'s value within clock resolution. `.Ticks` is the IDENTITY on that
 * representation (a long, decision 112), so the display is the bare BigInt -- setText already
 * coerces it to its exact decimal string; __dtStr is NOT on this path.
 *
 * `stamp` starts default(DateTime) = 0n ticks -> #out renders "0" (byte-equal to Blazor). It is
 * read by @stamp.Ticks AND assigned by Snap -> a signal. Snap is single-use -> inlined. The
 * helper is EMITTED into the module (closed runtime, decision 62).
 */

import { signal, effect, setText, listen, insert } from '../../src/filament-runtime/src/index.ts';

// -- wall clock: C# DateTime.UtcNow as BigInt ticks (decision 145) -----------
function __dtUtcNow() {
  return 621355968000000000n + BigInt(Date.now()) * 10000n;
}

export function mount(target) {
  const stamp = signal(0n);

  const p = document.createElement('p');
  const span = document.createElement('span');
  span.id = 'out';
  const t = document.createTextNode('');
  insert(span, t);
  insert(p, span);

  const button = document.createElement('button');
  button.id = 'snap';
  insert(button, document.createTextNode('snap'));

  effect(() => setText(t, stamp.value));

  listen(button, 'click', () => {
    stamp.value = __dtUtcNow();
  });

  insert(target, p);
  insert(target, document.createTextNode('\n\n'));
  insert(target, button);
}
