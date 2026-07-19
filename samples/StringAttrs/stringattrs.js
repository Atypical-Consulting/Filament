/**
 * StringAttrs — hand-written Filament app. Reference for the reactive-string-attribute-names widening
 * (BENCH n°16).
 *
 * ANSWER KEY (decisions 21/51): the generator's emission from baseline/StringAttrs.Blazor/App.razor is
 * snapshot- and alpha-equivalence-tested against this file. Every line is written the way a COMPILER
 * would emit it. Never edited to make a gate pass.
 *
 * The source, transcribed exactly (the blank line between the two siblings is a "\n\n" text node):
 *
 *     <a id="link" href="@url" title="@tip" aria-label="@label">Go</a>
 *
 *     <button id="toggle" @onclick="Toggle">Toggle</button>
 *
 *     @code { private string url = "/a", tip = "first", label = "one";
 *             private void Toggle() { url = "/b"; tip = "second"; label = "two"; } }
 *
 * THE POINT: title/href/aria-label are REACTIVE string attributes -- the SAME composed emission as
 * `class` (BENCH n°13/n°15), just more allow-listed names. Each is `effect(() => setAttr(a, name,
 * x.value))`. The three effects emit in document order (href, title, aria-label). setAttr already ships
 * and takes any attribute name (hyphens included); nothing new was added to the runtime.
 *
 * url/tip/label are read by the template AND assigned outside construction (in Toggle), so each lifts to
 * a Signal. Toggle writes three fields, so the handler batches: one flush, three signals.
 */

import { signal, effect, batch, setAttr, listen, insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  // -- @code: state -----------------------------------------------------------
  const url = signal('/a');
  const tip = signal('first');
  const label = signal('one');

  // -- create(): the tree, built detached -------------------------------------

  // <a id="link" href="@url" title="@tip" aria-label="@label">Go</a>  (the three attrs are bindings, below)
  const a = document.createElement('a');
  a.id = 'link';
  insert(a, document.createTextNode('Go'));

  // <button id="toggle" @onclick="Toggle">Toggle</button>
  const button = document.createElement('button');
  button.id = 'toggle';
  insert(button, document.createTextNode('Toggle'));

  // -- bindings ---------------------------------------------------------------
  // document order: href, title, aria-label.
  effect(() => setAttr(a, 'href', url.value));
  effect(() => setAttr(a, 'title', tip.value));
  effect(() => setAttr(a, 'aria-label', label.value));

  // -- events -----------------------------------------------------------------
  // Toggle writes three fields, so the handler batches: one flush, three signals.
  listen(button, 'click', () => batch(() => {
    url.value = '/b';
    tip.value = 'second';
    label.value = 'two';
  }));

  // -- attach: last, so the effects' first run made no MutationRecord ----------
  insert(target, a);
  insert(target, document.createTextNode('\n\n'));
  insert(target, button);
}
