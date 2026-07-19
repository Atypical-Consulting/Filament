/**
 * ReactiveAttr — hand-written Filament app. Reference for the reactive-`class` widening (BENCH n°13).
 *
 * ANSWER KEY (decisions 21/51): the generator's emission from baseline/ReactiveAttr.Blazor/App.razor
 * is snapshot- and alpha-equivalence-tested against this file. Every line is written the way a COMPILER
 * would emit it. Never edited to make a gate pass.
 *
 * The source, transcribed exactly (the blank lines between the three siblings are "\n\n" text nodes —
 * the shared DOM contract, see counter.js):
 *
 *     <h1 id="title">Counter</h1>
 *
 *     <p id="status" class="@statusClass">Current count: <span id="counter-value">@currentCount</span></p>
 *
 *     <button id="increment" @onclick="Increment">Click me</button>
 *
 *     @code {
 *         private int currentCount = 0;
 *         private string statusClass = "zero";
 *         private void Increment() { currentCount++; statusClass = "counting"; }
 *     }
 *
 * THE POINT: `class="@statusClass"` is a REACTIVE attribute. `statusClass` is read by the template
 * (the class attribute) AND assigned outside construction (in Increment), so it lifts to a Signal and
 * the class binding is `effect(() => setAttr(p, 'class', statusClass.value))` — the SAME reactive rule
 * as a text binding, with the write target being an attribute (setAttr) instead of a Text node
 * (setText). setAttr already ships in the runtime; nothing new was added there.
 *
 * The binding block emits the class effect BEFORE the text effect: the <p>'s attributes are walked
 * before its children, so the class effect (from the attribute) precedes the @currentCount effect
 * (from the inner span). Both first-run against the DETACHED tree, so neither makes a MutationRecord;
 * attach is last.
 */

import { signal, effect, batch, setText, setAttr, listen, insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  // -- @code: state -----------------------------------------------------------
  const currentCount = signal(0);
  const statusClass = signal('zero');

  // -- create(): the tree, built detached -------------------------------------

  // <h1 id="title">Counter</h1>
  const h1 = document.createElement('h1');
  h1.id = 'title';
  insert(h1, document.createTextNode('Counter'));

  // <p id="status" class="@statusClass">Current count: <span id="counter-value">@currentCount</span></p>
  const p = document.createElement('p');
  p.id = 'status';
  insert(p, document.createTextNode('Current count: '));
  const span = document.createElement('span');
  span.id = 'counter-value';
  const t = document.createTextNode('');
  insert(span, t);
  insert(p, span);

  // <button id="increment" @onclick="Increment">Click me</button>
  const button = document.createElement('button');
  button.id = 'increment';
  insert(button, document.createTextNode('Click me'));

  // -- bindings ---------------------------------------------------------------
  // class first (the <p> attribute), then the @currentCount text (the inner span).
  effect(() => setAttr(p, 'class', statusClass.value));
  effect(() => setText(t, currentCount.value));

  // -- events -----------------------------------------------------------------
  // Increment writes twice (currentCount and statusClass), so the handler batches:
  // one flush, both signals, one settle.
  listen(button, 'click', () => batch(() => {
    currentCount.value++;
    statusClass.value = 'counting';
  }));

  // -- attach: last, so the effects' first run made no MutationRecord ----------
  insert(target, h1);
  insert(target, document.createTextNode('\n\n'));
  insert(target, p);
  insert(target, document.createTextNode('\n\n'));
  insert(target, button);
}
