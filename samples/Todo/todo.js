/**
 * Todo — hand-written Filament answer key for baseline/Todo.Blazor/App.razor.
 *
 * THE POINT: Tailwind through the whole pipeline at once (decision 154). Every class below is a
 * real Tailwind utility, and the three attribute behaviours the program fixed/widened are all
 * live in one app:
 *
 *   - MULTI-TOKEN STATIC values with variant colons, fraction slashes, arbitrary-value brackets
 *     and leading dashes ('mx-auto max-w-[42rem] rounded-xl bg-white/90 …') survive byte-for-byte
 *     — Razor lowers them as several PREFIXED value nodes, and the prefix is part of the value
 *     (decision 151; the old concat welded them into one garbage class).
 *   - The REACTIVE ROW CLASS reads the loop variable: the fold sits in an effect INSIDE the row
 *     create function, over the record's per-record `done` signal, ternary PARENTHESISED (`+`
 *     binds tighter than `?:`) — so toggling a PERSISTING key restyles its reused row
 *     (decision 152; the #125 stale-row trap, applied to attributes).
 *   - A PLAIN @bind alone makes its field reactive: Razor's own lowering reads the field into
 *     `value` and assigns it in the binder, so `newText` is a signal even though no other display
 *     reads it (decision 154 lifting #104's deferral by #138's "a bind IS a write" argument).
 *
 * COMPOSITION CARRIES THE APP, fully erased: TodoShell places everything through ChildContent
 * (decision 131), TodoFooter takes the reactive bound string DOWN (decision 90) and raises
 * ClearDone UP through an EventCallback (decision 130). One mount(), no component instances.
 *
 * STATE IS THE DUEL IDIOM, and decision 153 is why it must be: `tasks` is mutated (Add/splice —
 * the version signal), `visible`/`leftText` are recomputed by REASSIGNMENT (signals themselves).
 * One field doing both is refused now — the mixed emission declared `const` and assigned to it.
 *
 * Blazor DOM contract (both shells must render byte-identical class attributes):
 *
 *   <section id="shell" class="mx-auto max-w-[42rem] rounded-xl bg-white/90 p-6 shadow-lg sm:px-4 md:px-8">
 *     <h1 id="title" class="text-2xl font-bold -mt-2">todos</h1>
 *     <div id="editor" class="flex gap-2">
 *       <input id="new" aria-label="New todo" class="w-1/2 rounded border px-3 py-2 focus:ring-2" />
 *       <button id="add" class="rounded bg-amber-500 px-4 py-2 hover:bg-amber-400 disabled:opacity-50">add</button>
 *     </div>
 *     \n\n
 *     <ul id="list" class="mt-4">
 *       <li class="flex gap-2 max-w-[42rem] text-slate-900|line-through text-slate-400">…</li>
 *     </ul>
 *     \n\n
 *     <footer id="footer" class="flex justify-between border-t pt-2">
 *       <span id="left" class="text-sm text-slate-500">N left</span>
 *       <button id="clear" class="text-sm hover:underline">clear done</button>
 *     </footer>
 *   </section>
 *
 * The measurement (BENCH n°65): add ×2 → toggle a PERSISTING row (its class gains line-through,
 * #left says "1 left") → #clear (the child's EventCallback runs the parent's ClearDone) → remove
 * → empty list, every className asserted byte-identical against Blazor at each step.
 */

import { signal, effect, batch, setText, setAttr, listen, insert, list } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const newText = signal('');
  const tasks = [];
  const tasksVersion = signal(0);

  function tasksChanged() {
    tasksVersion.value++;
  }

  const visible = signal([]);
  const leftText = signal('0 left');
  let nextId = 1;

  function refresh() {
    visible.value = tasks.filter(x => true);
    leftText.value = tasks.filter(x => !x.done.value).length + ' left';
  }

  function toggle(id) {
    for (let i = 0; i < tasks.length; i++) {
      if (tasks[i].id === id) {
        tasks[i].done.value = !tasks[i].done.value;
      }
    }
    refresh();
  }

  function remove(id) {
    for (let i = tasks.length - 1; i >= 0; i--) {
      if (tasks[i].id === id) {
        tasks.splice(i, 1);
        tasksChanged();
      }
    }
    refresh();
  }

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
    insert(label, document.createTextNode(t.label));
    insert(row, label);
    const toggleButton = document.createElement('button');
    setAttr(toggleButton, 'class', 'toggle rounded px-2 hover:bg-slate-200');
    insert(toggleButton, document.createTextNode('toggle'));
    insert(row, toggleButton);
    const removeButton = document.createElement('button');
    setAttr(removeButton, 'class', 'remove rounded px-2 hover:bg-red-200');
    insert(removeButton, document.createTextNode('remove'));
    insert(row, removeButton);
    effect(() => setAttr(row, 'class', 'flex gap-2 max-w-[42rem] ' + (t.done.value ? 'line-through text-slate-400' : 'text-slate-900')));
    listen(toggleButton, 'click', () => batch(() => {
      toggle(t.id);
    }));
    listen(removeButton, 'click', () => batch(() => {
      remove(t.id);
    }));
    return row;
  }
  list(listEl, () => visible.value, (t) => t.id, createRow, null);
  effect(() => setText(leftTx, leftText.value));

  listen(input, 'change', (e) => { newText.value = e.target.value; });
  listen(addButton, 'click', () => batch(() => {
    if (newText.value === '') {
      return;
    }
    const t = { id: nextId, label: newText.value, done: signal(false) };
    nextId = nextId + 1;
    tasks.push(t);
    tasksChanged();
    newText.value = '';
    refresh();
  }));
  listen(clearButton, 'click', () => batch(() => {
    for (let i = tasks.length - 1; i >= 0; i--) {
      if (tasks[i].done.value) {
        tasks.splice(i, 1);
        tasksChanged();
      }
    }
    refresh();
  }));

  insert(target, shell);
}
