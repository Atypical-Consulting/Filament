/**
 * IfElse — hand-written Filament answer key for samples/IfElse/IfElse.razor.
 *
 * Blazor DOM contract (read from IfElseRef_razor.g.cs, decision-64 method — built with
 * `dotnet build baseline/Counter.Blazor -c Debug -p:EmitCompilerGeneratedFiles=true`, inspected,
 * then deleted):
 *
 *   __builder.OpenElement(0, "div");
 *   __builder.AddAttribute(1, "id", "wrap");
 *   __builder.OpenElement(2, "button");
 *   __builder.AddAttribute(3, "id", "t");
 *   __builder.AddAttribute(4, "onclick", ...Next...);
 *   __builder.AddContent(5, "t");
 *   __builder.CloseElement();               // </button>
 *   if (n == 0)      { __builder.AddMarkupContent(6, "<span id=\"a\">a</span>"); }
 *   else if (n == 1) { __builder.AddMarkupContent(7, "<span id=\"b\">b</span>"); }
 *   else             { __builder.AddMarkupContent(8, "<span id=\"c\">c</span>"); }
 *   __builder.CloseElement();               // </div>
 *
 * Three findings pinned by that read:
 *
 *   1. NO OpenRegion/CloseRegion. This Razor version emits plain `if / else if / else` C# with an
 *      AddMarkupContent per branch — no region markers at all. Nothing DOM-visible to reproduce
 *      (and if.js finding #2 already noted regions are compiler-internal bookkeeping regardless).
 *
 *   2. NO WHITESPACE TEXT NODES. The source `</button>@if` and `}</div>` are adjacent — the @if and
 *      its braces are C#, not markup — so Razor turns no source whitespace into text nodes. This key
 *      builds none, matching Blazor exactly (same as if.js finding #1).
 *
 *   3. Each branch's content is a single opaque `AddMarkupContent(<span ...>)` — inert static markup,
 *      because the span has no bindings. That is a Blazor codegen shortcut, not a different DOM shape:
 *      the rendered result is one `<span>` with its text, which this key (and the generator) build via
 *      createElement/createTextNode.
 *
 * The @if/@else if/@else lowers to ONE conditional list(): the single item's VALUE is the active
 * branch index (0/1/2), the key IS that index, and create() dispatches on it — so flipping a
 * condition changes the key and list() swaps the branch. A COMMENT ANCHOR positions it among its
 * siblings: a DISCLOSED +1-node divergence from Blazor (category of decision #20's <!--!--> markers),
 * which positions conditional content via its render tree, not a DOM comment. Removing it needs
 * next-sibling anchoring, deferred. Zero new runtime primitive — the same list() @foreach and @if use.
 *
 * Next() performs one write (n = ...), so per decision #68's batch rule it gets NO batch(), same as
 * Counter's Increment(). Next is named by exactly one @onclick and called nowhere else, so decision
 * #68's single-use inlining folds its body straight into the handler rather than keeping a named
 * function — this key mirrors the generator's actual emission (canon reconciliation confirmed it;
 * there is no prior answer key for @else to source these choices from).
 */

import { signal, listen, insert, list } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const n = signal(0);

  const main = document.createElement('div');
  main.id = 'wrap';

  const btn = document.createElement('button');
  btn.id = 't';
  insert(btn, document.createTextNode('t'));
  insert(main, btn);

  const anchor = document.createComment('');
  insert(main, anchor);

  function branch0() {
    const span = document.createElement('span');
    span.id = 'a';
    insert(span, document.createTextNode('a'));
    return span;
  }
  function branch1() {
    const span = document.createElement('span');
    span.id = 'b';
    insert(span, document.createTextNode('b'));
    return span;
  }
  function branch2() {
    const span = document.createElement('span');
    span.id = 'c';
    insert(span, document.createTextNode('c'));
    return span;
  }
  list(main,
    () => (n.value === 0) ? [0] : (n.value === 1) ? [1] : [2],
    (i) => i,
    (i) => i === 0 ? branch0() : i === 1 ? branch1() : branch2(),
    anchor);

  listen(btn, 'click', () => {
    n.value = (n.value + 1) % 3;
  });

  insert(target, main);
}
