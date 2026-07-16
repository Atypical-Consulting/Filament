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
import {
  startServer, acceptsGzip, acceptsBrotli, negotiateEncoding, isCompressible,
  parseCliArgs, ENCODING_CEILINGS,
} from './server.mjs';
import {
  main as benchMain, quantile, summarize, loadExpectedLabels, findUntrackedRequests,
  representativeIndex, collectAotEvidence, classifyAotEvidence, SCENARIO_SPECS,
} from './bench.mjs';

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

/* Emits the contract's row EXACTLY:
     <tr><td class="col-md-1">{id}</td><td class="col-md-4"><a class="lbl">{label}</a></td></tr>
   This fixture previously emitted THREE unclassed cells (id, label, a literal "x"),
   which is not the contract and not what the Blazor baseline renders. It passed
   only because the old gate asked for cellsPerRow >= 2 and read textContent. The
   strict markup gate refuses it now — correctly. A "reference implementation of the
   shared DOM contract" that the contract check rejects was a reference to nothing. */
function buildRows(count) {
  const frag = document.createDocumentFragment();
  for (let i = 0; i < count; i++) {
    const id = nextId++;
    const tr = document.createElement('tr');
    const idCell = document.createElement('td');
    idCell.className = 'col-md-1';
    idCell.textContent = String(id);
    const labelCell = document.createElement('td');
    labelCell.className = 'col-md-4';
    const a = document.createElement('a');
    a.className = 'lbl';
    a.textContent = nextLabel();
    labelCell.appendChild(a);
    tr.appendChild(idCell);
    tr.appendChild(labelCell);
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
    labelCell.className = 'col-md-4';
    const a = document.createElement('a');
    a.className = 'lbl';
    a.textContent = nextLabel();
    labelCell.appendChild(a);
    tr.appendChild(idCell);
    tr.appendChild(labelCell);`,
  `    nextLabel(); /* keep the LCG in step; just never render the label */
    tr.appendChild(idCell);`,
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

/**
 * THE fixture for the headline gate. Fully correct on the FIRST #run — it runs the
 * real Park-Miller LCG and emits the byte-exact golden stream — and then reuses those
 * 1000 interned strings on every later #run, doing ZERO of the 3000 multiply/modulo
 * ops and ZERO of the 1000 three-part concatenations.
 *
 * This is the cheat CONST_LABEL_JS does not model. A constant-label app is refused on
 * its first run, i.e. for a reason a correct-first-run/cached-thereafter app never
 * triggers — so it proves nothing about the run create-warm actually times. This one
 * passes a first-run-only gate byte-exactly and then posts a create-warm win on
 * strictly less work, which is the exact shape of the failure the project cares most
 * about: C4 decided on unequal work, reported as status "ok".
 *
 * Note it keeps the id counter honest (ids really are 1001..2000 on the second run),
 * so it is the LABEL stream that must catch it, not the id check.
 */
const CACHED_LABEL_JS = mustReplace(
  mustReplace(
    ROWS_APP_JS,
    `function buildRows(count) {
  const frag = document.createDocumentFragment();`,
    `/* Generate once, reuse forever. Correct on run 1, free on every run after. */
let LABEL_CACHE = null;
function labelsFor(count) {
  if (!LABEL_CACHE) {
    LABEL_CACHE = [];
    for (let k = 0; k < count; k++) LABEL_CACHE.push(nextLabel());
  }
  return LABEL_CACHE;
}
function buildRows(count) {
  const labels = labelsFor(count);
  const frag = document.createDocumentFragment();`,
    'a cached label generator',
  ),
  `    a.textContent = nextLabel();`,
  `    a.textContent = labels[i];`,
  'a cached label lookup',
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
 * ---- Boot-cost fixtures: the direct test of the cold/warm split ---------------
 *
 * These model a framework that defers one-time runtime initialisation to its first
 * interaction — which is what Blazor does, and why the cold `create` number is
 * ~72% runtime boot rather than row-building. The FIRST click of any button
 * busy-waits BOOT_COST_MS before doing its DOM work; every later click does the
 * work alone.
 *
 * The boot cost is paid INSIDE the measured window (after the click, before the
 * mutation) — exactly where a real runtime pays it, and exactly where the harness
 * cannot avoid billing it to the click. At 400 ms it is ~6.7x the fixture's 60 ms
 * async dispatch delay, so the two variants cannot be confused:
 *
 *   create-cold  >= ~460 ms  (boot + dispatch + work)
 *   create-warm  ~=   60 ms  (dispatch + work — boot was paid by the untimed setup)
 *
 * This is what makes the warm variants falsifiable rather than merely asserted. If
 * the untimed setup silently did not run, or if the timed segment still enclosed
 * it, create-warm would land at >= 460 ms and section 14 fails.
 */
const BOOT_COST_MS = 400;

const BOOT_DEFERRED_SRC = `var booted = false;
/* One-time runtime boot, paid by whichever interaction happens FIRST — the same
   place a lazily-initialised framework pays it: inside the first click, inside the
   measured window. A busy-wait (not a timer) so it is real main-thread work that no
   amount of clever scheduling can hide. */
function bootOnce() {
  if (booted) return;
  booted = true;
  var end = performance.now() + ${BOOT_COST_MS};
  while (performance.now() < end) { /* models runtime boot */ }
}
function deferred(fn) {
  return function () {
    Promise.resolve().then(function () {
      setTimeout(function () { bootOnce(); fn(); }, ASYNC_DELAY_MS);
    });
  };
}`;

const PLAIN_DEFERRED_SRC = `function deferred(fn) {
  return function () {
    Promise.resolve().then(function () { setTimeout(fn, ASYNC_DELAY_MS); });
  };
}`;

/** Rows app that pays a 400 ms runtime boot on its first interaction. */
const BOOT_COST_ROWS_JS = mustReplace(
  ROWS_APP_JS,
  PLAIN_DEFERRED_SRC,
  BOOT_DEFERRED_SRC,
  'a one-time boot cost on the first interaction',
);

/** Counter app that pays a 400 ms runtime boot on its first interaction. */
const BOOT_COST_COUNTER_JS = mustReplace(
  COUNTER_APP_JS,
  PLAIN_DEFERRED_SRC,
  BOOT_DEFERRED_SRC,
  'a one-time boot cost on the first increment',
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

/**
 * ---- Strict row-markup fixture ---------------------------------------------
 *
 * THE cheat the strict markup gate exists to refuse, and the reason the gate was
 * written at all. This app is correct on every other axis — 1000 rows, the exact
 * golden Park-Miller label stream, honest monotonic ids, real #update/#swaprows/
 * #clear semantics — so every gate that existed before waves it through. What it
 * does is render
 *
 *     <tr><td>1</td><td>adorable pink desk</td></tr>
 *
 * instead of the contract's
 *
 *     <tr><td class="col-md-1">1</td><td class="col-md-4"><a class="lbl">adorable pink desk</a></td></tr>
 *
 * skipping 1000 <a> elements and 2000 class attributes per #run — roughly 3000
 * DOM operations Blazor performs and this app does not. The old gate asked only
 * for `cellsPerRow >= 2` and read `td:nth-child(n).textContent`, both of which
 * this satisfies perfectly. It would therefore have posted a large, entirely
 * unearned create-warm win against Blazor and been reported as status "ok".
 */
const LOOSE_MARKUP_JS = mustReplace(
  ROWS_APP_JS,
  `    const idCell = document.createElement('td');
    idCell.className = 'col-md-1';
    idCell.textContent = String(id);
    const labelCell = document.createElement('td');
    labelCell.className = 'col-md-4';
    const a = document.createElement('a');
    a.className = 'lbl';
    a.textContent = nextLabel();
    labelCell.appendChild(a);`,
  `    const idCell = document.createElement('td');
    idCell.textContent = String(id);
    const labelCell = document.createElement('td');
    labelCell.textContent = nextLabel();`,
  'simpler markup than the contract',
);

/**
 * ---- C3 fixtures: GROUND TRUTH for the DOM-write instrument -----------------
 *
 * The warm clock was validated against a fixture with a known 400 ms boot rather
 * than by asserting it looked right; the same precedent applies here. A DOM-write
 * counter that has never been shown a known number of DOM writes is not an
 * instrument, it is an opinion. Each fixture below performs an EXACTLY known
 * number of writes per increment, by construction.
 *
 * All handlers are SYNCHRONOUS. The 60 ms async dispatch every other fixture uses
 * would make the allocation probe (thousands of increments) take minutes, and C3
 * is not a timing measurement — nothing here depends on the dispatch shape.
 */

/** The counter contract's DOM, minus any framework. */
const C3_PRELUDE = `/* C3 fixture — vanilla JS, synchronous, known DOM-write count per increment. */
let count = 0;
const valueEl = document.getElementById('counter-value');
`;

/**
 * EXACTLY 1 DOM write per increment: one characterData mutation, by patching the
 * TEXT NODE's data in place. This is what Blazor itself does (measured: a single
 * characterData record per increment), and it is what C3 demands.
 *
 * NOTE THE TRAP, which this fixture caught the hard way. The obvious spelling
 *
 *     valueEl.textContent = String(count);
 *
 * is TWO DOM writes, not one: setting textContent on an element that already has a
 * text child REMOVES the old node and APPENDS a new one, which a MutationObserver
 * reports as a childList record carrying removedNodes:1 + addedNodes:1. This
 * fixture originally used exactly that spelling and asserted it was 1 write; the
 * instrument said 2 and the instrument was right.
 *
 * The distinction is not pedantry — it is C3. A Filament that assigns .textContent
 * FAILS "exactly 1 DOM write per increment" while Blazor, which patches the text
 * node in place, PASSES. Same visible result, twice the DOM writes.
 */
const C3_ONE_WRITE_JS = `${C3_PRELUDE}
document.getElementById('increment').addEventListener('click', function () {
  count += 1;
  /* 1 write: characterData on the EXISTING text node. Not .textContent = x, which
     is a remove + an append and therefore two writes. */
  valueEl.firstChild.data = String(count);
});
`;

/**
 * EXACTLY 3 DOM writes per increment, by construction — explicit ops rather than
 * anything whose record count has to be guessed:
 *   1. attributes  — setAttribute
 *   2. childList   — removeChild (removedNodes: 1)
 *   3. childList   — appendChild (addedNodes: 1)
 * The instrument must report 3, not 1 and not 2. This is the fixture that proves
 * the counter can FAIL something — a counter that only ever sees conforming apps
 * has demonstrated nothing.
 */
const C3_THREE_WRITES_JS = `${C3_PRELUDE}
document.getElementById('increment').addEventListener('click', function () {
  count += 1;
  var s = String(count);
  valueEl.setAttribute('data-count', s);              /* write 1: attributes */
  valueEl.removeChild(valueEl.firstChild);            /* write 2: childList, removed 1 */
  valueEl.appendChild(document.createTextNode(s));    /* write 3: childList, added 1 */
});
`;

/** 1 write, and a self-report that honestly says 1. The cross-check must AGREE. */
const C3_TRUTHFUL_STATS_JS = `${C3_PRELUDE}
const stats = { domWrites: 0 };
window.__filament = { stats: stats };
document.getElementById('increment').addEventListener('click', function () {
  count += 1;
  valueEl.firstChild.data = String(count);   /* 1 write — see C3_ONE_WRITE_JS */
  stats.domWrites += 1;
});
`;

/**
 * THE fixture for cross-check (b). Does 3 real DOM writes and self-reports 1.
 *
 * This is not a strawman: it is the exact shape of an honest bug. A runtime that
 * instruments its fast path and forgets its fallback path reports 1 while doing 3,
 * and every C3 claim built on the self-report is then false while looking perfect.
 * A harness that trusted __filament.stats — or worse, one that "reconciled" the
 * disagreement by preferring either side — would report C3 as a PASS here. The
 * instrument must catch it, and must refuse to resolve it.
 */
const C3_LYING_STATS_JS = `${C3_PRELUDE}
const stats = { domWrites: 0 };
window.__filament = { stats: stats };
document.getElementById('increment').addEventListener('click', function () {
  count += 1;
  var s = String(count);
  /* Three real writes... */
  valueEl.setAttribute('data-count', s);
  valueEl.removeChild(valueEl.firstChild);
  valueEl.appendChild(document.createTextNode(s));
  /* ...and a self-report that claims one. */
  stats.domWrites += 1;
});
`;

/**
 * GROUND TRUTH for the allocation probe: allocates a known, dominant payload per
 * increment on top of 1 DOM write.
 *
 * A packed double array is the most predictable thing to allocate in V8: 256
 * doubles is a FixedDoubleArray of 256*8 B + header, ~2 KB, plus a small JSArray.
 * It is retained in a sink so nothing can be optimised away, and the sink is
 * overwritten each time so this is allocation THROUGHPUT (garbage), not growth —
 * which is exactly the quantity C3 names and exactly what a heap-snapshot delta
 * would miss.
 *
 * The assertion band is deliberately wide (see the test). A 1024 B sampling
 * interval cannot deliver byte precision and pretending otherwise would be the
 * fabrication this harness exists to avoid. Order-of-magnitude separation from
 * the ~0 fixture is the real claim, and it is the claim C3 actually needs.
 */
const C3_ALLOC_PER_INCREMENT_DOUBLES = 256;
const C3_ALLOC_JS = `${C3_PRELUDE}
window.__sink = null;
document.getElementById('increment').addEventListener('click', function () {
  count += 1;
  var a = new Array(${C3_ALLOC_PER_INCREMENT_DOUBLES});
  for (var i = 0; i < ${C3_ALLOC_PER_INCREMENT_DOUBLES}; i++) { a[i] = i + 0.5; }
  window.__sink = a;   /* retained until the next increment: real garbage, not DCE'd */
  valueEl.firstChild.data = String(count);   /* 1 write, so this fixture differs from
                                                c3one ONLY in what it allocates */
});
`;

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
    // Correct on the first #run, cached on every one after — the cheat that a
    // first-run-only fairness gate cannot see, aimed at the run create-warm times.
    cachedlabel: path.join(root, 'cachedlabel'),
    lazyupdate: path.join(root, 'lazyupdate'),
    halfswap: path.join(root, 'halfswap'),
    // Correct labels, correct ids, correct semantics — but simpler markup than
    // the contract, i.e. ~3000 fewer DOM ops per #run than Blazor.
    loosemarkup: path.join(root, 'loosemarkup'),
    // Conforming, but synchronous — the vsync-quantization regression test.
    sync: path.join(root, 'sync'),
    // Conforming, but fetches from a Web Worker target (the WasmEnableThreads shape).
    worker: path.join(root, 'worker'),
    // Conforming, but pays a 400 ms runtime boot on first interaction — the
    // cold-vs-warm regression tests.
    bootcost: path.join(root, 'bootcost'),
    bootcounter: path.join(root, 'bootcounter'),
    // C3 ground truth. Each does an EXACTLY known number of DOM writes per
    // increment, so the instrument is validated against a known number rather
    // than assumed to be right.
    c3one: path.join(root, 'c3one'),
    c3three: path.join(root, 'c3three'),
    c3truthful: path.join(root, 'c3truthful'),
    c3liar: path.join(root, 'c3liar'),
    c3alloc: path.join(root, 'c3alloc'),
  };
  for (const d of Object.values(dirs)) await fsp.mkdir(d, { recursive: true });

  const rowsApps = [
    [dirs.rows, 'Rows fixture', ROWS_APP_JS],
    [dirs.broken, 'State-dependent-clear rows fixture', BROKEN_CLEAR_JS],
    [dirs.deadclear, 'Dead-clear rows fixture', DEAD_CLEAR_JS],
    [dirs.nolabel, 'No-label-cell fixture', NO_LABEL_CELL_JS],
    [dirs.constlabel, 'Constant-label fixture', CONST_LABEL_JS],
    [dirs.cachedlabel, 'Cached-label fixture', CACHED_LABEL_JS],
    [dirs.lazyupdate, 'One-row-update fixture', LAZY_UPDATE_JS],
    [dirs.halfswap, 'One-directional-swap fixture', HALF_SWAP_JS],
    [dirs.loosemarkup, 'Loose-markup rows fixture', LOOSE_MARKUP_JS],
    [dirs.sync, 'Synchronous rows fixture', SYNC_ROWS_JS],
    [dirs.worker, 'Web-Worker rows fixture', WORKER_ROWS_JS],
    [dirs.bootcost, 'Boot-cost rows fixture', BOOT_COST_ROWS_JS],
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

  const counterApps = [
    [dirs.counter, COUNTER_APP_JS],
    [dirs.bootcounter, BOOT_COST_COUNTER_JS],
    [dirs.c3one, C3_ONE_WRITE_JS],
    [dirs.c3three, C3_THREE_WRITES_JS],
    [dirs.c3truthful, C3_TRUTHFUL_STATS_JS],
    [dirs.c3liar, C3_LYING_STATS_JS],
    [dirs.c3alloc, C3_ALLOC_JS],
  ];
  for (const [dir, js] of counterApps) {
    // eslint-disable-next-line no-await-in-loop
    await writeCommonAssets(dir);
    // eslint-disable-next-line no-await-in-loop
    await fsp.writeFile(path.join(dir, 'index.html'), COUNTER_HTML, 'utf8');
    // eslint-disable-next-line no-await-in-loop
    await fsp.writeFile(path.join(dir, 'app.js'), js, 'utf8');
  }

  return dirs;
}

/**
 * Synthetic publish trees for the AOT evidence check. Sizes and the fingerprinted
 * filename shape are taken from this project's own publish output:
 *   blazor-rows-aot    dotnet.native.ogsd35n1u1.wasm  11,380,806 B
 *   blazor-rows-nojit  dotnet.native.kllr7zg72l.wasm   1,494,734 B
 * Written sparsely (fsp.truncate) so 11 MB costs no real disk or time.
 */
async function makeAotFixture(root) {
  const dirs = {
    aot: path.join(root, 'aot-publish'),
    nojit: path.join(root, 'nojit-publish'),
    indeterminate: path.join(root, 'indeterminate-publish'),
    nosignature: path.join(root, 'nosignature-publish'),
    // A publish tree holding BOTH a stale 11 MB AOT runtime from an earlier build
    // (never served) and the 4.5 MB runtime the app actually boots (served, and in
    // the indeterminate band — e.g. a newer SDK). The largest-first sort puts the
    // stale file at the head of the evidence list, so a classifier that prefers
    // "conclusive" over "served" confirms an AOT claim from a file the browser never
    // requested, and suppresses the INDETERMINATE warning about the one it did.
    stale: path.join(root, 'stale-publish'),
  };
  const artifacts = [
    [dirs.aot, 'dotnet.native.ogsd35n1u1.wasm', 11_380_806],
    [dirs.nojit, 'dotnet.native.kllr7zg72l.wasm', 1_494_734],
    // Between the thresholds: the harness must say "indeterminate", never guess.
    [dirs.indeterminate, 'dotnet.native.mid1234567.wasm', 4_500_000],
    [dirs.stale, 'dotnet.native.old0000000.wasm', 11_380_806],
    [dirs.stale, 'dotnet.native.new1111111.wasm', 4_500_000],
  ];
  for (const [dir, name, size] of artifacts) {
    const fw = path.join(dir, 'wwwroot', '_framework');
    // eslint-disable-next-line no-await-in-loop
    await fsp.mkdir(fw, { recursive: true });
    // eslint-disable-next-line no-await-in-loop
    const fh = await fsp.open(path.join(fw, name), 'w');
    // eslint-disable-next-line no-await-in-loop
    await fh.truncate(size);
    // eslint-disable-next-line no-await-in-loop
    await fh.close();
    // Compressed siblings, exactly as `dotnet publish` emits them. The walker must
    // read the RAW artifact and never mistake a .br/.gz for it — a .br of an AOT
    // runtime is ~3 MB and would classify as "interpreted".
    // eslint-disable-next-line no-await-in-loop
    await fsp.writeFile(path.join(fw, name + '.br'), Buffer.alloc(1024));
    // eslint-disable-next-line no-await-in-loop
    await fsp.writeFile(path.join(fw, name + '.gz'), Buffer.alloc(2048));
  }
  // A framework with no AOT signature at all — e.g. Filament. Must yield
  // "no evidence", never a false verdict.
  await fsp.mkdir(dirs.nosignature, { recursive: true });
  await fsp.writeFile(path.join(dirs.nosignature, 'app.js'), 'console.log(1)\n', 'utf8');
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

/**
 * runBench, but capturing what the harness said while refusing. Exit 3 alone proves
 * the app was refused; it does not prove it was refused for the RIGHT reason, and a
 * fixture that trips an unrelated gate would silently stop testing the gate it was
 * written for. bench.mjs writes its diagnostics with process.stderr.write, so they
 * are intercepted here and still forwarded, keeping the run debuggable.
 */
async function runBenchCapturingStderr(args) {
  const chunks = [];
  const original = process.stderr.write.bind(process.stderr);
  process.stderr.write = (chunk, ...rest) => {
    chunks.push(typeof chunk === 'string' ? chunk : String(chunk));
    return original(chunk, ...rest);
  };
  try {
    const code = await benchMain(args);
    return { code, stderr: chunks.join('') };
  } finally {
    process.stderr.write = original;
  }
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
  ok(r.weight.decodedBytesRepresentativeRun > bytes,
    'transferred bytes are BELOW decoded bytes — we measured the compressed wire, not disk size',
    `transferred=${bytes} decoded=${r.weight.decodedBytesRepresentativeRun}`);

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
  for (const name of ['create-cold', 'create-warm', 'update', 'swap', 'clear']) {
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

    // Every scenario must state, in the JSON next to its number, whether that
    // number carries a runtime-boot term. A reader who quotes `median` without
    // reading the docs still cannot mistake a cold number for a rendering one.
    ok(s.runtime === 'cold' || s.runtime === 'warm', `${name}: declares runtime cold|warm`, s.runtime);
    eq(typeof s.headline, 'boolean', `${name}: declares whether it is the headline number`);
    ok(Array.isArray(s.untimedSetup), `${name}: records its untimed setup steps (empty for cold)`);
    ok(typeof s.measures === 'string' && s.measures.length > 40, `${name}: documents what it measures`);
  }
  ok(r.scenarios['create-cold'].diagnostics.pageErrors.length === 0, 'no page errors during the rows run',
    JSON.stringify(r.scenarios['create-cold'].diagnostics.pageErrors));

  // --- cold and warm are BOTH reported, and only warm is the headline ---
  eq(r.scenarios['create-cold'].runtime, 'cold', 'create-cold is reported and marked cold');
  eq(r.scenarios['create-warm'].runtime, 'warm', 'create-warm is reported and marked warm');
  eq(r.scenarios['create-cold'].headline, false, 'create-cold is NOT the headline (it carries runtime boot)');
  eq(r.scenarios['create-warm'].headline, true, 'create-warm IS the headline for C4');
  for (const n of ['update', 'swap', 'clear']) {
    eq(r.scenarios[n].runtime, 'warm', `${n}: still warm (it always had an untimed #run setup)`);
    // The same beat create-warm gets, recorded in the JSON that claims they are
    // equivalent. Asserted on a REAL run, not just on the spec table.
    const setup = r.scenarios[n].untimedSetup;
    ok(/idle beat/.test(setup[setup.length - 1]),
      `${n}: the JSON records the same settle beat create-warm gets, immediately before the timed click`,
      JSON.stringify(setup));
  }

  // --- the timed-iteration count is COMPUTED, not hand-counted ---
  eq(r.totals.scenarioCount, 5, 'totals: 5 rows scenarios');
  eq(r.totals.runsPerScenario, 5, 'totals: runs per scenario recorded');
  eq(r.totals.timedIterationsPlanned, 25, 'totals: planned timed iterations = 5 scenarios x 5 runs');
  eq(r.totals.timedIterationsRecorded, 25, 'totals: recorded count is summed from the retained samples');
  eq(r.totals.timedIterationFailures, 0, 'totals: no failures');
  eq(
    r.totals.timedIterationsRecorded,
    Object.values(r.scenarios).reduce((a, s) => a + s.n, 0),
    'totals: the recorded count re-derives exactly from the per-scenario sample counts',
  );

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

  // --- the gate covers the click create-warm TIMES, not just the first one ---
  eq(cg.observed.secondRunRowCount, 1000, 'contract gate drove the SECOND #run (the click create-warm times)');
  ok(Array.isArray(cg.observed.secondRunFirst5) && cg.observed.secondRunFirst5[0] === golden.secondRun.first5[0],
    'contract gate compared the SECOND run\'s label bytes against the second golden stream',
    JSON.stringify(cg.observed.secondRunFirst5));
  eq(cg.observed.secondRunRow1000, golden.secondRun.row1000,
    'contract gate checked the second run\'s 1000th label — the LCG really advanced 3000 more draws');
  ok(cg.observed.secondRunFirst5[0] !== cg.observed.first5[0],
    'the two runs really did emit different streams (the LCG is seeded per page load, never per click)',
    `${cg.observed.first5[0]} vs ${cg.observed.secondRunFirst5[0]}`);
  // The monotonic-id claim in protocol.monotonicState, asserted rather than narrated.
  eq(cg.observed.firstRunFirstId, '1', 'first run ids start at 1');
  eq(cg.observed.firstRunLastId, '1000', 'first run ids end at 1000');
  eq(cg.observed.secondRunFirstId, '1001', 'second run ids CONTINUE at 1001 — the counter is never reset');
  eq(cg.observed.secondRunLastId, '2000', 'second run ids end at 2000');
  eq(cg.observed.secondRunFirstId, golden.secondRunFirstId, 'and that matches the golden fixture');

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
  ok(r.scenarios['create-cold'].samples.length === 5, 'raw samples retained in the JSON');

  // --- the declared AOT flag is separated from observed evidence ---
  eq(r.environment.aotDeclared, false, 'the SELF-DECLARED aot flag is recorded under its own name');
  eq(r.environment.aotObserved, null,
    'aotObserved is null for a fixture with no AOT signature — the harness declines to guess');
  ok(!!r.environment.aotVerification, 'the full AOT verification record is emitted');
  eq(r.environment.aotVerification.basis, 'no-signature-matched',
    'the verification names WHY it could not observe (no .NET runtime artifact in a vanilla-JS fixture)');
  eq(r.environment.aotVerification.warnings.length, 0,
    'declaring --no-aot on an unverifiable build is not a warning (only an unverified TRUE claim is)',
    JSON.stringify(r.environment.aotVerification.warnings));
  eq(r.environment.aotVerification.verified, false, 'and it is honestly reported as NOT verified');

  // --- the per-run breakdown names the run it came from, honestly ---
  const rep = r.weight.representativeRun;
  ok(!!rep, 'weight names the run its breakdown was quoted from');
  ok(rep.index >= 0 && rep.index < r.weight.toInteractive.samples.length,
    'representative run index is a real run index', `${rep.index}`);
  eq(rep.totalBytes, r.weight.toInteractive.samples[rep.index],
    'representativeRun.totalBytes is that run\'s actual total');
  // 2 weight runs => both are equidistant from the interpolated median, so the
  // documented tie-break (lower total) decides. Deterministic, and it can never
  // flatter a weight number by quoting the heavier of the two.
  eq(rep.totalBytes, Math.min(...r.weight.toInteractive.samples),
    'with an even run count the tie resolves to the LOWER total, as documented');
  ok(typeof rep.isExactlyTheMedian === 'boolean',
    'representativeRun states whether it is EXACTLY the median rather than assuming it');
  ok(/NEAREST the interpolated/.test(rep.selection), 'representativeRun documents its selection rule');

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
  for (const n of ['increment-cold', 'increment-warm']) {
    eq(c.scenarios[n].status, 'ok', `${n}: status ok`);
    ok(c.scenarios[n].median >= ASYNC_DELAY_MS - 5,
      `${n}: median (${c.scenarios[n].median} ms) reflects the real deferred update`);
  }
  eq(c.scenarios['increment-cold'].runtime, 'cold', 'increment-cold marked cold');
  eq(c.scenarios['increment-warm'].runtime, 'warm', 'increment-warm marked warm');
  eq(c.scenarios['increment-cold'].headline, false, 'increment-cold is NOT the headline');
  eq(c.scenarios['increment-warm'].headline, true, 'increment-warm IS the headline');
  ok(c.weight.toInteractive.median > 0, 'counter: transferred bytes non-zero',
    `got ${c.weight.toInteractive.median}`);
  eq(c.totals.timedIterationsPlanned, 10, 'counter totals: 2 scenarios x 5 runs = 10 planned');
  eq(c.totals.timedIterationsRecorded, 10, 'counter totals: 10 recorded');

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
  eq(b.scenarios['create-cold'].status, 'ok', 'create-cold still ok in the same app (failure is scoped to the scenario)');
  ok(b.scenarios['create-cold'].median > 0, 'create-cold still produced a number');
  eq(b.scenarios.clear.toPaint.median, null, 'clear: msToPaint median is null too — neither timing is fabricated');

  // create-warm's untimed setup is #run THEN #clear, and this fixture's #clear only
  // works after #swaprows — so the SETUP itself hangs. That must be a failure with a
  // null median, exactly like a timed-segment failure: an untimed setup that
  // silently did not happen would leave the timed click measuring a cold runtime
  // while the JSON still called the scenario "warm". This is the negative proof that
  // the setup is real work the harness actually waits for.
  eq(b.scenarios['create-warm'].status, 'failed',
    'create-warm: a hanging UNTIMED SETUP is a failure, not a silently-skipped setup');
  eq(b.scenarios['create-warm'].median, null, 'create-warm: no number is reported when its setup could not run');
  ok(/timed out/i.test(b.scenarios['create-warm'].failures[0].error),
    'create-warm: the failure names the setup timeout', b.scenarios['create-warm'].failures[0].error);
  eq(b.totals.timedIterationsRecorded, 3,
    'totals: only the 3 scenarios that produced samples are counted (create-cold, update, swap)');
  eq(b.totals.timedIterationsPlanned - b.totals.timedIterationsRecorded, b.totals.timedIterationFailures,
    'totals: planned - recorded == the iterations refused a number');

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
  // Was 3 — the fixture used to emit an unclassed id cell, an unclassed label cell
  // and a literal "x" action cell, which is NOT the contract and NOT what Blazor
  // renders. It survived only because the old gate asked for `cellsPerRow >= 2`.
  eq(r.contractCheck.observed.cellsPerRow, 2, 'preflight observed the contract row cell shape');
  ok(r.contractCheck.observed.row1Id !== r.contractCheck.observed.row998Id,
    'preflight confirmed row 1 and row 998 ids differ (swap predicate is non-vacuous)');
}

/**
 * The strict row-markup gate. The old check (`cellsPerRow >= 2` + textContent) let
 * a framework emit simpler DOM than its rival and bank the difference as speed.
 *
 * The load-bearing assertion in this section is the LAST one: the strict check must
 * pass against the real, unmodified Blazor app. A gate that only rejects things has
 * not been shown to be right — it has been shown to be strict. Blazor's published
 * rows are byte-identical to the contract (verified directly: exactly two <td>, the
 * col-md-1/col-md-4 classes, the nested <a class="lbl">, and no stray text nodes),
 * so if this ever fails on Blazor the check is wrong, not Blazor.
 */
async function testStrictRowMarkup(fixture, outDir) {
  section('16. Row markup is asserted EXACTLY, not "at least 2 cells"');

  const out = path.join(outDir, 'selftest-loosemarkup.json');
  const { code, stderr } = await runBenchCapturingStderr([
    '--dir', fixture.loosemarkup,
    '--app', 'rows',
    '--label', 'selftest-fixture-loosemarkup',
    '--runs', '1',
    '--weight-runs', '1',
    '--timeout', '4000',
    '--out', out,
  ]);
  eq(code, 3, 'an app with simpler-than-contract row markup is refused (exit 3)');
  eq(await fsp.readFile(out, 'utf8').catch(() => null), null,
    'no results file is written for a markup violation');
  ok(/col-md-1/.test(stderr),
    'the failure names the missing col-md-1 class rather than failing opaquely');
  ok(/a class="lbl"|<a>/.test(stderr),
    'the failure names the missing nested <a class="lbl">');
  ok(/comparison is void|byte-equivalent/.test(stderr),
    'the failure explains WHY markup parity decides whether the timings mean anything');

  // The conforming fixture must still pass, and must record what it was held to.
  const r = JSON.parse(await fsp.readFile(path.join(outDir, 'selftest-rows.json'), 'utf8'));
  eq(r.contractCheck.observed.rowMarkup.conforms, true,
    'the conforming fixture passes the strict markup gate');
  eq(r.contractCheck.observed.rowMarkup.row0outerHTML,
    '<tr><td class="col-md-1">1</td><td class="col-md-4"><a class="lbl">adorable pink desk</a></td></tr>',
    'the emitted row is byte-identical to the contract — and to what Blazor renders');
  ok(r.contractCheck.observed.rowMarkup.checkedIndices.length >= 5,
    'the markup gate samples several rows, not just row 0 (a correct first row + 999 cheap ones is the cheat)');
}

/**
 * C3's instruments, each validated against a fixture whose answer is known by
 * construction. This is the precedent the 400 ms boot fixture set for the warm
 * clock: an instrument is only worth its output once it has been shown a number
 * it cannot get wrong by accident.
 */
async function testC3Instruments(fixture, outDir) {
  section('17. C3: the DOM-write instrument is validated against known ground truth');

  const runC3 = async (dir, label, extra = []) => {
    const out = path.join(outDir, `selftest-${label}.json`);
    const { code } = await runBenchCapturingStderr([
      '--dir', dir,
      '--app', 'counter',
      '--label', `selftest-${label}`,
      '--runs', '1',
      '--weight-runs', '1',
      '--timeout', '4000',
      '--c3',
      '--c3-increments', '4',
      '--out', out,
      ...extra,
    ]);
    const json = JSON.parse(await fsp.readFile(out, 'utf8'));
    return { code, json };
  };

  // ---- ground truth: exactly 1 write ---------------------------------------
  const one = await runC3(fixture.c3one, 'c3-one-write');
  eq(one.code, 0, 'the 1-write fixture runs clean');
  eq(JSON.stringify(one.json.c3.domWrites.writesPerIncrement), '[1,1,1,1]',
    'GROUND TRUTH: a fixture doing exactly 1 DOM write reads as exactly 1, on every increment');
  eq(one.json.c3.domWrites.byType.characterData, 1,
    'the 1 write is correctly classified as characterData');
  eq(one.json.c3.domWrites.byType.childList, 0, 'no childList mutation is miscounted');
  ok(/Exactly 1 DOM write/.test(one.json.c3.domWrites.verdict),
    'the verdict states the C3 criterion is met');

  // ---- ground truth: exactly 3 writes --------------------------------------
  // The instrument must be able to FAIL something. Without this, "it said 1" is
  // consistent with a counter that always says 1.
  const three = await runC3(fixture.c3three, 'c3-three-writes');
  eq(three.code, 0, 'the 3-write fixture runs clean (C3 is reported, not enforced by exit code)');
  eq(JSON.stringify(three.json.c3.domWrites.writesPerIncrement), '[3,3,3,3]',
    'GROUND TRUTH: a fixture doing exactly 3 DOM writes reads as exactly 3 — the counter is not stuck on 1');
  eq(three.json.c3.domWrites.byType.attributes, 1, 'the setAttribute write is counted as attributes');
  eq(three.json.c3.domWrites.byType.childList, 2, 'the removeChild + appendChild writes are counted as childList');
  ok(/NOT exactly 1 DOM write/.test(three.json.c3.domWrites.verdict),
    'the verdict reports a C3 violation rather than rounding it away');

  // ---- records vs writes are distinguished ---------------------------------
  eq(JSON.stringify(three.json.c3.domWrites.recordsPerIncrement), '[3,3,3,3]',
    'MutationRecords are reported alongside writes (one record can carry many nodes)');

  // ---- cross-check (b): agreement ------------------------------------------
  const truthful = await runC3(fixture.c3truthful, 'c3-truthful-stats');
  eq(truthful.json.c3.statsCrossCheck.present, true,
    '__filament.stats is detected when the runtime exposes it');
  eq(truthful.json.c3.statsCrossCheck.agrees, true,
    'an honest self-report agrees with the independent MutationObserver count');

  // ---- cross-check (b): DISAGREEMENT is a finding, not a reconciliation ----
  const liar = await runC3(fixture.c3liar, 'c3-lying-stats');
  eq(liar.json.c3.statsCrossCheck.present, true, 'the lying runtime\'s stats object is detected');
  eq(liar.json.c3.statsCrossCheck.agrees, false,
    'GROUND TRUTH: a runtime reporting 1 write while making 3 is CAUGHT');
  eq(JSON.stringify(liar.json.c3.statsCrossCheck.selfReportedDomWritesPerIncrement), '[1,1,1,1]',
    'the self-report is recorded verbatim (it claims 1)');
  eq(JSON.stringify(liar.json.c3.statsCrossCheck.observedDomWritesPerIncrement), '[3,3,3,3]',
    'the independent count is recorded verbatim (it observed 3)');
  ok(/DISAGREEMENT/.test(liar.json.c3.statsCrossCheck.finding),
    'the disagreement is reported loudly as a finding');
  ok(!/reconcil(ed|ing) (to|as)/.test(liar.json.c3.statsCrossCheck.finding)
     && /NOT reconciled/.test(liar.json.c3.statsCrossCheck.finding),
    'the harness refuses to silently pick a winner between the two instruments');
  // The self-report, taken alone, would have declared C3 met. This is the whole point.
  eq(liar.json.c3.statsCrossCheck.selfReportedDomWritesPerIncrement[0], 1,
    'the self-report ALONE would have reported C3 as met — which is why it cannot be the instrument');

  // ---- the observe root cannot be narrowed to hide writes ------------------
  eq(one.json.c3.domWrites.observeRoot, 'body',
    'the default observe root is body — the widest available, so writes cannot be hidden by root choice');
}

/**
 * The allocation probe, against a fixture that allocates a KNOWN payload per
 * increment and one that allocates ~nothing.
 *
 * The assertions are bands, not byte equalities, and deliberately so: a 1024 B
 * sampling profiler cannot deliver byte precision, and asserting a precise figure
 * would be asserting noise. What the probe must demonstrate is that it separates a
 * known allocator from a known non-allocator by an order of magnitude and lands in
 * the right neighbourhood — which is exactly the claim C3 rests on.
 */
async function testC3AllocationProbe(fixture, outDir) {
  section('18. C3: the allocation probe separates a known allocator from a known non-allocator');

  const runAlloc = async (dir, label) => {
    const out = path.join(outDir, `selftest-${label}.json`);
    const { code } = await runBenchCapturingStderr([
      '--dir', dir,
      '--app', 'counter',
      '--label', `selftest-${label}`,
      '--runs', '1',
      '--weight-runs', '1',
      '--timeout', '4000',
      '--c3-alloc',
      '--c3-increments', '2',
      // Small and fast: the probe's correctness does not depend on N, only its
      // resolution does, and these fixtures allocate ~2 KB/increment — far above
      // what this span can miss.
      '--c3-alloc-n-low', '100',
      '--c3-alloc-n-high', '500',
      '--c3-alloc-repeats', '2',
      '--out', out,
    ]);
    const json = JSON.parse(await fsp.readFile(out, 'utf8'));
    return { code, json };
  };

  const allocRun = await runAlloc(fixture.c3alloc, 'c3-alloc');
  eq(allocRun.code, 0, 'the known-allocator fixture runs clean');
  const allocBytes = allocRun.json.c3.allocation.bytesPerIncrement.median;

  const zeroRun = await runAlloc(fixture.c3one, 'c3-alloc-zero');
  eq(zeroRun.code, 0, 'the near-zero-allocator fixture runs clean');
  const zeroBytes = zeroRun.json.c3.allocation.bytesPerIncrement.median;

  // 256 packed doubles => FixedDoubleArray 256*8 + header ~= 2064 B, plus a small
  // JSArray. The band is wide because the instrument is a SAMPLER.
  ok(allocBytes > 1024 && allocBytes < 8192,
    `GROUND TRUTH: a fixture allocating ~2 KB/increment reads in the right neighbourhood (got ${allocBytes} B)`,
    `expected 1024..8192 B, got ${allocBytes}`);
  ok(zeroBytes < 512,
    `GROUND TRUTH: a fixture allocating ~nothing reads near zero (got ${zeroBytes} B)`,
    `expected < 512 B, got ${zeroBytes}`);
  ok(allocBytes > zeroBytes * 4,
    `the probe separates the two by a wide margin (${allocBytes} B vs ${zeroBytes} B)`,
    `expected allocator >> non-allocator, got ${allocBytes} vs ${zeroBytes}`);

  // Method and scope must travel WITH the number, in the artifact — not in a
  // report someone has to remember to attach.
  eq(allocRun.json.c3.allocation.scope, 'javascript-heap-only',
    'the probe records its scope in the result, next to the number');
  ok(/WASM linear memory|linear memory/.test(allocRun.json.c3.allocation.caveat),
    'the caveat states that Blazor\'s .NET render tree lives outside the JS heap and is NOT measured');
  ok(/UNDER-REPORTS Blazor/.test(allocRun.json.c3.allocation.caveat),
    'the caveat states the direction of the error rather than merely admitting uncertainty');
  ok(/snapshot delta/i.test(allocRun.json.c3.allocation.whyNotHeapSnapshotDelta),
    'the result explains why allocation throughput, not retained-heap growth, is the quantity C3 names');
  ok(Array.isArray(allocRun.json.c3.allocation.topSites) && allocRun.json.c3.allocation.topSites.length > 0,
    'allocation sites are reported, so the scope caveat can be checked rather than believed');
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
      // The one aimed at the HEADLINE. Its first #run is byte-perfect, so it is
      // refused only by the second-run assertion — the gate that did not exist.
      dir: fixture.cachedlabel,
      label: 'cachedlabel',
      what: 'a correct first #run, then cached labels on the run create-warm TIMES',
      expect: /on the second #run, row 0 label is "adorable pink desk" but the deterministic sequence requires "mushy blue mouse"/,
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
    const { code, stderr } = await runBenchCapturingStderr([
      '--dir', c.dir, '--app', 'rows', '--label', `selftest-${c.label}`,
      '--runs', '1', '--weight-runs', '1', '--timeout', '2500', '--out', out, '--no-aot',
    ]);
    eq(code, 3, `${c.label} (${c.what}): exits 3 — numbers REFUSED`);
    // Refused for the RIGHT reason. Without this, a fixture could trip an unrelated
    // gate and the assertion above would still pass while testing nothing.
    ok(c.expect.test(stderr),
      `${c.label}: refused by the gate it was written to defeat, and the diagnostic names it`,
      `expected ${c.expect} in the refusal`);
    // eslint-disable-next-line no-await-in-loop
    const wrote = await fsp.readFile(out, 'utf8').catch(() => null);
    eq(wrote, null, `${c.label}: no results file written — it cannot become a headline number`);
  }

  // The cached-label fixture's whole point is that it is correct where the old gate
  // looked. State that as an assertion rather than a comment: its FIRST run really is
  // byte-identical to the golden stream, so nothing but the second-run gate refuses it.
  const golden = await loadExpectedLabels();
  const cachedStderr = (await runBenchCapturingStderr([
    '--dir', fixture.cachedlabel, '--app', 'rows', '--label', 'selftest-cachedlabel-why',
    '--runs', '1', '--weight-runs', '1', '--timeout', '2500',
    '--out', path.join(outDir, 'selftest-cachedlabel-why.json'), '--no-aot',
  ])).stderr;
  // Every problem it is refused for is a SECOND-run problem. That is the
  // counterfactual stated as an assertion: the first run, #update, #swaprows and
  // #clear all satisfied it, so every gate that existed before this change passed it
  // and create-warm — the headline — would have been reported "ok" on strictly less
  // work. Only the second-run stream sees it.
  const complaints = cachedStderr.split('\n').filter((l) => l.startsWith('  - '));
  ok(complaints.length > 0 && complaints.every((l) => /on the second #run/.test(l)),
    'cachedlabel: EVERY problem it is refused for is on the SECOND #run — the first run, #update, ' +
    '#swaprows and #clear all pass, i.e. every gate that existed before this change waved it through',
    complaints.filter((l) => !/on the second #run/.test(l)).join(' | ') || `${complaints.length} complaints`);
  ok(golden.secondRun.first5[0] !== golden.first5[0],
    'the two golden streams differ, which is what makes the second-run gate able to see this at all',
    `${golden.first5[0]} vs ${golden.secondRun.first5[0]}`);
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
  // 1000 rows; swap moves two. If both reported the frame interval — the old
  // behaviour — this ratio would be ~1.
  //
  // Measured on create-COLD: this assertion predates the cold/warm split and was
  // always about the cold number, so it stays pointed at it.
  //
  // The denominator is SWAP, not clear. Against clear this assertion was a latent
  // flake that happened to pass: on this fixture create-cold is bimodal (~1.8 ms
  // when V8 declines to tier up the builder, ~4.4 ms when it does) and clear is a
  // steady ~0.9 ms, so create/clear lands anywhere from 2.1x to 4.9x and a `> 3`
  // threshold is a coin toss. That fragility has nothing to do with the vsync
  // property under test. swap is two insertBefore calls at a steady ~0.1 ms, so
  // create/swap is ~19-46x — the same claim, tested against a work difference an
  // order of magnitude larger. This STRENGTHENS the assertion rather than relaxing
  // it: the floor below is half the observed 0.1 ms clock granularity, so even a
  // 0-reading swap cannot manufacture a pass the numbers have not earned.
  const ratio = s.scenarios['create-cold'].median / Math.max(s.scenarios.swap.median, 0.05);
  ok(ratio > 3,
    `create-cold/swap median ratio is ${ratio.toFixed(1)}x — the metric resolves different amounts of work`,
    'a vsync-quantized clock would flatten this to ~1x');

  // Observation, not an assertion — it is real but too small to bound reliably.
  // This fixture injects NO boot cost and still shows create-cold materially above
  // create-warm, because the first #run also pays V8's JIT warmup and the
  // allocator's first touch of 1000 fresh nodes. Worth seeing: it means the cold
  // number carries first-run cost even for a framework with no runtime at all, so
  // "cold" is contaminated by strictly more than the boot term the warm variant was
  // introduced to remove.
  process.stdout.write(
    `  note (sync fixture, NO injected boot) create-cold=${s.scenarios['create-cold'].median}ms vs ` +
    `create-warm=${s.scenarios['create-warm'].median}ms — first-run JIT/allocator warmup is visible ` +
    'even in vanilla JS\n',
  );
  process.stdout.write(
    `  note create-cold=${s.scenarios['create-cold'].median}ms create-warm=${s.scenarios['create-warm'].median}ms ` +
    `clear=${s.scenarios.clear.median}ms swap=${s.scenarios.swap.median}ms | toPaint: ` +
    `create-warm=${s.scenarios['create-warm'].toPaint.median}ms ` +
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
  eq(w.scenarios['create-cold'].status, 'ok', 'create-cold: status ok despite the worker target');
  ok(w.scenarios['create-cold'].median > 0, 'create-cold produced a real number');
  // Each iteration would have burned the full 8s maxSettleMs if inFlight stayed
  // pinned. Bound = 8s x (1 weight run + 1 contract gate + 5 scenarios x 1 run,
  // where the warm scenarios settle twice).
  ok(elapsed < 8000 * (1 + 1 + 7),
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

/**
 * ===========================================================================
 * THE test for this change. Everything else here guards a number from being
 * wrong; this guards a number from being ABOUT THE WRONG THING.
 *
 * Criterion C4 asks how fast a framework renders. The cold `create` number
 * cannot answer that: for a Blazor build ~72% of it is runtime boot, so a
 * framework that boots in ~0 ms wins C4 without rendering a single row faster —
 * a PASS for entirely the wrong reason, indistinguishable in the JSON from a
 * real one.
 *
 * The bootcost fixture makes that failure mode concrete and falsifiable: a
 * 400 ms one-time boot paid inside the first interaction's measured window.
 * The warm variant must remove it. If it does not, these assertions fail and
 * the harness cannot report a warm number it has not earned.
 * ===========================================================================
 */
async function testWarmVariantsExcludeSetupCost(fixture, outDir) {
  section('14. Warm variants EXCLUDE the untimed setup cost (C4 measures rendering, not boot)');

  // ---- rows: create-cold vs create-warm -------------------------------------
  const out = path.join(outDir, 'selftest-bootcost.json');
  const code = await runBench([
    '--dir', fixture.bootcost, '--app', 'rows', '--label', 'selftest-bootcost',
    '--scenarios', 'create-cold,create-warm', '--runs', '3', '--weight-runs', '1',
    '--out', out, '--no-aot',
  ]);
  eq(code, 0, 'the boot-cost fixture is contract-conforming and runs clean');
  const r = JSON.parse(await fsp.readFile(out, 'utf8'));
  const cold = r.scenarios['create-cold'];
  const warm = r.scenarios['create-warm'];

  eq(cold.status, 'ok', 'create-cold: status ok');
  eq(warm.status, 'ok', 'create-warm: status ok');
  eq(cold.untimedSetup.length, 0, 'create-cold has NO untimed setup — its first click is the timed one');
  ok(/#run/.test(warm.untimedSetup[0])
    && warm.untimedSetup.some((s) => /#clear/.test(s))
    && /idle beat/.test(warm.untimedSetup[warm.untimedSetup.length - 1]),
    'create-warm records its untimed setup (#run, #clear, and the beat before the clock starts) in the JSON',
    JSON.stringify(warm.untimedSetup));

  // ---- THE proof ------------------------------------------------------------
  const COLD_FLOOR = BOOT_COST_MS + ASYNC_DELAY_MS - 20;
  const WARM_CEILING = BOOT_COST_MS * 0.5;

  ok(cold.median >= COLD_FLOOR,
    `create-cold median (${cold.median} ms) >= ${COLD_FLOOR} ms — the cold number DOES carry the ${BOOT_COST_MS} ms boot`,
    'if this fails the fixture is not modelling boot contamination and the rest of this section proves nothing');
  ok(cold.samples.every((v) => v >= COLD_FLOOR),
    'create-cold: EVERY sample carries the boot cost (each iteration really is a cold runtime)',
    JSON.stringify(cold.samples));

  ok(warm.median < WARM_CEILING,
    `create-warm median (${warm.median} ms) < ${WARM_CEILING} ms — the boot cost is EXCLUDED from the timed segment`,
    `if the untimed setup had not run, or if the timed window still enclosed it, this would be >= ${COLD_FLOOR}`);
  ok(warm.samples.every((v) => v < WARM_CEILING),
    'create-warm: EVERY sample excludes the boot cost — not just the median',
    JSON.stringify(warm.samples));

  // The timed segment is still real work, not a fabricated ~0. The fixture defers
  // every mutation by 60 ms, so a warm number below that would mean the clock had
  // stopped before the DOM changed.
  ok(warm.median >= ASYNC_DELAY_MS - 5,
    `create-warm median (${warm.median} ms) still >= ${ASYNC_DELAY_MS - 5} ms — the timed segment measures REAL deferred work`,
    'warm must exclude the setup, not collapse to zero');

  const delta = cold.median - warm.median;
  ok(delta >= BOOT_COST_MS * 0.85,
    `cold - warm = ${delta.toFixed(1)} ms, recovering the ${BOOT_COST_MS} ms boot term that the cold number hides`,
    'this delta is exactly why the cold create number cannot be used as a rendering measurement');
  process.stdout.write(
    `  note create-cold=${cold.median}ms create-warm=${warm.median}ms delta=${delta.toFixed(1)}ms ` +
    `(injected boot=${BOOT_COST_MS}ms)\n`,
  );

  // A subset run must be visibly a subset — it can never be mistaken for a full one.
  eq(r.config.scenariosComplete, false, 'a --scenarios subset is recorded as an INCOMPLETE run');
  eq(r.totals.timedIterationsPlanned, 6, 'totals track the subset (2 scenarios x 3 runs), not the app\'s full set');
  eq(r.totals.timedIterationsRecorded, 6, 'totals: all 6 recorded');

  // ---- counter: the STRUCTURAL proof, independent of any clock ---------------
  //
  // increment-warm's timed predicate is #counter-value === "2". The only way that
  // can ever become true is if an untimed increment already took it 0 -> 1. So the
  // scenario merely PASSING is proof the setup ran — no timing argument, no
  // threshold, nothing to tune. Had the setup silently been skipped, the timed
  // click would have produced "1" and the predicate would have timed out.
  const cOut = path.join(outDir, 'selftest-bootcounter.json');
  const cCode = await runBench([
    '--dir', fixture.bootcounter, '--app', 'counter', '--label', 'selftest-bootcounter',
    '--runs', '3', '--weight-runs', '1', '--out', cOut, '--no-aot',
  ]);
  eq(cCode, 0, 'the boot-cost counter fixture runs clean');
  const c = JSON.parse(await fsp.readFile(cOut, 'utf8'));
  const iCold = c.scenarios['increment-cold'];
  const iWarm = c.scenarios['increment-warm'];

  eq(iWarm.status, 'ok',
    'increment-warm PASSES — and its predicate is #counter-value === "2", which is STRUCTURALLY ' +
    'unreachable unless the untimed setup increment really ran');
  eq(iCold.status, 'ok', 'increment-cold: status ok');
  ok(iCold.median >= COLD_FLOOR,
    `increment-cold median (${iCold.median} ms) carries the ${BOOT_COST_MS} ms boot`);
  ok(iWarm.median < WARM_CEILING,
    `increment-warm median (${iWarm.median} ms) excludes the boot paid by its untimed setup`);
  ok(iWarm.median >= ASYNC_DELAY_MS - 5,
    `increment-warm median (${iWarm.median} ms) still measures the real deferred increment`);
  ok(iWarm.samples.every((v) => v < WARM_CEILING),
    'increment-warm: every sample excludes the boot cost', JSON.stringify(iWarm.samples));
  process.stdout.write(
    `  note increment-cold=${iCold.median}ms increment-warm=${iWarm.median}ms ` +
    `delta=${(iCold.median - iWarm.median).toFixed(1)}ms\n`,
  );
}

/**
 * The --aot flag was recorded verbatim and never checked: the JSON would state
 * aot:true for a build where AOT never engaged, and no reader could tell. These
 * prove the harness now derives an INDEPENDENT verdict from the served bytes, and
 * that it refuses to guess when it cannot.
 */
async function testAotVerification(aotFixture) {
  section('15. The self-declared --aot flag is independently VERIFIED against served artifacts');

  const aotEv = await collectAotEvidence(aotFixture.aot);
  const nojitEv = await collectAotEvidence(aotFixture.nojit);
  const midEv = await collectAotEvidence(aotFixture.indeterminate);
  const noneEv = await collectAotEvidence(aotFixture.nosignature);
  const staleEv = await collectAotEvidence(aotFixture.stale);

  // The served-path lists a real weight run would produce for each tree. `verified`
  // requires the evidence to have been on the wire, so supplying these is not
  // decoration — it is the input the claim is confirmed against.
  const AOT_SERVED = ['/wwwroot/_framework/dotnet.native.ogsd35n1u1.wasm'];
  const NOJIT_SERVED = ['/wwwroot/_framework/dotnet.native.kllr7zg72l.wasm'];

  // --- the walker finds the fingerprinted runtime and reads its RAW size ---
  eq(aotEv.length, 1, 'AOT publish: exactly one signature match (the .br/.gz siblings are NOT mistaken for it)');
  eq(aotEv[0].rawBytes, 11_380_806, 'AOT publish: raw size read from the real artifact, not a compressed sibling');
  eq(aotEv[0].verdict, 'aot', 'AOT publish: verdict "aot"');
  ok(/^wwwroot\/_framework\/dotnet\.native\./.test(aotEv[0].path),
    'the fingerprinted filename (dotnet.native.<hash>.wasm) is matched at any depth', aotEv[0].path);
  eq(nojitEv.length, 1, 'non-AOT publish: one signature match');
  eq(nojitEv[0].rawBytes, 1_494_734, 'non-AOT publish: raw size read');
  eq(nojitEv[0].verdict, 'interpreted', 'non-AOT publish: verdict "interpreted"');
  eq(noneEv.length, 0, 'a framework with no AOT signature yields NO evidence (not a false verdict)');

  // --- a truthful declaration, corroborated by an artifact that was SERVED ---
  const okAot = classifyAotEvidence(true, aotEv, AOT_SERVED);
  eq(okAot.observed, true, 'declared --aot=true, observed AOT: observed true');
  eq(okAot.agrees, true, 'declared and observed agree');
  eq(okAot.verified, true, 'the claim is VERIFIED, not merely recorded');
  eq(okAot.warnings.length, 0, 'no warning for a corroborated claim');
  eq(okAot.declared, true, 'the declared flag is preserved alongside the observation');

  const okNojit = classifyAotEvidence(false, nojitEv, NOJIT_SERVED);
  eq(okNojit.observed, false, 'declared --no-aot, observed interpreted: observed false');
  eq(okNojit.verified, true, 'the non-AOT claim is verified too');
  eq(okNojit.warnings.length, 0, 'no warning for a corroborated non-AOT claim');

  // --- THE failure mode: a false declaration is caught and shouted about ---
  const lying = classifyAotEvidence(true, nojitEv, NOJIT_SERVED);
  eq(lying.observed, false, 'declared --aot=true over a 1.49 MB interpreted runtime: observed FALSE');
  eq(lying.agrees, false, 'the harness reports that declared and observed DISAGREE');
  eq(lying.verified, false, 'and refuses to call the claim verified');
  eq(lying.declared, true, 'the false declaration is still recorded verbatim — never silently rewritten');
  eq(lying.warnings.length, 1, 'exactly one loud warning');
  ok(/AOT MISMATCH/.test(lying.warnings[0]), 'the warning is unmissable', lying.warnings[0]);
  ok(/1494734 B/.test(lying.warnings[0]), 'the warning quotes the observed raw size as evidence', lying.warnings[0]);

  const lyingOther = classifyAotEvidence(false, aotEv, AOT_SERVED);
  eq(lyingOther.observed, true, 'declared --no-aot over an 11.38 MB AOT runtime: observed TRUE');
  eq(lyingOther.agrees, false, 'the reciprocal contradiction is caught too');
  ok(/AOT MISMATCH/.test(lyingOther.warnings[0]), 'and warned about', lyingOther.warnings[0]);

  // --- an unverifiable TRUE claim is reported as unverified, never confirmed ---
  const unverifiable = classifyAotEvidence(true, noneEv);
  eq(unverifiable.observed, null, 'no signature: observed stays null — the harness does not guess');
  eq(unverifiable.agrees, null, 'agreement is null, not a false true');
  eq(unverifiable.verified, false, 'an unverifiable claim is NOT verified');
  ok(/AOT UNVERIFIED/.test(unverifiable.warnings[0]),
    'declaring --aot=true on a build that cannot corroborate it warns', JSON.stringify(unverifiable.warnings));
  eq(classifyAotEvidence(false, noneEv).warnings.length, 0,
    'but --no-aot on an unverifiable build is silent (nothing is being claimed)');

  // --- the indeterminate band says so rather than guessing ---
  eq(midEv[0].verdict, 'indeterminate', 'a 4.5 MB runtime falls between the thresholds: verdict "indeterminate"');
  const mid = classifyAotEvidence(true, midEv);
  eq(mid.observed, null, 'indeterminate evidence yields observed null, not a coin flip');
  eq(mid.basis, 'indeterminate', 'and the basis says exactly that');
  ok(mid.warnings.some((w) => /AOT INDETERMINATE/.test(w)),
    'the indeterminate band is warned about so the thresholds can be recalibrated',
    JSON.stringify(mid.warnings));

  // --- "served" means served: an unserved artifact CANNOT confirm a claim ---
  //
  // The cross-check used to populate a field and stop there: `verified` was computed
  // without reference to it, so the JSON would report verified:true from bytes the
  // browser never requested — a claim confirmed by a file that played no part in the
  // run. Servedness is now a REQUIREMENT, and these are the assertions that hold it
  // to that.
  const served = classifyAotEvidence(true, aotEv, AOT_SERVED);
  eq(served.evidence[0].servedInWeightRun, true,
    'evidence read from disk is confirmed to have actually been SERVED during the weight run');

  const notServed = classifyAotEvidence(true, aotEv, ['/index.html']);
  eq(notServed.evidence[0].servedInWeightRun, false,
    'an artifact present in the publish dir but never requested is flagged as not served');
  eq(notServed.observed, true, 'the unserved artifact is still REPORTED (it is a fact about the directory)');
  eq(notServed.verified, false,
    'but it CANNOT verify the claim — an 11 MB file the browser never requested may be a stale build');
  ok(notServed.warnings.some((w) => /NOT SERVED/.test(w)),
    'and the reader is told exactly that, rather than being handed a silent verified:false',
    JSON.stringify(notServed.warnings));

  // Servedness unknown is not servedness proven: omitting the list cannot be the
  // cheap way to a confirmed claim.
  const unknownServed = classifyAotEvidence(true, aotEv);
  eq(unknownServed.verified, false,
    'no served-path list supplied: the claim is NOT verified (unknown is not confirmed)');
  ok(unknownServed.warnings.some((w) => /SERVEDNESS UNKNOWN/.test(w)),
    'and that is stated too', JSON.stringify(unknownServed.warnings));

  // --- the stale-artifact trap: served evidence outranks conclusive evidence ---
  //
  // A leftover 11 MB AOT runtime from an earlier build sits next to the 4.5 MB
  // runtime the app actually boots (indeterminate band, e.g. a newer SDK). Sorting
  // is largest-first, so the stale file heads the list. Selecting "conclusive" before
  // "served" would confirm aot=true from it AND suppress the INDETERMINATE warning
  // about the runtime that was really measured.
  eq(staleEv.length, 2, 'stale publish: both matching runtimes are collected');
  eq(staleEv[0].rawBytes, 11_380_806, 'stale publish: the 11 MB leftover sorts FIRST (largest-first)');
  eq(staleEv[1].rawBytes, 4_500_000, 'stale publish: the runtime actually booted sorts second');
  const stale = classifyAotEvidence(true, staleEv, ['/wwwroot/_framework/dotnet.native.new1111111.wasm']);
  eq(stale.observed, null,
    'the verdict comes from the SERVED runtime (indeterminate), NOT the unserved 11 MB leftover');
  eq(stale.verified, false, 'so an --aot=true claim over it is not verified');
  eq(stale.basis, 'indeterminate', 'and the basis names the band the served runtime fell in');
  ok(stale.warnings.some((w) => /AOT INDETERMINATE/.test(w)),
    'the served runtime\'s INDETERMINATE warning is SURFACED, not suppressed by the stale file',
    JSON.stringify(stale.warnings));
  ok(stale.warnings.some((w) => /AOT UNVERIFIED/.test(w)),
    'and the unverifiable --aot=true claim is called out', JSON.stringify(stale.warnings));
  ok(stale.evidence.some((e) => e.rawBytes === 11_380_806 && e.servedInWeightRun === false),
    'the stale artifact is still recorded in evidence[], flagged as never served — visible, not authoritative');
}

/** Unit test for the representative-run selection that used to mislabel itself. */
function testRepresentativeRun() {
  section('16. The per-run breakdown is quoted from a run it does not lie about (unit)');
  // Odd count: a run genuinely holds the median.
  eq(representativeIndex([300, 100, 200]), 2, 'odd count: the run holding the median (200) is selected');
  eq(representativeIndex([5]), 0, 'single run');
  // Even count: the median is interpolated and NO run holds it. The old code took
  // the upper-middle element and the JSON called it "the run whose total is the
  // median" — with [100, 200] it reported the 200 run as "the median" of 150.
  eq(representativeIndex([100, 200]), 0, 'even count: the tie resolves to the LOWER total, deterministically');
  eq(representativeIndex([200, 100]), 1, 'even count: same run chosen regardless of arrival order');
  // Nearest, not upper-middle: with [100, 190, 200, 210] the median is 195 and the
  // nearest run is 190 — the old upper-middle rule would have picked 200.
  eq(representativeIndex([100, 190, 200, 210]), 1, 'even count: the NEAREST run to the median is chosen');
  eq(representativeIndex([]), -1, 'no runs: no index');
}

/**
 * Defect (a): server.mjs's own CLI rejected --max-encoding, so the standalone
 * server could not reproduce the capped serving mode bench.mjs had used to produce
 * a published number. A measurement nobody can re-derive by hand is not reproducible.
 */
function testServerCli() {
  section('17. The standalone server CLI can reproduce every serving mode bench.mjs uses');
  eq(parseCliArgs(['--dir', '/x']).maxEncoding, 'br', 'default is br (no cap, production-like)');
  eq(parseCliArgs(['--dir', '/x', '--max-encoding', 'gzip']).maxEncoding, 'gzip',
    '--max-encoding gzip is ACCEPTED (it used to throw "unknown argument")');
  eq(parseCliArgs(['--dir', '/x', '--max-encoding=identity']).maxEncoding, 'identity',
    '--max-encoding=identity inline form accepted');
  eq(parseCliArgs(['--dir', '/x', '--max-encoding', 'br']).maxEncoding, 'br', '--max-encoding br accepted');
  eq(parseCliArgs(['--dir', '/x', '--port', '9000']).port, 9000, '--port still parsed');
  eq(parseCliArgs(['--dir', '/x', '--quiet']).quiet, true, '--quiet parsed');

  let threw = null;
  try { parseCliArgs(['--dir', '/x', '--max-encoding', 'bogus']); } catch (e) { threw = e.message; }
  ok(threw && /must be one of br \| gzip \| identity/.test(threw),
    'an invalid --max-encoding is a usage error listing the valid values', String(threw));

  let threw2 = null;
  try { parseCliArgs(['--nope']); } catch (e) { threw2 = e.message; }
  ok(threw2 && /unknown argument/.test(threw2), 'genuinely unknown arguments are still rejected');

  // The ceiling the CLI accepts must be exactly the set the server implements —
  // otherwise the CLI could accept a mode the server cannot serve.
  ok(ENCODING_CEILINGS.every((e) => parseCliArgs(['--dir', '/x', '--max-encoding', e]).maxEncoding === e),
    'the CLI accepts exactly the encodings the server implements', ENCODING_CEILINGS.join('|'));
}

/**
 * Every warm scenario must be measured under the SAME settling regime.
 *
 * The idle beat was originally applied only to the two NEW warm variants, while
 * update/swap/clear — all `headline: true`, all feeding reported medians — went from
 * the setup's #run straight into their timed click. So create-warm got two chained
 * rAFs plus a >= 50 ms macrotask of protection and the three scenarios it claims
 * parity with got measure()'s single rAF + setTimeout(0), which by this file's own
 * docstring is not enough to have been through layout and paint. Four scenarios, two
 * regimes, described in the JSON as equivalent.
 *
 * The fix routes them all through setupRows(), and this pins the property: it reads
 * SCENARIO_SPECS, which is what the JSON publishes, so a future edit that beats one
 * scenario and not another fails here.
 */
function testWarmScenariosSettleIdentically() {
  section('18. Every WARM scenario settles before its timed click (one regime, not two)');
  const warm = Object.entries(SCENARIO_SPECS).filter(([, s]) => s.runtime === 'warm');
  const cold = Object.entries(SCENARIO_SPECS).filter(([, s]) => s.runtime === 'cold');

  ok(warm.length === 5, `all five warm scenarios are covered: ${warm.map(([n]) => n).join(', ')}`,
    `got ${warm.length}`);
  for (const [name, spec] of warm) {
    ok(spec.setup.some((s) => /idle beat/.test(s)),
      `${name}: its untimed setup includes an idle beat`, JSON.stringify(spec.setup));
    ok(/idle beat/.test(spec.setup[spec.setup.length - 1]),
      `${name}: the idle beat is the LAST thing before the clock starts`, JSON.stringify(spec.setup));
  }
  // The reciprocal: a cold scenario must have no setup at all, or it is not cold.
  for (const [name, spec] of cold) {
    eq(spec.setup.length, 0, `${name}: a cold scenario has NO untimed setup — its first click is the timed one`);
  }
  // update/swap/clear and create-warm must agree on how they are settled, which is
  // the specific claim SCENARIO_SPECS['create-warm'].measures makes in prose.
  const beatsOf = (n) => SCENARIO_SPECS[n].setup.filter((s) => /idle beat/.test(s)).length;
  ok(['update', 'swap', 'clear'].every((n) => beatsOf(n) === 1),
    'update/swap/clear each get exactly the one beat their single #run setup earns');
  eq(beatsOf('create-warm'), 2,
    'create-warm gets one per untimed interaction (#run, #clear) — the same rule, applied twice');
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
const aotFixture = await makeAotFixture(path.join(scratch, 'aotfixture'));

try {
  testAcceptEncodingParsing();
  testStatistics();
  testUntrackedReconciliation();
  testRepresentativeRun();
  testServerCli();
  testWarmScenariosSettleIdentically();
  await testServer(fixture);
  await testAotVerification(aotFixture);
  await testEndToEnd(fixture, outDir);
  await testFailureReporting(fixture, outDir);
  await testContractPreflight(fixture, outDir);
  await testUnequalWorkIsRefused(fixture, outDir);
  await testSubFrameWorkIsVisible(fixture, outDir);
  await testWorkerTargetVisibility(fixture, outDir);
  await testWarmVariantsExcludeSetupCost(fixture, outDir);
  await testStrictRowMarkup(fixture, outDir);
  await testC3Instruments(fixture, outDir);
  await testC3AllocationProbe(fixture, outDir);
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
