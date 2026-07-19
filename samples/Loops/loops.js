/**
 * Loops — hand-written Filament answer key for baseline/Loops.Blazor/App.razor.
 *
 * Three @code handlers exercising the loop/switch statements that entered §5 at decision #102:
 *   DoWhile  -> `while` loop, n -> 5
 *   DoSwitch -> `switch` with constant labels + break, n(5) -> 9
 *   DoDo     -> `do…while` loop, n -> 3
 * Each writes n more than once (assignment + loop/case body), so per decision 68's batch rule each
 * handler is a batch(). `n` is read by the template and assigned in the handlers -> lifted to a signal.
 *
 * DOM contract mirrors Counter.Blazor (decision 64 read): h1#title, p("n = " + span#v), three buttons,
 * with the blank lines between the five siblings shipped as "\n\n" text nodes.
 */

import { signal, effect, batch, setText, listen, insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const n = signal(0);

  const h1 = document.createElement('h1');
  h1.id = 'title';
  insert(h1, document.createTextNode('Loops'));

  const p = document.createElement('p');
  insert(p, document.createTextNode('n = '));
  const span = document.createElement('span');
  span.id = 'v';
  const t = document.createTextNode('');
  insert(span, t);
  insert(p, span);

  const bwhile = document.createElement('button');
  bwhile.id = 'bwhile';
  insert(bwhile, document.createTextNode('while'));

  const bswitch = document.createElement('button');
  bswitch.id = 'bswitch';
  insert(bswitch, document.createTextNode('switch'));

  const bdo = document.createElement('button');
  bdo.id = 'bdo';
  insert(bdo, document.createTextNode('do'));

  effect(() => setText(t, n.value));

  // private void DoWhile() { n = 0; while (n < 5) { n = n + 1; } }
  listen(bwhile, 'click', () => batch(() => {
    n.value = 0;
    while (n.value < 5) {
      n.value = n.value + 1;
    }
  }));

  // private void DoSwitch() { switch (n) { case 5: n = 9; break; default: n = 0; break; } }
  listen(bswitch, 'click', () => batch(() => {
    switch (n.value) {
    case 5:
      n.value = 9;
      break;
    default:
      n.value = 0;
      break;
    }
  }));

  // private void DoDo() { n = 0; do { n = n + 1; } while (n < 3); }
  listen(bdo, 'click', () => batch(() => {
    n.value = 0;
    do {
      n.value = n.value + 1;
    } while (n.value < 3);
  }));

  insert(target, h1);
  insert(target, document.createTextNode('\n\n'));
  insert(target, p);
  insert(target, document.createTextNode('\n\n'));
  insert(target, bwhile);
  insert(target, document.createTextNode('\n\n'));
  insert(target, bswitch);
  insert(target, document.createTextNode('\n\n'));
  insert(target, bdo);
}
