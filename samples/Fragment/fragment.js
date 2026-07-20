/**
 * Fragment — hand-written Filament answer key for baseline/Fragment.Blazor/App.razor.
 *
 * THE POINT: the STRUCTURAL half of composition. #88/#90 passed a VALUE down, #130 passed an EVENT
 * up; this passes MARKUP. `<Card Title="hits"><span id="body">@count</span></Card>` hands the child
 * everything between the tags as its [Parameter] RenderFragment ChildContent, and the CHILD decides
 * where that content sits. Decision 131.
 *
 * Blazor DOM contract (same shape Blazor renders <App> into #app):
 *
 *   <div id="wrap">
 *     <div id="card">                  <!-- the CHILD's element -->
 *       <h3 id="title">hits</h3>       <!-- the child's own markup, static Title folded -->
 *       <span id="body">0</span>       <!-- the PARENT's markup, placed by the child -->
 *     </div>
 *     <button id="inc">inc</button>
 *   </div>
 *
 * Clicking #inc moves #body "0" -> "1" -> "2". That is the measurement (BENCH n°50), and it is the
 * part a naive implementation gets wrong: it is easy to render the fragment's markup and lose its
 * BINDING, leaving #body at "0" forever.
 *
 * WHY THE BINDING SURVIVES. The fragment is compiled in the scope it was WRITTEN in — the parent's —
 * even though it is emitted at a position the child chose. So `@count` inside it resolves to the
 * parent's own `count` signal, which is in scope because the whole composition inlines into ONE
 * mount(). Hence `effect(() => setText(tx, count.value))` sitting under an element the child created.
 * There is no RenderFragment object here, no delegate and no second render pass: a fragment is a
 * COMPILE-TIME splice of one subtree into another's position.
 *
 * ORDER IS THE CHILD'S. #title comes before #body because Card.razor writes `<h3>@Title</h3>` then
 * `@ChildContent`. The parent supplies content; it does not choose placement.
 */

import { signal, effect, setText, listen, insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const count = signal(0);

  const wrap = document.createElement('div');
  wrap.id = 'wrap';

  const card = document.createElement('div');
  card.id = 'card';

  const title = document.createElement('h3');
  title.id = 'title';
  insert(title, document.createTextNode('hits'));
  insert(card, title);

  const body = document.createElement('span');
  body.id = 'body';
  const tx = document.createTextNode('');
  insert(body, tx);
  insert(card, body);

  insert(wrap, card);

  const incBtn = document.createElement('button');
  incBtn.id = 'inc';
  insert(incBtn, document.createTextNode('inc'));
  insert(wrap, incBtn);

  effect(() => setText(tx, count.value));

  listen(incBtn, 'click', () => {
    count.value++;
  });

  insert(target, wrap);
}
