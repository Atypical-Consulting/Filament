/**
 * RowActions — hand-written Filament answer key for baseline/RowActions.Blazor/App.razor.
 *
 * THE POINT: a PER-ROW event handler (decision 141) — `@onclick="() => Del(r.Id)"` on a button
 * inside a keyed @foreach row, the lambda CAPTURING the loop variable. Decision 105 wrapped lambda
 * bodies as synthetic methods at CLASS scope, where a loop variable does not exist, so this idiom
 * (the delete/select/toggle button of every real list app) was refused with a message that wrongly
 * blamed the input ("Blazor would refuse to build this file too" — it builds it fine).
 *
 * THE MAPPING, and why it is almost nothing: the row template is already a FUNCTION taking the row
 * (createR(r), decision 124's list() shape) — so a lambda that captures the loop variable in C# is
 * an arrow that captures the function parameter in JS. `() => Del(r.Id)` compiles INSIDE createR to
 * `() => batch(() => { del(r.id); })` and the closure does exactly what C#'s does. The listener is wired
 * at row CREATE time and dies with the row (list()'s per-row disposal scope) — no delegation table,
 * no row registry, no runtime addition.
 *
 * WHY batch(): Del's body is a loop that can splice + bump the version more than once (statically),
 * and a handler whose transitive writes exceed one is batched (decision 68; rows.js batches clear
 * for exactly this shape). del STAYS a function (it is not itself the handler, it is CALLED with an
 * argument from the arrow, so single-use inlining does not fold it away).
 *
 * Blazor DOM contract: #list holds #add then the rows. Two #add clicks -> <li>task 1 x</li>
 * <li>task 2 x</li> ("task 1xtask 2x" as observed text, count 2). Clicking the FIRST row's .del
 * removes THAT row -> "task 2x", count 1 — Blazor's captured-lambda behaviour, byte-for-byte the
 * same observable DOM.
 *
 * ROW FIELDS: r.id is the @key identity (plain, decision 2); r.label is written only at
 * construction, never reassigned after insertion -> NOT a signal, a plain create-time text write.
 * The record construction folds to one object literal (rows.js's addRow shape), fields in C#
 * assignment order, _next advancing between the literal and the push exactly as the C# does.
 */

import { signal, batch, setAttr, listen, insert, list } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const _rows = [];
  const _rowsVersion = signal(0);

  function _rowsChanged() {
    _rowsVersion.value++;
  }

  let _next = 1;

  /** `void Del(int id)` — RemoveAt from the tail (O(1) each, as in C#), version bump per removal. */
  function del(id) {
    for (let i = _rows.length - 1; i >= 0; i--) {
      if (_rows[i].id === id) {
        _rows.splice(i, 1);
        _rowsChanged();
      }
    }
  }

  const ul = document.createElement('ul');
  ul.id = 'list';
  const addBtn = document.createElement('button');
  addBtn.id = 'add';
  insert(addBtn, document.createTextNode('add'));
  insert(ul, addBtn);

  function createR(r) {
    const li = document.createElement('li');
    const span = document.createElement('span');
    setAttr(span, 'class', 'lbl');
    insert(span, document.createTextNode(r.label));
    insert(li, span);
    const btn = document.createElement('button');
    setAttr(btn, 'class', 'del');
    insert(btn, document.createTextNode('x'));
    insert(li, btn);
    listen(btn, 'click', () => batch(() => {
      del(r.id);
    }));
    return li;
  }
  list(ul, () => {
    _rowsVersion.value;
    return _rows;
  }, (r) => r.id, createR, null);

  // `Add` is named by exactly one @onclick and called nowhere else -> its body inlines into the
  // click handler (decision 68's single-use rule). ONE version bump -> no batch.
  listen(addBtn, 'click', () => {
    const r = { id: _next, label: 'task ' + _next };
    _next = _next + 1;
    _rows.push(r);
    _rowsChanged();
  });

  insert(target, ul);
}
