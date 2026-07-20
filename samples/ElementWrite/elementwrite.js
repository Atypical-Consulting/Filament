/**
 * ElementWrite — hand-written Filament answer key for baseline/ElementWrite.Blazor/App.razor.
 *
 * THE POINT: mutable ELEMENT writes on a reactive array + Dictionary (decision 127), as COPY-ON-WRITE. The runtime
 * signal fires only on a NEW reference -- its setter guards with Object.is -- so a plain in-place write
 * (`xs.value[1] = v`) would change the array the display already holds WITHOUT firing any effect, leaving every
 * `@xs[i]` stale. That silent-wrong render is exactly why #117/#118 refused element writes. The fix is to REPLACE
 * the reference:
 *
 *   xs[1] = xs[1] + 5             ->  xs.value = xs.value.with(1, xs.value[1] + 5)
 *   scores["b"] = scores["b"]+100 ->  scores.value = new Map(scores.value).set('b', scores.value.get('b') + 100)
 *
 * `.with(i, v)` returns a copy of the array with element i replaced; `new Map(old).set(k, v)` returns a fresh Map
 * with the entry replaced. Each is a new reference, so the signal fires and the effects reading it re-run. Both
 * fields are read by the template (@xs[1] / @scores["b"]) AND written in Go, so both lift to signals (the array/
 * Dict-as-signal of #124/#125); a field nothing displays would have no observer and its write stays refused.
 *
 * Blazor DOM contract (same shape Blazor renders <App> into #app):
 *
 *   <p>Arr: <span id="a">20</span>, Map: <span id="m">2</span></p>
 *   <button id="go">go</button>
 *
 * xs starts [10,20,30] so @xs[1] = 20; scores { a=1, b=2 } so @scores["b"] = 2. #go does xs[1] += 5 (-> 25) and
 * scores["b"] += 100 (-> 102), so #a goes "20" -> "25" and #m "2" -> "102". The click IS the measurement (BENCH n°46).
 *
 * Go writes TWO signals, so per decision 68 its body is wrapped in batch() -- the two copy-on-write reassignments
 * collapse into one flush. No runtime primitive is added -- Array.with / Map / .set are JS builtins; signal/effect/
 * batch/setText/listen/insert are Rows' runtime.
 */

import { signal, effect, batch, setText, listen, insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const xs = signal([10, 20, 30]);
  const scores = signal(new Map([['a', 1], ['b', 2]]));

  const p = document.createElement('p');
  insert(p, document.createTextNode('Arr: '));
  const aSpan = document.createElement('span');
  aSpan.id = 'a';
  const txA = document.createTextNode('');
  insert(aSpan, txA);
  insert(p, aSpan);
  insert(p, document.createTextNode(', Map: '));
  const mSpan = document.createElement('span');
  mSpan.id = 'm';
  const txM = document.createTextNode('');
  insert(mSpan, txM);
  insert(p, mSpan);
  const goBtn = document.createElement('button');
  goBtn.id = 'go';
  insert(goBtn, document.createTextNode('go'));

  effect(() => setText(txA, xs.value[1]));
  effect(() => setText(txM, scores.value.get('b')));

  listen(goBtn, 'click', () => batch(() => {
    xs.value = xs.value.with(1, xs.value[1] + 5);
    scores.value = new Map(scores.value).set('b', scores.value.get('b') + 100);
  }));

  insert(target, p);
  insert(target, document.createTextNode('\n\n'));
  insert(target, goBtn);
}
