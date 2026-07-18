/**
 * RootIf — hand-written Filament answer key for baseline/RootIf.Blazor/App.razor.
 *
 * THE POINT: a root-level @if with NO wrapping element. Its conditional list() mounts and
 * unmounts the branch directly against `target` (the mount point, #app), not against a created
 * wrapper. This is #77's THIRD and last disclosed false positive, closed by decision 89: when
 * the component's root itself holds template C#, the method is the region container and the
 * mapping attaches to target.
 *
 * Blazor DOM contract (same shape Blazor renders <App> into #app), with show === true:
 *
 *   <button id="toggle">toggle</button>
 *   <!-- comment anchor -->
 *   <span id="cond">visible</span>
 *
 * The button and the conditional's content are direct children of #app -- Blazor renders a
 * component's content straight into its mount element, no wrapper, and so does this. Clicking
 * #toggle flips `show`: the <span> is REMOVED when show is false and re-INSERTED (before the
 * anchor) when show is true again. That mount/unmount at the ROOT is the measurement (BENCH n°11):
 * unconditional markup would stay put; a real root @if gates.
 *
 * THE @if LOWERS to a conditional list(): a 0/1 source over the condition, a constant key (0),
 * and a COMMENT ANCHOR. The comment node is a DISCLOSED +1-node divergence from Blazor (category
 * of decision 20's <!--!--> markers): Blazor positions conditional content via its render tree,
 * not a DOM comment. Removing it needs next-sibling anchoring, deferred. The anchor is inserted
 * into target in source order (right after the button), and list() inserts the body BEFORE it,
 * so the conditional lands in the right place no matter what follows.
 *
 * THE HANDLER. Toggle() performs exactly one write (show = !show), so per decision 68's batch
 * rule (batch iff there is more than one write to coalesce) it gets NO batch(), same as Counter's
 * Increment(). Toggle is named by exactly one @onclick and called nowhere else, so decision 68's
 * single-use inlining folds its body straight into the click handler. This key mirrors the
 * generator's actual emission.
 */

import { signal, listen, insert, list } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const show = signal(true);

  const toggleBtn = document.createElement('button');
  toggleBtn.id = 'toggle';
  insert(toggleBtn, document.createTextNode('toggle'));
  insert(target, toggleBtn);

  const anchor = document.createComment('');
  insert(target, anchor);

  function ifBody() {
    const span = document.createElement('span');
    span.id = 'cond';
    insert(span, document.createTextNode('visible'));
    return span;
  }
  list(target, () => (show.value) ? [0] : [], () => 0, ifBody, anchor);

  listen(toggleBtn, 'click', () => {
    show.value = !show.value;
  });
}
