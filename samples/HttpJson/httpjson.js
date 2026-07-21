/**
 * HttpJson — hand-written Filament answer key for baseline/HttpJson.Blazor/App.razor.
 *
 * HttpClient -> fetch (decision 147). The honesty argument mirrors decision 133's: Blazor WASM's
 * HttpClient is implemented ON TOP of fetch -- the bridge exists because .NET must marshal across
 * a boundary, and this module IS the other side, so the bridge erases:
 *
 *   await Http.GetFromJsonAsync<List<Item>>("data/items.json")  ->  await __getJson('data/items.json')
 *
 * __getJson carries GetFromJsonAsync's semantics: throw on !ok (EnsureSuccess, catchable with
 * #110's try/catch), JSON.parse, then __camel lowercases each key's FIRST character -- faithful to
 * System.Text.Json's Web defaults (camelCase + case-insensitive) for the Pascal/camel case real
 * APIs use. The type argument is GATED to JSON-faithful shapes (int/double/bool/string, records,
 * List/array of those); long/decimal/DateTime/float members are refused with their reasons.
 *
 * `items` is a reassigned List read by @foreach -> a signal, list()'s own source (decision 140);
 * `status` a plain string signal. The single-use async handler inlines (decisions 57/119), its two
 * writes batch (decision 68), and `data != null` is `data !== null` -- the same reference test.
 */

import { signal, effect, batch, setText, listen, insert, list } from '../../src/filament-runtime/src/index.ts';

// -- HTTP: C# HttpClient erased to fetch (decision 147) --
function __camel(v) {
  if (Array.isArray(v)) return v.map(__camel);
  if (v && typeof v === 'object') {
    const o = {};
    for (const k of Object.keys(v)) o[k.charAt(0).toLowerCase() + k.slice(1)] = __camel(v[k]);
    return o;
  }
  return v;
}

async function __getJson(u) {
  const r = await fetch(u);
  if (!r.ok) throw new Error('Response status code does not indicate success: ' + r.status);
  return __camel(await r.json());
}

export function mount(target) {
  const items = signal([]);
  const status = signal('idle');

  const ul = document.createElement('ul');
  ul.id = 'list';

  const statusP = document.createElement('p');
  const statusSpan = document.createElement('span');
  statusSpan.id = 'status';
  const statusText = document.createTextNode('');
  insert(statusSpan, statusText);
  insert(statusP, statusSpan);

  const loadButton = document.createElement('button');
  loadButton.id = 'load';
  insert(loadButton, document.createTextNode('load'));

  function createRow(it) {
    const li = document.createElement('li');
    insert(li, document.createTextNode(it.name));
    insert(li, document.createTextNode(': '));
    insert(li, document.createTextNode(it.rank));
    return li;
  }
  list(ul, () => items.value, (it) => it.name, createRow, null);
  effect(() => setText(statusText, status.value));

  listen(loadButton, 'click', () => batch(async () => {
    const data = await __getJson('data/items.json');
    if (data !== null) {
      items.value = data;
      status.value = 'loaded ' + items.value.length;
    }
  }));

  insert(target, ul);
  insert(target, document.createTextNode('\n\n'));
  insert(target, statusP);
  insert(target, document.createTextNode('\n\n'));
  insert(target, loadButton);
}
