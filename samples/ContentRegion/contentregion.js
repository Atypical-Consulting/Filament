/**
 * ContentRegion — hand-written Filament answer key for baseline/ContentRegion.Blazor/App.razor.
 *
 * THE POINT: a COMPONENT's CHILD CONTENT is a CONTAINER like any other, so when it holds template
 * C# it is a REGION. Decision 162, and it is honesty work rather than surface work: this is one bug
 * wearing three costumes, and it CRASHED the tool on three sources Blazor compiles.
 *
 *   <Card>@if (show) { … }</Card>                       a RenderFragment's content (decision 131)
 *   <CascadingValue Value="@level">…</CascadingValue>    a cascade's content     (decision 134)
 *   <EditForm …>@if (show) { … }</EditForm>             a form's content        (decision 138)
 *
 * WHY IT CRASHED, AND WHY THE FIX IS ONE LINE OF POLICY. An element whose children hold template C#
 * is planned by the collect walk as a REGION: its children stop being a document-ordered list and
 * become a RE-PARSED STATEMENT LIST (decision 54), so the emitter must ask `_regions` and emit the
 * region's OPS instead of walking children. EmitElement does. The root does (decision 89). The three
 * places that emit a component's child content did NOT — they walked `content.Children` — so raw C#
 * (`if (show) {`) reached the emitter and the tool aborted with FIL-WIRING. Not a mis-compile: a
 * CRASH, loud and honest, and the reason it stayed hidden is that no witness ever put control flow
 * inside a component's tags. Probing the eleven closed §3 non-goals is what found it.
 *
 * A SECOND DEFECT, FOUND WHILE FIXING THE FIRST, AND WORSE. The cascade's child loop guarded
 * `if (child is not null && parent is not null)` — and a <CascadingValue> emits NO element of its
 * own, so at the template ROOT its children HAVE no parent element. The content was built and
 * inserted NOWHERE: exit 0, module written, page renders without it. Silent. The root's container is
 * `target`, which is exactly what Compile's own root loop attaches to, so the cascade's children are
 * attached like the siblings they are — and IN SOURCE ORDER, which is the claim #head/#list/#tail
 * exists to measure.
 *
 * Blazor DOM contract (same shape Blazor renders <App> into #app):
 *
 *   <div id="head">head</div>
 *   <ul id="list"><li>1</li><li>2</li><li>3</li></ul>     <!-- the CASCADE's content, at the root -->
 *   <p id="tail">tail</p>
 *   <div id="card"><span id="body">0</span></div>          <!-- the CARD's content: a region -->
 *   <form><input id="name"><button id="save">save</button></form>   <!-- the FORM's content: a region -->
 *   <p id="out"></p>
 *   <button id="toggle">toggle</button>
 *   <button id="add">add</button>
 *
 * FOUR CLAIMS, AND THEY FAIL INDEPENDENTLY (BENCH n°68):
 *   PRESENCE — #body, #name and #list exist at all. Before the fix the tool could not emit them.
 *   ORDER    — #list sits between #head and #tail. A cascade attached at the end of the attach
 *              block, or dropped entirely, still renders three correct-looking elements.
 *   LIVENESS — #toggle unmounts and remounts #body AND #name; #add reassigns the list (key 2 out,
 *              4/5 in, 1/3 moved: "123" -> "3415") and bumps the count ("0" -> "1"). A region that
 *              compiled once and never re-ran passes presence and fails here.
 *   THE FORM — typing into #name reaches the model and submitting reads it back into #out. The
 *              @bind lives INSIDE the region, so it is the region's create() that wires it.
 *
 * THE LOWERING IS THE ONE EVERY OTHER CONTAINER GETS, which is the whole content of the slice.
 * `<Card>@if …</Card>` becomes a list() against the CHILD's own <div id="card">, anchored on a
 * comment; `<EditForm>@if …</EditForm>` becomes a list() against the <form> the EditForm lowered to;
 * the cascade's <ul> is an ordinary element whose own @foreach is an ordinary list(). No new emission
 * shape, no new runtime primitive, no <CascadingValue> wrapper element — a cascade is still lexical
 * scope, and `level` is still a plain const nothing subscribes to.
 *
 * _pool is read only inside Add and never written: a hoisted plain array (decision 121). Add writes
 * TWO signals, so decision 68's batch rule wraps it; Toggle writes one, so it does not.
 */

import { signal, effect, batch, setText, setAttr, listen, insert, list } from '../../src/filament-runtime/src/index.ts';

// Immutable literal data hoists to module scope (rows.js mapping decision 4).
const _pool = [3, 4, 1, 5, 2];

export function mount(target) {
  // `level` is cascaded and nothing reads it: a cascade is lexical scope, so it costs a plain const.
  const level = 1;
  const count = signal(0);
  const show = signal(true);
  const saved = signal('');
  const model = { name: signal('') };
  const items = signal([1, 2, 3]);

  const head = document.createElement('div');
  head.id = 'head';
  insert(head, document.createTextNode('head'));

  // THE CASCADE'S CONTENT. An ordinary <ul>, built like any other element -- the <CascadingValue>
  // itself emits nothing at all. What matters is where it is ATTACHED, at the bottom of mount().
  const ul = document.createElement('ul');
  ul.id = 'list';

  const tail = document.createElement('p');
  tail.id = 'tail';
  insert(tail, document.createTextNode('tail'));

  // THE CARD'S CONTENT. The <div id="card"> is the CHILD's own element (Card.razor), and the
  // parent's @if is a region against it -- the child chose the position, the parent owns the content.
  const card = document.createElement('div');
  card.id = 'card';
  const cardAnchor = document.createComment('');
  insert(card, cardAnchor);

  // THE FORM'S CONTENT. <EditForm> lowers to <form> (decision 138) and its @if is a region against
  // that <form>, exactly as the card's is against the card.
  const form = document.createElement('form');
  const formAnchor = document.createComment('');
  insert(form, formAnchor);

  const out = document.createElement('p');
  out.id = 'out';
  const outTx = document.createTextNode('');
  insert(out, outTx);

  const toggleBtn = document.createElement('button');
  toggleBtn.id = 'toggle';
  insert(toggleBtn, document.createTextNode('toggle'));

  const addBtn = document.createElement('button');
  addBtn.id = 'add';
  insert(addBtn, document.createTextNode('add'));

  function createRow(n) {
    const li = document.createElement('li');
    insert(li, document.createTextNode(n));
    return li;
  }
  list(ul, () => items.value, (n) => n, createRow, null);

  function cardBody() {
    const body = document.createElement('span');
    body.id = 'body';
    const bodyTx = document.createTextNode('');
    insert(body, bodyTx);
    effect(() => setText(bodyTx, count.value));
    return body;
  }
  list(card, () => (show.value) ? [0] : [], () => 0, cardBody, cardAnchor);

  function formInput() {
    const name = document.createElement('input');
    name.id = 'name';
    effect(() => { name.value = model.name.value; });
    listen(name, 'change', (e) => { model.name.value = e.target.value; });
    return name;
  }
  function formSave() {
    const save = document.createElement('button');
    save.id = 'save';
    setAttr(save, 'type', 'submit');
    insert(save, document.createTextNode('save'));
    return save;
  }
  list(form, () => (show.value) ? [0, 1] : [], (i) => i, (i) => i === 0 ? formInput() : formSave(), formAnchor);

  effect(() => setText(outTx, saved.value));

  listen(form, 'submit', (e) => {
    e.preventDefault();
    saved.value = model.name.value;
  });
  listen(toggleBtn, 'click', () => {
    show.value = !show.value;
  });
  listen(addBtn, 'click', () => batch(() => {
    count.value++;
    items.value = _pool.filter(x => x !== 2);
  }));

  // ATTACH, IN SOURCE ORDER. The cascade's <ul> sits BETWEEN #head and #tail because a cascade's
  // root-level children are the siblings they look like. Attaching them last -- or not at all, which
  // is what shipped -- renders a document the source never described.
  insert(target, head);
  insert(target, document.createTextNode('\n'));
  insert(target, ul);
  insert(target, document.createTextNode('\n'));
  insert(target, tail);
  insert(target, document.createTextNode('\n\n'));
  insert(target, card);
  insert(target, document.createTextNode('\n\n'));
  insert(target, form);
  insert(target, document.createTextNode('\n\n'));
  insert(target, out);
  insert(target, document.createTextNode('\n\n'));
  insert(target, toggleBtn);
  insert(target, document.createTextNode('\n'));
  insert(target, addBtn);
}
