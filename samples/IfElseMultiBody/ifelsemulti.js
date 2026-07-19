/**
 * IfElseMultiBody — hand-written Filament answer key for baseline/IfElseMultiBody.Blazor/App.razor.
 *
 * Blazor DOM contract (read from the baseline's own App_razor.g.cs, decision-64 method):
 *
 *   __builder.OpenElement(0, "div"); __builder.AddAttribute(1, "id", "w");
 *   __builder.OpenElement(2, "button"); id="toggle"; onclick=Toggle; content "toggle"; CloseElement();
 *   if (show) {
 *       __builder.AddMarkupContent(6, "<span id=\"a\">a</span>");
 *   }
 *   else {
 *       __builder.AddMarkupContent(7, "<span id=\"b\">b</span>");
 *       __builder.AddMarkupContent(8, "<span id=\"c\">c</span>");
 *   }
 *   __builder.CloseElement();   // </div>
 *
 * Findings pinned by that read: the @if branch is ONE <span> (a); the @else branch is TWO adjacent
 * <span>s (b, c) -- opaque AddMarkupContent blobs, no bindings, no wrapper, no interleaved text node.
 * Rendered: #w holds [span a] when show, [span b, span c] otherwise, all direct children.
 *
 * The @if/@else lowers to a conditional list() keyed by a GLOBAL NODE INDEX: each branch owns a
 * contiguous range -- branch 0 (the @if) = [0], branch 1 (the @else) = [1, 2]. The active branch's
 * whole range is the source, so flipping `show` swaps [0] <-> [1, 2]: one node out, two in, all in
 * order. Dispatch `(i) => i === 0 ? ifBody0_0() : i === 1 ? ifBody0_1() : ifBody0_2()`. The comment
 * anchor is the disclosed +1-node divergence from Blazor (decision 81/20).
 *
 * `Toggle` performs one write -> no batch() (decision 68); single-use -> inlined.
 */

import { signal, listen, insert, list } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const show = signal(true);

  const main = document.createElement('div');
  main.id = 'w';

  const btn = document.createElement('button');
  btn.id = 'toggle';
  insert(btn, document.createTextNode('toggle'));
  insert(main, btn);

  const anchor = document.createComment('');
  insert(main, anchor);

  function ifBody0_0() {
    const span = document.createElement('span');
    span.id = 'a';
    insert(span, document.createTextNode('a'));
    return span;
  }
  function ifBody0_1() {
    const span = document.createElement('span');
    span.id = 'b';
    insert(span, document.createTextNode('b'));
    return span;
  }
  function ifBody0_2() {
    const span = document.createElement('span');
    span.id = 'c';
    insert(span, document.createTextNode('c'));
    return span;
  }
  list(main, () => (show.value) ? [0] : [1, 2], (i) => i, (i) => i === 0 ? ifBody0_0() : i === 1 ? ifBody0_1() : ifBody0_2(), anchor);

  listen(btn, 'click', () => {
    show.value = !show.value;
  });

  insert(target, main);
}
