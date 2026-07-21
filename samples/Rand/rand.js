/**
 * Rand — hand-written Filament answer key for baseline/Rand.Blazor/App.razor.
 *
 * System.Random -> the emitted __rnd factory (decision 146), TWO regimes behind one interface:
 *
 *   SEEDED `new Random(42)` -> __rnd(42): the exact .NET Knuth-subtractive generator
 *   (Net5CompatSeedImpl -- MSEED 161803398, 55-slot table, 4 shuffle rounds, inext/inextp 0/21,
 *   the ==MaxValue decrement, Sample scale 1/int.MaxValue, the two-sample large-range path).
 *   Deterministic, so the ORACLE's byte-equality against Blazor proves the sequence against the
 *   real BCL: Next(1,7) draws 5, 1, 1 (sums 5 -> 6 -> 7), values read off `dotnet run`.
 *
 *   UNSEEDED `Random.Shared` -> ONE module-level __rnd(null): Math.random behind the same
 *   interface. C#'s unseeded xoshiro is not reproducible across runs either; range and
 *   distribution are the observable contract (asserted [0, 10) by the harness).
 *
 * `rng` is held in a const (never read by the template, never reassigned -> not a signal); `sum`
 * and `last` are signals. Method mapping: Next() -> .next(), Next(max) -> .nextTo(max),
 * Next(min, max) -> .nextIn(min, max), NextDouble() -> .nextDouble().
 */

import { signal, effect, setText, listen, insert } from '../../src/filament-runtime/src/index.ts';

// -- Random: C# System.Random -- seeded = the exact .NET Knuth-subtractive sequence --
function __rnd(seed) {
  if (seed === null) {
    return {
      next: () => Math.floor(Math.random() * 2147483647),
      nextTo: (max) => Math.floor(Math.random() * max),
      nextIn: (min, max) => min + Math.floor(Math.random() * (max - min)),
      nextDouble: () => Math.random(),
    };
  }
  const arr = new Int32Array(56);
  let mj = 161803398 - (seed === -2147483648 ? 2147483647 : Math.abs(seed));
  arr[55] = mj;
  let mk = 1;
  for (let i = 1; i < 55; i++) {
    const ii = (21 * i) % 55;
    arr[ii] = mk;
    mk = mj - mk;
    if (mk < 0) mk += 2147483647;
    mj = arr[ii];
  }
  for (let k = 1; k < 5; k++) {
    for (let i = 1; i < 56; i++) {
      arr[i] -= arr[1 + (i + 30) % 55];
      if (arr[i] < 0) arr[i] += 2147483647;
    }
  }
  let inext = 0, inextp = 21;
  const sample = () => {
    if (++inext >= 56) inext = 1;
    if (++inextp >= 56) inextp = 1;
    let ret = arr[inext] - arr[inextp];
    if (ret === 2147483647) ret--;
    if (ret < 0) ret += 2147483647;
    arr[inext] = ret;
    return ret;
  };
  return {
    next: sample,
    nextTo: (max) => Math.trunc(sample() * (1 / 2147483647) * max),
    nextIn: (min, max) => {
      const range = max - min;
      if (range <= 2147483647) return min + Math.trunc(sample() * (1 / 2147483647) * range);
      let result = sample();
      if (sample() % 2 === 0) result = -result;
      return min + Math.trunc(((result + 2147483646) / 4294967293) * range);
    },
    nextDouble: () => sample() * (1 / 2147483647),
  };
}

// Random.Shared: ONE module-level unseeded instance -- the static-singleton lifetime C# gives it.
const __rndShared = __rnd(null);

export function mount(target) {
  const rng = __rnd(42);
  const sum = signal(0);
  const last = signal(-1);

  const sumP = document.createElement('p');
  const sumSpan = document.createElement('span');
  sumSpan.id = 'out';
  const sumText = document.createTextNode('');
  insert(sumSpan, sumText);
  insert(sumP, sumSpan);

  const pickP = document.createElement('p');
  const pickSpan = document.createElement('span');
  pickSpan.id = 'pick';
  const pickText = document.createTextNode('');
  insert(pickSpan, pickText);
  insert(pickP, pickSpan);

  const rollButton = document.createElement('button');
  rollButton.id = 'roll';
  insert(rollButton, document.createTextNode('roll'));

  const sharedButton = document.createElement('button');
  sharedButton.id = 'shared';
  insert(sharedButton, document.createTextNode('pick'));

  effect(() => setText(sumText, sum.value));
  effect(() => setText(pickText, last.value));

  listen(rollButton, 'click', () => {
    sum.value += rng.nextIn(1, 7);
  });
  listen(sharedButton, 'click', () => {
    last.value = __rndShared.nextTo(10);
  });

  insert(target, sumP);
  insert(target, document.createTextNode('\n'));
  insert(target, pickP);
  insert(target, document.createTextNode('\n\n'));
  insert(target, rollButton);
  insert(target, document.createTextNode('\n'));
  insert(target, sharedButton);
}
