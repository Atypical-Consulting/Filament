#!/usr/bin/env node
/**
 * bench.mjs — Filament POC measurement harness (spec section 7).
 *
 * HARD REQUIREMENT: this file is framework-agnostic. The only thing that varies
 * between frameworks is the URL passed on the CLI. There is no branch anywhere in
 * this file on "blazor" / "filament" / any framework identity. The harness drives
 * the shared DOM contract and nothing else.
 *
 * Shared DOM contract (must be implemented identically by every app under test):
 *   Rows app
 *     #tbody       <tbody> holding the rows
 *     tbody row    <tr><td>{id}</td><td>{label}</td>...</tr>
 *                    - cell 1 text = row id
 *                    - cell 2 text = row label
 *     #run         creates 1000 rows
 *     #update      appends " !!!" to every 10th row's label
 *     #swaprows    swaps row 1 and row 998
 *     #clear       removes all rows
 *   Counter app
 *     #increment       increments
 *     #counter-value   element whose text is the current count
 *
 * Metrics
 *   - Transferred bundle weight: summed CDP encodedDataLength, NOT disk size.
 *   - Timing: 10 runs per scenario, reported as MEDIAN and IQR (p75-p25). Never mean.
 *     TWO timings are reported per sample: `msToMutation` (the headline — click to
 *     the DOM predicate becoming true) and `msToPaint` (mutation plus the wait for
 *     the next frame). msToPaint carries a 0-17ms vsync-phase offset contributed by
 *     the HARNESS, not the framework, so it must never be the headline number.
 *   - A scenario that times out is a FAILURE, never a number.
 *
 * COLD vs WARM (see SCENARIO_SPECS below)
 *   A scenario whose timed click is the FIRST interaction on a freshly loaded page
 *   measures the framework's runtime boot PLUS the DOM work, because a lazily
 *   initialised runtime does its one-time setup inside that first click. For a
 *   Blazor build the boot term dominates: it is ~72% of the cold `create` number,
 *   so a framework with no runtime to boot "wins" create on boot alone — while the
 *   actual row-building cost, which is what the scenario is named after, may be a
 *   near tie. Reporting only the cold number therefore answers "how long is the
 *   first click" and calls the answer "rendering".
 *
 *   So `create` and `increment` — the only two scenarios whose click was
 *   first-on-cold-runtime — are each measured BOTH ways:
 *     *-cold  fresh load, the first click is timed         => boot + work
 *     *-warm  fresh load, an UNTIMED setup interaction runs
 *             first, then an identical click is timed      => work
 *   update/swap/clear already had an untimed #run setup and were already warm; the
 *   warm variants simply make create/increment consistent with them. Both numbers
 *   are reported. Neither is deleted: cold is a real user-visible cost, it just is
 *   not a rendering measurement.
 *
 * Usage:
 *   node bench.mjs --dir <publishDir> --port <port> --app <counter|rows> \
 *                  --label <string> --runs 10 [--aot] [--headed] [--out <file>]
 */

import os from 'node:os';
import path from 'node:path';
import fsp from 'node:fs/promises';
import crypto from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { createRequire } from 'node:module';
import { pathToFileURL, fileURLToPath } from 'node:url';
import { chromium } from 'playwright';
import { startServer, ENCODING_CEILINGS } from './server.mjs';

const require = createRequire(import.meta.url);

export const HARNESS_VERSION = '1.2.0';
/**
 * 3: `create` -> `create-cold` + `create-warm`, `increment` -> `increment-cold` +
 *    `increment-warm`; `weight.decodedBytesMedianRun` -> `decodedBytesRepresentativeRun`
 *    (the old name lied for even run counts); `environment.aotObserved` /
 *    `environment.aotVerification` added alongside the self-declared `environment.aot`;
 *    `totals` added so the timed-iteration count is computed, never hand-counted.
 */
export const SCHEMA_VERSION = 3;

// ---------------------------------------------------------------------------
// Golden label fixture.
//
// The contract calls the deterministic label sequence "CRITICAL for fairness":
// every framework must run the same Park-Miller LCG and emit the byte-identical
// stream. Blazor performs 3000 double multiply/modulo ops plus 1000 three-part
// string concatenations per #run. An app that emits a constant label, hoists the
// array to compile time, or reuses one interned string does strictly less
// allocation and text-node work and posts a faster `create` median.
//
// expected-labels.json existed to prevent exactly that and was imported by
// NOTHING — dead data. verifyContract read row 990's label only to check it did
// not already end in " !!!". So the single most expensive failure mode the project
// named was entirely unguarded. It is now loaded here and asserted in-page.
// ---------------------------------------------------------------------------
const HARNESS_DIR = path.dirname(fileURLToPath(import.meta.url));
export const EXPECTED_LABELS_PATH = path.join(HARNESS_DIR, 'expected-labels.json');

/**
 * Load and validate the golden fixture. Throws rather than degrading: a missing or
 * malformed fixture must stop the run, never silently disable the fairness gate.
 */
export async function loadExpectedLabels(file = EXPECTED_LABELS_PATH) {
  let raw;
  try {
    raw = await fsp.readFile(file, 'utf8');
  } catch (e) {
    throw new Error(
      `bench.mjs: cannot read the golden label fixture at ${file} (${e.code || e.message}). ` +
      'It gates cross-framework workload equality and must never be skipped.',
    );
  }
  const parsed = JSON.parse(raw);
  if (!Array.isArray(parsed.first5) || parsed.first5.length !== 5 || !parsed.first5.every((s) => typeof s === 'string')) {
    throw new Error(`bench.mjs: ${file} must contain "first5" as an array of exactly 5 strings`);
  }
  if (typeof parsed.row1000 !== 'string') {
    throw new Error(`bench.mjs: ${file} must contain "row1000" as a string`);
  }
  return {
    first5: parsed.first5,
    row1000: parsed.row1000,
    // Recorded in the result JSON so a later fixture edit is visible in the output
    // rather than silently redefining what "correct" means.
    sha256: crypto.createHash('sha256').update(raw).digest('hex'),
    path: file,
  };
}

// ---------------------------------------------------------------------------
// App shape configuration.
//
// This is contract-level configuration (what the *app* looks like), not
// framework-level. Every framework implementing the Rows contract is driven by
// the exact same entries below.
// ---------------------------------------------------------------------------
const APPS = {
  rows: {
    // App is "interactive" when this control exists and passes actionability.
    readySelector: '#run',
    // MutationObserver root for the timing primitive.
    observeSelector: '#tbody',
    scenarios: ['create-cold', 'create-warm', 'update', 'swap', 'clear'],
    // Used by the weight runs to observe interaction-triggered lazy loading.
    primaryAction: { button: '#run', predicate: { kind: 'rowCount', args: { count: 1000 } } },
  },
  counter: {
    readySelector: '#increment',
    observeSelector: '#counter-value',
    scenarios: ['increment-cold', 'increment-warm'],
    primaryAction: {
      button: '#increment',
      predicate: { kind: 'textEquals', args: { selector: '#counter-value', expected: '1' } },
    },
  },
};

/**
 * What each scenario measures, as DATA rather than as prose in a report someone
 * has to remember to update. Emitted verbatim into every result so a reader of the
 * JSON can never mistake a cold number for a rendering number.
 *
 * `runtime`:
 *   'cold' — the timed click is the first interaction after a fresh page load, so a
 *            lazily-initialised framework runtime boots INSIDE the measured window.
 *            The number is boot + work and must be reported as such.
 *   'warm' — an identical interaction ran untimed first, so the runtime is already
 *            booted and the measured window is the work alone.
 *
 * `headline` marks the number that answers the question the scenario is named
 * after ("how fast does this framework render 1000 rows?"). For create/increment
 * that is the WARM variant: the cold one is dominated by a boot term that has
 * nothing to do with rendering, and a framework with no runtime would win it
 * without rendering anything faster.
 */
const SCENARIO_SPECS = {
  'create-cold': {
    runtime: 'cold',
    headline: false,
    button: '#run',
    setup: [],
    measures:
      'Fresh page load, then the FIRST #run click is timed. Includes any one-time runtime boot the ' +
      'framework defers to first interaction, so this is boot + row-building, not row-building. Real, ' +
      'user-visible, and NOT a rendering measurement: compare frameworks on create-warm instead.',
  },
  'create-warm': {
    runtime: 'warm',
    headline: true,
    button: '#run',
    setup: ['#run (untimed, to 1000 rows)', '#clear (untimed, to 0 rows)', 'idle beat'],
    measures:
      'Fresh page load; #run then #clear run UNTIMED so the runtime is booted and the row path is ' +
      'exercised; then an identical #run is TIMED. This is the row-building cost with the boot term ' +
      'removed, and it is consistent with update/swap/clear, which have always had an untimed #run ' +
      'setup. Per the DOM contract row ids are monotonic and never reset and the label LCG is seeded ' +
      'only at page load, so the timed #run legitimately produces ids 1001..2000 with the LCG ' +
      'continuing from where the setup left it. That is intended: the WORK is identical (1000 rows, ' +
      '3000 LCG draws, 1000 three-part concatenations) and the harness must not reset the seed or the ' +
      'id counter to make the two runs look alike — doing so would be the harness fabricating ' +
      'conditions no user ever has.',
  },
  update: {
    runtime: 'warm',
    headline: true,
    button: '#update',
    setup: ['#run (untimed, to 1000 rows)'],
    measures: 'Appending " !!!" to every 10th row label, on an already-booted runtime.',
  },
  swap: {
    runtime: 'warm',
    headline: true,
    button: '#swaprows',
    setup: ['#run (untimed, to 1000 rows)'],
    measures: 'Reciprocally swapping rows 1 and 998, on an already-booted runtime.',
  },
  clear: {
    runtime: 'warm',
    headline: true,
    button: '#clear',
    setup: ['#run (untimed, to 1000 rows)'],
    measures: 'Removing all 1000 rows, on an already-booted runtime.',
  },
  'increment-cold': {
    runtime: 'cold',
    headline: false,
    button: '#increment',
    setup: [],
    measures:
      'Fresh page load, then the FIRST #increment click is timed (0 -> 1). Includes any one-time ' +
      'runtime boot deferred to first interaction, so this is boot + increment.',
  },
  'increment-warm': {
    runtime: 'warm',
    headline: true,
    button: '#increment',
    setup: ['#increment (untimed, 0 -> 1)', 'idle beat'],
    measures:
      'Fresh page load; one UNTIMED #increment (0 -> 1) boots the runtime, then a second, identical ' +
      '#increment (1 -> 2) is TIMED. This is the increment cost with the boot term removed.',
  },
};

// ---------------------------------------------------------------------------
// In-page harness. Injected via CDP (addInitScript), NOT via a <script src>, so
// it costs zero network bytes and cannot pollute the weight measurement.
// ---------------------------------------------------------------------------
function inPageHarness() {
  if (window.__FILAMENT_BENCH__) return;

  // Row cell accessors. Kept as textContent lookups so a framework is free to wrap
  // the label in an <a>/<span> without changing the contract.
  const ROW_ID_CELL = 'td:nth-child(1)';
  const ROW_LABEL_CELL = 'td:nth-child(2)';

  const q = (sel) => document.querySelector(sel);
  const tbody = () => q('#tbody');

  function rowText(index, cellSel) {
    const t = tbody();
    if (!t) return null;
    const row = t.children[index];
    if (!row) return null;
    const cell = row.querySelector(cellSel);
    if (!cell) return null;
    return cell.textContent.trim();
  }

  const PREDICATES = {
    rowCount: (a) => {
      const t = tbody();
      return !!t && t.children.length === a.count;
    },
    labelSuffix: (a) => {
      const v = rowText(a.index, ROW_LABEL_CELL);
      return typeof v === 'string' && v.endsWith(a.suffix);
    },
    rowIdEquals: (a) => {
      const v = rowText(a.index, ROW_ID_CELL);
      return v !== null && v === a.expected;
    },
    /**
     * TOTAL predicate for #update. `labelSuffix` on a single index accepted an app
     * that mutated one row out of the ~100 the contract requires, stopped the clock
     * after ~1% of the work, and reported the result as a win. This requires EVERY
     * targeted row to carry the suffix, and every non-targeted row to NOT carry it
     * (so "suffix everything" is not a shortcut either).
     */
    labelSuffixAll: (a) => {
      for (const i of a.indices) {
        const v = rowText(i, ROW_LABEL_CELL);
        if (typeof v !== 'string' || !v.endsWith(a.suffix)) return false;
      }
      for (const i of a.notIndices || []) {
        const v = rowText(i, ROW_LABEL_CELL);
        if (typeof v !== 'string' || v.endsWith(a.suffix)) return false;
      }
      return true;
    },
    /**
     * TOTAL predicate for #swaprows. `rowIdEquals(1, id998)` alone is satisfied by
     * `rows[1] = rows[998]` — a duplicate, not a swap — i.e. half the DOM moves.
     * Both directions must hold before the clock stops.
     */
    rowIdsEqualAll: (a) => {
      for (const e of a.expect) {
        const v = rowText(e.index, ROW_ID_CELL);
        if (v === null || v !== e.expected) return false;
      }
      return true;
    },
    textEquals: (a) => {
      const el = q(a.selector);
      return !!el && el.textContent.trim() === a.expected;
    },
  };

  function makePredicate(spec) {
    const fn = PREDICATES[spec.kind];
    if (!fn) throw new Error('unknown predicate kind: ' + spec.kind);
    return () => fn(spec.args || {});
  }

  /**
   * MutationObserver-driven wait. MUST reject on timeout — a silent early return
   * would fabricate an impossibly fast number, which is exactly the failure mode
   * this harness exists to avoid.
   *
   * Resolves with the performance.now() timestamp captured at the instant the
   * predicate was observed true, INSIDE the observer callback. Reading the clock in
   * the `await` continuation instead would fold the promise-resolution microtask
   * hop into every sample.
   */
  function waitForCondition(predicate, observeSelector, timeoutMs) {
    return new Promise((resolve, reject) => {
      if (predicate()) { resolve(performance.now()); return; }

      const root = document.querySelector(observeSelector);
      if (!root) { reject(new Error('observe root not found: ' + observeSelector)); return; }

      let done = false;
      const observers = [];
      let timer = null;

      const finish = (err, at) => {
        if (done) return;
        done = true;
        if (timer !== null) clearTimeout(timer);
        for (const o of observers) o.disconnect();
        if (err) reject(err); else resolve(at);
      };

      const check = () => {
        let ok = false;
        try { ok = predicate(); } catch (e) { finish(e); return; }
        // Clock stops HERE: the moment the contract's post-condition is observably
        // true. Not at the next vsync, not after a task hop.
        if (ok) finish(null, performance.now());
      };

      timer = setTimeout(
        () => finish(new Error('waitForCondition timed out after ' + timeoutMs + 'ms')),
        timeoutMs,
      );

      // Primary observer: the contract root. characterData+subtree is required so
      // label text edits (the `update` scenario) are seen, not just childList churn.
      const primary = new MutationObserver(check);
      primary.observe(root, { childList: true, subtree: true, characterData: true, attributes: true });
      observers.push(primary);

      // Secondary observer: the parent, childList only. Catches a framework that
      // replaces the whole #tbody element rather than mutating it — without this
      // the primary observer would be watching an orphaned node and we would hang.
      // This can only ever prevent a false timeout; it cannot make a run look fast,
      // because timing still ends only when the predicate is genuinely true.
      if (root.parentNode && root.parentNode.nodeType === 1) {
        const secondary = new MutationObserver(check);
        secondary.observe(root.parentNode, { childList: true, subtree: false });
        observers.push(secondary);
      }

      check();
    });
  }

  /**
   * The timing primitive. Every timestamp is taken in-page, so no CDP/WebDriver
   * protocol latency is folded into the number.
   *
   * The async-click race: Blazor's @onclick dispatches asynchronously, so
   * `click(); await rAF;` can complete BEFORE the DOM has updated and would report
   * a fabricated sub-millisecond time. We never trust a frame boundary as a proxy
   * for completion — we wait for the DOM predicate to actually become true.
   *
   * Returns TWO numbers, and the distinction is load-bearing:
   *
   *   msToMutation — click -> the contract post-condition is true. This is the
   *     headline metric: it is the framework's work and nothing else.
   *
   *   msToPaint — msToMutation plus one requestAnimationFrame + setTimeout(0).
   *     The predicate resolves inside a MutationObserver microtask at an arbitrary
   *     phase relative to the vsync clock, so the pending rAF fires anywhere from
   *     ~0 to ~16.7 ms later. That offset is contributed by the display's refresh
   *     cycle and by this harness — NOT by the framework under test.
   *
   * Reporting only the rAF-terminated number (as this harness previously did) makes
   * every sub-millisecond scenario — swap, clear, increment — report the frame
   * interval, and makes the IQR a measurement of vsync phase jitter rather than of
   * framework variance. Two frameworks 6x apart both read ~9ms +/- 8ms. The failure
   * is silent and looks exactly like a legitimate result, so both numbers are
   * reported and msToMutation is the one that is compared.
   */
  async function measure(btnSelector, predSpec, observeSelector, timeoutMs) {
    const predicate = makePredicate(predSpec);
    const btn = document.querySelector(btnSelector);
    if (!btn) throw new Error('button not found: ' + btnSelector);

    // Anti-vacuity guard: if the predicate already holds we would "measure" zero.
    // Every contract scenario is false at this point by construction, so this
    // firing means the app or the contract is broken, not that the app is fast.
    if (predicate()) {
      throw new Error('predicate already satisfied before click (' + predSpec.kind + '); refusing to report a vacuous 0');
    }

    const t0 = performance.now();
    btn.click();
    const tMutation = await waitForCondition(predicate, observeSelector, timeoutMs);
    await new Promise((r) => requestAnimationFrame(() => setTimeout(r, 0)));
    const tPaint = performance.now();
    return { msToMutation: tMutation - t0, msToPaint: tPaint - t0 };
  }

  /**
   * The beat between a warm scenario's untimed setup and its timed segment.
   *
   * Two chained requestAnimationFrames guarantee the setup's mutations have been
   * through style, layout and paint — one rAF only proves we reached the NEXT
   * frame's callback phase, which runs BEFORE that frame's layout. The trailing
   * macrotask then drains microtasks and whatever post-render bookkeeping the
   * framework queued for itself (diff-tree reconciliation, disposal queues, GC-ish
   * cleanup).
   *
   * Without this, the setup's tail would still be executing when the clock starts
   * and would be billed to the timed click — which would defeat the entire purpose
   * of the warm variant, and would do so silently, producing a "warm" number that
   * still carries setup cost.
   */
  function settleBeat(minMs) {
    return new Promise((resolve) => {
      const start = performance.now();
      const done = () => {
        const remaining = minMs - (performance.now() - start);
        setTimeout(resolve, remaining > 0 ? remaining : 0);
      };
      requestAnimationFrame(() => requestAnimationFrame(done));
    });
  }

  window.__FILAMENT_BENCH__ = {
    measure,
    settleBeat,
    waitForCondition,
    makePredicate,
    rowText,
    rowId: (i) => rowText(i, ROW_ID_CELL),
    rowLabel: (i) => rowText(i, ROW_LABEL_CELL),
    rowCount: () => { const t = tbody(); return t ? t.children.length : -1; },
    contract: { ROW_ID_CELL, ROW_LABEL_CELL },
  };
}

// ---------------------------------------------------------------------------
// Statistics. Median + IQR only — never the mean.
// ---------------------------------------------------------------------------

/** Linear-interpolation quantile (type 7, the numpy/R default). */
export function quantile(sortedAsc, p) {
  const n = sortedAsc.length;
  if (n === 0) return null;
  if (n === 1) return sortedAsc[0];
  const idx = (n - 1) * p;
  const lo = Math.floor(idx);
  const hi = Math.ceil(idx);
  if (lo === hi) return sortedAsc[lo];
  return sortedAsc[lo] + (sortedAsc[hi] - sortedAsc[lo]) * (idx - lo);
}

const round3 = (v) => (v === null || v === undefined ? null : Math.round(v * 1000) / 1000);

/**
 * Index of the run to quote a detailed breakdown from: the one whose value is
 * NEAREST the interpolated median.
 *
 * The previous code took the upper-middle element of the sorted order and the JSON
 * called the result "the run whose total is the median". For an odd run count those
 * coincide. For an EVEN count — and the default is `--weight-runs 3` but the
 * selftest and plenty of real invocations use 2 — the type-7 median is interpolated
 * BETWEEN two runs, so no run has it, and the field name was simply false: with
 * totals [100, 200] the median is 150 and the "median run" was the 200 one.
 *
 * Nothing downstream was wrong by much, but a field named after a statistic it does
 * not hold is exactly the kind of small lie this harness exists to not tell. So:
 * pick the nearest run and name it what it is (`representativeRun`), recording the
 * distance so a reader can see when it is not the median at all.
 *
 * Ties (e.g. [100, 200], both 50 away from 150) resolve to the LOWER value, then to
 * the lower index — deterministic, and it never flatters a weight number by
 * quoting the heavier of two equidistant runs.
 */
export function representativeIndex(values) {
  if (!values.length) return -1;
  const median = quantile([...values].sort((a, b) => a - b), 0.5);
  let best = 0;
  let bestDist = Math.abs(values[0] - median);
  for (let i = 1; i < values.length; i++) {
    const d = Math.abs(values[i] - median);
    if (d < bestDist || (d === bestDist && values[i] < values[best])) {
      best = i;
      bestDist = d;
    }
  }
  return best;
}

export function summarize(samples) {
  if (!samples.length) {
    return { n: 0, samples: [], median: null, p25: null, p75: null, iqr: null, min: null, max: null };
  }
  const s = [...samples].sort((a, b) => a - b);
  const p25 = quantile(s, 0.25);
  const p50 = quantile(s, 0.5);
  const p75 = quantile(s, 0.75);
  return {
    n: s.length,
    samples: samples.map(round3),
    median: round3(p50),
    p25: round3(p25),
    p75: round3(p75),
    iqr: round3(p75 - p25),
    min: round3(s[0]),
    max: round3(s[s.length - 1]),
  };
}

// ---------------------------------------------------------------------------
// CDP network byte tracker.
//
// Transferred weight is Chrome's own encodedDataLength — the same quantity the
// DevTools Network panel labels "Transferred". It is wire bytes (compressed body
// + response headers), NOT disk size and NOT decompressed size.
// ---------------------------------------------------------------------------
const IGNORED_SCHEMES = ['data:', 'blob:', 'chrome-extension:', 'chrome:', 'devtools:', 'about:'];

function isCountable(url) {
  if (!url) return false;
  return !IGNORED_SCHEMES.some((s) => url.startsWith(s));
}

/**
 * MIME types whose bytes are already entropy-coded, so shipping them without a
 * Content-Encoding is correct rather than a bug. Note the absence of
 * application/octet-stream: that is exactly what .dat/.dll/.blat/.bin/.data use,
 * and those compress 3-4x. Exempting it would blind the check to the very case it
 * exists to catch.
 */
const ALREADY_COMPRESSED_MIME =
  /^(image\/(png|jpe?g|gif|webp|avif)|video\/|audio\/|font\/woff2?|application\/(gzip|x-gzip|zip|zstd|brotli|x-7z-compressed|x-rar-compressed|x-bzip2?|x-xz))/i;

/**
 * A response above this size, with no Content-Encoding, whose type is not already
 * compressed, is almost certainly a compression-policy hole. 4 KB is comfortably
 * above the point where compression stops being noise.
 */
const UNCOMPRESSED_WARN_BYTES = 4096;

/** Target types whose network events never reach a page-scoped CDP session. */
const WORKER_TARGET_TYPES = new Set(['worker', 'service_worker', 'shared_worker', 'worklet']);

function attachNetworkTracker(cdp) {
  /** requestId -> record */
  const byId = new Map();
  const state = {
    inFlight: 0,
    lastActivity: Date.now(),
    warnings: [],
  };

  const touch = () => { state.lastActivity = Date.now(); };

  const rec = (id) => byId.get(id);

  cdp.on('Network.requestWillBeSent', (e) => {
    touch();
    const existing = rec(e.requestId);
    if (e.redirectResponse && existing) {
      // Redirect chain reuses the requestId. The redirect response's own wire bytes
      // are not included in the final loadingFinished total, so bank them here.
      existing.redirectBytes += e.redirectResponse.encodedDataLength || 0;
      existing.redirectCount += 1;
      existing.url = e.request.url;
      return;
    }
    byId.set(e.requestId, {
      requestId: e.requestId,
      url: e.request.url,
      type: e.type || null,
      status: null,
      mimeType: null,
      protocol: null,
      contentEncoding: null,
      headerBytes: 0,
      dataEncodedSum: 0,
      dataDecodedSum: 0,
      finishedEncoded: 0,
      redirectBytes: 0,
      redirectCount: 0,
      fromDiskCache: false,
      fromPrefetchCache: false,
      fromServiceWorker: false,
      servedFromMemoryCache: false,
      failed: false,
      failureText: null,
      finished: false,
    });
    state.inFlight += 1;
  });

  cdp.on('Network.responseReceived', (e) => {
    touch();
    const r = rec(e.requestId);
    if (!r) return;
    const resp = e.response || {};
    r.status = resp.status ?? null;
    r.mimeType = resp.mimeType ?? null;
    r.protocol = resp.protocol ?? null;
    r.url = resp.url || r.url;
    // encodedDataLength on responseReceived == response header bytes so far.
    r.headerBytes = resp.encodedDataLength || 0;
    r.fromDiskCache = !!resp.fromDiskCache;
    r.fromPrefetchCache = !!resp.fromPrefetchCache;
    r.fromServiceWorker = !!resp.fromServiceWorker;
    const headers = resp.headers || {};
    for (const k of Object.keys(headers)) {
      if (k.toLowerCase() === 'content-encoding') r.contentEncoding = headers[k];
    }
  });

  cdp.on('Network.dataReceived', (e) => {
    touch();
    const r = rec(e.requestId);
    if (!r) return;
    if (typeof e.encodedDataLength === 'number' && e.encodedDataLength > 0) {
      r.dataEncodedSum += e.encodedDataLength;
    }
    if (typeof e.dataLength === 'number' && e.dataLength > 0) {
      r.dataDecodedSum += e.dataLength;
    }
  });

  cdp.on('Network.loadingFinished', (e) => {
    touch();
    const r = rec(e.requestId);
    if (!r) return;
    r.finishedEncoded = e.encodedDataLength || 0;
    r.finished = true;
    state.inFlight = Math.max(0, state.inFlight - 1);
  });

  cdp.on('Network.loadingFailed', (e) => {
    touch();
    const r = rec(e.requestId);
    if (!r) return;
    r.failed = true;
    r.finished = true;
    r.failureText = e.errorText || 'unknown';
    r.canceled = !!e.canceled;
    state.inFlight = Math.max(0, state.inFlight - 1);
  });

  /**
   * A worker target attached. Chrome delivers `requestWillBeSent` for the worker's
   * own SCRIPT to this (page-scoped) session, but routes `responseReceived` and
   * `loadingFinished` for it to the worker's target session — which Playwright's
   * CDPSession cannot address. The request therefore incremented `inFlight` and can
   * never decrement it: `waitForNetworkQuiet` would spin until maxSettleMs on EVERY
   * iteration of any worker-using app and never report settled.
   *
   * Previously that was invisible, because both call sites discarded the settle
   * result — the run just paid 30s per iteration and reported numbers anyway. With
   * the settle result now enforced it would be a hard failure, so the orphaned
   * counter has to be corrected at the source rather than tolerated.
   *
   * targetInfo.url is exactly the URL of the orphaned request, so the hand-off can
   * be matched precisely. This uses auto-attach purely to OBSERVE; it never tries
   * to route a command to the child session (see findUntrackedRequests).
   */
  cdp.on('Target.attachedToTarget', (e) => {
    const info = (e && e.targetInfo) || {};
    if (!WORKER_TARGET_TYPES.has(info.type)) return;
    touch();
    for (const r of byId.values()) {
      if (r.finished || r.url !== info.url) continue;
      r.finished = true;
      r.handedOffToWorkerTarget = true;
      state.inFlight = Math.max(0, state.inFlight - 1);
      state.warnings.push(
        `request handed off to a ${info.type} target: ${r.url} — its completion, and every request it ` +
        'goes on to issue, are invisible to the page-scoped CDP session. Byte totals for this target are ' +
        'reconciled against the server ledger instead (see weight.untrackedRequests).',
      );
    }
  });

  // A response served from Chrome's memory cache means the cold-cache guarantee
  // was violated and bytes would be under-reported. Never expected given
  // setCacheDisabled + no-store + a fresh context, but we must know if it happens.
  cdp.on('Network.requestServedFromCache', (e) => {
    const r = rec(e.requestId);
    if (r) r.servedFromMemoryCache = true;
    state.warnings.push(`request served from memory cache (cold-cache violation): ${r ? r.url : e.requestId}`);
  });

  /** Authoritative wire bytes for one request, with defensive fallbacks. */
  function bytesOf(r) {
    if (r.finishedEncoded > 0) return r.finishedEncoded + r.redirectBytes;
    const fallback = r.headerBytes + r.dataEncodedSum;
    if (fallback > 0) return fallback + r.redirectBytes;
    return r.redirectBytes;
  }

  function snapshot() {
    const requests = [];
    const warnings = [...state.warnings];
    let totalBytes = 0;
    let decodedBytes = 0;
    const byOrigin = {};

    for (const r of byId.values()) {
      if (!isCountable(r.url)) continue;
      if (r.failed) {
        if (!r.canceled) warnings.push(`request failed: ${r.url} (${r.failureText})`);
        continue;
      }
      const bytes = bytesOf(r);
      // A handed-off request already has its own, more specific warning; its byte
      // count legitimately lives on the server side of the ledger.
      if (bytes === 0 && r.finished && !r.handedOffToWorkerTarget) {
        warnings.push(`request finished with 0 transferred bytes (suspicious): ${r.url}`);
      }
      if (!r.finished) {
        warnings.push(`request still in flight at snapshot time: ${r.url}`);
      }
      if (r.status !== null && r.status >= 400) {
        warnings.push(`HTTP ${r.status} for ${r.url} — publish output may be incomplete`);
      }
      if (r.fromDiskCache || r.fromPrefetchCache || r.servedFromMemoryCache) {
        warnings.push(`cold-cache violation (served from cache): ${r.url}`);
      }
      if (r.fromServiceWorker) {
        warnings.push(`response served by a service worker; transferred bytes may be under-reported: ${r.url}`);
      }
      // A compressible payload shipped raw inflates that framework's weight 3-4x on
      // that asset. The harness had this data (contentEncoding, per request) and did
      // not look at it, so the inflation could quietly become a headline number.
      if (
        bytes > UNCOMPRESSED_WARN_BYTES &&
        !r.contentEncoding &&
        !r.fromDiskCache &&
        !r.fromPrefetchCache &&
        !r.servedFromMemoryCache &&
        !(r.mimeType && ALREADY_COMPRESSED_MIME.test(r.mimeType))
      ) {
        warnings.push(
          `compressible response shipped with NO Content-Encoding (${bytes} B, ${r.mimeType || 'unknown type'}): ` +
          `${r.url} — this asset's transferred weight is inflated relative to a compressed peer`,
        );
      }
      totalBytes += bytes;
      decodedBytes += r.dataDecodedSum;
      let origin = 'unknown';
      try { origin = new URL(r.url).origin; } catch { /* ignore */ }
      byOrigin[origin] = (byOrigin[origin] || 0) + bytes;

      requests.push({
        url: r.url,
        type: r.type,
        status: r.status,
        mimeType: r.mimeType,
        protocol: r.protocol,
        contentEncoding: r.contentEncoding,
        transferredBytes: bytes,
        decodedBytes: r.dataDecodedSum,
        redirectCount: r.redirectCount,
      });
    }

    requests.sort((a, b) => b.transferredBytes - a.transferredBytes);
    return {
      totalBytes,
      decodedBytes,
      requestCount: requests.length,
      bytesByOrigin: byOrigin,
      requests,
      warnings: [...new Set(warnings)],
    };
  }

  return { state, snapshot };
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

/**
 * Reconcile Chrome's view of the network against the server's own byte ledger, by
 * PATH, and return every path the server actually served that CDP never reported.
 *
 * Why this exists. The CDP session is page-scoped, and Playwright's public
 * CDPSession cannot address auto-attached child targets: `Target.setAutoAttach`
 * succeeds and `Target.attachedToTarget` fires with a sessionId, but
 * `cdp.send(method, { sessionId })` passes sessionId as a *params* field rather
 * than as the CDP envelope field, so it resolves without routing anywhere. Verified
 * against playwright 1.61.1: a dedicated Worker's 300 KB fetch stays invisible to
 * the page session with or without the auto-attach call. Shipping that call anyway
 * would be worse than not having it — a no-op that reads as a fix.
 *
 * The server ledger has no such blind spot: it counts what it WROTE, so a request
 * is counted no matter which target issued it. Any path present in the ledger but
 * absent from CDP is, by definition, bytes the primary metric is missing — whether
 * from a service worker, a Web Worker (dotnet.native.worker.mjs under
 * WasmEnableThreads), or something nobody thought to enumerate. That makes this
 * strictly more complete than auto-attaching to the target types we anticipated.
 */
export function findUntrackedRequests(cdpSnapshot, serverSnapshot, origin) {
  const cdpByPath = new Map();
  for (const r of cdpSnapshot.requests || []) {
    try {
      const u = new URL(r.url);
      if (u.origin !== origin) continue;
      cdpByPath.set(u.pathname, (cdpByPath.get(u.pathname) || 0) + (r.transferredBytes || 0));
    } catch { /* non-URL entries cannot be reconciled by path */ }
  }

  const untracked = [];
  for (const [p, info] of Object.entries(serverSnapshot.byPath || {})) {
    const cdpBytes = cdpByPath.get(p);
    if (cdpBytes === undefined) {
      // The request was never reported at all: issued from a target this session
      // cannot see. Every one of its bytes is missing from the total.
      untracked.push({
        path: p,
        reason: 'never reported to the page-scoped CDP session',
        serverBodyBytes: info.bytes,
        cdpTransferredBytes: 0,
        missingBytes: info.bytes,
        hits: info.hits,
      });
    } else if (cdpBytes < info.bytes) {
      // CDP must be >= the server's BODY bytes, because CDP additionally counts
      // response headers. Below it means the completion events went somewhere else
      // — e.g. a worker script, whose requestWillBeSent lands here but whose
      // responseReceived/loadingFinished land on the worker target.
      untracked.push({
        path: p,
        reason: 'CDP reported fewer bytes than the server wrote (completion delivered to another target)',
        serverBodyBytes: info.bytes,
        cdpTransferredBytes: cdpBytes,
        missingBytes: info.bytes - cdpBytes,
        hits: info.hits,
      });
    }
  }
  return untracked.sort((a, b) => b.missingBytes - a.missingBytes);
}

/**
 * Settle detector built on the CDP tracker itself (not Playwright's networkidle):
 * zero in-flight requests AND no network event for `quietMs`.
 */
async function waitForNetworkQuiet(tracker, { quietMs, maxSettleMs }) {
  const deadline = Date.now() + maxSettleMs;
  for (;;) {
    const now = Date.now();
    if (tracker.state.inFlight === 0 && now - tracker.state.lastActivity >= quietMs) {
      return { settled: true };
    }
    if (now > deadline) {
      return {
        settled: false,
        reason: `network did not settle within ${maxSettleMs}ms (inFlight=${tracker.state.inFlight})`,
      };
    }
    await sleep(25);
  }
}

/**
 * App-ready = the contract hook exists AND is genuinely clickable.
 * `click({ trial: true })` runs Playwright's full actionability suite (attached,
 * visible, stable, enabled, receives pointer events) WITHOUT dispatching a click.
 */
async function waitForAppReady(page, readySelector, timeoutMs) {
  const loc = page.locator(readySelector);
  await loc.waitFor({ state: 'visible', timeout: timeoutMs });
  await loc.click({ trial: true, timeout: timeoutMs });
}

// ---------------------------------------------------------------------------
// Page lifecycle
// ---------------------------------------------------------------------------
async function withFreshPage(browser, opts, fn) {
  // A fresh BrowserContext per iteration gives an isolated storage/cache partition.
  // Combined with Network.setCacheDisabled and the server's Cache-Control: no-store,
  // that is three independent guarantees of a cold cache.
  //
  // Service workers are BLOCKED by default (--allow-service-workers opts back in).
  // The CDP session below is page-scoped, and requests issued from a service-worker
  // or Web Worker target are never delivered to it — they hit neither state.inFlight
  // nor the byte totals. With the Blazor PWA template (service-worker.published.js
  // cache.addAll's the whole _framework payload on install) that produced two
  // independent corruptions: waitForNetworkQuiet saw inFlight===0 and snapshotted
  // atReady mid-download, and those megabytes were absent from totalBytes entirely.
  // Blocking makes the cold-cache and byte guarantees hold BY CONSTRUCTION rather
  // than by hoping the app under test has no service worker.
  const context = await browser.newContext({
    viewport: { width: 1280, height: 960 },
    serviceWorkers: opts.allowServiceWorkers ? 'allow' : 'block',
  });
  await context.addInitScript(inPageHarness);
  const page = await context.newPage();

  const consoleErrors = [];
  const pageErrors = [];
  page.on('pageerror', (e) => pageErrors.push(String((e && e.message) || e)));
  page.on('console', (m) => { if (m.type() === 'error') consoleErrors.push(m.text()); });

  // CDP session must be attached BEFORE navigation or the earliest requests
  // (index.html itself) would be missed and weight under-reported.
  const cdp = await context.newCDPSession(page);
  const tracker = attachNetworkTracker(cdp);
  // Auto-attach is enabled to OBSERVE worker targets (Target.attachedToTarget tells
  // the tracker which in-flight request was handed off), never to drive them.
  //
  // waitForDebuggerOnStart MUST stay false. Playwright's CDPSession cannot address
  // a child session, so a worker paused on start could never be sent
  // Runtime.runIfWaitingForDebugger and would hang forever — turning a measurement
  // into a deadlock. flatten:true is required for attachedToTarget to be delivered
  // here at all.
  await cdp
    .send('Target.setAutoAttach', { autoAttach: true, waitForDebuggerOnStart: false, flatten: true })
    .catch(() => { /* older Chrome without flat auto-attach: reconciliation still covers us */ });
  await cdp.send('Network.enable');
  await cdp.send('Network.setCacheDisabled', { cacheDisabled: true });

  try {
    return await fn({ page, cdp, tracker, consoleErrors, pageErrors });
  } finally {
    await cdp.detach().catch(() => {});
    await context.close().catch(() => {});
  }
}

async function loadAndSettle(ctx, url, app, opts) {
  await ctx.page.goto(url, { waitUntil: 'commit', timeout: opts.navTimeoutMs });
  await waitForAppReady(ctx.page, APPS[app].readySelector, opts.readyTimeoutMs);
  // Blazor lazy-loads framework files, so "the button exists" is NOT the end of
  // the network. Settle before taking any byte reading.
  const settle = await waitForNetworkQuiet(ctx.tracker, opts);
  return settle;
}

/**
 * loadAndSettle for the TIMED path, where an unsettled network is not a footnote
 * but a contaminated sample: the click would race in-flight download and decode
 * work, inflating that sample, and the run would still be recorded as `ok` with a
 * median and an IQR that a reader would misattribute to framework variance.
 *
 * The previous code discarded the settle result entirely at both call sites, so
 * this condition produced no warning anywhere in the scenario output. Per this
 * file's own rule — a timeout is a FAILURE, never a number — it throws, and
 * runScenario records the iteration as a failure.
 */
async function loadAndSettleStrict(ctx, url, app, opts) {
  const settle = await loadAndSettle(ctx, url, app, opts);
  if (!settle.settled) {
    throw new Error(
      `refusing to time an iteration on an unsettled network: ${settle.reason}. ` +
      'A sample taken here would race in-flight asset download/decode and be inflated by it.',
    );
  }
  return settle;
}

/** Same rule for the untimed setup phase — a lazy-load still in flight lands inside the measured window. */
async function settleStrict(ctx, opts, what) {
  const settle = await waitForNetworkQuiet(ctx.tracker, opts);
  if (!settle.settled) {
    throw new Error(`refusing to proceed after ${what} on an unsettled network: ${settle.reason}`);
  }
  return settle;
}

/** Run a contract action and await its predicate, untimed. Same code path as measure(). */
async function actUntimed(page, button, predicate, observeSelector, timeoutMs) {
  await page.evaluate(
    ([btn, pred, obs, t]) => window.__FILAMENT_BENCH__.measure(btn, pred, obs, t),
    [button, predicate, observeSelector, timeoutMs],
  );
}

/** Let the setup's rendering and post-render bookkeeping fully drain. See settleBeat(). */
async function idleBeat(page, opts) {
  await page.evaluate((ms) => window.__FILAMENT_BENCH__.settleBeat(ms), opts.idleBeatMs);
}

// ---------------------------------------------------------------------------
// Contract preflight
//
// Without this, a DOM-contract mismatch (say, the label living somewhere other
// than cell 2) surfaces only as a predicate timeout — which reads as "this
// framework is slow" rather than "the harness is pointed at the wrong element".
// Numbers from an unmet contract are worse than no numbers, so this fails fast.
// ---------------------------------------------------------------------------
async function verifyContract(browser, url, app, opts, expectedLabels) {
  return withFreshPage(browser, opts, async (ctx) => {
    await loadAndSettleStrict(ctx, url, app, opts);
    const obs = APPS[app].observeSelector;
    const T = opts.actionTimeoutMs;

    if (app === 'counter') {
      return ctx.page.evaluate(() => {
        const out = { problems: [], observed: {} };
        for (const sel of ['#increment', '#counter-value']) {
          if (!document.querySelector(sel)) out.problems.push(`missing required element: ${sel}`);
        }
        if (out.problems.length) return out;
        out.observed.initialValue = document.querySelector('#counter-value').textContent.trim();
        if (out.observed.initialValue === '1') {
          out.problems.push('#counter-value already reads "1" before #increment; the predicate would be vacuous');
        }
        return out;
      });
    }

    // The fixture cannot be require()d from page context, so it is passed in.
    return ctx.page.evaluate(async ([o, t, expected, updateSpec, suffix, updIdx, notIdx]) => {
      const B = window.__FILAMENT_BENCH__;
      const out = { problems: [], observed: {} };
      for (const sel of ['#run', '#update', '#swaprows', '#clear', '#tbody']) {
        if (!document.querySelector(sel)) out.problems.push(`missing required element: ${sel}`);
      }
      if (out.problems.length) return out;

      // ---- #run ------------------------------------------------------------
      try {
        await B.measure('#run', { kind: 'rowCount', args: { count: 1000 } }, o, t);
      } catch (e) {
        out.problems.push(`#run did not produce exactly 1000 rows in #tbody within ${t}ms: ${e.message}`);
        out.observed.rowCount = B.rowCount();
        return out;
      }

      const tb = document.querySelector('#tbody');
      out.observed.rowCount = tb.children.length;
      out.observed.cellsPerRow = tb.children[0] ? tb.children[0].children.length : 0;
      if (out.observed.cellsPerRow < 2) {
        out.problems.push(
          `rows expose ${out.observed.cellsPerRow} cell(s); the contract needs >= 2 ` +
          '(cell 1 = id, cell 2 = label)',
        );
        return out;
      }

      out.observed.row1Id = B.rowId(1);
      out.observed.row998Id = B.rowId(998);
      out.observed.row990Label = B.rowLabel(990);

      if (out.observed.row1Id === null) out.problems.push('cannot read row 1 id via td:nth-child(1)');
      if (out.observed.row998Id === null) out.problems.push('cannot read row 998 id via td:nth-child(1)');
      if (out.observed.row1Id !== null && out.observed.row1Id === out.observed.row998Id) {
        out.problems.push(`row 1 and row 998 share the id "${out.observed.row1Id}"; the swap predicate would be vacuous`);
      }
      if (out.observed.row990Label === null) {
        out.problems.push('cannot read row 990 label via td:nth-child(2)');
      } else if (out.observed.row990Label.endsWith(' !!!')) {
        out.problems.push('row 990 label already ends with " !!!" before #update; the update predicate would be vacuous');
      }

      // ---- deterministic label stream (CRITICAL for fairness) ---------------
      // Read BEFORE #update, which rewrites every 10th label.
      const first5 = [0, 1, 2, 3, 4].map((i) => B.rowLabel(i));
      const row1000 = B.rowLabel(999);
      out.observed.first5 = first5;
      out.observed.row1000 = row1000;
      for (let i = 0; i < 5; i++) {
        if (first5[i] !== expected.first5[i]) {
          out.problems.push(
            `row ${i} label is ${JSON.stringify(first5[i])} but the deterministic sequence requires ` +
            `${JSON.stringify(expected.first5[i])}. Every framework MUST run the Park-Miller LCG ` +
            '(seed 42.0, three draws per row: adjective/colour/noun) and emit the byte-identical stream. ' +
            'A different stream means a different amount of work, so the timings are not comparable.',
          );
        }
      }
      if (row1000 !== expected.row1000) {
        out.problems.push(
          `row 999 (the 1000th) label is ${JSON.stringify(row1000)} but the deterministic sequence requires ` +
          `${JSON.stringify(expected.row1000)}. The LCG must advance exactly 3 draws per row across all 1000 rows.`,
        );
      }
      // A single constant label satisfies "first5[0] matches" only by luck, but it
      // would fail the rest. Catch it explicitly for a clearer diagnostic.
      if (new Set(first5).size === 1 && first5[0] !== null) {
        out.problems.push(
          `the first 5 rows all share the label ${JSON.stringify(first5[0])}; the label generator is constant, ` +
          'not the required pseudo-random stream',
        );
      }
      if (out.problems.length) return out;

      // ---- #update ---------------------------------------------------------
      // Driven with the TOTAL predicate, then asserted explicitly so a violation
      // reports which rows are wrong instead of an opaque timeout.
      let updateErr = null;
      try {
        await B.measure('#update', updateSpec, o, t);
      } catch (e) {
        updateErr = e.message;
      }
      const missed = updIdx.filter((i) => {
        const v = B.rowLabel(i);
        return typeof v !== 'string' || !v.endsWith(suffix);
      });
      const spurious = notIdx.filter((i) => {
        const v = B.rowLabel(i);
        return typeof v === 'string' && v.endsWith(suffix);
      });
      out.observed.updateMissedCount = missed.length;
      if (missed.length) {
        out.problems.push(
          `#update appended "${suffix}" to only ${updIdx.length - missed.length} of the ${updIdx.length} required rows; ` +
          `${missed.length} rows were not touched (e.g. indices ${missed.slice(0, 6).join(', ')}). ` +
          'The contract is: for i = 0, 10, 20, ... < rowCount, append the literal string to that row\'s label.',
        );
      }
      if (spurious.length) {
        out.problems.push(
          `#update appended "${suffix}" to rows it must NOT touch (indices ${spurious.join(', ')}); ` +
          'only every 10th row may be modified',
        );
      }
      if (updateErr && !missed.length && !spurious.length) {
        out.problems.push(`#update eventually satisfied the contract but not within ${t}ms: ${updateErr}`);
      }
      if (out.problems.length) return out;

      // ---- #swaprows -------------------------------------------------------
      const beforeSwap = { id1: B.rowId(1), id998: B.rowId(998) };
      let swapErr = null;
      try {
        await B.measure(
          '#swaprows',
          {
            kind: 'rowIdsEqualAll',
            args: { expect: [{ index: 1, expected: beforeSwap.id998 }, { index: 998, expected: beforeSwap.id1 }] },
          },
          o,
          t,
        );
      } catch (e) {
        swapErr = e.message;
      }
      const afterSwap = { id1: B.rowId(1), id998: B.rowId(998) };
      out.observed.swap = { before: beforeSwap, after: afterSwap };
      if (afterSwap.id1 !== beforeSwap.id998) {
        out.problems.push(
          `#swaprows left index 1 holding id "${afterSwap.id1}"; it must hold the row from index 998 ("${beforeSwap.id998}")`,
        );
      }
      if (afterSwap.id998 !== beforeSwap.id1) {
        out.problems.push(
          `#swaprows left index 998 holding id "${afterSwap.id998}"; it must hold the row from index 1 ("${beforeSwap.id1}"). ` +
          'A one-directional assignment (rows[1] = rows[998]) is a duplicate, not a swap, and does half the DOM work.',
        );
      }
      if (swapErr && afterSwap.id1 === beforeSwap.id998 && afterSwap.id998 === beforeSwap.id1) {
        out.problems.push(`#swaprows eventually satisfied the contract but not within ${t}ms: ${swapErr}`);
      }
      if (out.problems.length) return out;

      // ---- #clear ----------------------------------------------------------
      let clearErr = null;
      try {
        await B.measure('#clear', { kind: 'rowCount', args: { count: 0 } }, o, t);
      } catch (e) {
        clearErr = e.message;
      }
      out.observed.rowCountAfterClear = B.rowCount();
      if (out.observed.rowCountAfterClear !== 0) {
        out.problems.push(
          `#clear left ${out.observed.rowCountAfterClear} row(s) in #tbody; the contract requires an empty tbody` +
          (clearErr ? ` (${clearErr})` : ''),
        );
      }
      return out;
    }, [obs, T, { first5: expectedLabels.first5, row1000: expectedLabels.row1000 },
        UPDATE_PREDICATE, ' !!!', UPDATE_INDICES, UPDATE_NOT_INDICES]);
  });
}

// ---------------------------------------------------------------------------
// Scenarios
// ---------------------------------------------------------------------------
const RUN_PREDICATE = { kind: 'rowCount', args: { count: 1000 } };
const CLEAR_PREDICATE = { kind: 'rowCount', args: { count: 0 } };
const counterPredicate = (expected) => ({ kind: 'textEquals', args: { selector: '#counter-value', expected } });

/** Every 10th row of 1000: 0, 10, 20, ... 990. Exactly the set #update must touch. */
const UPDATE_INDICES = Array.from({ length: 100 }, (_, i) => i * 10);
/**
 * Rows #update must NOT touch. Sampled either side of a targeted index so an
 * off-by-one stride (i += 9 / i += 11) or a blanket "suffix everything" is caught.
 */
const UPDATE_NOT_INDICES = [1, 9, 11, 19, 21, 499, 501, 989, 991, 999];

const UPDATE_PREDICATE = {
  kind: 'labelSuffixAll',
  args: { indices: UPDATE_INDICES, notIndices: UPDATE_NOT_INDICES, suffix: ' !!!' },
};

/** Reciprocal swap: row 1 holds the old id998 AND row 998 holds the old id1. */
const swapPredicate = (ids) => ({
  kind: 'rowIdsEqualAll',
  args: { expect: [{ index: 1, expected: ids.id998 }, { index: 998, expected: ids.id1 }] },
});

/**
 * One timed iteration. Every iteration starts from a fresh page load.
 * Returns milliseconds, or throws (which the caller records as a FAILURE).
 */
async function runScenarioIteration(ctx, app, scenario, url, opts) {
  const { page } = ctx;
  const obs = APPS[app].observeSelector;
  const T = opts.actionTimeoutMs;

  await loadAndSettleStrict(ctx, url, app, opts);

  const measureIn = (button, predicate) =>
    page.evaluate(
      ([btn, pred, o, t]) => window.__FILAMENT_BENCH__.measure(btn, pred, o, t),
      [button, predicate, obs, T],
    );

  // Untimed setup: create the 1000 rows and let any interaction-triggered lazy
  // loading finish, so update/swap/clear measure DOM work rather than network.
  const setupRows = async () => {
    await actUntimed(page, '#run', RUN_PREDICATE, obs, T);
    await settleStrict(ctx, opts, 'the untimed #run setup');
  };

  switch (scenario) {
    case 'create-cold':
      // Fresh page load for EVERY iteration, and the timed click is the FIRST
      // interaction — so a runtime that initialises lazily boots inside this
      // window. Deliberately kept: it is a real cost. It is simply not the answer
      // to "how fast does this framework build 1000 rows", which is create-warm.
      return await measureIn('#run', RUN_PREDICATE);

    case 'create-warm': {
      // Untimed setup: build the rows once (this is what pays the runtime boot and
      // any interaction-triggered lazy load), then tear them back down, so the
      // timed #run starts from the same empty-tbody state as create-cold does.
      await setupRows();
      await actUntimed(page, '#clear', CLEAR_PREDICATE, obs, T);
      // Cheap: no network activity since the #run settle, so the quiet window has
      // already elapsed. Present because #clear is an interaction like any other
      // and a framework is entitled to lazy-load on it.
      await settleStrict(ctx, opts, 'the untimed #clear setup');
      await idleBeat(page, opts);
      // measure() re-asserts non-vacuity: #tbody is empty here, so rowCount === 1000
      // is false at click time exactly as it is for create-cold. The two scenarios
      // time the identical button against the identical predicate from the identical
      // DOM state; the ONLY difference is whether the runtime is already booted.
      return await measureIn('#run', RUN_PREDICATE);
    }

    case 'update':
      await setupRows();
      // TOTAL predicate: every 10th row must carry the suffix and no other row may.
      // Gating on index 990 alone stopped the clock once 1 of ~100 mutations had
      // landed, so an app touching only the last row posted a ~100x "win".
      return await measureIn('#update', UPDATE_PREDICATE);

    case 'swap': {
      await setupRows();
      const ids = await page.evaluate(() => ({
        id1: window.__FILAMENT_BENCH__.rowId(1),
        id998: window.__FILAMENT_BENCH__.rowId(998),
      }));
      if (ids.id1 === null || ids.id998 === null) {
        throw new Error(`swap: could not read row ids (row1=${ids.id1}, row998=${ids.id998}); DOM contract not met`);
      }
      // If the two ids were equal the predicate would be vacuously true the instant
      // it is evaluated and would report a fake ~0ms swap.
      if (ids.id1 === ids.id998) {
        throw new Error(`swap: row 1 and row 998 have identical ids ("${ids.id1}"); predicate would be vacuous`);
      }
      // TOTAL predicate: BOTH directions. Checking only that row 1 received id998
      // is satisfied by `rows[1] = rows[998]` — a duplicate, not a swap.
      return await measureIn('#swaprows', swapPredicate(ids));
    }

    case 'clear':
      await setupRows();
      return await measureIn('#clear', CLEAR_PREDICATE);

    case 'increment-cold':
      // First interaction on a cold runtime: boot + increment.
      return await measureIn('#increment', counterPredicate('1'));

    case 'increment-warm':
      // One untimed increment boots the runtime and warms the increment path...
      await actUntimed(page, '#increment', counterPredicate('1'), obs, T);
      await settleStrict(ctx, opts, 'the untimed #increment setup');
      await idleBeat(page, opts);
      // ...then an identical click is timed. The predicate is "2" rather than "1"
      // purely because the counter is monotonic, exactly as the contract requires;
      // the WORK is the same single increment. It is non-vacuous at click time
      // (#counter-value reads "1"), which measure() re-asserts.
      return await measureIn('#increment', counterPredicate('2'));

    default:
      throw new Error(`unknown scenario: ${scenario}`);
  }
}

async function runScenario(browser, url, app, scenario, opts) {
  const samples = [];      // msToMutation — the headline metric
  const paintSamples = []; // msToPaint    — mutation + one vsync-quantized frame wait
  const failures = [];
  const consoleErrors = [];
  const pageErrors = [];
  const bytesToReady = [];

  const total = opts.warmup + opts.runs;
  for (let i = 0; i < total; i++) {
    const isWarmup = i < opts.warmup;
    try {
      // eslint-disable-next-line no-await-in-loop
      const r = await withFreshPage(browser, opts, async (ctx) => {
        const t = await runScenarioIteration(ctx, app, scenario, url, opts);
        if (!isWarmup) {
          bytesToReady.push(ctx.tracker.snapshot().totalBytes);
          consoleErrors.push(...ctx.consoleErrors);
          pageErrors.push(...ctx.pageErrors);
        }
        return t;
      });
      if (!isWarmup) {
        samples.push(r.msToMutation);
        paintSamples.push(r.msToPaint);
      }
      process.stderr.write(
        `  [${app}/${scenario}] ${isWarmup ? 'warmup' : `run ${samples.length}/${opts.runs}`}: ` +
        `${r.msToMutation.toFixed(2)} ms to mutation (${r.msToPaint.toFixed(2)} ms to paint)\n`,
      );
    } catch (err) {
      const message = String((err && err.message) || err);
      // A timeout is a FAILURE. It is never converted into a number.
      if (!isWarmup) failures.push({ iteration: i - opts.warmup, error: message });
      process.stderr.write(`  [${app}/${scenario}] FAILURE: ${message.split('\n')[0]}\n`);
    }
  }

  const stats = summarize(samples);
  let status = 'ok';
  if (samples.length === 0) status = 'failed';
  else if (failures.length > 0) status = 'partial';

  const spec = SCENARIO_SPECS[scenario];
  return {
    scenario,
    status,
    ok: status === 'ok',
    // Cold vs warm, carried in the JSON next to the number rather than left to a
    // report's prose. A reader who takes `median` without reading `runtime` still
    // cannot mistake a boot-contaminated number for a rendering one, because
    // `runtime: "cold"` sits beside it and `headline: false` says not to compare on it.
    runtime: spec.runtime,
    headline: spec.headline,
    untimedSetup: spec.setup,
    measures: spec.measures,
    // The top-level stats are msToMutation. Named explicitly so no reader has to
    // guess which of the two timings the median/IQR describe.
    metric: 'msToMutation',
    ...stats,
    unit: 'ms',
    toPaint: {
      ...summarize(paintSamples),
      unit: 'ms',
      note:
        'msToMutation plus one requestAnimationFrame + setTimeout(0). The predicate resolves in a ' +
        'MutationObserver microtask at an arbitrary phase of the vsync cycle, so this carries a ' +
        '0-16.7ms additive offset contributed by the display refresh and this harness, NOT by the ' +
        'framework. Its IQR is largely vsync phase jitter. Never compare frameworks on this number; ' +
        'it is reported so the ~16ms quantum is visibly the harness\'s.',
    },
    failureCount: failures.length,
    failures,
    diagnostics: {
      transferredBytesToInteractive: bytesToReady,
      consoleErrors: [...new Set(consoleErrors)].slice(0, 20),
      pageErrors: [...new Set(pageErrors)].slice(0, 20),
    },
  };
}

/**
 * Weight run: navigate cold, wait until the app is interactive AND the network has
 * settled, then sum. A second reading is taken after the first interaction so that
 * assemblies lazy-loaded *on click* are visible rather than silently excluded.
 */
async function measureWeightOnce(browser, url, app, opts) {
  return withFreshPage(browser, opts, async (ctx) => {
    const settle = await loadAndSettle(ctx, url, app, opts);
    const atReady = ctx.tracker.snapshot();

    const action = APPS[app].primaryAction;
    let afterInteraction = null;
    let settle2 = { settled: true };
    try {
      await actUntimed(ctx.page, action.button, action.predicate, APPS[app].observeSelector, opts.actionTimeoutMs);
      settle2 = await waitForNetworkQuiet(ctx.tracker, opts);
      afterInteraction = ctx.tracker.snapshot();
    } catch (err) {
      afterInteraction = { error: String((err && err.message) || err) };
    }

    return {
      atReady,
      afterInteraction,
      // Complete CDP view of the run, for reconciliation against the server ledger.
      final: ctx.tracker.snapshot(),
      settledToInteractive: settle.settled,
      settleReason: settle.reason ?? null,
      settledAfterInteraction: settle2.settled,
      consoleErrors: ctx.consoleErrors,
      pageErrors: ctx.pageErrors,
    };
  });
}

async function measureWeight(browser, url, app, opts, server) {
  const runs = [];
  const serverOrigin = new URL(server.url).origin;
  for (let i = 0; i < opts.weightRuns; i++) {
    server.stats.reset();
    // eslint-disable-next-line no-await-in-loop
    const r = await measureWeightOnce(browser, url, app, opts);
    r.serverSide = server.stats.snapshot();
    // Bytes the server wrote that Chrome's page-scoped CDP session never reported:
    // service-worker / Web Worker targets, or anything else invisible to it.
    r.untrackedRequests = findUntrackedRequests(r.final, r.serverSide, serverOrigin);
    runs.push(r);
    process.stderr.write(
      `  [${app}/weight] run ${i + 1}/${opts.weightRuns}: ${r.atReady.totalBytes} B transferred to interactive` +
      ` across ${r.atReady.requestCount} requests (server wrote ${r.serverSide.totalBodyBytes} B of body)\n`,
    );
  }

  const toInteractive = runs.map((r) => r.atReady.totalBytes);
  const afterInteraction = runs
    .map((r) => (r.afterInteraction && typeof r.afterInteraction.totalBytes === 'number' ? r.afterInteraction.totalBytes : null))
    .filter((v) => v !== null);

  // Report the request breakdown from the run NEAREST the median total. See
  // representativeIndex: for an even run count no run holds the interpolated
  // median, so this is named for what it actually is.
  const repIdx = representativeIndex(toInteractive);
  const medianRun = runs[repIdx];

  const warnings = [...new Set(runs.flatMap((r) => r.atReady.warnings))];
  for (const r of runs) {
    if (!r.settledToInteractive) warnings.push(`weight run did not reach network idle: ${r.settleReason}`);
    for (const u of r.untrackedRequests) {
      warnings.push(
        `${u.missingBytes} B MISSING from the transferred total for ${u.path} (x${u.hits}): ${u.reason} ` +
        `(server wrote ${u.serverBodyBytes} B of body, CDP reported ${u.cdpTransferredBytes} B). ` +
        'Most likely a service-worker or Web Worker target. Treat the weight number as a lower bound.',
      );
    }
  }

  // Independent cross-check: CDP counts wire bytes (body + response headers), the
  // server counts body bytes only. CDP must be >= server, by roughly one set of
  // response headers per request. A large divergence means something is wrong.
  const representativeRun = {
    index: repIdx,
    totalBytes: toInteractive[repIdx],
    medianBytes: summarize(toInteractive).median,
    isExactlyTheMedian: toInteractive[repIdx] === summarize(toInteractive).median,
    selection:
      'The run whose transferred total is NEAREST the interpolated (type-7) median of all weight runs; ' +
      'ties resolve to the lower total, then the lower index. Every per-run breakdown below ' +
      '(decodedBytesRepresentativeRun, requestCount, bytesByOrigin, topRequests, crossCheck, ' +
      'serverEncodings, untrackedRequests) is quoted from THIS run. For an even number of weight runs ' +
      'the median falls between two runs and no run holds it, so isExactlyTheMedian is false and this ' +
      'is the nearer of the two — it is not, and is no longer called, "the median run".',
  };

  const crossCheck = {
    cdpTransferredBytes: medianRun.atReady.totalBytes,
    serverBodyBytes: medianRun.serverSide.totalBodyBytes,
    serverRequestCount: medianRun.serverSide.totalRequests,
    deltaBytes: medianRun.atReady.totalBytes - medianRun.serverSide.totalBodyBytes,
    note: 'CDP counts wire bytes (compressed body + response headers); the server counts body bytes only. delta ~= response header overhead. A negative delta or a delta far larger than ~300 B/request indicates a measurement problem.',
  };
  if (crossCheck.deltaBytes < 0) {
    warnings.push(`CDP total (${crossCheck.cdpTransferredBytes} B) is BELOW server-written body bytes (${crossCheck.serverBodyBytes} B) — bytes are being under-counted`);
  }
  if (medianRun.serverSide.notFound.length) {
    warnings.push(`server returned 404 for: ${medianRun.serverSide.notFound.join(', ')}`);
  }

  const summaryToInteractive = summarize(toInteractive);
  return {
    unit: 'bytes',
    method: 'CDP Network.loadingFinished.encodedDataLength summed across all requests from navigation until app-interactive AND network idle',
    toInteractive: {
      ...summaryToInteractive,
      samples: toInteractive,
      median: summaryToInteractive.median,
    },
    afterFirstInteraction: afterInteraction.length
      ? { ...summarize(afterInteraction), samples: afterInteraction }
      : null,
    lazyLoadedOnInteractionBytes:
      afterInteraction.length && summarize(afterInteraction).median !== null
        ? summarize(afterInteraction).median - summaryToInteractive.median
        : null,
    representativeRun,
    decodedBytesRepresentativeRun: medianRun.atReady.decodedBytes,
    requestCount: medianRun.atReady.requestCount,
    bytesByOrigin: medianRun.atReady.bytesByOrigin,
    topRequests: medianRun.atReady.requests.slice(0, 15),
    crossCheck,
    serverEncodings: medianRun.serverSide.byEncoding,
    untrackedRequests: medianRun.untrackedRequests,
    // Every path the server actually served during the representative run. Used to
    // confirm that an artifact the AOT evidence was read from was genuinely SERVED,
    // not merely sitting in the publish directory.
    servedPaths: Object.keys(medianRun.serverSide.byPath || {}),
    warnings: [...new Set(warnings)],
  };
}

// ---------------------------------------------------------------------------
// Environment capture
// ---------------------------------------------------------------------------
function safeExec(cmd, args) {
  try {
    return execFileSync(cmd, args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'], timeout: 15000 }).trim();
  } catch {
    return null;
  }
}

function playwrightVersion() {
  try {
    return require('playwright/package.json').version;
  } catch {
    return null;
  }
}

async function captureEnvironment(browser, opts) {
  const cpus = os.cpus();
  return {
    machine: {
      hostname: os.hostname(),
      model: os.platform() === 'darwin' ? safeExec('sysctl', ['-n', 'hw.model']) : null,
      cpu: cpus.length ? cpus[0].model : null,
      cpuCount: cpus.length,
      arch: os.arch(),
      totalMemBytes: os.totalmem(),
    },
    os: {
      platform: os.platform(),
      release: os.release(),
      version: os.version ? os.version() : null,
      productVersion: os.platform() === 'darwin' ? safeExec('sw_vers', ['-productVersion']) : null,
    },
    chrome: {
      channel: 'chrome',
      version: browser.version(),
      headless: opts.headless,
    },
    dotnetSdk: safeExec('dotnet', ['--version']),
    node: process.version,
    playwright: playwrightVersion(),
    aot: opts.aot,
    harnessVersion: HARNESS_VERSION,
  };
}

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------
function parseArgs(argv) {
  const o = {
    dir: null,
    port: 0,
    host: '127.0.0.1',
    app: null,
    label: null,
    route: '/',
    runs: 10,
    warmup: 0,
    weightRuns: 3,
    headless: true,
    aot: null,
    out: null,
    // Default-DENY. See withFreshPage: a page-scoped CDP session cannot see bytes
    // fetched from a service-worker target, so allowing them silently under-reports
    // weight and lets waitForNetworkQuiet declare idle mid-download.
    allowServiceWorkers: false,
    // Cap on the negotiated Content-Encoding: 'br' (no cap, production-like) |
    // 'gzip' | 'identity'. Section 7 of the spec specifies "gzip on", so a run held
    // to that protocol passes --max-encoding gzip and genuinely serves gzip, rather
    // than serving brotli and labelling the bytes "gzip". Capping only inflates the
    // reported weight, never flatters it.
    maxEncoding: 'br',
    // Timing predicate timeout — the spec's ~10s.
    actionTimeoutMs: 10000,
    readyTimeoutMs: 60000,
    navTimeoutMs: 60000,
    quietMs: 2000,
    maxSettleMs: 30000,
  };
  const asInt = (v, name) => {
    const n = Number.parseInt(v, 10);
    if (!Number.isFinite(n)) throw new Error(`bench.mjs: ${name} expects an integer, got "${v}"`);
    return n;
  };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    const eq = a.indexOf('=');
    const key = eq > -1 ? a.slice(0, eq) : a;
    const inline = eq > -1 ? a.slice(eq + 1) : null;
    const next = () => (inline !== null ? inline : argv[++i]);
    switch (key) {
      case '--dir': o.dir = next(); break;
      case '--port': o.port = asInt(next(), '--port'); break;
      case '--host': o.host = next(); break;
      case '--app': o.app = next(); break;
      case '--label': o.label = next(); break;
      case '--route': o.route = next(); break;
      case '--runs': o.runs = asInt(next(), '--runs'); break;
      case '--warmup': o.warmup = asInt(next(), '--warmup'); break;
      case '--weight-runs': o.weightRuns = asInt(next(), '--weight-runs'); break;
      case '--out': o.out = next(); break;
      case '--headed': o.headless = false; break;
      case '--headless': o.headless = true; break;
      case '--aot': o.aot = inline === null ? true : inline !== 'false'; break;
      case '--no-aot': o.aot = false; break;
      case '--allow-service-workers': o.allowServiceWorkers = true; break;
      case '--max-encoding': o.maxEncoding = next(); break;
      case '--timeout': o.actionTimeoutMs = asInt(next(), '--timeout'); break;
      case '--quiet-ms': o.quietMs = asInt(next(), '--quiet-ms'); break;
      case '--max-settle-ms': o.maxSettleMs = asInt(next(), '--max-settle-ms'); break;
      case '--help': case '-h': o.help = true; break;
      default: throw new Error(`bench.mjs: unknown argument ${a}`);
    }
  }
  return o;
}

const USAGE = `Usage:
  node bench.mjs --dir <publishDir> --port <port> --app <counter|rows> --label <string> --runs 10

Required:
  --dir <path>        directory to serve (a publish output)
  --app <name>        counter | rows
  --label <string>    identifies this app+config; also the output filename

Optional:
  --port <n>          default 0 (ephemeral)
  --route <path>      app route, default /
  --runs <n>          timed iterations per scenario, default 10
  --warmup <n>        discarded iterations before the timed ones, default 0
  --weight-runs <n>   cold-load byte measurements, default 3
  --aot[=true|false]  record whether AOT was enabled for this build
  --headed            run Chrome headed (default: headless)
  --out <file>        output path, default bench/results/<label>.json
  --timeout <ms>      predicate timeout, default 10000
  --max-encoding <br|gzip|identity>
                      cap the negotiated Content-Encoding, default br (no cap,
                      production-like). Use gzip to hold a run to a "gzip on"
                      protocol and genuinely serve gzip rather than serving brotli
                      and calling the bytes gzip. Capping only increases reported
                      weight; it is recorded in config.maxEncoding.
  --allow-service-workers
                      permit service workers (DEFAULT: blocked). Bytes fetched from a
                      service-worker target are invisible to the page-scoped CDP
                      session, so this under-reports weight and lets the network read
                      as idle mid-download. Only use it if you intend to measure the
                      service worker itself, and read weight.untrackedRequests.
`;

export async function main(argv) {
  const opts = parseArgs(argv);
  if (opts.help) { process.stdout.write(USAGE); return 0; }

  const missing = ['dir', 'app', 'label'].filter((k) => !opts[k]);
  if (missing.length) {
    process.stderr.write(`bench.mjs: missing required argument(s): ${missing.map((m) => '--' + m).join(', ')}\n\n${USAGE}`);
    return 2;
  }
  if (!APPS[opts.app]) {
    process.stderr.write(`bench.mjs: --app must be one of ${Object.keys(APPS).join(' | ')}, got "${opts.app}"\n`);
    return 2;
  }
  if (opts.runs < 1) {
    process.stderr.write('bench.mjs: --runs must be >= 1\n');
    return 2;
  }

  const dir = path.resolve(opts.dir);
  const dirStat = await fsp.stat(dir).catch(() => null);
  if (!dirStat || !dirStat.isDirectory()) {
    process.stderr.write(`bench.mjs: --dir is not a directory: ${dir}\n`);
    return 2;
  }

  // Loaded before anything is launched: a missing/malformed fixture must stop the
  // run outright rather than let it proceed with the fairness gate disabled.
  const expectedLabels = opts.app === 'rows' ? await loadExpectedLabels() : null;

  if (!ENCODING_CEILINGS.includes(opts.maxEncoding)) {
    process.stderr.write(
      `bench.mjs: --max-encoding must be one of ${ENCODING_CEILINGS.join(' | ')}, got "${opts.maxEncoding}"\n`,
    );
    return 2;
  }

  const server = await startServer({
    dir, port: opts.port, host: opts.host, quiet: false, maxEncoding: opts.maxEncoding,
  });
  const url = server.url + (opts.route.startsWith('/') ? opts.route : '/' + opts.route);

  const browser = await chromium.launch({
    channel: 'chrome',
    headless: opts.headless,
    args: [
      // Chrome's own background chatter only. Nothing here alters JS execution,
      // rendering, or the network path of the app under test.
      '--disable-background-networking',
      '--disable-component-update',
      '--disable-sync',
      '--no-first-run',
      '--no-default-browser-check',
    ],
  });

  const startedAt = new Date().toISOString();
  let exitCode = 0;
  let result;

  try {
    const environment = await captureEnvironment(browser, opts);
    process.stderr.write(
      `\n[bench] ${opts.label} — app=${opts.app} url=${url}\n` +
      `[bench] chrome ${environment.chrome.version} (headless=${opts.headless}) | .NET SDK ${environment.dotnetSdk} | aot=${opts.aot}\n` +
      `[bench] serving ${dir}\n\n`,
    );

    // Gate: refuse to emit numbers from an app that does not meet the contract.
    // For the rows app this includes the deterministic label stream and the full
    // semantics of #update / #swaprows / #clear — all of which were previously
    // unverified, so an app doing strictly less work read as a performance win.
    const contract = await verifyContract(browser, url, opts.app, opts, expectedLabels);
    if (contract.problems.length) {
      process.stderr.write(
        `\n[bench] DOM CONTRACT NOT MET at ${url} — refusing to report numbers:\n` +
        contract.problems.map((p) => `  - ${p}\n`).join('') +
        `\n[bench] observed: ${JSON.stringify(contract.observed)}\n` +
        '[bench] The app must expose: #tbody with <tr><td>{id}</td><td>{label}</td>...,\n' +
        '[bench] plus #run, #update, #swaprows, #clear (rows) or #increment + #counter-value (counter).\n',
      );
      return 3;
    }
    process.stderr.write(`[bench] DOM contract OK: ${JSON.stringify(contract.observed)}\n\n`);

    const weight = await measureWeight(browser, url, opts.app, opts, server);

    const scenarios = {};
    for (const scenario of APPS[opts.app].scenarios) {
      // eslint-disable-next-line no-await-in-loop
      scenarios[scenario] = await runScenario(browser, url, opts.app, scenario, opts);
    }

    const anyFailed = Object.values(scenarios).some((s) => s.status !== 'ok');

    result = {
      schemaVersion: SCHEMA_VERSION,
      label: opts.label,
      app: opts.app,
      url,
      servedDir: dir,
      startedAt,
      finishedAt: new Date().toISOString(),
      ok: !anyFailed,
      contractCheck: contract,
      // The fixture that defines "the same workload". Recorded so a later edit to
      // expected-labels.json is visible in the output rather than silently
      // redefining what every framework is being held to.
      expectedLabels: expectedLabels
        ? { path: path.relative(HARNESS_DIR, expectedLabels.path), sha256: expectedLabels.sha256 }
        : null,
      environment,
      config: {
        runs: opts.runs,
        warmup: opts.warmup,
        weightRuns: opts.weightRuns,
        actionTimeoutMs: opts.actionTimeoutMs,
        networkQuietMs: opts.quietMs,
        maxSettleMs: opts.maxSettleMs,
        serviceWorkers: opts.allowServiceWorkers ? 'allow' : 'block',
        maxEncoding: opts.maxEncoding,
        encodingNote:
          opts.maxEncoding === 'br'
            ? 'No encoding cap: brotli is served to any client that accepts it (production-like).'
            : `Negotiation CAPPED at "${opts.maxEncoding}" via --max-encoding, so the transferred bytes are ` +
              `${opts.maxEncoding} bytes and are labelled as such. Chrome accepts brotli, and an uncapped run ` +
              'would transfer measurably fewer bytes; this cap therefore makes the weight a CONSERVATIVE ' +
              '(upper-bound) figure. Any framework compared against it must be measured under the same cap.',
        compression:
          'Content negotiated per request, smallest encoding the client accepts: brotli before gzip. ' +
          'Precompressed sibling (.br/.gz) when present, else on-the-fly at publish-equivalent quality ' +
          '(brotli q11 / gzip level 9). Eligibility is a denylist of already-compressed formats, so an ' +
          'unanticipated extension is compressed rather than silently shipped raw.',
      },
      protocol: {
        weight:
          'Transferred bytes = sum of CDP Network.loadingFinished.encodedDataLength (falling back to ' +
          'Network.responseReceived header bytes + summed Network.dataReceived.encodedDataLength when ' +
          'loadingFinished reports 0) over every request, from navigation until the app-ready contract ' +
          'hook is clickable AND the network has been idle for networkQuietMs. This is wire bytes, not ' +
          'disk size and not decompressed size. Cold cache is enforced three ways: a fresh BrowserContext ' +
          'per iteration, CDP Network.setCacheDisabled, and Cache-Control: no-store from the server.',
        timing:
          'All timestamps are taken in-page. A click is followed by a MutationObserver-driven wait for a DOM ' +
          'predicate; the clock stops INSIDE the observer callback at the instant the predicate is observed ' +
          'true (msToMutation, the headline metric). Frame boundaries are never used as a proxy for ' +
          'completion, so an asynchronously dispatched handler (e.g. Blazor @onclick) cannot produce a ' +
          'fabricated sub-millisecond time. msToPaint additionally waits one requestAnimationFrame + ' +
          'setTimeout(0); because the predicate resolves at an arbitrary phase of the vsync cycle, msToPaint ' +
          'carries a 0-16.7ms offset contributed by the display and the harness rather than the framework, ' +
          'and is reported separately so it can never be mistaken for framework cost. A predicate already ' +
          'true before the click is rejected as vacuous. A predicate that does not become true within ' +
          'actionTimeoutMs throws and is recorded as a failure, never as a number. An iteration whose ' +
          'network has not gone idle is a failure too, not a silently inflated sample.',
        predicates:
          'Scenario predicates are TOTAL: #update requires all 100 targeted rows to carry the suffix and ' +
          'sampled non-targeted rows to lack it; #swaprows requires BOTH index 1 and index 998 to hold the ' +
          "other's original id. A partial implementation times out as a failure rather than stopping the " +
          'clock early and reporting the shortfall as speed.',
        workloadEquality:
          'verifyContract asserts the deterministic Park-Miller label stream (expected-labels.json, hash ' +
          'recorded above) against rows 0-4 and 999, and exercises #update, #swaprows and #clear untimed, ' +
          'asserting their full post-conditions. An app that emits constant labels, updates fewer rows, or ' +
          'duplicates instead of swapping is refused with exit code 3 rather than reported as fast.',
        statistics:
          'MEDIAN and IQR (p75 - p25) over `runs` iterations, using linear-interpolation quantiles ' +
          '(type 7, the numpy/R default). The mean is never reported.',
        agnosticism:
          'This harness contains no framework-specific code path. Only --dir/--route change between ' +
          'frameworks; every app is driven exclusively through the shared DOM contract.',
      },
      weight,
      scenarios,
    };

    const outPath = opts.out
      ? path.resolve(opts.out)
      : path.resolve(
          // fileURLToPath, not URL.pathname: the latter percent-encodes spaces and
          // would silently write to a "My%20Repos" directory.
          path.dirname(fileURLToPath(import.meta.url)),
          '..',
          'results',
          `${String(opts.label).replace(/[^a-zA-Z0-9._-]+/g, '-')}.json`,
        );
    await fsp.mkdir(path.dirname(outPath), { recursive: true });
    await fsp.writeFile(outPath, JSON.stringify(result, null, 2) + '\n', 'utf8');

    process.stderr.write(`\n[bench] wrote ${outPath}\n`);
    process.stderr.write(`[bench] transferred to interactive (median): ${weight.toInteractive.median} bytes\n`);
    for (const [name, s] of Object.entries(scenarios)) {
      process.stderr.write(
        `[bench] ${name.padEnd(10)} ${s.status === 'ok' ? '' : '[' + s.status.toUpperCase() + '] '}` +
        `median=${s.median} ms  iqr=${s.iqr} ms  (n=${s.n}, failures=${s.failureCount})` +
        `   [toPaint median=${s.toPaint.median} ms iqr=${s.toPaint.iqr} ms — includes the harness's vsync wait]\n`,
      );
    }
    for (const w of weight.warnings) process.stderr.write(`[bench] WARNING: ${w}\n`);

    if (anyFailed) exitCode = 1;
  } finally {
    await browser.close().catch(() => {});
    await server.close().catch(() => {});
  }

  return exitCode;
}

const isMain = process.argv[1] && pathToFileURL(process.argv[1]).href === import.meta.url;
if (isMain) {
  try {
    process.exitCode = await main(process.argv.slice(2));
  } catch (err) {
    process.stderr.write(`bench.mjs: ${(err && err.stack) || err}\n`);
    process.exitCode = 1;
  }
}

export { APPS, attachNetworkTracker, inPageHarness, waitForNetworkQuiet, UPDATE_INDICES, UPDATE_NOT_INDICES };
