/**
 * ListOps — hand-written Filament answer key for baseline/ListOps.Blazor/App.razor.
 *
 * List<T>.Clear() (decision 106): the last of the section-5 List operations. A List<T> maps to a live
 * array plus a version signal (rows.js mapping); .Clear() empties the array IN PLACE (`items.length = 0`)
 * and bumps the version, so the list() re-runs and @foreach reconciles to EMPTY. .Add pushes (as before).
 *
 * `items` is read by @foreach and mutated by Add/Clear -> a reactive List (its version signal). Add writes
 * more than once -> batch (#68); Clear is one mutation -> no batch.
 */

import { signal, batch, setAttr, listen, insert, list } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const items = [];
  const itemsVersion = signal(0);

  function itemsChanged() {
    itemsVersion.value++;
  }

  const ul = document.createElement('ul');
  ul.id = 'list';
  const add = document.createElement('button');
  add.id = 'add';
  insert(add, document.createTextNode('add'));
  insert(ul, add);
  const clear = document.createElement('button');
  clear.id = 'clear';
  insert(clear, document.createTextNode('clear'));
  insert(ul, clear);

  function createItem(item) {
    const li = document.createElement('li');
    setAttr(li, 'class', 'row');
    insert(li, document.createTextNode(item));
    return li;
  }
  list(ul, () => {
    itemsVersion.value;
    return items;
  }, (item) => item, createItem, null);

  listen(add, 'click', () => batch(() => {
    items.push('alpha');
    items.push('beta');
    items.push('gamma');
    itemsChanged();
  }));
  listen(clear, 'click', () => {
    items.length = 0;
    itemsChanged();
  });

  insert(target, ul);
}
