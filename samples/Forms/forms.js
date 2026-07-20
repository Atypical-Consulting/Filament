/**
 * Forms — hand-written Filament answer key for baseline/Forms.Blazor/App.razor.
 *
 * THE POINT: <EditForm> + <InputText> + @bind-Value onto a MODEL's property. Decision 138.
 *
 * Blazor DOM contract (same shape Blazor renders <App> into #app):
 *
 *   <form>
 *     <input id="name">
 *     <button id="save" type="submit">save</button>
 *   </form>
 *   <p id="live"></p>                 <!-- mirrors the model as it is edited -->
 *   <p id="out"></p>                  <!-- empty until submit -->
 *
 * Typing "ok" into #name moves #live to "ok" while #out stays ""; clicking #save then moves #out to
 * "ok". Both halves are the measurement (BENCH n°56), and they fail independently: a form that bound
 * one way passes the first and fails the second, one that submitted without reading the model passes
 * the second and fails the first.
 *
 * TWO THINGS HAD TO BE TRUE, AND THEY ARE THE REAL CONTENT OF THIS SLICE.
 *
 * 1. RAZOR MUST RESOLVE THE FORM COMPONENTS. With the Forms namespace imported, `@bind-Value` lowers
 *    to Blazor's own Value / ValueChanged / ValueExpression triple, and this compiler reads the one
 *    that carries the author's expression. Without that it would arrive as the raw text "model.Name"
 *    and forms would mean re-deriving Blazor's binding semantics by hand — wiring described twice,
 *    which is the mistake decision 53 exists to prevent.
 *
 * 2. THE BOUND PROPERTY HAD TO BECOME A SIGNAL. Reactivity here is defined over FIELDS (read AND
 *    assigned), and `model.Name` is a record PROPERTY that nothing in @code assigns — the only thing
 *    that writes it is the input. So the TEMPLATE'S WRITE is what makes it reactive, which is why
 *    `name` is a `signal('')` inside the model literal below. That also closes decision 104's
 *    deferral, which named this exact case ("a pure @bind-only target needs its reactivity marked
 *    from the template").
 *
 * preventDefault() IS PART OF THE MAPPING, not a nicety: without it the browser navigates on submit
 * and the page reloads, which is exactly what Blazor's EditForm suppresses.
 *
 * WHAT IS NOT HERE: validation. Without validator components every submit IS valid — that is Blazor's
 * behaviour, not a simplification — so OnValidSubmit fires on every submit and there is nothing to
 * emit for validity. A <DataAnnotationsValidator /> is refused rather than ignored.
 */

import { signal, effect, setText, setAttr, listen, insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const model = { name: signal('') };
  const saved = signal('');

  const form = document.createElement('form');

  const name = document.createElement('input');
  name.id = 'name';
  insert(form, name);

  const saveBtn = document.createElement('button');
  saveBtn.id = 'save';
  setAttr(saveBtn, 'type', 'submit');
  insert(saveBtn, document.createTextNode('save'));
  insert(form, saveBtn);

  const live = document.createElement('p');
  live.id = 'live';
  const liveTx = document.createTextNode('');
  insert(live, liveTx);

  const out = document.createElement('p');
  out.id = 'out';
  const outTx = document.createTextNode('');
  insert(out, outTx);

  effect(() => { name.value = model.name.value; });
  effect(() => setText(liveTx, model.name.value));
  effect(() => setText(outTx, saved.value));

  listen(name, 'change', (e) => { model.name.value = e.target.value; });
  listen(form, 'submit', (e) => {
    e.preventDefault();
    saved.value = model.name.value;
  });

  insert(target, form);
  insert(target, live);
  insert(target, out);
}
