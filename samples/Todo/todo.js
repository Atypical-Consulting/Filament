/**
 * Todo v2 — hand-written Filament answer key for baseline/Todo.Blazor/App.razor.
 *
 * v1 (decision 154, BENCH n°65) proved the Tailwind class surface. v2 (decision 161, BENCH n°66)
 * is the REAL app the todo-v2 program's five widenings buy:
 *
 *   - PERSISTENCE (156+157+133): onInitializedAsync() is called UN-AWAITED before create() —
 *     its continuation seeds `tasks` from localStorage through the ERASED IJSRuntime bridge and
 *     the per-record JSON converters. __serItem writes the DECLARED PascalCase names reading
 *     `.value` off the signal props; __desItem re-wraps Label/Done in signal() — so the STORED
 *     STRING is byte-identical to what Blazor's real System.Text.Json writes, and the oracle
 *     asserts exactly that.
 *   - ENTER ADDS (159): the keydown listener's arrow BINDS the event; `e.key` IS the DOM's key.
 *   - IN-PLACE EDIT (158+152): the row's @if is a row-anchored list() over
 *     `t.id === editingId.value`; its two-node branch (input + ok) mounts INSIDE the reused row,
 *     and the input's @bind works there because effect+listen land in the branch create.
 *   - THE COUNT IS COMPUTED (160): `left` derives from `visible` AND each row's `done` signal —
 *     the first generator use of the runtime's computed export.
 *
 * State stays the Duel idiom — `tasks` mutated (version signal), `visible` reassigned (a signal
 * itself); decision 153 refuses the mixed shape on one field.
 *
 * The measurement (BENCH n°66): add via click + add via ENTER → toggle a persisting row (class
 * flips to line-through, computed count moves) → in-place edit commits a new label → the stored
 * JSON is asserted BYTE-EQUAL vs Blazor → the page RELOADS and the app restores identically →
 * clear-done (the child's EventCallback) → remove → empty, storage "[]".
 */

import { signal, computed, effect, batch, setText, setAttr, listen, insert, list } from '../../src/filament-runtime/src/index.ts';

function __desItem(o) {
  return { id: o.Id, label: signal(o.Label), done: signal(o.Done) };
}
function __serItem(v) {
  return { Id: v.id, Label: v.label.value, Done: v.done.value };
}

export function mount(target) {
  const newText = signal('');
  const tasks = [];
  const tasksVersion = signal(0);

  function tasksChanged() {
    tasksVersion.value++;
  }

  const visible = signal([]);
  const editingId = signal(0);
  const editText = signal('');
  let nextId = 1;
  const left = computed(() => visible.value.filter(x => !x.done.value).length + ' left');

  function refresh() {
    visible.value = tasks.filter(x => true);
  }

  async function onInitializedAsync() {
    const raw = await localStorage.getItem('todos');
    if (raw !== null) {
      const data = JSON.parse(raw).map(__desItem);
      if (data !== null) {
        for (let i = 0; i < data.length; i++) {
          tasks.push(data[i]);
          tasksChanged();
          if (data[i].id >= nextId) {
            nextId = data[i].id + 1;
          }
        }
        refresh();
      }
    }
  }

  async function save() {
    await localStorage.setItem('todos', JSON.stringify(tasks.map(__serItem)));
  }

  async function add() {
    if (newText.value === '') {
      return;
    }
    const t = { id: nextId, label: signal(newText.value), done: signal(false) };
    nextId = nextId + 1;
    tasks.push(t);
    tasksChanged();
    newText.value = '';
    refresh();
    await save();
  }

  async function toggle(id) {
    for (let i = 0; i < tasks.length; i++) {
      if (tasks[i].id === id) {
        tasks[i].done.value = !tasks[i].done.value;
      }
    }
    refresh();
    await save();
  }

  async function remove(id) {
    for (let i = tasks.length - 1; i >= 0; i--) {
      if (tasks[i].id === id) {
        tasks.splice(i, 1);
        tasksChanged();
      }
    }
    refresh();
    await save();
  }

  function startEdit(id) {
    editingId.value = id;
    for (let i = 0; i < tasks.length; i++) {
      if (tasks[i].id === id) {
        editText.value = tasks[i].label.value;
      }
    }
  }

  async function saveEdit() {
    for (let i = 0; i < tasks.length; i++) {
      if (tasks[i].id === editingId.value) {
        tasks[i].label.value = editText.value;
      }
    }
    editingId.value = 0;
    refresh();
    await save();
  }

  onInitializedAsync();

  const shell = document.createElement('section');
  shell.id = 'shell';
  setAttr(shell, 'class', 'mx-auto max-w-[42rem] rounded-xl bg-white/90 p-6 shadow-lg sm:px-4 md:px-8');
  const title = document.createElement('h1');
  title.id = 'title';
  setAttr(title, 'class', 'text-2xl font-bold -mt-2');
  insert(title, document.createTextNode('todos'));
  insert(shell, title);
  const editor = document.createElement('div');
  editor.id = 'editor';
  setAttr(editor, 'class', 'flex gap-2');
  const input = document.createElement('input');
  input.id = 'new';
  setAttr(input, 'aria-label', 'New todo');
  setAttr(input, 'class', 'w-1/2 rounded border px-3 py-2 focus:ring-2');
  insert(editor, input);
  const addButton = document.createElement('button');
  addButton.id = 'add';
  setAttr(addButton, 'class', 'rounded bg-amber-500 px-4 py-2 hover:bg-amber-400 disabled:opacity-50');
  insert(addButton, document.createTextNode('add'));
  insert(editor, addButton);
  insert(shell, editor);
  insert(shell, document.createTextNode('\n\n'));
  const listEl = document.createElement('ul');
  listEl.id = 'list';
  setAttr(listEl, 'class', 'mt-4');
  insert(shell, listEl);
  insert(shell, document.createTextNode('\n\n'));
  const footer = document.createElement('footer');
  footer.id = 'footer';
  setAttr(footer, 'class', 'flex justify-between border-t pt-2');
  const leftSpan = document.createElement('span');
  leftSpan.id = 'left';
  setAttr(leftSpan, 'class', 'text-sm text-slate-500');
  const leftTx = document.createTextNode('');
  insert(leftSpan, leftTx);
  insert(footer, leftSpan);
  const clearButton = document.createElement('button');
  clearButton.id = 'clear';
  setAttr(clearButton, 'class', 'text-sm hover:underline');
  insert(clearButton, document.createTextNode('clear done'));
  insert(footer, clearButton);
  insert(shell, footer);

  effect(() => { input.value = newText.value; });
  function createRow(t) {
    const row = document.createElement('li');
    const label = document.createElement('span');
    setAttr(label, 'class', 'grow');
    const labelTx = document.createTextNode('');
    insert(label, labelTx);
    insert(row, label);
    const editAnchor = document.createComment('');
    insert(row, editAnchor);
    const editButton = document.createElement('button');
    setAttr(editButton, 'class', 'edit rounded px-2 hover:bg-amber-200');
    insert(editButton, document.createTextNode('edit'));
    insert(row, editButton);
    const toggleButton = document.createElement('button');
    setAttr(toggleButton, 'class', 'toggle rounded px-2 hover:bg-slate-200');
    insert(toggleButton, document.createTextNode('toggle'));
    insert(row, toggleButton);
    const removeButton = document.createElement('button');
    setAttr(removeButton, 'class', 'remove rounded px-2 hover:bg-red-200');
    insert(removeButton, document.createTextNode('remove'));
    insert(row, removeButton);
    effect(() => setAttr(row, 'class', 'flex gap-2 max-w-[42rem] ' + (t.done.value ? 'line-through text-slate-400' : 'text-slate-900')));
    effect(() => setText(labelTx, t.label.value));
    function editInputBranch() {
      const editInput = document.createElement('input');
      setAttr(editInput, 'class', 'editbox rounded border px-1');
      effect(() => { editInput.value = editText.value; });
      listen(editInput, 'change', (e) => { editText.value = e.target.value; });
      return editInput;
    }
    function okButtonBranch() {
      const okButton = document.createElement('button');
      setAttr(okButton, 'class', 'save rounded px-2 hover:bg-emerald-200');
      insert(okButton, document.createTextNode('ok'));
      listen(okButton, 'click', () => batch(() => {
        saveEdit();
      }));
      return okButton;
    }
    list(row, () => (t.id === editingId.value) ? [0, 1] : [], (i) => i, (i) => i === 0 ? editInputBranch() : okButtonBranch(), editAnchor);
    listen(editButton, 'click', () => batch(() => {
      startEdit(t.id);
    }));
    listen(toggleButton, 'click', () => batch(() => {
      toggle(t.id);
    }));
    listen(removeButton, 'click', () => batch(() => {
      remove(t.id);
    }));
    return row;
  }
  list(listEl, () => visible.value, (t) => t.id, createRow, null);
  effect(() => setText(leftTx, left.value));

  listen(input, 'change', (e) => { newText.value = e.target.value; });
  listen(input, 'keydown', (e) => {
    batch(() => {
      if (e.key === 'Enter') {
        add();
      }
    });
  });
  listen(addButton, 'click', () => batch(add));
  listen(clearButton, 'click', () => batch(() => {
    for (let i = tasks.length - 1; i >= 0; i--) {
      if (tasks[i].done.value) {
        tasks.splice(i, 1);
        tasksChanged();
      }
    }
    refresh();
    save();
  }));

  insert(target, shell);
}
