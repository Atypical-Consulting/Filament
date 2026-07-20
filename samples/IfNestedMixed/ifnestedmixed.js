/**
 * IfNestedMixed — hand-written Filament answer key for baseline/IfNestedMixed.Blazor/App.razor.
 *
 * A @if branch body that MIXES markup with a nested @if (decision 120). The outer @if (show) body is a markup
 * node `<p id="x">x</p>` (always active when show) FOLLOWED by a nested `@if (other) { <span id="a">a</span> }`
 * (active iff other). The whole structure flattens to ONE list() (like the pure-nested case, #100), but the
 * branch's active-index expression SPREADS the nested @if's indices beside the constant markup leaf:
 *   (show.value) ? [0, ...((other.value) ? [1] : [])] : []
 * -- leaf 0 (the <p>) is always mounted when show; leaf 1 (the <span>) only when other. keyOf is the index
 * itself; the create dispatch is `(i) => i === 0 ? ifBody0_0() : ifBody0_1()`. #s toggles show (the whole
 * branch), #o toggles other (just the nested span). No runtime primitive is added -- it is the same list() the
 * pure-nested @if uses. DOM contract: a <div id="w"> holding the conditional nodes + an anchor comment.
 */

import { signal, setAttr, listen, insert, list } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const show = signal(true);
  const other = signal(true);

  const _el0 = document.createElement('div');
  _el0.id = 'w';
  const _if0 = document.createComment('');
  insert(_el0, _if0);

  const _el3 = document.createElement('button');
  _el3.id = 's';
  insert(_el3, document.createTextNode('s'));
  const _el4 = document.createElement('button');
  _el4.id = 'o';
  insert(_el4, document.createTextNode('o'));

  function ifBody0_0() {
    const _el1 = document.createElement('p');
    _el1.id = 'x';
    insert(_el1, document.createTextNode('x'));
    return _el1;
  }
  function ifBody0_1() {
    const _el2 = document.createElement('span');
    _el2.id = 'a';
    insert(_el2, document.createTextNode('a'));
    return _el2;
  }
  list(_el0, () => (show.value) ? [0, ...((other.value) ? [1] : [])] : [], (i) => i, (i) => i === 0 ? ifBody0_0() : ifBody0_1(), _if0);

  listen(_el3, 'click', () => {
    show.value = !show.value;
  });
  listen(_el4, 'click', () => {
    other.value = !other.value;
  });

  insert(target, _el0);
  insert(target, document.createTextNode('\n\n'));
  insert(target, _el3);
  insert(target, document.createTextNode('\n'));
  insert(target, _el4);
}
