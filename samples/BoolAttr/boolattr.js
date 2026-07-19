/**
 * BoolAttr — hand-written Filament app. Reference for the boolean-`disabled` widening (BENCH n°14).
 *
 * ANSWER KEY (decisions 21/51): the generator's emission from baseline/BoolAttr.Blazor/App.razor is
 * snapshot- and alpha-equivalence-tested against this file. Every line is written the way a COMPILER
 * would emit it. Never edited to make a gate pass.
 *
 * The source, transcribed exactly (the blank line between the two buttons is a "\n\n" text node):
 *
 *     <button id="target" disabled="@locked">Target</button>
 *
 *     <button id="toggle" @onclick="Toggle">Toggle</button>
 *
 *     @code {
 *         private bool locked = true;
 *         private void Toggle() { locked = !locked; }
 *     }
 *
 * THE POINT: `disabled="@locked"` is a BOOLEAN attribute. `locked` is read by the template (the
 * disabled attribute) AND assigned outside construction (in Toggle), so it lifts to a Signal and the
 * disabled binding is `effect(() => setAttr(target, 'disabled', locked.value ? '' : null))` — the SAME
 * reactive rule as a string attribute (BENCH n°13), with the value mapped through a present/absent
 * ternary: true -> '' -> setAttribute (present, <button disabled="">); false -> null -> removeAttribute
 * (absent). setAttr's null->remove branch already ships; nothing new was added to the runtime.
 *
 * `locked` starts TRUE, so the binding's first run (against the DETACHED tree, before attach) writes
 * setAttribute -> #target disabled present, making no MutationRecord. The click flips locked to false
 * -> the effect re-runs -> removeAttribute -> #target disabled absent (the path a string attribute
 * never takes). Toggle writes once, so the handler is a plain assignment (no batch).
 */

import { signal, effect, setAttr, listen, insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  // -- @code: state -----------------------------------------------------------
  const locked = signal(true);

  // -- create(): the tree, built detached -------------------------------------

  // <button id="target" disabled="@locked">Target</button>  (disabled is a binding, below)
  const targetButton = document.createElement('button');
  targetButton.id = 'target';
  insert(targetButton, document.createTextNode('Target'));

  // <button id="toggle" @onclick="Toggle">Toggle</button>
  const toggleButton = document.createElement('button');
  toggleButton.id = 'toggle';
  insert(toggleButton, document.createTextNode('Toggle'));

  // -- bindings ---------------------------------------------------------------
  // disabled: boolean present/absent via setAttr's null->remove. true -> '' (present), false -> null (absent).
  effect(() => setAttr(targetButton, 'disabled', locked.value ? '' : null));

  // -- events -----------------------------------------------------------------
  // Toggle writes once (locked), so the handler is a plain assignment -- no batch.
  listen(toggleButton, 'click', () => locked.value = !locked.value);

  // -- attach: last, so the effect's first run made no MutationRecord ----------
  insert(target, targetButton);
  insert(target, document.createTextNode('\n\n'));
  insert(target, toggleButton);
}
