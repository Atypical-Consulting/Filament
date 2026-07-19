/**
 * DivideInt — hand-written Filament answer key for baseline/DivideInt.Blazor/App.razor.
 *
 * The point: @value halved by INTEGER division. C# int/int truncates toward zero (7/2 = 3), where
 * JS's `/` yields 3.5 -- so the faithful lowering is Math.trunc(value / 2), not `value / 2`. The "3"
 * this renders (vs the 3.5 a bare `/` would) is what lets the DOM-contract oracle catch a generator
 * that emitted the wrong division. Closes decision #87's deferral of int/int.
 *
 * DOM contract mirrors Divide.Blazor (decision 64 read): h1#title, p with "Value: " + span#divide-value,
 * button#halve, with the blank lines between siblings shipped as "\n\n" text nodes.
 */

import { signal, effect, setText, listen, insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  // @code { private int value = 7; } — read by the template, assigned in Halve() -> lifted.
  const value = signal(7);

  // <h1 id="title">DivideInt</h1>
  const h1 = document.createElement('h1');
  h1.id = 'title';
  insert(h1, document.createTextNode('DivideInt'));

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

  // private void Halve() { value = value / 2; } — INT division maps to Math.trunc(... / ...).
  listen(button, 'click', () => {
    value.value = Math.trunc(value.value / 2);
  });

  // attach last; the two "\n\n" nodes are App.razor's blank lines between siblings.
  insert(target, h1);
  insert(target, document.createTextNode('\n\n'));
  insert(target, p);
  insert(target, document.createTextNode('\n\n'));
  insert(target, button);
}
