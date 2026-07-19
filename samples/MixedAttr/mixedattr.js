/**
 * MixedAttr — hand-written Filament app. Reference for the mixed literal+expression `class` widening
 * (BENCH n°15).
 *
 * ANSWER KEY (decisions 21/51): the generator's emission from baseline/MixedAttr.Blazor/App.razor is
 * snapshot- and alpha-equivalence-tested against this file. Every line is written the way a COMPILER
 * would emit it. Never edited to make a gate pass.
 *
 * The source, transcribed exactly (the blank lines between the three siblings are "\n\n" text nodes):
 *
 *     <h1 id="title">Counter</h1>
 *
 *     <p id="status" class="badge @statusClass rounded">Current count: <span id="counter-value">@currentCount</span></p>
 *
 *     <button id="increment" @onclick="Increment">Click me</button>
 *
 *     @code {
 *         private int currentCount = 0;
 *         private string statusClass = "zero";
 *         private void Increment() { currentCount++; statusClass = "counting"; }
 *     }
 *
 * THE POINT: `class="badge @statusClass rounded"` is a MIXED literal+expression value. Razor gives the
 * value as ordered parts, each with a Prefix; the compiler folds them into one concatenation:
 * `'badge '` (literal "badge" + the expression's leading " ") + `statusClass.value` + `' rounded'`
 * (the trailing literal's " " + "rounded"). `statusClass` is read by the template AND assigned outside
 * construction, so it lifts to a Signal and the whole class binding is a live
 * `effect(() => setAttr(p, 'class', 'badge ' + statusClass.value + ' rounded'))`. The pure `@expr`
 * case (BENCH n°13) is the degenerate fold; this adds the literal terms around the expression. Nothing
 * new was added to the runtime (setAttr + JS string concat).
 *
 * The class effect emits BEFORE the @currentCount text effect (the <p>'s attributes are walked before
 * its children). Both first-run against the DETACHED tree, so neither makes a MutationRecord; attach is
 * last. Increment writes twice, so the handler batches.
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

  // <p id="status" class="badge @statusClass rounded">Current count: <span id="counter-value">@currentCount</span></p>
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
  // class first (the <p> attribute, composed), then the @currentCount text (the inner span).
  effect(() => setAttr(p, 'class', 'badge ' + statusClass.value + ' rounded'));
  effect(() => setText(t, currentCount.value));

  // -- events -----------------------------------------------------------------
  // Increment writes twice (currentCount and statusClass), so the handler batches.
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
