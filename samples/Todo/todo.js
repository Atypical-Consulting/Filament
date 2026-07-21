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
 *     `t.id === editingId.value`; its two-node branch (input + save) mounts INSIDE the reused
 *     row, and the input's @bind works there because effect+listen land in the branch create.
 *   - THE COUNT IS COMPUTED (160): `left` derives from `visible` AND each row's `done` signal —
 *     the first generator use of the runtime's computed export.
 *
 * v2.1 — THE FILAMENT RESTYLE (BENCH n°67): dark stone panel, amber accent; the rows themselves
 * build the design signature — each li carries a border-l-2 wire segment, lit amber while
 * pending, extinguished to stone when done. The strike-through is the LABEL span's OWN reactive
 * ternary (a li-level line-through propagates into the flex items and strikes the action
 * buttons — the mirror caught it), so a row now carries TWO class effects. The toggle label is
 * a REACTIVE TERNARY in text position (`done`/`undo`): the same Expr fold the row class uses,
 * landing in setText — a per-row text effect after the branch list(). Static additions: the
 * shell's eyebrow line and the input's placeholder (static attrs emit verbatim, whatever their
 * name).
 *
 * State stays the Duel idiom — `tasks` mutated (version signal), `visible` reassigned (a signal
 * itself); decision 153 refuses the mixed shape on one field.
 *
 * The measurement (BENCH n°66/67): add via click + add via ENTER → toggle a persisting row (the
 * wire segment extinguishes, the label flips to undo, the computed count moves) → in-place edit
 * commits a new label → the stored JSON is asserted BYTE-EQUAL vs Blazor → the page RELOADS and
 * the app restores identically → clear-done (the child's EventCallback) → remove → empty,
 * storage "[]".
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
  setAttr(shell, 'class', 'mx-auto max-w-[42rem] rounded-2xl border border-stone-800 bg-stone-900 p-6 shadow-lg sm:px-4 md:px-8');
  const eyebrow = document.createElement('p');
  setAttr(eyebrow, 'class', 'font-mono text-[10px] uppercase tracking-[0.25em] text-stone-500');
  insert(eyebrow, document.createTextNode('filament · 1,943-byte runtime'));
  insert(shell, eyebrow);
  const title = document.createElement('h1');
  title.id = 'title';
  setAttr(title, 'class', '-mt-1 font-mono text-2xl font-semibold tracking-tight text-amber-50');
  insert(title, document.createTextNode('todos'));
  insert(shell, title);
  const editor = document.createElement('div');
  editor.id = 'editor';
  setAttr(editor, 'class', 'mt-5 flex gap-2');
  const input = document.createElement('input');
  input.id = 'new';
  setAttr(input, 'aria-label', 'New todo');
  setAttr(input, 'placeholder', 'what needs doing?');
  setAttr(input, 'class', 'w-1/2 grow rounded-lg border border-stone-700 bg-stone-950 px-3 py-2 text-amber-50 placeholder:text-stone-500 focus:outline-none focus:ring-2 focus:ring-amber-400/60');
  insert(editor, input);
  const addButton = document.createElement('button');
  addButton.id = 'add';
  setAttr(addButton, 'class', 'rounded-lg bg-amber-400 px-4 py-2 font-mono text-sm font-semibold text-stone-950 hover:bg-amber-300 disabled:opacity-50');
  insert(addButton, document.createTextNode('add'));
  insert(editor, addButton);
  insert(shell, editor);
  insert(shell, document.createTextNode('\n\n'));
  const listEl = document.createElement('ul');
  listEl.id = 'list';
  setAttr(listEl, 'class', 'mt-6');
  insert(shell, listEl);
  insert(shell, document.createTextNode('\n\n'));
  const footer = document.createElement('footer');
  footer.id = 'footer';
  setAttr(footer, 'class', 'mt-6 flex items-center justify-between border-t border-stone-800 pt-3');
  const leftSpan = document.createElement('span');
  leftSpan.id = 'left';
  setAttr(leftSpan, 'class', 'font-mono text-xs text-stone-400');
  const leftTx = document.createTextNode('');
  insert(leftSpan, leftTx);
  insert(footer, leftSpan);
  const clearButton = document.createElement('button');
  clearButton.id = 'clear';
  setAttr(clearButton, 'class', 'font-mono text-xs text-stone-500 hover:text-amber-300 hover:underline');
  insert(clearButton, document.createTextNode('clear done'));
  insert(footer, clearButton);
  insert(shell, footer);

  effect(() => { input.value = newText.value; });
  function createRow(t) {
    const row = document.createElement('li');
    const label = document.createElement('span');
    const labelTx = document.createTextNode('');
    insert(label, labelTx);
    insert(row, label);
    const editAnchor = document.createComment('');
    insert(row, editAnchor);
    const editButton = document.createElement('button');
    setAttr(editButton, 'class', 'edit rounded px-2 py-0.5 font-mono text-xs text-stone-500 hover:bg-stone-800 hover:text-amber-300');
    insert(editButton, document.createTextNode('edit'));
    insert(row, editButton);
    const toggleButton = document.createElement('button');
    setAttr(toggleButton, 'class', 'toggle rounded px-2 py-0.5 font-mono text-xs text-stone-500 hover:bg-stone-800 hover:text-amber-300');
    const toggleTx = document.createTextNode('');
    insert(toggleButton, toggleTx);
    insert(row, toggleButton);
    const removeButton = document.createElement('button');
    setAttr(removeButton, 'class', 'remove rounded px-2 py-0.5 font-mono text-xs text-stone-500 hover:bg-stone-800 hover:text-red-400');
    insert(removeButton, document.createTextNode('remove'));
    insert(row, removeButton);
    effect(() => setAttr(row, 'class', 'flex items-center gap-2 border-l-2 py-1.5 pl-4 transition-colors hover:bg-stone-800/50 ' + (t.done.value ? 'border-stone-700 text-stone-500' : 'border-amber-400 text-amber-50')));
    effect(() => setAttr(label, 'class', 'grow ' + (t.done.value ? 'line-through decoration-stone-600' : 'no-underline')));
    effect(() => setText(labelTx, t.label.value));
    function editInputBranch() {
      const editInput = document.createElement('input');
      setAttr(editInput, 'class', 'editbox w-40 rounded border border-stone-700 bg-stone-950 px-2 py-0.5 text-sm text-amber-50 focus:outline-none focus:ring-2 focus:ring-amber-400/60');
      effect(() => { editInput.value = editText.value; });
      listen(editInput, 'change', (e) => { editText.value = e.target.value; });
      return editInput;
    }
    function saveButtonBranch() {
      const saveButton = document.createElement('button');
      setAttr(saveButton, 'class', 'save rounded px-2 py-0.5 font-mono text-xs text-amber-300 hover:bg-stone-800');
      insert(saveButton, document.createTextNode('save'));
      listen(saveButton, 'click', () => batch(() => {
        saveEdit();
      }));
      return saveButton;
    }
    list(row, () => (t.id === editingId.value) ? [0, 1] : [], (i) => i, (i) => i === 0 ? editInputBranch() : saveButtonBranch(), editAnchor);
    effect(() => setText(toggleTx, t.done.value ? 'undo' : 'done'));
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
