/**
 * Rows — hand-written Filament app. Phase 1 reference.
 *
 * The answer key for baseline/Rows.Blazor/RowsApp.razor. Read that file
 * alongside this one: every construct here maps to a named construct there, and
 * Phase 2's generator is gated on emitting something EQUIVALENT to this from the
 * same .razor source. So this is written the way a compiler would emit it —
 * mechanically, statement by statement — not the way a human tuning a benchmark
 * would.
 *
 * ===========================================================================
 * THE FOUR MAPPING DECISIONS, and why each is one a generator can actually make
 * ===========================================================================
 *
 * (1) `List<Row> _rows` -> a MUTABLE array + a version Signal.
 *
 *     The tempting mapping is `Signal<Row[]>` with copy-on-write. It is wrong,
 *     and not for style reasons: `Run()` is `Clear(); for (1000) AddRow();`, and
 *     `AddRow` is `_rows.Add(row)`. Copy-on-write turns 1000 amortised-O(1)
 *     Adds into 1000 array copies — O(n^2), ~500k element copies per #run —
 *     against a C# List that does none of that. Filament would be handed an
 *     asymptotic handicap Blazor never pays, and C4's headline (create-warm) is
 *     exactly the number it would show up in.
 *
 *     A `List<T>` IS a mutable collection, so it maps to a mutable array. What
 *     reactivity needs on top is a way to say "the structure changed", which is
 *     the version signal: every mutating operation bumps it, and list()'s source
 *     reads it. That is a mechanical rule a generator applies per mutation site,
 *     not an insight.
 *
 * (2) `Row.Id` is a PLAIN field; `Row.Label` is a Signal.
 *
 *     The rule: a property is reactive iff it is assigned anywhere other than
 *     its object's construction site. `Label` is (`Update()` does `Label +=`),
 *     `Id` is not. That is a bog-standard escape analysis, not cleverness.
 *
 *     It is also FORCED here, twice over:
 *       - `@key="row.Id"` compiles to list()'s keyOf, which reconcile() calls
 *         with the list effect as the active subscriber. A signal read there
 *         would subscribe the list to all 1000 row ids — 1000 dependency edges
 *         whose only possible effect is to re-reconcile the entire table.
 *       - `@row.Id` therefore has a non-reactive source, so its binding compiles
 *         to a create-time text write, not an effect. One effect per row, and it
 *         is the one binding that can actually change.
 *
 * (3) Every `@onclick` handler body runs inside `batch()`.
 *
 *     Blazor renders ONCE per event handler, after the body returns. batch() is
 *     that semantic, and without it `Run()` is quadratic for a second reason:
 *     each of the 1001 version bumps would flush its own full reconcile.
 *
 * (4) The word lists are module consts; the LABELS are not.
 *
 *     `_adjectives`/`_colours`/`_nouns` are the same three literal lists Blazor
 *     holds as fields — inert data. The labels themselves are GENERATED per row
 *     at runtime by the LCG below: 3 double multiply/modulo draws and one
 *     three-part concatenation each, 3000 + 1000 per #run, exactly as Blazor
 *     does them. Hoisting a label, interning a string, or reusing a previous
 *     run's stream is the cheat this whole POC exists to not commit, and the
 *     harness checks for it two ways (a byte-exact oracle, and a distinct
 *     second-run stream).
 */

import { signal, effect, batch, setText, setAttr, listen, insert, list } from '../../src/filament-runtime/src/index.ts';

/* ---------------------------------------------------------------------------
 * The three word lists. `List<string> _adjectives = new List<string>{...}` &c.
 *
 * Data, not labels. Blazor holds these as instance fields; hoisting immutable
 * literal lists to module scope is a generator-level constant-folding decision
 * and changes nothing about the work done per row — the label is still three
 * draws and a concatenation, every time, for every row.
 * ------------------------------------------------------------------------- */

const _adjectives = [
  'pretty', 'large', 'big', 'small', 'tall', 'short', 'long', 'handsome',
  'plain', 'quaint', 'clean', 'elegant', 'easy', 'angry', 'crazy', 'helpful',
  'mushy', 'odd', 'unsightly', 'adorable', 'important', 'inexpensive',
  'cheap', 'expensive', 'fancy',
];

const _colours = [
  'red', 'yellow', 'blue', 'green', 'pink', 'brown', 'purple', 'brown',
  'white', 'black', 'orange',
];

const _nouns = [
  'table', 'chair', 'house', 'bbq', 'desk', 'car', 'pony', 'cookie',
  'sandwich', 'burger', 'pizza', 'mouse', 'keyboard',
];

/**
 * Compiled render for RowsApp.razor.
 *
 * @param {Node} target the mount point (#app).
 */
export function mount(target) {
  /* -------------------------------------------------------------------------
   * @code — component state.
   *
   * These are INSTANCE fields, so they live in the component's scope, and they
   * are initialised exactly once when the component is constructed: i.e. once
   * per page load. That is precisely the property the label contract depends on
   * (`_seed` is never re-seeded per #run), and it falls out of the field-
   * initialiser mapping rather than being a special case anyone had to remember.
   * ---------------------------------------------------------------------- */

  // `List<Row> _rows = new List<Row>();` — see mapping decision (1).
  const _rows = [];
  const _rowsVersion = signal(0);

  /** Bumped at every site that structurally mutates `_rows`. */
  function _rowsChanged() {
    _rowsVersion.value++;
  }

  // `int _nextId = 1;` — monotonic, NEVER reset. Not rendered reactively (it is
  // read into a row's plain `id` at construction), so it stays a plain number.
  let _nextId = 1;

  // `double _seed = 42.0;`
  //
  // Park-Miller in DOUBLE arithmetic, mirroring the C# comment: 16807 * 2^31 is
  // ~3.6e13 < 2^53, so every intermediate product is exactly representable as a
  // double in BOTH languages and the two label streams are byte-identical. Do
  // not "optimise" this to `| 0` / Math.imul: it would overflow differently and
  // break the cross-language parity that is the entire point.
  let _seed = 42.0;

  /** `double Next()` */
  function next() {
    _seed = (_seed * 16807.0) % 2147483647.0;
    return _seed;
  }

  /**
   * `string NextLabel()` — draws EXACTLY three times, in this order.
   *
   * `Math.trunc` is C#'s `(int)` cast on a double (truncate toward zero). The
   * seed is always positive so floor would agree, but trunc is the cast's actual
   * semantic and is what a generator emits for `(int)d`.
   */
  function nextLabel() {
    const a = Math.trunc(next() % 25.0);
    const c = Math.trunc(next() % 11.0);
    const n = Math.trunc(next() % 13.0);
    return _adjectives[a] + ' ' + _colours[c] + ' ' + _nouns[n];
  }

  /**
   * `void AddRow()`
   *
   * Field order matters and is preserved: Id is read from _nextId, THEN Label is
   * drawn (3 LCG draws), THEN _nextId advances, THEN the row is added.
   */
  function addRow() {
    const row = { id: _nextId, label: signal(nextLabel()) };
    _nextId += 1;
    _rows.push(row);
    _rowsChanged();
  }

  /** `void Clear()` — `for (i = Count-1; i >= 0; i--) _rows.RemoveAt(i);` */
  function clear() {
    for (let i = _rows.length - 1; i >= 0; i--) {
      _rows.splice(i, 1); // RemoveAt. From the tail, so O(1) each, as in C#.
      _rowsChanged();
    }
  }

  // `void Run()`, `void Update()` and `void SwapRows()` are NOT defined here:
  // each is named by exactly one @onclick and called from nowhere else, so
  // decision 68's single-use inlining folds each body straight into its handler
  // (see the events section below). `clear` and `addRow` DO stay functions:
  // `clear` is also a handler-referenced-from-`run`, `addRow` is called by
  // `run`, so neither is single-use. This is the owner's correction of decision
  // 80 — the answer key adopting the rule the generator already applied.

  /* -------------------------------------------------------------------------
   * create(): the static tree.
   * ---------------------------------------------------------------------- */

  // <div id="main">
  const main = document.createElement('div');
  main.id = 'main';

  // RowsApp.razor puts each <button> on its own line, and Razor turns the
  // newline+indent BETWEEN siblings into a real text node — Blazor ships four,
  // `AddMarkupContent(6/11/16/21, "\n    ")` in its own generated
  // BuildRenderTree. The answer key used to omit them; decision 80 (the owner's
  // call, decision 64's situation) restores them so the DOM contract is the
  // baseline's exactly. NOTE which way this cuts: it makes the module LARGER and
  // builds four DOM nodes — the correction costs Filament, it does not flatter it.

  // <button id="run" @onclick="Run">Create 1000 rows</button>
  const runBtn = document.createElement('button');
  runBtn.id = 'run';
  insert(runBtn, document.createTextNode('Create 1000 rows'));
  insert(main, runBtn);
  insert(main, document.createTextNode('\n    '));

  // <button id="update" @onclick="Update">Update every 10th row</button>
  const updateBtn = document.createElement('button');
  updateBtn.id = 'update';
  insert(updateBtn, document.createTextNode('Update every 10th row'));
  insert(main, updateBtn);
  insert(main, document.createTextNode('\n    '));

  // <button id="swaprows" @onclick="SwapRows">Swap Rows</button>
  const swapBtn = document.createElement('button');
  swapBtn.id = 'swaprows';
  insert(swapBtn, document.createTextNode('Swap Rows'));
  insert(main, swapBtn);
  insert(main, document.createTextNode('\n    '));

  // <button id="clear" @onclick="Clear">Clear</button>
  const clearBtn = document.createElement('button');
  clearBtn.id = 'clear';
  insert(clearBtn, document.createTextNode('Clear'));
  insert(main, clearBtn);
  insert(main, document.createTextNode('\n    '));

  // <table><tbody id="tbody">...</tbody></table>
  const table = document.createElement('table');
  const tbody = document.createElement('tbody');
  tbody.id = 'tbody';
  insert(table, tbody);
  insert(main, table);

  /* -------------------------------------------------------------------------
   * @foreach (Row row in _rows) { <tr @key="row.Id">...</tr> }
   * ---------------------------------------------------------------------- */

  /**
   * The row template. Called ONCE per key by list(), inside that row's own
   * disposal scope and untracked — so the effect below is adopted by the row and
   * dies with it. That is what stops #run leaking 1000 effects per iteration.
   *
   * Markup is the shared DOM contract, exactly:
   *   <tr><td class="col-md-1">{id}</td><td class="col-md-4"><a class="lbl">{label}</a></td></tr>
   * Exactly two <td> children of <tr>, no stray text nodes between them, and the
   * label inside a real <a class="lbl">. Blazor builds those 1000 <a> elements
   * and 2000 class attributes; skipping them would be a ~3000-node-per-#run
   * discount on the exact number C4 is decided by.
   */
  function createRow(row) {
    const tr = document.createElement('tr');

    // <td class="col-md-1">@row.Id</td>
    const td1 = document.createElement('td');
    setAttr(td1, 'class', 'col-md-1');
    // @row.Id — non-reactive source (mapping decision 2), so this is a plain
    // create-time write and gets no effect. createTextNode coerces the number
    // the same way the DOM would coerce it on any later write.
    insert(td1, document.createTextNode(row.id));
    insert(tr, td1);

    // <td class="col-md-4"><a class="lbl">@row.Label</a></td>
    const td2 = document.createElement('td');
    setAttr(td2, 'class', 'col-md-4');
    const a = document.createElement('a');
    setAttr(a, 'class', 'lbl');
    const labelText = document.createTextNode('');
    insert(a, labelText);
    insert(td2, a);
    insert(tr, td2);

    // @row.Label — THE binding point. One effect, closed over labelText, so
    // #update costs one character-data write per changed row.
    effect(() => setText(labelText, row.label.value));

    return tr;
  }

  list(
    tbody,
    () => {
      // Subscribe to structural changes, then hand reconcile the live array.
      // reconcile() only reads `items` during the pass; it never retains it.
      _rowsVersion.value;
      return _rows;
    },
    (row) => row.id, // @key="row.Id" — a plain read: the list must not subscribe.
    createRow,
    null,
  );

  /* -------------------------------------------------------------------------
   * @onclick handlers. batch() == Blazor's one render per event handler.
   *
   * Decision 68 (single-use inlining), applied on the owner's call (decision 80):
   * a method named by exactly ONE @onclick and called from nowhere else is
   * INLINED into its handler — `run`, `update` and `swapRows` are. `clear` is
   * NOT: `run` also calls it, so it stays a `function` above and is referenced
   * here. The handler is ALWAYS an arrow: addEventListener invokes its listener
   * with the DOM Event, so a bare reference would hand a zero-arg method one arg.
   * ---------------------------------------------------------------------- */

  // Run() — replace the table with exactly 1000 fresh rows.
  listen(runBtn, 'click', () => batch(() => {
    clear();
    for (let i = 0; i < 1000; i++) {
      addRow();
    }
  }));

  // Update() — append " !!!" to every 10th row's label, cumulatively. Writes
  // ONLY label signals: `_rows` is structurally untouched, so the version never
  // moves and list() never re-reconciles — 100 character-data writes, no diff,
  // no key map, no LIS, no row rebuilt. `+=` is the compound assignment VERBATIM
  // (decision 68, "no syntactic desugaring"): `Label += " !!!"` evaluates
  // `_rows[i]` exactly ONCE, where `x.value = x.value + y` would evaluate it twice.
  listen(updateBtn, 'click', () => batch(() => {
    for (let i = 0; i < _rows.length; i += 10) {
      _rows[i].label.value += ' !!!';
    }
  }));

  // SwapRows() — reciprocal swap of the row objects at 1 and 998. One logical
  // mutation; inside a batch the bump count is unobservable anyway.
  listen(swapBtn, 'click', () => batch(() => {
    if (_rows.length > 998) {
      const tmp = _rows[1];
      _rows[1] = _rows[998];
      _rows[998] = tmp;
      _rowsChanged();
    }
  }));

  // Clear() — kept a `function` above (Run also calls it), so referenced here.
  listen(clearBtn, 'click', () => batch(clear));

  insert(target, main);
}
