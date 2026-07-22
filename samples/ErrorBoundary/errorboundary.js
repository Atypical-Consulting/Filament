/**
 * ErrorBoundary — hand-written Filament answer key for samples/ErrorBoundary/ErrorBoundary.razor.
 *
 * WHAT A BOUNDARY CATCHES WAS MEASURED BEFORE ANY OF THIS WAS WRITTEN, against the real Blazor
 * renderer rather than against the documentation — Renderer.HandleExceptionViaErrorBoundary is
 * ordinary .NET and needs no browser, so `tools/error-boundary-oracle` drives it headless:
 *
 *   W1  a throw from an event handler the PARENT owns, written inside the boundary   NOT caught
 *   W2  a throw from a CHILD COMPONENT's event handler                                   caught
 *   W3  a throw from a CHILD COMPONENT's OnInitialized                                    caught
 *   W4  a throw raised while the parent EVALUATES the content                             caught
 *
 * W1 is the shape every author expects a boundary to catch, and Blazor does not catch it: a
 * boundary catches what its DESCENDANTS raise, and a handler written in the boundary's child
 * content belongs to the component that WROTE the fragment — an ancestor of the boundary, not a
 * descendant. So this key lets such a throw propagate too. W2 and W3 need a stateful child, which
 * `[composition-out-of-subset]` refuses, so they are structurally unreachable here. W4 is therefore
 * the whole of what this boundary can faithfully catch, and it is what this file is built around.
 *
 * Blazor DOM contract, read from the baseline's own generated ErrorBoundary_razor.g.cs and
 * confirmed by driving the real renderer:
 *
 *   __builder.OpenElement(0, "div");
 *   __builder.AddAttribute(1, "id", "wrap");
 *   __builder.AddMarkupContent(2, "<p id=\"outside\">outside</p>");
 *   __builder.OpenComponent<ErrorBoundary>(3);
 *   __builder.AddComponentParameter(4, "ChildContent", ...);   // <p id="in">@Risky()</p>
 *   __builder.AddComponentParameter(5, "ErrorContent", ...);   // RenderFragment<Exception>
 *   __builder.CloseComponent();
 *   __builder.CloseElement();                                  // </div>
 *
 * `Risky()` throws while the boundary is invoking ChildContent, so Blazor never renders `#in`:
 * it stores the exception, renders ErrorContent with it, and — measured — leaves `#outside`
 * mounted and live. The rendered result on the Blazor side is
 *
 *   <div id="wrap"><p id="outside">outside</p><p id="err">Sorry: the content could not be rendered</p></div>
 *
 * THE MAPPING: A BOUNDARY IS A LATCH PLUS A CONDITIONAL, AND BOTH ALREADY EXIST.
 * There is no ErrorBoundary component at runtime here — everything inlines into one mount(). What
 * remains of it is exactly two things:
 *
 *   1. a `signal(null)` holding the caught exception. null IS Blazor's CurrentException.
 *   2. one `list()` over a COMMENT ANCHOR whose active key set is the content while the latch is
 *      null and the error UI once it is not — `@if`/`@else`'s own shape (decisions 81/82), keyed
 *      0/1 on the latch instead of on a user condition.
 *
 * ZERO NEW RUNTIME PRIMITIVES: signal, list, insert and document.createComment all predate this.
 * `git diff -- src/filament-runtime` is empty for this slice, which is the answer it owed the
 * non-goals register's S16 — the slice that was flagged as possibly needing a change to the frozen
 * runtime. It did not need one, and the reason is the keying: a fragment is N top-level nodes while
 * a list() row owns exactly ONE, so each top-level node of a slot becomes its OWN leaf key in the
 * same list. That is how a multi-node @if body already compiles (decision 120), reused rather than
 * widening list()'s row contract.
 *
 * THE LATCH IS STICKY, AND `??=` IS WHY. Measured on the Blazor side (W5): with the content
 * throwing a different message on every re-render, ErrorContent keeps showing the FIRST one, and
 * IErrorBoundaryLogger is called exactly once. Assigning unconditionally would show the newest,
 * which is a different page. Sticky also falls out structurally: flipping the latch changes the
 * list's key set, which unmounts the content rows and disposes their effects, so the content is
 * never evaluated again — the same latching Blazor gets from not re-invoking ChildContent.
 *
 * THE BUILD IS GUARDED, AND IT HAS TO BE INSIDE THE BRANCH FUNCTION. The throw happens while
 * list()'s reconcile is calling create(), midway through rebuilding its row array; letting it
 * escape would corrupt the list. Catching inside keeps list()'s contract intact — create ALWAYS
 * returns a node — and turns the throw into the latch write that swaps the boundary. The empty
 * text node returned on that path lives exactly one flush: the write re-runs the list, whose new
 * key set removes it. Measured against the real runtime: one build, no spin.
 *
 * THE COMMENT ANCHOR is the same disclosed +1-node divergence from Blazor that `@if` carries
 * (decisions 81/82, category of decision 20's <!--!--> markers): Blazor positions conditional
 * content through its render tree, not through a DOM node.
 *
 * WHAT THIS KEY DOES NOT COVER, AND IS REFUSED RATHER THAN APPROXIMATED: a throw routed through a
 * computed property. A computed is refreshed by checkDirty() from INSIDE flush() — before the
 * binding that reads it is entered — so it passes no guard wrapped around that binding and is
 * re-thrown at the write site instead (decision 38). Blazor CATCHES that case, so the compiler
 * refuses it with a located diagnostic rather than ship a boundary that looks like a guard and is
 * not one. Closing it would take error ownership in the runtime, i.e. the byte freeze.
 */

import { signal, insert, list } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const fail = true;

  function risky() {
    if (fail) {
      throw new Error('the content could not be rendered');
    }
    return 'ok';
  }

  const wrap = document.createElement('div');
  wrap.id = 'wrap';

  const outside = document.createElement('p');
  outside.id = 'outside';
  insert(outside, document.createTextNode('outside'));
  insert(wrap, outside);

  const caught = signal(null);
  const anchor = document.createComment('');
  insert(wrap, anchor);

  function content() {
    try {
      const p = document.createElement('p');
      p.id = 'in';
      insert(p, document.createTextNode(risky()));
      return p;
    } catch (_e) {
      caught.value ??= _e;
      return document.createTextNode('');
    }
  }

  function errorContent() {
    const p = document.createElement('p');
    p.id = 'err';
    insert(p, document.createTextNode('Sorry: '));
    insert(p, document.createTextNode(caught.value.message));
    return p;
  }

  list(wrap, () => caught.value === null ? [0] : [1], (i) => i,
       (i) => i === 0 ? content() : errorContent(), anchor);

  insert(target, wrap);
}
