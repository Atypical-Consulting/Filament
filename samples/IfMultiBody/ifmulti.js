/**
 * IfMultiBody — hand-written Filament answer key for baseline/IfMultiBody.Blazor/App.razor.
 *
 * Blazor DOM contract (read from the baseline's own App_razor.g.cs, decision-64 method — built
 * with `dotnet build baseline/IfMultiBody.Blazor -p:EmitCompilerGeneratedFiles=true`, then the
 * obj/.../generated tree deleted):
 *
 *   __builder.OpenElement(0, "div"); __builder.AddAttribute(1, "id", "w");
 *   __builder.OpenElement(2, "button");
 *     __builder.AddAttribute(3, "id", "toggle");
 *     __builder.AddAttribute(4, "onclick", ...Toggle...);
 *     __builder.AddContent(5, "toggle");
 *   __builder.CloseElement();                 // </button>
 *   if (show) {
 *       __builder.AddMarkupContent(6, "<span id=\"a\">a</span>");
 *       __builder.AddMarkupContent(7, "<span id=\"b\">b</span>");
 *   }
 *   __builder.CloseElement();                 // </div>
 *
 * Findings pinned by that read:
 *   1. The two spans are TWO separate opaque AddMarkupContent blobs (seq 6, 7) — inert static
 *      markup, no bindings. Rendered, that is two <span> elements, direct children of #w, in
 *      order a then b, with NO wrapper and NO interleaved text node (the source has no whitespace
 *      between </span> and <span>). Whether Blazor emits one blob or two is a codegen detail with
 *      no DOM-visible consequence; the rendered result is identical to this key's two spans.
 *   2. NO whitespace text nodes between the button and @if, nor between the spans.
 *
 * The @if lowers to a conditional list() with ONE ITEM PER BODY NODE: a source over the
 * condition yielding [0, 1] (both nodes) or [] (neither), keyed by identity, dispatched by
 * `(i) => i === 0 ? ifBody0_0() : ifBody0_1()`. Both spans mount/unmount TOGETHER, in order.
 * The comment anchor is the DISCLOSED +1-node divergence from Blazor (decision 81/20): Blazor
 * positions conditional content via its render tree, not a DOM comment.
 *
 * `Toggle` performs one write (`show = !show`) -> no batch() (decision 68); named by exactly one
 * @onclick and called nowhere else -> single-use inlining folds its body into the handler.
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
  list(main, () => (show.value) ? [0, 1] : [], (i) => i, (i) => i === 0 ? ifBody0_0() : ifBody0_1(), anchor);

  listen(btn, 'click', () => {
    show.value = !show.value;
  });

  insert(target, main);
}
