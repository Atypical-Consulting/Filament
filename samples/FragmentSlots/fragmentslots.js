/**
 * FragmentSlots — hand-written Filament answer key for baseline/FragmentSlots.Blazor/App.razor.
 *
 * THE POINT: A FRAGMENT HOLE HAS A NAME, AND A FRAGMENT CAN BE PASSED ON. Decision 168 closes three
 * silent divergences that decision 131 shipped together, all in one field: `Fragment? _fragment`,
 * single and un-named.
 *
 * Blazor DOM contract (same shape Blazor renders <App> into #app):
 *
 *   <div id="wrap">
 *     <div id="card">                        <!-- <Card>: TWO holes, Header and ChildContent -->
 *       <h3 id="title">hits</h3>
 *       <div id="head"></div>                <!-- EMPTY: bare content binds to ChildContent ALONE -->
 *       <div id="body"><span id="mark">0</span></div>
 *     </div>
 *     <div id="card2">                       <!-- <Slotted>: a NAMED hole, `Body` -->
 *       <h3 id="title2">named</h3>
 *       <span id="slot">0</span>             <!-- ...filled by <Body>, NOT by Body.razor -->
 *     </div>
 *     <div id="inner">                       <!-- <Middle> forwarded its content to <Inner> -->
 *       <span id="deep">0</span>
 *     </div>
 *     <button id="inc">inc</button>
 *   </div>
 *
 * There is no #decoy anywhere, and exactly ONE #mark. Clicking #inc moves all three of #mark, #slot
 * and #deep "0" -> "1" -> "2" — every one of them is a live binding on the SAME parent signal, which
 * is what makes the three counters one measurement rather than three.
 *
 * 1. THE HOLE IS KEYED BY NAME. Card declares Header AND ChildContent, and Razor's own codegen for
 *    this source is `AddAttribute(4, "ChildContent", …)` with NO Header attribute — an unassigned
 *    RenderFragment renders nothing, so #head is empty. One un-named fragment could not miss, so it
 *    was inlined at BOTH holes: two elements carrying id='mark', two effects on one signal, exit 0.
 *    The answer key builds #head and leaves it childless, because the emptiness is the claim.
 *
 * 2. THE SLOT NAME BEATS THE SIBLING FILE. `<Slotted><Body>…</Body></Slotted>` where Slotted declares
 *    a `Body` fragment names the child's HOLE. Blazor: `AddAttribute(10, "Body", …)`, and
 *    OpenComponent<…Body> appears NOWHERE — even though Body.razor exists next door and renders
 *    #decoy. Resolving the sibling first emitted the decoy instead, and emitted the SAME BYTES
 *    whether or not the child declared `Body`: invariant to the declaration that decides the meaning.
 *
 * 3. A FORWARD IS A SCOPE, NOT A COPY. Middle.razor renders `<Inner>@ChildContent</Inner>`: it does
 *    not place the content, it passes it on. Emitting a fragment used to CLEAR the slot map first —
 *    correct as far as it went (it is what stops a fragment containing @ChildContent from re-inlining
 *    itself), but it left Middle's own hole resolving against nothing, and the grandparent's markup
 *    was dropped in total silence. The map to consult inside a fragment is the one in scope where
 *    that fragment was WRITTEN, which for Middle's content is App's. Hence #deep, inside #inner,
 *    still bound to App's `count`.
 *
 * ZERO RUNTIME PRIMITIVES, as decision 131 already established: a fragment is a compile-time splice
 * of one subtree into another's position. Naming the holes and forwarding them costs nothing at run
 * time either — this module imports exactly what the counter imports.
 */

import { signal, effect, setText, listen, insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const count = signal(0);

  const wrap = document.createElement('div');
  wrap.id = 'wrap';

  // --- <Card Title="hits"><span id="mark">@count</span></Card> ----------------
  const card = document.createElement('div');
  card.id = 'card';

  const title = document.createElement('h3');
  title.id = 'title';
  insert(title, document.createTextNode('hits'));
  insert(card, title);

  // @Header: the parent bound nothing to this name, so nothing is emitted here. A null
  // RenderFragment renders as nothing in Blazor, and "nothing" is the faithful answer.
  const head = document.createElement('div');
  head.id = 'head';
  insert(card, head);

  const body = document.createElement('div');
  body.id = 'body';
  const mark = document.createElement('span');
  mark.id = 'mark';
  const markTx = document.createTextNode('');
  insert(mark, markTx);
  insert(body, mark);
  insert(card, body);

  insert(wrap, card);

  // --- <Slotted Title="named"><Body><span id="slot">@count</span></Body></Slotted> ---
  const card2 = document.createElement('div');
  card2.id = 'card2';

  const title2 = document.createElement('h3');
  title2.id = 'title2';
  insert(title2, document.createTextNode('named'));
  insert(card2, title2);

  const slot = document.createElement('span');
  slot.id = 'slot';
  const slotTx = document.createTextNode('');
  insert(slot, slotTx);
  insert(card2, slot);

  insert(wrap, card2);

  // --- <Middle><span id="deep">@count</span></Middle>, forwarded through <Inner> ---
  const inner = document.createElement('div');
  inner.id = 'inner';

  const deep = document.createElement('span');
  deep.id = 'deep';
  const deepTx = document.createTextNode('');
  insert(deep, deepTx);
  insert(inner, deep);

  insert(wrap, inner);

  const incBtn = document.createElement('button');
  incBtn.id = 'inc';
  insert(incBtn, document.createTextNode('inc'));
  insert(wrap, incBtn);

  effect(() => setText(markTx, count.value));
  effect(() => setText(slotTx, count.value));
  effect(() => setText(deepTx, count.value));

  listen(incBtn, 'click', () => {
    count.value++;
  });

  insert(target, wrap);
}
