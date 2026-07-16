#!/usr/bin/env node
/**
 * selftest.mjs — proves the harness before it is trusted with real numbers.
 *
 * Generates throwaway vanilla-JS fixtures (no framework, no .NET) that implement the
 * shared DOM contract, then:
 *   1. Asserts the server's brotli/gzip negotiation, Content-Encoding, Content-Type
 *      and no-store behaviour at the HTTP level, with raw sockets (no
 *      auto-decompression) — including that a br-capable client gets br, and that
 *      extensions no allowlist author anticipated are still compressed.
 *   2. Runs the REAL bench.mjs end-to-end against the fixture and asserts the CDP
 *      encodedDataLength summing returns non-zero, captures lazy-loaded assets,
 *      and agrees with the server's independent byte ledger.
 *   3. Asserts the async-click race is actually defeated: the conforming fixture
 *      defers every DOM mutation by 60 ms behind a microtask + timer (exactly the
 *      shape of Blazor's @onclick dispatch). A harness with the naive
 *      rAF-after-click bug would report < 16 ms. We assert every median is >= 55 ms.
 *   4. Asserts a non-responding button is reported as a FAILURE, never a number.
 *   5. Asserts that apps doing LESS WORK than the contract requires (constant
 *      labels, a one-row #update, a duplicating #swaprows) are REFUSED with exit 3
 *      rather than reported as fast. This is the failure mode the whole project
 *      rests on not happening.
 *   6. Asserts sub-millisecond work is measurable, i.e. the headline timing is not
 *      quantized to the vsync interval by the harness's own frame wait.
 *
 * The fixtures double as proof of framework-agnosticism: the harness drives plain
 * DOM APIs with zero knowledge of what produced them.
 *
 * Usage: node selftest.mjs [--keep]
 */

import fsp from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import http from 'node:http';
import zlib from 'node:zlib';
import crypto from 'node:crypto';
import { startServer, acceptsGzip, acceptsBrotli, negotiateEncoding, isCompressible } from './server.mjs';
import { main as benchMain, quantile, summarize, loadExpectedLabels, findUntrackedRequests } from './bench.mjs';

const ASYNC_DELAY_MS = 60;

// ---------------------------------------------------------------------------
// Tiny assertion framework
// ---------------------------------------------------------------------------
let passed = 0;
const failures = [];

function ok(cond, name, detail) {
  if (cond) {
    passed += 1;
    process.stdout.write(`  ok   ${name}\n`);
  } else {
    failures.push({ name, detail });
    process.stdout.write(`  FAIL ${name}${detail ? ` — ${detail}` : ''}\n`);
  }
}
function eq(actual, expected, name) {
  ok(actual === expected, name, `expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`);
}
function section(title) {
  process.stdout.write(`\n${title}\n${'-'.repeat(title.length)}\n`);
}

// ---------------------------------------------------------------------------
// Raw HTTP client — node:fetch auto-decompresses and injects accept-encoding,
// which would hide exactly what we need to observe.
// ---------------------------------------------------------------------------
function rawGet(port, host, urlPath, headers = {}) {
  return new Promise((resolve, reject) => {
    const req = http.request({ host, port, path: urlPath, method: 'GET', headers }, (res) => {
      const chunks = [];
      res.on('data', (c) => chunks.push(c));
      res.on('end', () => resolve({ status: res.statusCode, headers: res.headers, body: Buffer.concat(chunks) }));
    });
    req.on('error', reject);
    req.end();
  });
}

// ---------------------------------------------------------------------------
// Fixture generation
// ---------------------------------------------------------------------------
function leb128u(value) {
  const out = [];
  let n = value;
  do {
    let byte = n & 0x7f;
    n >>>= 7;
    if (n !== 0) byte |= 0x80;
    out.push(byte);
  } while (n !== 0);
  return Buffer.from(out);
}

/**
 * A structurally valid, empty WebAssembly module padded with a large custom
 * section. Valid enough for WebAssembly.instantiateStreaming, which REFUSES to
 * compile unless the response is served as application/wasm — so the fixture app
 * booting at all is itself the proof that the server's wasm Content-Type is right.
 */
function makeWasm(fillerBytes) {
  const name = Buffer.from('filler', 'utf8');
  const filler = Buffer.alloc(fillerBytes, 'FILAMENT-BENCH-FIXTURE-PADDING-');
  const content = Buffer.concat([leb128u(name.length), name, filler]);
  const section = Buffer.concat([Buffer.from([0x00]), leb128u(content.length), content]);
  const header = Buffer.from([0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00]);
  return Buffer.concat([header, section]);
}

/** A minimal valid PNG (1x1) — an already-compressed type that must never be gzipped. */
const PNG_1x1 = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==',
  'base64',
);

/**
 * The rows fixture is a REFERENCE IMPLEMENTATION of the shared DOM contract, not a
 * sketch. It previously picked labels with Math.random(), which meant it could never
 * have passed the deterministic-label gate — the gate that exists to stop a
 * framework doing less work than its rival and posting the difference as a win.
 */
const ROWS_APP_JS = `/* Rows fixture — vanilla JS, implements the shared DOM contract. */
const ASYNC_DELAY_MS = ${ASYNC_DELAY_MS};

/* Contract lists: EXACTLY 25 adjectives, 11 colours ("brown" twice, deliberately),
   13 nouns. Any deviation changes the label stream and the amount of work done. */
const ADJ = ['pretty','large','big','small','tall','short','long','handsome','plain','quaint','clean','elegant','easy','angry','crazy','helpful','mushy','odd','unsightly','adorable','important','inexpensive','cheap','expensive','fancy'];
const COL = ['red','yellow','blue','green','pink','brown','purple','brown','white','black','orange'];
const NOUN = ['table','chair','house','bbq','desk','car','pony','cookie','sandwich','burger','pizza','mouse','keyboard'];

let nextId = 1;
const tbody = document.getElementById('tbody');

/* Park-Miller LCG in double arithmetic, so JS and C# agree byte-for-byte.
   Seeded at page load ONLY — never per #run click. */
let seed = 42.0;
function next() { seed = (seed * 16807.0) % 2147483647.0; return seed; }
function nextLabel() {
  var a = Math.trunc(next() % 25.0);
  var c = Math.trunc(next() % 11.0);
  var n = Math.trunc(next() % 13.0);
  return ADJ[a] + ' ' + COL[c] + ' ' + NOUN[n];
}

/* Mimics an async-dispatching framework (Blazor's @onclick): the DOM is NOT touched
   in the click handler's synchronous turn, nor in the frame that follows it. A
   harness that trusted a bare requestAnimationFrame would measure ~0 ms here. */
function deferred(fn) {
  return function () {
    Promise.resolve().then(function () { setTimeout(fn, ASYNC_DELAY_MS); });
  };
}

function buildRows(count) {
  const frag = document.createDocumentFragment();
  for (let i = 0; i < count; i++) {
    const id = nextId++;
    const tr = document.createElement('tr');
    const idCell = document.createElement('td');
    idCell.textContent = String(id);
    const labelCell = document.createElement('td');
    const a = document.createElement('a');
    a.className = 'lbl';
    a.textContent = nextLabel();
    labelCell.appendChild(a);
    const actionCell = document.createElement('td');
    actionCell.textContent = 'x';
    tr.appendChild(idCell);
    tr.appendChild(labelCell);
    tr.appendChild(actionCell);
    frag.appendChild(tr);
  }
  return frag;
}

/* #run REPLACES tbody with 1000 fresh rows. The id counter is NEVER reset. */
document.getElementById('run').addEventListener('click', deferred(function () {
  tbody.textContent = '';
  tbody.appendChild(buildRows(1000));
}));

document.getElementById('update').addEventListener('click', deferred(function () {
  const rows = tbody.children;
  for (let i = 0; i < rows.length; i += 10) {
    const a = rows[i].querySelector('td:nth-child(2) a');
    a.textContent = a.textContent + ' !!!';
  }
}));

document.getElementById('swaprows').addEventListener('click', deferred(function () {
  const rows = tbody.children;
  if (rows.length <= 998) return;
  const a = rows[1];
  const b = rows[998];
  const afterB = b.nextSibling;
  tbody.insertBefore(b, a);
  tbody.insertBefore(a, afterB);
}));

document.getElementById('clear').addEventListener('click', deferred(function () {
  tbody.textContent = '';
}));

/* Lazy-load runtime assets AFTER the app is interactive — the same shape as
   Blazor pulling framework files in after first render. The harness must still
   count these bytes, which is why it waits for network idle, not just for the
   button to exist. */
setTimeout(function () {
  WebAssembly.instantiateStreaming(fetch('fixture.wasm'), {})
    .then(function () { window.__WASM_OK__ = true; })
    .catch(function (e) { window.__WASM_ERR__ = String(e); });
  fetch('icudt.dat').then(function (r) { return r.arrayBuffer(); })
    .then(function (b) { window.__DAT_BYTES__ = b.byteLength; });
}, 200);
`;

const COUNTER_APP_JS = `/* Counter fixture — vanilla JS, implements the shared DOM contract. */
const ASYNC_DELAY_MS = ${ASYNC_DELAY_MS};
let count = 0;
const valueEl = document.getElementById('counter-value');
function deferred(fn) {
  return function () {
    Promise.resolve().then(function () { setTimeout(fn, ASYNC_DELAY_MS); });
  };
}
document.getElementById('increment').addEventListener('click', deferred(function () {
  count += 1;
  valueEl.textContent = String(count);
}));
setTimeout(function () {
  WebAssembly.instantiateStreaming(fetch('fixture.wasm'), {}).catch(function () {});
}, 200);
`;

/**
 * String.replace() with a string pattern silently returns the input unchanged when
 * the pattern does not match. An adversarial fixture that silently became a copy of
 * the conforming one would make its gate-proving test meaningless, so a missed
 * patch is a hard error.
 */
function mustReplace(src, find, repl, what) {
  if (!src.includes(find)) {
    throw new Error(`selftest: fixture patch "${what}" did not match its anchor; the fixture would not be ${what}`);
  }
  return src.replace(find, repl);
}

/** Same rows app, but #clear is wired to a handler that never mutates the DOM. */
const DEAD_CLEAR_JS = mustReplace(
  ROWS_APP_JS,
  `document.getElementById('clear').addEventListener('click', deferred(function () {
  tbody.textContent = '';
}));`,
  `document.getElementById('clear').addEventListener('click', function () {
  /* deliberately does nothing — the contract gate must catch this before timing */
});`,
  'a dead #clear button',
);

/**
 * #clear works only once #swaprows has run on this page.
 *
 * This shape is deliberate. The contract gate now clicks #run -> #update ->
 * #swaprows -> #clear, so a PLAINLY dead button (DEAD_CLEAR_JS above) is refused at
 * the gate with exit 3 and never reaches the stopwatch. That is stricter than
 * before, but it also means a plainly dead button can no longer exercise the TIMED
 * failure path. The timed `clear` scenario clicks #run then #clear, with no
 * intervening #swaprows — so this fixture passes the gate and hangs when timed,
 * isolating exactly the property under test: a predicate that never becomes true is
 * recorded as a FAILURE with a null median, and the other scenarios in the same app
 * still report normally. State-dependent breakage is a real bug class, not a straw
 * man.
 */
const BROKEN_CLEAR_JS = mustReplace(
  mustReplace(
    ROWS_APP_JS,
    `document.getElementById('clear').addEventListener('click', deferred(function () {
  tbody.textContent = '';
}));`,
    `let swapped = false;
document.getElementById('clear').addEventListener('click', deferred(function () {
  if (!swapped) return; /* hangs unless #swaprows ran first */
  tbody.textContent = '';
}));`,
    'a state-dependent #clear',
  ),
  `  tbody.insertBefore(b, a);
  tbody.insertBefore(a, afterB);
}));`,
  `  tbody.insertBefore(b, a);
  tbody.insertBefore(a, afterB);
  swapped = true;
}));`,
  'a #swaprows that arms #clear',
);

/** Rows app whose rows omit the label cell — a DOM-contract violation. */
const NO_LABEL_CELL_JS = mustReplace(
  ROWS_APP_JS,
  `    const labelCell = document.createElement('td');
    const a = document.createElement('a');
    a.className = 'lbl';
    a.textContent = nextLabel();
    labelCell.appendChild(a);
    const actionCell = document.createElement('td');
    actionCell.textContent = 'x';
    tr.appendChild(idCell);
    tr.appendChild(labelCell);
    tr.appendChild(actionCell);`,
  `    tr.appendChild(idCell);`,
  'missing its label cell',
);

/**
 * ---- Adversarial fixtures --------------------------------------------------
 * Each does STRICTLY LESS WORK than the contract requires while satisfying every
 * gate the harness had before this audit. Each therefore used to be reported as a
 * clean, fast, `ok` result. They exist to prove the new gates refuse them.
 */

/** Emits one interned constant string instead of 1000 generated labels. */
const CONST_LABEL_JS = mustReplace(
  ROWS_APP_JS,
  `function nextLabel() {
  var a = Math.trunc(next() % 25.0);
  var c = Math.trunc(next() % 11.0);
  var n = Math.trunc(next() % 13.0);
  return ADJ[a] + ' ' + COL[c] + ' ' + NOUN[n];
}`,
  `function nextLabel() { return 'pretty red table'; }`,
  'a constant-label generator',
);

/** #update touches only row 990 — 1 mutation instead of 100. */
const LAZY_UPDATE_JS = mustReplace(
  ROWS_APP_JS,
  `document.getElementById('update').addEventListener('click', deferred(function () {
  const rows = tbody.children;
  for (let i = 0; i < rows.length; i += 10) {
    const a = rows[i].querySelector('td:nth-child(2) a');
    a.textContent = a.textContent + ' !!!';
  }
}));`,
  `document.getElementById('update').addEventListener('click', deferred(function () {
  const rows = tbody.children;
  const a = rows[990].querySelector('td:nth-child(2) a');
  a.textContent = a.textContent + ' !!!';
}));`,
  'a one-row #update',
);

/** #swaprows does rows[1] = rows[998] — a duplicate, not a swap. Half the DOM moves. */
const HALF_SWAP_JS = mustReplace(
  ROWS_APP_JS,
  `document.getElementById('swaprows').addEventListener('click', deferred(function () {
  const rows = tbody.children;
  if (rows.length <= 998) return;
  const a = rows[1];
  const b = rows[998];
  const afterB = b.nextSibling;
  tbody.insertBefore(b, a);
  tbody.insertBefore(a, afterB);
}));`,
  `document.getElementById('swaprows').addEventListener('click', deferred(function () {
  const rows = tbody.children;
  if (rows.length <= 998) return;
  tbody.replaceChild(rows[998].cloneNode(true), rows[1]);
}));`,
  'a one-directional #swaprows',
);

/**
 * Fully conforming, but every handler mutates the DOM SYNCHRONOUSLY. #clear and
 * #swaprows here are genuinely sub-millisecond, so this fixture is the regression
 * test for the vsync-quantization defect: the old rAF-terminated clock reported the
 * frame interval (~8-17 ms) for this work no matter how fast it actually was.
 */
const SYNC_ROWS_JS = mustReplace(
  ROWS_APP_JS,
  `function deferred(fn) {
  return function () {
    Promise.resolve().then(function () { setTimeout(fn, ASYNC_DELAY_MS); });
  };
}`,
  `function deferred(fn) { return fn; }`,
  'synchronous handlers',
);

/**
 * Fully conforming, but pulls a payload from a WEB WORKER target — the shape of a
 * .NET WASM build with WasmEnableThreads (dotnet.native.worker.mjs). Two distinct
 * harness bugs live here:
 *   1. Chrome sends requestWillBeSent for the worker SCRIPT to the page session but
 *      routes its completion to the worker target, so inFlight incremented and
 *      could never decrement — waitForNetworkQuiet could never settle.
 *   2. The worker's own fetches are invisible to the page session entirely.
 */
const WORKER_ROWS_JS = `${ROWS_APP_JS}
/* Spawn a Web Worker that fetches a payload the page-scoped CDP session cannot see. */
setTimeout(function () { new Worker('fetchworker.js'); }, 150);
`;

const WORKER_PAYLOAD_BYTES = 256 * 1024;

function rowsHtml(title) {
  return `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${title}</title>
<link rel="icon" type="image/png" href="favicon.png">
<link rel="stylesheet" href="app.css">
</head>
<body>
<h1>${title}</h1>
<p>
  <button id="run" type="button">Create 1000 rows</button>
  <button id="update" type="button">Update every 10th row</button>
  <button id="swaprows" type="button">Swap Rows</button>
  <button id="clear" type="button">Clear</button>
</p>
<img src="logo.png" alt="" width="1" height="1">
<table>
  <tbody id="tbody"></tbody>
</table>
<script src="filler.js"></script>
<script src="sibling.js"></script>
<script src="app.js"></script>
</body>
</html>
`;
}

const COUNTER_HTML = `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Counter fixture</title>
<link rel="icon" type="image/png" href="favicon.png">
<link rel="stylesheet" href="app.css">
</head>
<body>
<h1>Counter fixture</h1>
<p>Current count: <span id="counter-value">0</span></p>
<button id="increment" type="button">Click me</button>
<script src="filler.js"></script>
<script src="app.js"></script>
</body>
</html>
`;

const SIBLING_RAW = 'window.__SIBLING_VARIANT__ = "raw-on-the-fly";\n';
const SIBLING_GZ_SOURCE = 'window.__SIBLING_VARIANT__ = "precompressed-sibling";\n';
const BRSIB_RAW = 'window.__BR_VARIANT__ = "raw-on-the-fly";\n'.repeat(40);
const BRSIB_BR_SOURCE = 'window.__BR_VARIANT__ = "precompressed-br-sibling";\n'.repeat(40);

/**
 * Extensions the OLD COMPRESSIBLE allowlist never contained. `dotnet publish` emits
 * .dat/.blat/.dll, which were on it; a non-.NET framework shipping .data or an
 * extensionless bundle was served RAW and its weight inflated 3-4x with no signal.
 * These assets exist so that regression cannot come back silently.
 */
const UNANTICIPATED_ASSETS = [
  ['payload.data', Buffer.alloc(64 * 1024, 'FILAMENT-DATA-SEGMENT-')],
  ['heap.mem', Buffer.alloc(48 * 1024, 'LINEAR-MEMORY-IMAGE-')],
  ['module.wat', Buffer.alloc(32 * 1024, '(module (func $noop))  ;; ')],
  // No extension at all.
  ['bundle', Buffer.alloc(64 * 1024, 'EXTENSIONLESS-BUNDLE-')],
];

async function writeCommonAssets(dir) {
  // ~180 KB of repetitive-but-not-trivial JS so transferred bytes are clearly
  // non-zero and the gzip ratio is visible.
  const filler = Array.from(
    { length: 3000 },
    (_, i) => `/* filler ${i} */ window.__filler_${i} = { id: ${i}, name: "filler-entry-${i}", tags: ["alpha","beta","gamma"] };`,
  ).join('\n');
  await fsp.writeFile(path.join(dir, 'filler.js'), filler, 'utf8');

  await fsp.writeFile(path.join(dir, 'app.css'), 'body{font-family:system-ui;margin:2rem}table{width:100%}td{padding:2px 6px}\n'.repeat(50), 'utf8');

  // Precompressed sibling. Its DECOMPRESSED content deliberately differs from the
  // raw file so the test can prove the .gz bytes were the ones served rather than
  // an on-the-fly recompression of the raw file.
  await fsp.writeFile(path.join(dir, 'sibling.js'), SIBLING_RAW, 'utf8');
  await fsp.writeFile(path.join(dir, 'sibling.js.gz'), zlib.gzipSync(Buffer.from(SIBLING_GZ_SOURCE), { level: 9 }));

  // Brotli sibling, exactly as `dotnet publish` emits alongside every .gz. Its
  // decompressed content differs from the raw file so the test can prove the .br
  // BYTES were served rather than an on-the-fly recompression.
  await fsp.writeFile(path.join(dir, 'brsib.js'), BRSIB_RAW, 'utf8');
  await fsp.writeFile(path.join(dir, 'brsib.js.br'), zlib.brotliCompressSync(Buffer.from(BRSIB_BR_SOURCE)));

  for (const [name, buf] of UNANTICIPATED_ASSETS) {
    // eslint-disable-next-line no-await-in-loop
    await fsp.writeFile(path.join(dir, name), buf);
  }

  await fsp.writeFile(path.join(dir, 'fixture.wasm'), makeWasm(96 * 1024));
  await fsp.writeFile(path.join(dir, 'icudt.dat'), Buffer.alloc(64 * 1024, 'ICU-DATA-BLOB-'));
  await fsp.writeFile(path.join(dir, 'logo.png'), PNG_1x1);
  // Declared in the fixture HTML exactly as the Blazor template declares its own.
  // Without it Chrome auto-requests /favicon.ico, which 404s and adds bytes that
  // the real apps would not have.
  await fsp.writeFile(path.join(dir, 'favicon.png'), PNG_1x1);
  await fsp.writeFile(path.join(dir, 'archive.tar.gz'), zlib.gzipSync(Buffer.from('already compressed payload'), { level: 9 }));
}

async function makeFixture(root) {
  const dirs = {
    rows: path.join(root, 'rows'),
    counter: path.join(root, 'counter'),
    broken: path.join(root, 'broken'),
    deadclear: path.join(root, 'deadclear'),
    nolabel: path.join(root, 'nolabel'),
    // Adversarial: each does less work than the contract requires.
    constlabel: path.join(root, 'constlabel'),
    lazyupdate: path.join(root, 'lazyupdate'),
    halfswap: path.join(root, 'halfswap'),
    // Conforming, but synchronous — the vsync-quantization regression test.
    sync: path.join(root, 'sync'),
    // Conforming, but fetches from a Web Worker target (the WasmEnableThreads shape).
    worker: path.join(root, 'worker'),
  };
  for (const d of Object.values(dirs)) await fsp.mkdir(d, { recursive: true });

  const rowsApps = [
    [dirs.rows, 'Rows fixture', ROWS_APP_JS],
    [dirs.broken, 'State-dependent-clear rows fixture', BROKEN_CLEAR_JS],
    [dirs.deadclear, 'Dead-clear rows fixture', DEAD_CLEAR_JS],
    [dirs.nolabel, 'No-label-cell fixture', NO_LABEL_CELL_JS],
    [dirs.constlabel, 'Constant-label fixture', CONST_LABEL_JS],
    [dirs.lazyupdate, 'One-row-update fixture', LAZY_UPDATE_JS],
    [dirs.halfswap, 'One-directional-swap fixture', HALF_SWAP_JS],
    [dirs.sync, 'Synchronous rows fixture', SYNC_ROWS_JS],
    [dirs.worker, 'Web-Worker rows fixture', WORKER_ROWS_JS],
  ];
  for (const [dir, title, js] of rowsApps) {
    // eslint-disable-next-line no-await-in-loop
    await writeCommonAssets(dir);
    // eslint-disable-next-line no-await-in-loop
    await fsp.writeFile(path.join(dir, 'index.html'), rowsHtml(title), 'utf8');
    // eslint-disable-next-line no-await-in-loop
    await fsp.writeFile(path.join(dir, 'app.js'), js, 'utf8');
  }

  // Worker fixture extras. The payload is RANDOM so compression cannot shrink it to
  // noise — the point is that a materially sized asset goes missing.
  await fsp.writeFile(
    path.join(dirs.worker, 'fetchworker.js'),
    "fetch('worker-payload.data').then(function (r) { return r.arrayBuffer(); });\n",
    'utf8',
  );
  await fsp.writeFile(path.join(dirs.worker, 'worker-payload.data'), crypto.randomBytes(WORKER_PAYLOAD_BYTES));

  await writeCommonAssets(dirs.counter);
  await fsp.writeFile(path.join(dirs.counter, 'index.html'), COUNTER_HTML, 'utf8');
  await fsp.writeFile(path.join(dirs.counter, 'app.js'), COUNTER_APP_JS, 'utf8');

  return dirs;
}

// ---------------------------------------------------------------------------
// 1. Pure unit tests
// ---------------------------------------------------------------------------
function testAcceptEncodingParsing() {
  section('1. Accept-Encoding negotiation (unit)');
  ok(acceptsGzip('gzip'), 'gzip');
  ok(acceptsGzip('gzip, deflate, br, zstd'), 'gzip among many');
  ok(acceptsGzip('GZIP'), 'case-insensitive');
  ok(acceptsGzip('*'), 'wildcard implies gzip');
  ok(acceptsGzip('br;q=1.0, gzip;q=0.8'), 'gzip with q=0.8');
  ok(!acceptsGzip('gzip;q=0'), 'gzip;q=0 means NOT acceptable');
  ok(!acceptsGzip('br, deflate'), 'no gzip token');
  ok(!acceptsGzip('identity'), 'identity only');
  ok(!acceptsGzip(''), 'empty header');
  ok(!acceptsGzip(undefined), 'absent header');

  ok(acceptsBrotli('br'), 'br');
  ok(acceptsBrotli('gzip, deflate, br, zstd'), 'br in the header Chrome actually sends');
  ok(acceptsBrotli('*'), 'wildcard implies br');
  ok(!acceptsBrotli('br;q=0'), 'br;q=0 means NOT acceptable');
  ok(!acceptsBrotli('gzip, deflate'), 'no br token');
  ok(!acceptsBrotli('identity'), 'identity only');

  // The smallest encoding the client accepts wins. A real host/CDN serves .br to a
  // br-capable client; gzip-only negotiation reported Blazor ~21.7% heavier than
  // any real deployment of the same publish output.
  eq(negotiateEncoding('gzip, deflate, br, zstd'), 'br', 'Chrome (gzip+br) negotiates brotli');
  eq(negotiateEncoding('gzip, deflate'), 'gzip', 'gzip-only client negotiates gzip');
  eq(negotiateEncoding('br'), 'br', 'br-only client negotiates brotli');
  eq(negotiateEncoding('identity'), null, 'identity negotiates no encoding');
  eq(negotiateEncoding('br;q=0, gzip'), 'gzip', 'br;q=0 falls back to gzip');
  eq(negotiateEncoding(undefined), null, 'absent header negotiates no encoding');

  // Compression eligibility is a denylist: unknown extensions are compressible.
  ok(isCompressible('.data'), '.data (never on the old allowlist) is compressible');
  ok(isCompressible('.mem'), '.mem is compressible');
  ok(isCompressible('.wat'), '.wat is compressible');
  ok(isCompressible(''), 'extensionless is compressible');
  ok(isCompressible('.wasm'), '.wasm is compressible');
  ok(isCompressible('.ttf'), '.ttf is compressible (not an already-compressed format)');
  ok(!isCompressible('.png'), '.png is NOT compressible');
  ok(!isCompressible('.woff2'), '.woff2 is NOT compressible');
  ok(!isCompressible('.gz'), '.gz is NOT compressible (no double-encode)');
  ok(!isCompressible('.br'), '.br is NOT compressible (no double-encode)');
}

function testStatistics() {
  section('2. Statistics (unit)');
  // Linear-interpolation (type 7) quantiles over 1..10.
  const s = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
  eq(quantile(s, 0.5), 5.5, 'median of 1..10 is 5.5');
  eq(quantile(s, 0.25), 3.25, 'p25 of 1..10 is 3.25');
  eq(quantile(s, 0.75), 7.75, 'p75 of 1..10 is 7.75');
  const sum = summarize([10, 1, 5, 3, 2]);
  eq(sum.median, 3, 'median is order-independent');
  eq(sum.min, 1, 'min');
  eq(sum.max, 10, 'max');
  eq(sum.iqr, 3, 'iqr = p75 - p25');
  // The mean of [1,2,3,4,100] is 22 — the median must ignore the outlier.
  eq(summarize([1, 2, 3, 4, 100]).median, 3, 'median resists an outlier the mean would not');
  eq(summarize([]).median, null, 'empty sample set yields null, not 0');
}

// ---------------------------------------------------------------------------
// 3. Server HTTP behaviour
// ---------------------------------------------------------------------------
async function testServer(fixture) {
  section('3. Server: compression negotiation, Content-Type, no-store');
  const srv = await startServer({ dir: fixture.rows, port: 0, host: '127.0.0.1', quiet: true });
  const { port, host } = srv;
  // GZ pins gzip so gzip-specific assertions stay meaningful now that br wins when
  // both are offered. CHROME is the header Chrome actually sends.
  const GZ = { 'Accept-Encoding': 'gzip' };
  const CHROME = { 'Accept-Encoding': 'gzip, deflate, br, zstd' };
  const ID = { 'Accept-Encoding': 'identity' };

  try {
    // --- on-the-fly gzip of html ---
    const html = await rawGet(port, host, '/index.html', GZ);
    eq(html.status, 200, 'index.html: 200');
    eq(html.headers['content-encoding'], 'gzip', 'index.html: Content-Encoding: gzip');
    eq(html.headers['content-type'], 'text/html; charset=utf-8', 'index.html: Content-Type');
    eq(html.headers['cache-control'], 'no-store', 'index.html: Cache-Control: no-store');
    eq(html.headers['vary'], 'Accept-Encoding', 'index.html: Vary');
    eq(html.headers['x-bench-encoding-source'], 'ondemand', 'index.html: compressed on the fly');
    const htmlRaw = await fsp.readFile(path.join(fixture.rows, 'index.html'));
    ok(zlib.gunzipSync(html.body).equals(htmlRaw), 'index.html: gunzips to the exact original bytes');
    ok(html.body.length < htmlRaw.length, 'index.html: gzip actually shrank it',
      `${html.body.length} vs ${htmlRaw.length}`);
    eq(Number(html.headers['content-length']), html.body.length, 'index.html: Content-Length matches wire bytes');

    // --- identity ---
    const htmlId = await rawGet(port, host, '/index.html', ID);
    eq(htmlId.headers['content-encoding'], undefined, 'identity: no Content-Encoding');
    ok(htmlId.body.equals(htmlRaw), 'identity: raw bytes served verbatim');

    // --- root serves index.html ---
    const root = await rawGet(port, host, '/', GZ);
    eq(root.status, 200, '/: 200');
    eq(root.headers['content-type'], 'text/html; charset=utf-8', '/: serves index.html');

    // --- wasm ---
    const wasm = await rawGet(port, host, '/fixture.wasm', GZ);
    eq(wasm.status, 200, 'fixture.wasm: 200');
    eq(wasm.headers['content-type'], 'application/wasm', 'fixture.wasm: Content-Type application/wasm');
    eq(wasm.headers['content-encoding'], 'gzip', 'fixture.wasm: Content-Encoding: gzip');
    const wasmRaw = await fsp.readFile(path.join(fixture.rows, 'fixture.wasm'));
    ok(zlib.gunzipSync(wasm.body).equals(wasmRaw), 'fixture.wasm: gunzips to the exact original module');
    ok(wasm.body.length < wasmRaw.length / 2, 'fixture.wasm: compressed well',
      `${wasm.body.length} vs ${wasmRaw.length}`);

    // --- precompressed sibling wins, with the ORIGINAL file's Content-Type ---
    const sib = await rawGet(port, host, '/sibling.js', GZ);
    eq(sib.status, 200, 'sibling.js: 200');
    eq(sib.headers['content-encoding'], 'gzip', 'sibling.js: Content-Encoding: gzip');
    eq(sib.headers['content-type'], 'text/javascript; charset=utf-8', "sibling.js: uses the ORIGINAL file's Content-Type, not application/gzip");
    eq(sib.headers['x-bench-encoding-source'], 'precompressed', 'sibling.js: served the precompressed sibling');
    eq(zlib.gunzipSync(sib.body).toString('utf8'), SIBLING_GZ_SOURCE,
      'sibling.js: the .gz sibling BYTES were served (not an on-the-fly recompression of the raw file)');
    const sibGzOnDisk = await fsp.readFile(path.join(fixture.rows, 'sibling.js.gz'));
    ok(sib.body.equals(sibGzOnDisk), 'sibling.js: wire bytes are byte-identical to the .gz on disk');

    // --- sibling ignored when the client cannot inflate it ---
    const sibId = await rawGet(port, host, '/sibling.js', ID);
    eq(sibId.headers['content-encoding'], undefined, 'sibling.js (identity): no Content-Encoding');
    eq(sibId.body.toString('utf8'), SIBLING_RAW, 'sibling.js (identity): raw file, not the sibling');

    // --- already-compressed types are NOT gzipped ---
    const png = await rawGet(port, host, '/logo.png', GZ);
    eq(png.headers['content-encoding'], undefined, 'logo.png: NOT gzipped (already compressed)');
    eq(png.headers['content-type'], 'image/png', 'logo.png: Content-Type');
    ok(png.body.equals(PNG_1x1), 'logo.png: bytes untouched');

    const targz = await rawGet(port, host, '/archive.tar.gz', GZ);
    eq(targz.headers['content-encoding'], undefined, 'archive.tar.gz: NOT re-encoded (no double gzip)');
    eq(targz.headers['content-type'], 'application/gzip', 'archive.tar.gz: Content-Type');
    ok(zlib.gunzipSync(targz.body).toString('utf8') === 'already compressed payload',
      'archive.tar.gz: served verbatim, single-gzipped');

    // --- q=0 must be honoured ---
    const q0 = await rawGet(port, host, '/index.html', { 'Accept-Encoding': 'gzip;q=0' });
    eq(q0.headers['content-encoding'], undefined, 'gzip;q=0: served identity');

    // --- BROTLI: a br-capable client must get br, as any real host/CDN does ------
    section('3b. Brotli negotiation (the 21.7% weight inflation)');
    const htmlBr = await rawGet(port, host, '/index.html', CHROME);
    eq(htmlBr.headers['content-encoding'], 'br', 'index.html + Chrome Accept-Encoding: served brotli, not gzip');
    ok(zlib.brotliDecompressSync(htmlBr.body).equals(htmlRaw), 'index.html: brotli decodes to the exact original bytes');
    ok(htmlBr.body.length < html.body.length,
      'index.html: brotli is smaller than gzip — this delta is what the gzip-only server was adding to every framework',
      `br=${htmlBr.body.length} gzip=${html.body.length}`);
    eq(htmlBr.headers['vary'], 'Accept-Encoding', 'index.html (br): Vary');

    const wasmBr = await rawGet(port, host, '/fixture.wasm', CHROME);
    eq(wasmBr.headers['content-encoding'], 'br', 'fixture.wasm + Chrome: brotli');
    eq(wasmBr.headers['content-type'], 'application/wasm', 'fixture.wasm (br): Content-Type still application/wasm');
    ok(zlib.brotliDecompressSync(wasmBr.body).equals(wasmRaw), 'fixture.wasm: brotli decodes to the exact original module');

    // A .br sibling emitted by publish must be preferred, byte-for-byte.
    const brsib = await rawGet(port, host, '/brsib.js', CHROME);
    eq(brsib.headers['content-encoding'], 'br', 'brsib.js: Content-Encoding: br');
    eq(brsib.headers['content-type'], 'text/javascript; charset=utf-8', "brsib.js: uses the ORIGINAL file's Content-Type");
    eq(brsib.headers['x-bench-encoding-source'], 'precompressed', 'brsib.js: served the precompressed .br sibling');
    eq(zlib.brotliDecompressSync(brsib.body).toString('utf8'), BRSIB_BR_SOURCE,
      'brsib.js: the .br sibling BYTES were served (not an on-the-fly recompression of the raw file)');
    const brOnDisk = await fsp.readFile(path.join(fixture.rows, 'brsib.js.br'));
    ok(brsib.body.equals(brOnDisk), 'brsib.js: wire bytes are byte-identical to the .br on disk');

    // No .br sibling + br-capable client => on-the-fly brotli at publish quality.
    // A framework that ships no siblings must not be handed a worse encoding than
    // one that does; that asymmetry is the whole fairness problem.
    const sibBr = await rawGet(port, host, '/sibling.js', CHROME);
    eq(sibBr.headers['content-encoding'], 'br', 'sibling.js (no .br sibling, br client): on-the-fly brotli');
    eq(sibBr.headers['x-bench-encoding-source'], 'ondemand', 'sibling.js: compressed on the fly');
    eq(zlib.brotliDecompressSync(sibBr.body).toString('utf8'), SIBLING_RAW,
      'sibling.js: on-the-fly brotli encodes the RAW file, not the .gz sibling');

    // br must never be double-encoded.
    const brDirect = await rawGet(port, host, '/brsib.js.br', CHROME);
    eq(brDirect.headers['content-encoding'], undefined, 'brsib.js.br requested directly: NOT re-encoded');
    ok(brDirect.body.equals(brOnDisk), 'brsib.js.br: served verbatim, single-encoded');

    // --- DENYLIST: extensions the old allowlist never anticipated ---------------
    section('3c. Compression eligibility is a denylist, not a .NET-shaped allowlist');
    for (const [name] of UNANTICIPATED_ASSETS) {
      // eslint-disable-next-line no-await-in-loop
      const g = await rawGet(port, host, '/' + name, GZ);
      eq(g.headers['content-encoding'], 'gzip', `/${name}: gzipped (allowlist would have shipped it RAW)`);
      // eslint-disable-next-line no-await-in-loop
      const b = await rawGet(port, host, '/' + name, CHROME);
      eq(b.headers['content-encoding'], 'br', `/${name}: brotli for a br-capable client`);
      // eslint-disable-next-line no-await-in-loop
      const rawOnDisk = await fsp.readFile(path.join(fixture.rows, name));
      ok(zlib.brotliDecompressSync(b.body).equals(rawOnDisk), `/${name}: decodes to the exact original bytes`);
      ok(b.body.length * 10 < rawOnDisk.length,
        `/${name}: compression actually applied (the raw-serving bug inflated this asset ~10x+)`,
        `${b.body.length} vs ${rawOnDisk.length}`);
    }

    // --- .dat is compressible ---
    const dat = await rawGet(port, host, '/icudt.dat', GZ);
    eq(dat.headers['content-encoding'], 'gzip', 'icudt.dat: Content-Encoding: gzip');
    eq(dat.headers['content-type'], 'application/octet-stream', 'icudt.dat: Content-Type');

    // --- css ---
    const css = await rawGet(port, host, '/app.css', GZ);
    eq(css.headers['content-encoding'], 'gzip', 'app.css: Content-Encoding: gzip');
    eq(css.headers['content-type'], 'text/css; charset=utf-8', 'app.css: Content-Type');

    // --- SPA fallback vs honest 404 ---
    const route = await rawGet(port, host, '/counter', GZ);
    eq(route.status, 200, '/counter (extension-less route): falls back to index.html');
    const missing = await rawGet(port, host, '/_framework/nope.wasm', GZ);
    eq(missing.status, 404, '/_framework/nope.wasm: honest 404 (asset requests never get an HTML fallback)');

    // --- traversal ---
    // The property that matters is "no file outside the served root is ever
    // returned", NOT a particular status code: an extension-less traversal target
    // legitimately lands on the SPA fallback and returns 200 index.html.
    const SECRET = 'TOP-SECRET-OUTSIDE-ROOT';
    const secretPath = path.join(fixture.rows, '..', 'secret.txt');
    await fsp.writeFile(secretPath, SECRET, 'utf8');
    const attempts = [
      '/../secret.txt',
      '/%2e%2e/secret.txt',
      // ..%2f survives URL normalisation and only becomes "../" after decoding —
      // it escapes iff the server normalises before it decodes.
      '/..%2fsecret.txt',
      '/foo/../../secret.txt',
      '/%252e%252e/secret.txt',
      '/../../../../etc/passwd',
      '/%2e%2e/%2e%2e/etc/passwd',
    ];
    let leaked = null;
    for (const a of attempts) {
      // eslint-disable-next-line no-await-in-loop
      const res = await rawGet(port, host, a, ID);
      const body = res.body.toString('utf8');
      if (body.includes(SECRET) || body.includes('root:')) leaked = `${a} -> ${res.status}`;
    }
    ok(leaked === null, 'no traversal encoding escapes the served root (7 variants incl. ..%2f)', leaked ?? '');
    const travExt = await rawGet(port, host, '/../secret.txt', ID);
    eq(travExt.status, 404, '/../secret.txt: resolves inside root and honestly 404s');

    // --- cache-control everywhere ---
    eq(png.headers['cache-control'], 'no-store', 'logo.png: Cache-Control: no-store');
    eq(missing.headers['cache-control'], 'no-store', '404: Cache-Control: no-store');
  } finally {
    await srv.close();
  }
}

// ---------------------------------------------------------------------------
// 4/5. End-to-end bench against the fixture
// ---------------------------------------------------------------------------
async function runBench(args) {
  const code = await benchMain(args);
  return code;
}

async function testEndToEnd(fixture, outDir) {
  section('4. End-to-end: CDP byte summing + timing against the vanilla fixture');

  const rowsOut = path.join(outDir, 'selftest-rows.json');
  const code = await runBench([
    '--dir', fixture.rows,
    '--app', 'rows',
    '--label', 'selftest-fixture-rows',
    '--runs', '5',
    '--weight-runs', '2',
    '--out', rowsOut,
    '--no-aot',
  ]);
  eq(code, 0, 'bench.mjs exited 0 for the rows fixture');

  const r = JSON.parse(await fsp.readFile(rowsOut, 'utf8'));

  // --- THE headline assertion: CDP byte summing is non-zero ---
  const bytes = r.weight.toInteractive.median;
  ok(typeof bytes === 'number' && bytes > 0, 'CDP encodedDataLength summing returns NON-ZERO', `got ${bytes}`);
  // Threshold is brotli-calibrated: the fixture's ~500 KB of deliberately repetitive
  // filler compresses ~30x under br (it compressed ~10x under the gzip-only server
  // this suite was originally written against). The meaningful "plausibly sized"
  // check is the transferred-vs-decoded ratio asserted below, not an absolute floor.
  ok(bytes > 10_000, 'transferred bytes are plausibly sized (> 10 KB under brotli)', `got ${bytes}`);
  ok(r.weight.requestCount >= 6, 'all fixture requests were counted', `got ${r.weight.requestCount}`);

  // --- lazy-loaded-after-interactive assets must be included ---
  const urls = r.weight.topRequests.map((x) => x.url);
  ok(urls.some((u) => u.endsWith('/fixture.wasm')),
    'lazily-loaded fixture.wasm IS counted (proves we wait for network idle, not just for the button)');
  ok(urls.some((u) => u.endsWith('/icudt.dat')), 'lazily-loaded icudt.dat IS counted');
  ok(urls.some((u) => u.endsWith('/filler.js')), 'filler.js counted');
  ok(urls.some((u) => u.endsWith('/') || u.endsWith('/index.html')), 'the navigation request itself is counted');

  // --- transferred != disk, and transferred < decoded (i.e. we measured the wire) ---
  ok(r.weight.decodedBytesMedianRun > bytes,
    'transferred bytes are BELOW decoded bytes — we measured the compressed wire, not disk size',
    `transferred=${bytes} decoded=${r.weight.decodedBytesMedianRun}`);

  // --- independent cross-check against the server's own ledger ---
  const cc = r.weight.crossCheck;
  ok(cc.deltaBytes > 0, 'CDP total exceeds server-written body bytes (headers accounted for)',
    `delta=${cc.deltaBytes}`);
  ok(cc.deltaBytes < cc.serverRequestCount * 400,
    'CDP/server delta is consistent with response-header overhead only',
    `delta=${cc.deltaBytes} over ${cc.serverRequestCount} requests`);

  // --- the wasm actually instantiated => application/wasm was correct ---
  ok(!r.weight.warnings.some((w) => w.includes('404')), 'no 404s during the weight run');

  // --- timing: the async-click race must be defeated ---
  section('5. Async-click race: fixture defers every mutation by 60 ms');
  for (const name of ['create', 'update', 'swap', 'clear']) {
    const s = r.scenarios[name];
    eq(s.status, 'ok', `${name}: status ok`);
    eq(s.n, 5, `${name}: 5 samples recorded`);
    eq(s.metric, 'msToMutation', `${name}: the headline stats name their metric`);
    ok(typeof s.median === 'number' && s.median > 0, `${name}: median is a positive number`, `got ${s.median}`);
    ok(typeof s.iqr === 'number' && s.iqr >= 0, `${name}: IQR reported`, `got ${s.iqr}`);
    ok(s.median >= ASYNC_DELAY_MS - 5,
      `${name}: median (${s.median} ms) >= ${ASYNC_DELAY_MS - 5} ms — the deferred DOM update was actually awaited`,
      'a naive rAF-after-click would have fabricated < 16 ms here');
    ok(s.samples.every((v) => v >= ASYNC_DELAY_MS - 5),
      `${name}: EVERY sample cleared the async delay (no fabricated fast run)`);

    // Both timings must be present, and paint must strictly follow mutation.
    ok(s.toPaint && typeof s.toPaint.median === 'number', `${name}: msToPaint reported as a SECOND field`);
    eq(s.toPaint.n, 5, `${name}: 5 paint samples`);
    ok(s.toPaint.median >= s.median,
      `${name}: msToPaint median (${s.toPaint.median}) >= msToMutation median (${s.median})`);
    ok(typeof s.toPaint.note === 'string' && /vsync/i.test(s.toPaint.note),
      `${name}: msToPaint carries the note explaining the vsync offset is the harness's`);
  }
  ok(r.scenarios.create.diagnostics.pageErrors.length === 0, 'no page errors during the rows run',
    JSON.stringify(r.scenarios.create.diagnostics.pageErrors));

  // --- the fairness fixture is wired up and its identity recorded ---
  const golden = await loadExpectedLabels();
  ok(!!r.expectedLabels, 'result records the golden label fixture it enforced');
  eq(r.expectedLabels.sha256, golden.sha256, 'recorded fixture hash matches expected-labels.json on disk');
  ok(/^[0-9a-f]{64}$/.test(r.expectedLabels.sha256), 'fixture hash is a sha256 hex digest');

  // --- the contract gate actually exercised update/swap/clear ---
  const cg = r.contractCheck;
  eq(cg.observed.updateMissedCount, 0, 'contract gate clicked #update and found 0 of 100 rows missed');
  ok(cg.observed.swap && cg.observed.swap.after.id1 === cg.observed.swap.before.id998,
    'contract gate clicked #swaprows and verified index 1 received row 998');
  ok(cg.observed.swap && cg.observed.swap.after.id998 === cg.observed.swap.before.id1,
    'contract gate verified the RECIPROCAL: index 998 received row 1');
  eq(cg.observed.rowCountAfterClear, 0, 'contract gate clicked #clear and verified an empty tbody');
  ok(Array.isArray(cg.observed.first5) && cg.observed.first5[0] === golden.first5[0],
    'contract gate compared real label bytes against the golden stream',
    JSON.stringify(cg.observed.first5));

  // --- service workers blocked by default; no invisible bytes ---
  eq(r.config.serviceWorkers, 'block', 'service workers are BLOCKED by default (byte-tracking blind spot)');
  eq(r.weight.untrackedRequests.length, 0,
    'every byte the server wrote was also seen by CDP (no worker-target blind spot)',
    JSON.stringify(r.weight.untrackedRequests));
  ok(!r.weight.warnings.some((w) => /NO Content-Encoding/.test(w)),
    'no compressible asset was shipped uncompressed',
    JSON.stringify(r.weight.warnings.filter((w) => /NO Content-Encoding/.test(w))));
  ok(r.weight.serverEncodings && r.weight.serverEncodings.br && r.weight.serverEncodings.br.responses > 0,
    'the real Chrome run negotiated brotli for its assets',
    JSON.stringify(r.weight.serverEncodings));

  // --- metadata ---
  section('6. Recorded metadata');
  ok(!!r.environment.chrome.version, `Chrome version recorded: ${r.environment.chrome.version}`);
  eq(r.environment.chrome.channel, 'chrome', 'Chrome channel recorded');
  eq(typeof r.environment.chrome.headless, 'boolean', 'headless flag recorded');
  ok(!!r.environment.dotnetSdk, `.NET SDK version recorded: ${r.environment.dotnetSdk}`);
  ok(!!r.environment.machine.cpu, `machine recorded: ${r.environment.machine.cpu}`);
  ok(!!r.environment.os.platform, `OS recorded: ${r.environment.os.platform} ${r.environment.os.release}`);
  eq(r.environment.aot, false, 'AOT flag recorded');
  ok(!!r.environment.playwright, `Playwright version recorded: ${r.environment.playwright}`);
  ok(Array.isArray(r.weight.topRequests) && r.weight.topRequests.length > 0, 'per-request breakdown present');
  ok(r.weight.topRequests.length <= 15, 'per-request breakdown capped at top 15');
  ok(r.scenarios.create.samples.length === 5, 'raw samples retained in the JSON');

  // --- counter app ---
  section('7. Counter app');
  const counterOut = path.join(outDir, 'selftest-counter.json');
  const code2 = await runBench([
    '--dir', fixture.counter,
    '--app', 'counter',
    '--label', 'selftest-fixture-counter',
    '--runs', '5',
    '--weight-runs', '1',
    '--out', counterOut,
    '--no-aot',
  ]);
  eq(code2, 0, 'bench.mjs exited 0 for the counter fixture');
  const c = JSON.parse(await fsp.readFile(counterOut, 'utf8'));
  eq(c.scenarios.increment.status, 'ok', 'increment: status ok');
  ok(c.scenarios.increment.median >= ASYNC_DELAY_MS - 5,
    `increment: median (${c.scenarios.increment.median} ms) reflects the real deferred update`);
  ok(c.weight.toInteractive.median > 0, 'counter: transferred bytes non-zero',
    `got ${c.weight.toInteractive.median}`);

  return { rowsResult: r, counterResult: c };
}

async function testFailureReporting(fixture, outDir) {
  section('8. A non-responding button is a FAILURE, never a number');
  const brokenOut = path.join(outDir, 'selftest-broken.json');
  const code = await runBench([
    '--dir', fixture.broken,
    '--app', 'rows',
    '--label', 'selftest-fixture-broken',
    '--runs', '1',
    '--weight-runs', '1',
    '--timeout', '1200',
    '--out', brokenOut,
    '--no-aot',
  ]);
  eq(code, 1, 'bench.mjs exits non-zero when a scenario fails');

  const b = JSON.parse(await fsp.readFile(brokenOut, 'utf8'));
  eq(b.ok, false, 'result.ok is false');
  eq(b.scenarios.clear.status, 'failed', 'clear (dead button): status "failed"');
  eq(b.scenarios.clear.median, null, 'clear: median is null — NOT a fabricated number');
  eq(b.scenarios.clear.n, 0, 'clear: zero samples recorded');
  eq(b.scenarios.clear.failureCount, 1, 'clear: the timeout was recorded as a failure');
  ok(/timed out/i.test(b.scenarios.clear.failures[0].error), 'clear: failure reason is the predicate timeout',
    b.scenarios.clear.failures[0].error);
  // The working scenarios in the same app must still report normally.
  eq(b.scenarios.create.status, 'ok', 'create still ok in the same app (failure is scoped to the scenario)');
  ok(b.scenarios.create.median > 0, 'create still produced a number');
  eq(b.scenarios.clear.toPaint.median, null, 'clear: msToPaint median is null too — neither timing is fabricated');

  // A PLAINLY dead button never even reaches the stopwatch now: the contract gate
  // clicks #clear and refuses the app outright. Strictly earlier than the timed
  // failure above, and with a diagnostic that names the contract rather than the
  // clock.
  const deadOut = path.join(outDir, 'selftest-deadclear.json');
  const deadCode = await runBench([
    '--dir', fixture.deadclear, '--app', 'rows', '--label', 'selftest-deadclear',
    '--runs', '1', '--weight-runs', '1', '--timeout', '1200', '--out', deadOut, '--no-aot',
  ]);
  eq(deadCode, 3, 'a permanently dead #clear is refused by the contract gate (exit 3), before any timing');
  eq(await fsp.readFile(deadOut, 'utf8').catch(() => null), null, 'dead #clear: no results file written');
}

async function testContractPreflight(fixture, outDir) {
  section('9. A DOM-contract mismatch fails fast, not as a fake "slow" number');
  const out = path.join(outDir, 'selftest-nolabel.json');
  const code = await runBench([
    '--dir', fixture.nolabel,
    '--app', 'rows',
    '--label', 'selftest-fixture-nolabel',
    '--runs', '1',
    '--weight-runs', '1',
    '--timeout', '1200',
    '--out', out,
  ]);
  eq(code, 3, 'bench.mjs exits 3 (contract not met) rather than reporting numbers');
  const wrote = await fsp.readFile(out, 'utf8').catch(() => null);
  eq(wrote, null, 'no results file is written when the contract is unmet');

  // And the good fixture must pass the same gate.
  const r = JSON.parse(await fsp.readFile(path.join(outDir, 'selftest-rows.json'), 'utf8'));
  eq(r.contractCheck.problems.length, 0, 'the conforming fixture passes the contract preflight');
  eq(r.contractCheck.observed.rowCount, 1000, 'preflight observed 1000 rows');
  eq(r.contractCheck.observed.cellsPerRow, 3, 'preflight observed the row cell shape');
  ok(r.contractCheck.observed.row1Id !== r.contractCheck.observed.row998Id,
    'preflight confirmed row 1 and row 998 ids differ (swap predicate is non-vacuous)');
}

/**
 * The load-bearing test of this whole audit. Each fixture below does STRICTLY LESS
 * WORK than the contract requires, yet satisfied every gate the harness had before:
 * 1000 rows, >= 2 cells, distinct ids at 1/998, row 990 without " !!!". Each would
 * therefore have been reported as a clean, FAST, `ok` result — "Filament beats
 * Blazor" measured on unequal work. Every one must now be refused with exit 3.
 */
async function testUnequalWorkIsRefused(fixture, outDir) {
  section('10. An app doing LESS WORK is refused, not reported as fast');

  const cases = [
    {
      dir: fixture.constlabel,
      label: 'constlabel',
      what: 'constant label instead of 1000 generated ones',
      expect: /deterministic sequence requires|label generator is constant/,
    },
    {
      dir: fixture.lazyupdate,
      label: 'lazyupdate',
      what: '#update touching 1 row instead of 100',
      expect: /#update appended " !!!" to only 1 of the 100 required rows/,
    },
    {
      dir: fixture.halfswap,
      label: 'halfswap',
      what: '#swaprows duplicating instead of swapping',
      expect: /index 998.*must hold the row from index 1|duplicate, not a swap/s,
    },
  ];

  for (const c of cases) {
    const out = path.join(outDir, `selftest-${c.label}.json`);
    // eslint-disable-next-line no-await-in-loop
    const code = await runBench([
      '--dir', c.dir, '--app', 'rows', '--label', `selftest-${c.label}`,
      '--runs', '1', '--weight-runs', '1', '--timeout', '2500', '--out', out, '--no-aot',
    ]);
    eq(code, 3, `${c.label} (${c.what}): exits 3 — numbers REFUSED`);
    // eslint-disable-next-line no-await-in-loop
    const wrote = await fsp.readFile(out, 'utf8').catch(() => null);
    eq(wrote, null, `${c.label}: no results file written — it cannot become a headline number`);
  }
}

/**
 * Regression test for the fatal vsync-quantization defect. This fixture is fully
 * conforming but mutates synchronously, so #clear and #swaprows are genuinely
 * sub-millisecond. The old rAF+setTimeout-terminated clock could not report them as
 * anything other than ~one frame interval.
 */
async function testSubFrameWorkIsVisible(fixture, outDir) {
  section('11. Sub-millisecond work is measurable (vsync quantization defeated)');
  const out = path.join(outDir, 'selftest-sync.json');
  const code = await runBench([
    '--dir', fixture.sync, '--app', 'rows', '--label', 'selftest-sync',
    '--runs', '7', '--weight-runs', '1', '--out', out, '--no-aot',
  ]);
  eq(code, 0, 'the synchronous fixture is contract-conforming and runs clean');
  const s = JSON.parse(await fsp.readFile(out, 'utf8'));

  // clear/swap are a textContent reset and two insertBefore calls respectively.
  for (const name of ['clear', 'swap']) {
    const sc = s.scenarios[name];
    eq(sc.status, 'ok', `${name}: status ok`);
    ok(sc.median < 5,
      `${name}: msToMutation median (${sc.median} ms) is sub-frame — the real DOM work is visible`,
      'the old rAF-terminated clock reported ~8-17 ms here regardless of framework speed');
    ok(sc.toPaint.median >= sc.median,
      `${name}: msToPaint (${sc.toPaint.median} ms) >= msToMutation (${sc.median} ms)`);
  }

  // The headline metric must be able to distinguish work that differs. create builds
  // 1000 rows; clear empties them. If both reported the frame interval — the old
  // behaviour — this ratio would be ~1.
  const ratio = s.scenarios.create.median / Math.max(s.scenarios.clear.median, 0.001);
  ok(ratio > 3,
    `create/clear median ratio is ${ratio.toFixed(1)}x — the metric resolves different amounts of work`,
    'a vsync-quantized clock would flatten this to ~1x');
  process.stdout.write(
    `  note create=${s.scenarios.create.median}ms clear=${s.scenarios.clear.median}ms ` +
    `swap=${s.scenarios.swap.median}ms | toPaint: create=${s.scenarios.create.toPaint.median}ms ` +
    `clear=${s.scenarios.clear.toPaint.median}ms swap=${s.scenarios.swap.toPaint.median}ms\n`,
  );
}

/**
 * End-to-end regression test for the Web Worker blind spot.
 *
 * Without the Target.attachedToTarget hand-off, the worker SCRIPT's request pins
 * inFlight at 1 forever: every iteration burns the full maxSettleMs and then fails
 * outright now that the settle result is enforced. Without the server-ledger
 * reconciliation, the worker's own 256 KB fetch vanishes from the weight silently.
 * Both must hold, or a threaded WASM build is either un-benchmarkable or
 * mis-measured.
 */
async function testWorkerTargetVisibility(fixture, outDir) {
  section('13. Web Worker target: settles, and its invisible bytes are reported');
  const out = path.join(outDir, 'selftest-worker.json');
  const started = Date.now();
  const code = await runBench([
    '--dir', fixture.worker, '--app', 'rows', '--label', 'selftest-worker',
    '--runs', '1', '--weight-runs', '1', '--max-settle-ms', '8000', '--out', out, '--no-aot',
  ]);
  const elapsed = Date.now() - started;
  eq(code, 0, 'a Web-Worker app still benchmarks cleanly (inFlight is not pinned by the worker script)');
  const w = JSON.parse(await fsp.readFile(out, 'utf8'));
  eq(w.scenarios.create.status, 'ok', 'create: status ok despite the worker target');
  ok(w.scenarios.create.median > 0, 'create produced a real number');
  // Each iteration would have burned the full 8s maxSettleMs if inFlight stayed pinned.
  ok(elapsed < 8000 * (1 + 1 + 4),
    `run did not stall on maxSettleMs (${(elapsed / 1000).toFixed(1)}s elapsed)`,
    'a pinned inFlight would add 8s to every page load');

  // The worker's own fetch must be reported as missing, with its size.
  const missing = w.weight.untrackedRequests.find((u) => u.path === '/worker-payload.data');
  ok(!!missing, 'the worker-fetched payload is reported as untracked',
    JSON.stringify(w.weight.untrackedRequests.map((u) => u.path)));
  ok(missing && missing.missingBytes > WORKER_PAYLOAD_BYTES * 0.9,
    `its full size is reported as missing (${missing && missing.missingBytes} B of ~${WORKER_PAYLOAD_BYTES} B)`,
    'random bytes cannot be compressed away, so this is a real byte gap');
  ok(w.weight.warnings.some((x) => /MISSING from the transferred total/.test(x)),
    'weight.warnings names the missing bytes so they cannot become a silent under-count');
  ok(w.weight.warnings.some((x) => /handed off to a worker target/.test(x)),
    'weight.warnings records the worker hand-off that would otherwise pin inFlight');
}

/** Unit test for the server/CDP reconciliation that replaces unreachable auto-attach. */
function testUntrackedReconciliation() {
  section('12. Bytes invisible to the page-scoped CDP session are detected (unit)');
  const origin = 'http://127.0.0.1:8080';
  const cdp = {
    requests: [
      { url: `${origin}/index.html`, transferredBytes: 700 },
      { url: `${origin}/app.js`, transferredBytes: 1100 },
      // Worker SCRIPT: requestWillBeSent reached this session, but the completion
      // events went to the worker target, so CDP banked 0 bytes for it.
      { url: `${origin}/worker.js`, transferredBytes: 0 },
    ],
  };
  const server = {
    byPath: {
      '/index.html': { bytes: 500, hits: 1 },
      '/app.js': { bytes: 900, hits: 1 },
      '/worker.js': { bytes: 400, hits: 1 },
      // Fetched BY the worker: CDP never saw it at all.
      '/_framework/dotnet.native.wasm': { bytes: 2_000_000, hits: 1 },
    },
  };
  const untracked = findUntrackedRequests(cdp, server, origin);
  eq(untracked.length, 2, 'both the invisible fetch and the under-counted worker script are flagged');
  eq(untracked[0].path, '/_framework/dotnet.native.wasm', 'largest gap first: the invisible path is named');
  eq(untracked[0].missingBytes, 2_000_000, 'its missing bytes are quantified');
  ok(/never reported/.test(untracked[0].reason), 'reason distinguishes "never reported"');
  eq(untracked[1].path, '/worker.js', 'the under-counted worker script is flagged too');
  eq(untracked[1].missingBytes, 400, 'a partial under-count is quantified, not just a missing path');
  ok(/fewer bytes/.test(untracked[1].reason), 'reason distinguishes "fewer bytes than the server wrote"');

  // CDP >= server body is the healthy case (CDP additionally counts headers).
  eq(findUntrackedRequests(
    { requests: [{ url: `${origin}/index.html`, transferredBytes: 700 }] },
    { byPath: { '/index.html': { bytes: 500, hits: 1 } } },
    origin,
  ).length, 0, 'no false positives when CDP saw everything (headers make CDP >= server body)');
}

// ---------------------------------------------------------------------------
// main
// ---------------------------------------------------------------------------
const keep = process.argv.includes('--keep');
const scratch = process.env.SELFTEST_DIR
  || path.join(os.tmpdir(), `filament-bench-selftest-${process.pid}`);
const fixtureRoot = path.join(scratch, 'fixture');
const outDir = path.join(scratch, 'out');

process.stdout.write(`Filament bench harness self-test\nfixture: ${fixtureRoot}\n`);
await fsp.mkdir(outDir, { recursive: true });
const fixture = await makeFixture(fixtureRoot);

try {
  testAcceptEncodingParsing();
  testStatistics();
  testUntrackedReconciliation();
  await testServer(fixture);
  await testEndToEnd(fixture, outDir);
  await testFailureReporting(fixture, outDir);
  await testContractPreflight(fixture, outDir);
  await testUnequalWorkIsRefused(fixture, outDir);
  await testSubFrameWorkIsVisible(fixture, outDir);
  await testWorkerTargetVisibility(fixture, outDir);
} finally {
  if (!keep) {
    await fsp.rm(scratch, { recursive: true, force: true }).catch(() => {});
  } else {
    process.stdout.write(`\n(kept fixture + results at ${scratch})\n`);
  }
}

section('Summary');
process.stdout.write(`${passed} passed, ${failures.length} failed\n`);
if (failures.length) {
  for (const f of failures) process.stdout.write(`  FAIL ${f.name}${f.detail ? ` — ${f.detail}` : ''}\n`);
  process.exitCode = 1;
}
