/**
 * Submit — hand-written Filament answer key for baseline/Submit.Blazor/App.razor.
 *
 * THE POINT: THE SUBMIT CONTRACT. Decision 165. A submit event with a registered handler has its
 * browser default suppressed — always, for every form shape, with no `:preventDefault` modifier and
 * regardless of whether a callback was supplied. That is not a Filament convention; it is Blazor's,
 * and it is one line of Blazor's shipped dispatcher:
 *
 *     _ = { submit: !0 }
 *     … Object.prototype.hasOwnProperty.call(_, t.type) && t.preventDefault()
 *
 * A table with ONE entry, keyed on the EVENT TYPE. Filament had read it for <EditForm OnValidSubmit>
 * and nowhere else, so three other shapes emitted a bare listener and the browser did what a browser
 * does with an un-suppressed submit: NAVIGATE. The document reloaded, the module was re-mounted from
 * nothing, and every field the author had filled in came back empty — at exit 0, with no diagnostic,
 * on sources Blazor compiles and runs without moving (register defects A1 and A2).
 *
 * Blazor DOM contract (same shape Blazor renders <App> into #app):
 *
 *   <form>                                        <!-- 1. plain form, @code method (A1) -->
 *     <input id="name">
 *     <button id="save" type="submit">save</button>
 *   </form>
 *   <p id="out"></p>                              <!-- "" until #save, then "ok" -->
 *
 *   <form>                                        <!-- 2. <EditForm Model>, NO OnValidSubmit (A2) -->
 *     <input id="quiet">
 *     <button id="quiet-save" type="submit">save</button>
 *   </form>
 *   <p id="live"></p>                             <!-- follows the model as it is typed -->
 *
 *   <form>                                        <!-- 3. @onsubmit AND @onkeydown, ONE element -->
 *     <button id="tally" type="submit">tally</button>
 *   </form>
 *   <p id="hits"></p>                             <!-- "0" -> "1" -->
 *   <p id="key"></p>                              <!-- "" -> "a", from the keydown's OWN event -->
 *
 *   <form>                                        <!-- 4. inline lambda handler (decision 105) -->
 *     <button id="lam-go" type="submit">lam</button>
 *   </form>
 *   <p id="lam"></p>                              <!-- "0" -> "1" -->
 *
 * Each form fails alone, and above all of them sits the claim a reload would silently erase: a marker
 * planted on `window` before each click is still there after it, and location.href has not moved
 * (BENCH n°71).
 *
 * WHY FOUR FORMS AND NOT ONE. They are four different code paths, not four spellings of one:
 *
 *   1. a handler RECORDED during the walk and rendered after it (the arrow is chosen at render time);
 *   2. NO handler at all — the listener's entire body is the suppression, because EditForm registers
 *      `onsubmit` unconditionally in Blazor's own render tree and the callback only decides what runs
 *      after the default is dead;
 *   3. TWO handlers on ONE element — the reason the rule is keyed on the EVENT and not the element:
 *      an element-keyed rule would have suppressed this form's keydown too (Blazor's table is
 *      submit-only) and called a method that DECLARED a KeyboardEventArgs with nothing to read;
 *   4. a handler EMITTED during the walk (an inline lambda) rather than recorded — a second emission
 *      site, which is exactly why the rule now lives in one function that both sites call.
 *
 * WHAT IS NOT HERE. Field-state classes. Blazor's <InputText> renders `class="valid"`, and
 * `class="modified valid"` once touched, from the EditContext's per-field state; Filament emits no
 * class at all (register A16). It is disclosed in BENCH n°71, not asserted around.
 */

import { signal, effect, setText, setAttr, listen, insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const name = signal('');
  const saved = signal('');
  const hits = signal(0);
  const lastKey = signal('');
  const lamHits = signal(0);
  const model = { name: signal('') };

  // 1 — the plain form: <form @onsubmit="Save">
  const plainForm = document.createElement('form');
  const nameInput = document.createElement('input');
  nameInput.id = 'name';
  insert(plainForm, nameInput);
  const saveBtn = document.createElement('button');
  saveBtn.id = 'save';
  setAttr(saveBtn, 'type', 'submit');
  insert(saveBtn, document.createTextNode('save'));
  insert(plainForm, saveBtn);

  const out = document.createElement('p');
  out.id = 'out';
  const outTx = document.createTextNode('');
  insert(out, outTx);

  // 2 — the callback-less <EditForm Model>: still a <form>, still suppressing
  const quietForm = document.createElement('form');
  const quietInput = document.createElement('input');
  quietInput.id = 'quiet';
  insert(quietForm, quietInput);
  const quietBtn = document.createElement('button');
  quietBtn.id = 'quiet-save';
  setAttr(quietBtn, 'type', 'submit');
  insert(quietBtn, document.createTextNode('save'));
  insert(quietForm, quietBtn);

  const live = document.createElement('p');
  live.id = 'live';
  const liveTx = document.createTextNode('');
  insert(live, liveTx);

  // 3 — submit AND keydown on the SAME element
  const keyForm = document.createElement('form');
  const tallyBtn = document.createElement('button');
  tallyBtn.id = 'tally';
  setAttr(tallyBtn, 'type', 'submit');
  insert(tallyBtn, document.createTextNode('tally'));
  insert(keyForm, tallyBtn);

  const hitsP = document.createElement('p');
  hitsP.id = 'hits';
  const hitsTx = document.createTextNode('');
  insert(hitsP, hitsTx);

  const keyP = document.createElement('p');
  keyP.id = 'key';
  const keyTx = document.createTextNode('');
  insert(keyP, keyTx);

  // 4 — the inline lambda handler
  const lamForm = document.createElement('form');
  const lamBtn = document.createElement('button');
  lamBtn.id = 'lam-go';
  setAttr(lamBtn, 'type', 'submit');
  insert(lamBtn, document.createTextNode('lam'));
  insert(lamForm, lamBtn);

  const lam = document.createElement('p');
  lam.id = 'lam';
  const lamTx = document.createTextNode('');
  insert(lam, lamTx);

  effect(() => { nameInput.value = name.value; });
  effect(() => setText(outTx, saved.value));
  effect(() => { quietInput.value = model.name.value; });
  effect(() => setText(liveTx, model.name.value));
  effect(() => setText(hitsTx, hits.value));
  effect(() => setText(keyTx, lastKey.value));
  effect(() => setText(lamTx, lamHits.value));

  // The listeners emitted DURING the walk come first — the two @bind changes, the callback-less
  // form's bare suppression, and the inline lambda — then the ones RECORDED and rendered after it,
  // because whether a recorded handler's body is inlined is not known until the walk ends.
  listen(nameInput, 'change', (e) => { name.value = e.target.value; });
  listen(quietForm, 'submit', (e) => { e.preventDefault(); });
  listen(quietInput, 'change', (e) => { model.name.value = e.target.value; });
  listen(lamForm, 'submit', (e) => {
    e.preventDefault();
    lamHits.value++;
  });
  listen(plainForm, 'submit', (e) => {
    e.preventDefault();
    saved.value = name.value;
  });
  listen(keyForm, 'submit', (e) => {
    e.preventDefault();
    hits.value++;
  });
  // THE ONE THAT IS NOT SUPPRESSED, on the SAME element as the one above it: Blazor's table names
  // submit and only submit, and this handler declared a KeyboardEventArgs it must actually receive.
  listen(keyForm, 'keydown', (e) => {
    lastKey.value = e.key;
  });

  insert(target, plainForm);
  insert(target, document.createTextNode('\n'));
  insert(target, out);
  insert(target, document.createTextNode('\n\n'));
  insert(target, quietForm);
  insert(target, document.createTextNode('\n'));
  insert(target, live);
  insert(target, document.createTextNode('\n\n'));
  insert(target, keyForm);
  insert(target, document.createTextNode('\n'));
  insert(target, hitsP);
  insert(target, document.createTextNode('\n'));
  insert(target, keyP);
  insert(target, document.createTextNode('\n\n'));
  insert(target, lamForm);
  insert(target, document.createTextNode('\n'));
  insert(target, lam);
}
