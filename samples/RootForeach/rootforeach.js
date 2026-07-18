/**
 * RootForeach — hand-written Filament answer key for baseline/RootForeach.Blazor/App.razor.
 *
 * THE POINT: a root-level @foreach with NO wrapping element. Its list() reconciles directly
 * into `target` (the mount point, #app), not into a created wrapper. This is #77's THIRD and
 * last disclosed false positive, closed by decision 89: when the component's root itself holds
 * template C#, the method is the region container and the mapping attaches to target.
 *
 * Blazor DOM contract (same shape Blazor renders <App> into #app):
 *
 *   <button id="add">add</button>
 *   <li class="row">alpha</li>
 *   <li class="row">beta</li>
 *   <li class="row">gamma</li>
 *
 * The button and the three rows are ALL direct children of #app -- Blazor renders a component's
 * content straight into its mount element, no wrapper, and so does this. The rows appear only
 * AFTER #add is clicked: @foreach needs a REACTIVE collection, and a never-mutated list is
 * refused (it has no version signal for list() to re-run on), so the list starts empty and the
 * click populates it. The click IS the measurement (BENCH n°11) -- three rows reconciled into
 * target is the mapping under test.
 *
 * THE REACTIVE-LIST SHAPE (Rows' mapping, decisions 54/80): `items` lifts to a plain array plus
 * an `itemsVersion` signal; the mutating method bumps the version (itemsChanged), and list()'s
 * source READS the version then returns the LIVE array. list()'s keyOf is @key="item" -- the
 * string value itself, a stable non-reactive identity. anchor is null: a root list with nothing
 * after it appends to target, so unlike @if there is NO comment anchor node here.
 *
 * THE HANDLER. Add() performs THREE writes (three items.Add), so per decision 68's batch rule
 * (batch iff there is more than one write to coalesce) it is wrapped in batch(); the three
 * pushes and the version bump collapse into one reconcile pass. Add is named by exactly one
 * @onclick and called nowhere else, so decision 68's single-use inlining folds its body straight
 * into the click handler. This key mirrors the generator's actual emission.
 */

import { signal, batch, setAttr, listen, insert, list } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const items = [];
  const itemsVersion = signal(0);

  function itemsChanged() {
    itemsVersion.value++;
  }

  const addBtn = document.createElement('button');
  addBtn.id = 'add';
  insert(addBtn, document.createTextNode('add'));
  insert(target, addBtn);

  function createItem(item) {
    const li = document.createElement('li');
    setAttr(li, 'class', 'row');
    insert(li, document.createTextNode(item));
    return li;
  }
  list(target, () => {
    itemsVersion.value;
    return items;
  }, (item) => item, createItem, null);

  listen(addBtn, 'click', () => batch(() => {
    items.push('alpha');
    items.push('beta');
    items.push('gamma');
    itemsChanged();
  }));
}
