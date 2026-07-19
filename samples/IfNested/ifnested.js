/**
 * IfNested — hand-written Filament answer key for baseline/IfNested.Blazor/App.razor.
 *
 * Blazor DOM contract (read from the baseline's own App_razor.g.cs, decision-64 method):
 *
 *   __builder.OpenElement(0, "div"); __builder.AddAttribute(1, "id", "w");
 *   __builder.OpenElement(2, "button"); id="tshow"; onclick=ToggleShow; content "show"; CloseElement();
 *   __builder.OpenElement(_, "button"); id="tother"; onclick=ToggleOther; content "other"; CloseElement();
 *   if (show) {
 *       if (other) {
 *           __builder.AddMarkupContent(10, "<span id=\"a\">a</span>");
 *       }
 *   }
 *   __builder.CloseElement();   // </div>
 *
 * Finding: #a is present iff show && other -- a leaf under two nested conditions, no wrapper.
 *
 * The nested @if flattens to ONE conditional list() whose source is a DECISION TREE: the outer @if's
 * branch is the inner @if's own expression, so the source reads
 *   () => (show.value) ? ((other.value) ? [0] : []) : []
 * (short-circuit ?: matches nested-@if evaluation: `other` is read only while `show` is true, so the
 * list effect subscribes to `other` only then -- exactly as two nested list()s would). One leaf
 * builder (global index 0), identity key, dispatch `(i) => ifBody0_0()`. The comment anchor is the
 * disclosed +1-node divergence from Blazor (decision 81/20).
 *
 * Each toggle writes one field -> no batch() (decision 68); each is single-use -> inlined.
 */

import { signal, listen, insert, list } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const show = signal(true);
  const other = signal(true);

  const main = document.createElement('div');
  main.id = 'w';

  const btnShow = document.createElement('button');
  btnShow.id = 'tshow';
  insert(btnShow, document.createTextNode('show'));
  insert(main, btnShow);

  const btnOther = document.createElement('button');
  btnOther.id = 'tother';
  insert(btnOther, document.createTextNode('other'));
  insert(main, btnOther);

  const anchor = document.createComment('');
  insert(main, anchor);

  function ifBody0_0() {
    const span = document.createElement('span');
    span.id = 'a';
    insert(span, document.createTextNode('a'));
    return span;
  }
  list(main, () => (show.value) ? ((other.value) ? [0] : []) : [], (i) => i, (i) => ifBody0_0(), anchor);

  listen(btnShow, 'click', () => {
    show.value = !show.value;
  });
  listen(btnOther, 'click', () => {
    other.value = !other.value;
  });

  insert(target, main);
}
