/**
 * If — hand-written Filament answer key for samples/If/If.razor.
 *
 * Blazor DOM contract (read from baseline/Counter.Blazor's own generated
 * IfRef_razor.g.cs, throwaway component, decision-64 method — built with
 * `dotnet build baseline/Counter.Blazor -c Debug -p:EmitCompilerGeneratedFiles=true
 * -p:CompilerGeneratedFilesOutputPath=generated`, then deleted; see Step 9):
 *
 *   __builder.OpenElement(0, "div");
 *   __builder.AddAttribute(1, "id", "wrap");
 *   __builder.OpenElement(2, "button");
 *   __builder.AddAttribute(3, "id", "t");
 *   __builder.AddAttribute(4, "onclick", ...Toggle...);
 *   __builder.AddContent(5, "t");
 *   __builder.CloseElement();               // </button>
 *   if (show) {
 *       __builder.AddMarkupContent(6, "<span id=\"msg\">hi</span>");
 *   }
 *   __builder.CloseElement();               // </div>
 *
 * Two findings pinned by that read:
 *
 *   1. NO WHITESPACE TEXT NODES. Unlike RowsApp.razor (decision 80), the source here
 *      has no newline/indentation between `</button>` and `@if`, nor between the `}`
 *      and `</div>` — `...Toggle">t</button>@if (show)\n{\n    <span ...` are adjacent
 *      with no markup between them, and Razor only turns SOURCE whitespace between
 *      siblings into AddMarkupContent text nodes; there is none here to turn. This
 *      answer key builds none, matching Blazor exactly.
 *
 *   2. NO OpenRegion/CloseRegion. A plain `if` with no `else` does not need one:
 *      Blazor only wraps branches in region markers when sequence numbers could
 *      diverge structurally between alternatives (e.g. if/else-if/else), so the diff
 *      algorithm can key on the region rather than raw sequence numbers. This is a
 *      compiler-internal bookkeeping detail with no DOM-visible consequence and
 *      nothing for the answer key to reproduce.
 *
 *   The conditional content itself is compiled by Blazor as a single opaque
 *   `AddMarkupContent(6, "<span id=\"msg\">hi</span>")` — inert static markup, not
 *   decomposed into OpenElement/AddAttribute/AddContent/CloseElement, because the
 *   span has no bindings. That is a Blazor codegen shortcut, not a different DOM
 *   shape: the rendered result is still one `<span id="msg">` element containing the
 *   text "hi", which is what this key (and the generator) build via createElement/
 *   setAttr/createTextNode.
 *
 * The @if lowers to a conditional list(): a 0/1 source over the condition, a constant
 * key, and a COMMENT ANCHOR. The comment node is a DISCLOSED +1-node divergence from
 * Blazor (category of decision #20's <!--!--> markers): Blazor positions conditional
 * content via its render tree, not a DOM comment. Removing it needs next-sibling
 * anchoring, deferred.
 *
 * THE HANDLER. `Toggle()` performs exactly one write (`show = !show`), so per
 * decision 68's batch rule (batch iff there is more than one write to coalesce) it
 * gets NO batch(), same as Counter's `Increment()`. `Toggle` is also named by exactly
 * one @onclick and called from nowhere else, so decision 68's single-use inlining
 * folds its body straight into the handler rather than keeping a named `toggle`
 * function — this key mirrors the generator's actual output (there is no prior
 * answer key for @if to source this rule from; canon reconciliation in Step 6
 * confirmed both choices against the generator's real emission).
 */

import { signal, listen, insert, list } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const show = signal(true);

  const main = document.createElement('div');
  main.id = 'wrap';

  const btn = document.createElement('button');
  btn.id = 't';
  insert(btn, document.createTextNode('t'));
  insert(main, btn);

  const anchor = document.createComment('');
  insert(main, anchor);

  function ifBody() {
    const span = document.createElement('span');
    span.id = 'msg';
    insert(span, document.createTextNode('hi'));
    return span;
  }
  list(main, () => (show.value) ? [0] : [], () => 0, ifBody, anchor);

  listen(btn, 'click', () => {
    show.value = !show.value;
  });

  insert(target, main);
}
