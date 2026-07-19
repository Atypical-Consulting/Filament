/**
 * PositionalRecord — hand-written Filament answer key for baseline/PositionalRecord.Blazor/App.razor.
 *
 * A POSITIONAL record (decision 111): `record Item(string Name, int Rank)`. A positional record is the SAME
 * data shape a body record declares, written shorter, so it compiles to the SAME object literal — its
 * generated ctor/Equals/GetHashCode/Deconstruct are simply unused (a read-only shape, and the subset admits
 * neither value-equality nor deconstruction). Construction is INLINE and maps by CONSTRUCTOR ORDER:
 *   new Item("alpha", 1)  ->  { name: 'alpha', rank: 1 }
 * once in the seed list literal, once inside .Add (-> push) on click.
 *
 * `items` is read by @foreach AND mutated by Add, so it is a version signal (rows.js decision 1): the list()
 * source reads itemsVersion.value and returns the live array, and Add pushes then bumps the version so the
 * list reconciles. Each Item's props are read-only (never assigned after construction) -> plain, never signals.
 * @key="item.Name" is the identity list() reconciles against. DOM: #list goes one <li> -> two.
 */

import { signal, setAttr, listen, insert, list } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const items = [{ name: 'alpha', rank: 1 }];
  const itemsVersion = signal(0);

  function bumpItems() {
    itemsVersion.value++;
  }

  const ul = document.createElement('ul');
  ul.id = 'list';
  const button = document.createElement('button');
  button.id = 'add';
  insert(button, document.createTextNode('add'));

  function createRow(item) {
    const li = document.createElement('li');
    setAttr(li, 'class', 'row');
    insert(li, document.createTextNode(item.name));
    insert(li, document.createTextNode(': '));
    insert(li, document.createTextNode(item.rank));
    return li;
  }
  list(ul, () => {
    itemsVersion.value;
    return items;
  }, (item) => item.name, createRow, null);

  listen(button, 'click', () => {
    items.push({ name: 'beta', rank: 2 });
    bumpItems();
  });

  insert(target, ul);
  insert(target, document.createTextNode('\n\n'));
  insert(target, button);
}
