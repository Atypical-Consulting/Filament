/**
 * Divide — hand-written Filament app. The ANSWER KEY for baseline/Divide.Blazor/App.razor.
 *
 * Every line is written the way the COMPILER emits it, not the way a human would prefer, and it
 * is transcribed from the generator's own faithful output (readable local names here; the
 * name-blind canon gate treats h1/p/span/t/button as alpha-equivalent to the generator's _elN).
 *
 * WHY THIS APP EXISTS: value = value / 2.0 is DOUBLE division. C#'s double `/` and JS's `/` are
 * the same IEEE-754 op, so it maps to `/` verbatim — unlike int/int, which truncates in C# and
 * would be a silently wrong number in JS (spec 10) and is refused. 7.0 / 2.0 = 3.5, a value that
 * integer division (== 3) could never produce: that divergence is what the DOM-contract oracle
 * measures baseline/Divide.Blazor against. The `2.0` literal normalises to `2` — JS `/` is float
 * division either way.
 *
 * THE WHITESPACE TEXT NODES are the blank lines App.razor has between its three siblings; Blazor
 * ships them as "\n\n" text nodes and so does the generator, so this file must too (see counter.js).
 */

import { signal, effect, setText, listen, insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  // @code { private double value = 7.0; } — read by the template, assigned in Halve() -> lifted.
  const value = signal(7);

  // <h1 id="title">Divide</h1>
  const h1 = document.createElement('h1');
  h1.id = 'title';
  insert(h1, document.createTextNode('Divide'));

  // <p>Value: <span id="divide-value">@value</span></p>
  const p = document.createElement('p');
  insert(p, document.createTextNode('Value: '));
  const span = document.createElement('span');
  span.id = 'divide-value';
  const t = document.createTextNode('');
  insert(span, t);
  insert(p, span);

  // <button id="halve" @onclick="Halve">Halve</button>
  const button = document.createElement('button');
  button.id = 'halve';
  insert(button, document.createTextNode('Halve'));

  // @value
  effect(() => setText(t, value.value));

  // private void Halve() { value = value / 2.0; } — DOUBLE division maps to `/` verbatim.
  listen(button, 'click', () => {
    value.value = value.value / 2;
  });

  // attach last; the two "\n\n" nodes are App.razor's blank lines between siblings.
  insert(target, h1);
  insert(target, document.createTextNode('\n\n'));
  insert(target, p);
  insert(target, document.createTextNode('\n\n'));
  insert(target, button);
}
