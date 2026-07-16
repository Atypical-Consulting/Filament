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
 *   warm variants simply make create/increment consistent with them. Every warm
 *   scenario — all four — is separated from its timed click by the same idle beat
 *   (see settleBeat), so "consistent with them" is a property of the code and not
 *   just of this comment. Both numbers are reported. Neither is deleted: cold is a
 *   real user-visible cost, it just is not a rendering measurement.
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

export const HARNESS_VERSION = '1.3.0';

// ---------------------------------------------------------------------------
// Harness identity.
//
// WHY THIS EXISTS. HARNESS_VERSION was hand-maintained and stayed '1.2.0' across a
// 701-line change to this file that added the C3 instruments and the strict row-markup
// check. Two results therefore both claimed "1.2.0" while having been produced by
// materially different harnesses, and the version string in the JSON asserted they were
// comparable when they were not. Nothing detected it, because nothing could: a human has
// to remember to bump a constant, and the one time it mattered, nobody did.
//
// A hash cannot be forgotten. It is computed from the bytes that actually ran, every run,
// so two results are comparable if and only if their hashes match — a property a reader
// can CHECK rather than trust. HARNESS_VERSION stays for human legibility, but it is the
// claim; the hash is the evidence.
//
// SCOPE. The files whose bytes decide what a number MEANS:
//   bench.mjs          - the driver, the timing primitive, and inPageHarness() (the
//                        in-page harness is a function in this file, so its bytes are
//                        covered here; there is no separate in-page asset to miss).
//   server.mjs         - negotiation, compression levels, cache headers. Changing brotli
//                        quality changes every weight number without touching bench.mjs.
//   expected-labels.json - the golden fixture defines the workload every framework is
//                        held to. Widening the gate changes what "the same work" means.
//
// Deliberately EXCLUDED: selftest.mjs (never on the measurement path) and package*.json
// (the Playwright/Chrome versions that matter are already observed into `environment`).
// ---------------------------------------------------------------------------
export const HARNESS_SOURCE_FILES = ['bench.mjs', 'server.mjs', 'expected-labels.json'];

/**
 * Content hash of the harness sources.
 *
 * Per-file digests are reported alongside the aggregate so a mismatch is diagnosable
 * ("server.mjs moved, bench.mjs didn't") instead of merely detectable. The aggregate is
 * taken over `name:sha256` lines sorted by name, so it depends on the files' content and
 * nothing else — not on absolute paths, not on read order, not on the machine.
 *
 * Throws rather than degrading. A run that cannot establish its own identity must stop:
 * a result carrying a null hash is exactly the unfalsifiable claim this replaces.
 */
export async function computeHarnessIdentity(dir = HARNESS_DIR) {
  const files = {};
  for (const name of [...HARNESS_SOURCE_FILES].sort()) {
    let bytes;
    try {
      bytes = await fsp.readFile(path.join(dir, name));
    } catch (e) {
      throw new Error(
        `bench.mjs: cannot read harness source ${name} at ${path.join(dir, name)} (${e.code || e.message}). ` +
        'The harness content hash gates cross-result comparability and must never be skipped.',
      );
    }
    files[name] = crypto.createHash('sha256').update(bytes).digest('hex');
  }
  const canonical = Object.keys(files)
    .sort()
    .map((n) => `${n}:${files[n]}`)
    .join('\n');
  return {
    version: HARNESS_VERSION,
    sha256: crypto.createHash('sha256').update(canonical).digest('hex'),
    files,
    note:
      'sha256 is taken over the sorted "name:sha256" lines of the files in `files`. Two results are ' +
      'like-for-like comparable IFF this value matches. HARNESS_VERSION is hand-maintained and has ' +
      'already been observed to go stale across a breaking harness change; it is a label, not evidence. ' +
      'Compare the hash.',
  };
}

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
//
// BOTH RUNS ARE GATED. The LCG is seeded once per page load, so the second #run on a
// page emits a different 1000-label stream from the first. create-cold times the
// first; create-warm — C4's HEADLINE — times the second. A fixture describing only
// the first run therefore gated only the non-headline number, and left the headline
// click gated by one predicate: "#tbody has 1000 children". The cheat that walks
// through that hole is small and entirely natural-looking:
//
//     let labels = null;
//     function labelsFor() {
//       if (!labels) labels = Array.from({ length: 1000 }, nextLabel);
//       return labels;               // every #run after the first: zero LCG work
//     }
//
// The first #run runs the real LCG, so a first-run-only gate passes byte-exactly and
// exits 0 — while the timed run does none of the 3000 multiply/modulo ops and none of
// the 1000 three-part concatenations, and posts a create-warm "win" on strictly less
// work. So the fixture carries the second run's stream and its ids too, and
// verifyContract drives #run ... #clear -> #run and asserts them.
// ---------------------------------------------------------------------------
const HARNESS_DIR = path.dirname(fileURLToPath(import.meta.url));
export const EXPECTED_LABELS_PATH = path.join(HARNESS_DIR, 'expected-labels.json');

/** Validate one {first5, row1000} stream description. */
function validateStream(obj, file, where) {
  if (!obj || typeof obj !== 'object') {
    throw new Error(`bench.mjs: ${file} must contain ${where} as an object`);
  }
  if (!Array.isArray(obj.first5) || obj.first5.length !== 5 || !obj.first5.every((s) => typeof s === 'string')) {
    throw new Error(`bench.mjs: ${file} must contain ${where}.first5 as an array of exactly 5 strings`);
  }
  if (typeof obj.row1000 !== 'string') {
    throw new Error(`bench.mjs: ${file} must contain ${where}.row1000 as a string`);
  }
  return { first5: obj.first5, row1000: obj.row1000 };
}

/** A row id in the fixture must be a decimal integer string — ids are monotonic counters. */
function validateId(v, file, where) {
  if (typeof v !== 'string' || !/^[0-9]+$/.test(v)) {
    throw new Error(`bench.mjs: ${file} must contain ${where} as a decimal integer string (e.g. "1")`);
  }
  return v;
}

/**
 * Load and validate the golden fixture. Throws rather than degrading: a missing or
 * malformed fixture must stop the run, never silently disable the fairness gate.
 *
 * TWO streams are required, because two #run clicks on one page emit two different
 * streams and BOTH are timed by a reported scenario (create-cold times the first,
 * create-warm — the headline — times the second). A fixture that described only the
 * first run would leave the headline click gated by nothing but "1000 rows exist".
 *
 * The two streams are cross-checked against each other here: if they were equal, the
 * second-run assertion would be satisfied by an app that computed the stream once and
 * reused it, which is precisely the cheat the fixture exists to refuse. Same for the
 * ids — equal first ids would mean the id counter had been reset.
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
  const firstRun = validateStream(parsed, file, '"first5"/"row1000"');
  const secondRun = validateStream(parsed.secondRun, file, '"secondRun"');
  const firstRunFirstId = validateId(parsed.firstRunFirstId, file, '"firstRunFirstId"');
  const secondRunFirstId = validateId(parsed.secondRunFirstId, file, '"secondRunFirstId"');

  const sameStream =
    firstRun.row1000 === secondRun.row1000 &&
    firstRun.first5.every((s, i) => s === secondRun.first5[i]);
  if (sameStream) {
    throw new Error(
      `bench.mjs: ${file} describes the SAME label stream for the first and second #run. The LCG is ` +
      'seeded once per page load and never reset, so the second run must emit draws 3001..6000 — a ' +
      'different stream. As written this fixture would rubber-stamp an app that generates the stream ' +
      'once and reuses the interned strings on every later #run, which is exactly the workload ' +
      'inequality the fixture exists to refuse.',
    );
  }
  if (firstRunFirstId === secondRunFirstId) {
    throw new Error(
      `bench.mjs: ${file} gives the first and second #run the same starting id (${firstRunFirstId}). ` +
      'Row ids are monotonic and never reset, so the second run must start where the first ended + 1.',
    );
  }

  return {
    first5: firstRun.first5,
    row1000: firstRun.row1000,
    firstRunFirstId,
    secondRun,
    secondRunFirstId,
    // Recorded in the result JSON so a later fixture edit is visible in the output
    // rather than silently redefining what "correct" means. Widening the gate to the
    // second run necessarily changes this hash, which is the point: a result carrying
    // the old hash was produced by the narrower gate.
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
 * The untimed setup steps, named once.
 *
 * SCENARIO_SPECS declares a scenario's setup from these, runScenarioIteration records
 * what it actually ran from these, and assertSetupMatchesSpec compares the two before
 * every timed click. Sharing the strings is what makes that comparison meaningful:
 * re-typed prose would drift into agreement-by-coincidence or disagreement-by-typo.
 */
const SETUP_STEP = {
  run: '#run (untimed, to 1000 rows)',
  clear: '#clear (untimed, to 0 rows)',
  increment: '#increment (untimed, 0 -> 1)',
  // Two chained rAFs + a >= idleBeatMs macrotask. Every warm scenario gets one
  // immediately before its timed click; see settleBeat().
  beat: 'idle beat',
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
    setup: [SETUP_STEP.run, SETUP_STEP.beat, SETUP_STEP.clear, SETUP_STEP.beat],
    measures:
      'Fresh page load; #run then #clear run UNTIMED so the runtime is booted and the row path is ' +
      'exercised; then an identical #run is TIMED. This is the row-building cost with the boot term ' +
      'removed, and it is consistent with update/swap/clear, which have always had an untimed #run ' +
      'setup and which are separated from their timed click by the same idle beat. Per the DOM ' +
      'contract row ids are monotonic and never reset and the label LCG is seeded ' +
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
    setup: [SETUP_STEP.run, SETUP_STEP.beat],
    measures:
      'Appending " !!!" to every 10th row label, on an already-booted runtime whose untimed #run has ' +
      'been through style, layout and paint and had its post-render bookkeeping drained.',
  },
  swap: {
    runtime: 'warm',
    headline: true,
    button: '#swaprows',
    setup: [SETUP_STEP.run, SETUP_STEP.beat],
    measures:
      'Reciprocally swapping rows 1 and 998, on an already-booted runtime whose untimed #run has been ' +
      'through style, layout and paint and had its post-render bookkeeping drained.',
  },
  clear: {
    runtime: 'warm',
    headline: true,
    button: '#clear',
    setup: [SETUP_STEP.run, SETUP_STEP.beat],
    measures:
      'Removing all 1000 rows, on an already-booted runtime whose untimed #run has been through style, ' +
      'layout and paint and had its post-render bookkeeping drained.',
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
    setup: [SETUP_STEP.increment, SETUP_STEP.beat],
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

  // Row cell accessors, used by the TIMING predicates. Deliberately loose
  // textContent lookups: a predicate's job is to stop the clock the instant the
  // post-condition holds, and it must not also be a markup validator.
  //
  // The markup IS validated, exactly once per run and strictly, by
  // checkRowMarkup() below. Keeping the two separate is the point — see the
  // comment there for why "loose predicate + strict one-shot gate" is the right
  // split rather than an inconsistency.
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

  // -------------------------------------------------------------------------
  // STRICT row-markup contract check.
  //
  // The gate this replaces asked only for `cellsPerRow >= 2` and then read
  // `td:nth-child(n).textContent`. An auditor found the hole: that admits ANY
  // row with two-or-more cells whose text happens to match. Filament could emit
  //
  //     <tr><td>1</td><td>adorable pink desk</td></tr>
  //
  // — no classes, no nested <a> — pass every gate, and post a create-warm win
  // against Blazor's markup. Blazor builds 1000 <a class="lbl"> elements and
  // 2000 class attributes that Filament would simply not have built. That is
  // ~3000 fewer DOM nodes/attributes per #run: a large, invisible discount on
  // the exact number C4 is decided by.
  //
  // So the markup is asserted EXACTLY, against the shared contract:
  //
  //   <tr><td class="col-md-1">{id}</td><td class="col-md-4"><a class="lbl">{label}</a></td></tr>
  //
  // Verified against the published Blazor apps before being written: their rows
  // are byte-identical to the line above — exactly two <td> element children,
  // no stray text nodes, no Razor comment markers. (RowsApp.razor keeps the row
  // on ONE line precisely to avoid emitting whitespace text nodes; this check is
  // what makes that discipline load-bearing instead of decorative.) If this ever
  // fails on Blazor, the check is wrong, not Blazor.
  //
  // Note the asymmetry this closes runs one way only: extra nodes are MORE work,
  // never less. This check refuses them anyway, because "exactly" is what the
  // contract says and a row with extra cells is not the row both frameworks
  // agreed to render.
  // -------------------------------------------------------------------------
  const ROW_MARKUP_CONTRACT =
    '<tr><td class="col-md-1">{id}</td><td class="col-md-4"><a class="lbl">{label}</a></td></tr>';

  function describeNode(n) {
    if (n.nodeType === 1) {
      return '<' + n.tagName.toLowerCase() + (n.className ? ' class="' + n.className + '"' : '') + '>';
    }
    if (n.nodeType === 3) return '#text ' + JSON.stringify(n.nodeValue);
    if (n.nodeType === 8) return '#comment ' + JSON.stringify(n.nodeValue);
    return '#node(' + n.nodeType + ')';
  }

  /**
   * Returns an array of problem strings — empty means the markup conforms.
   * Checks a SAMPLE of row indices rather than row 0 alone: an app that builds
   * the contract's markup for the first row and something cheaper for the other
   * 999 is precisely the cheat worth catching, and it costs nothing to look.
   */
  function checkRowMarkup(indices) {
    const problems = [];
    const t = tbody();
    if (!t) return ['#tbody not found'];

    for (const i of indices) {
      const row = t.children[i];
      if (!row) { problems.push(`row ${i}: does not exist`); continue; }
      const at = (msg) => `row ${i}: ${msg}`;

      if (row.tagName !== 'TR') {
        problems.push(at(`is <${row.tagName.toLowerCase()}>; the contract requires <tr>`));
        continue;
      }
      // childNodes, not just children: a text node or a framework comment marker
      // between the two <td>s is a real node the other framework did not emit.
      if (row.children.length !== 2 || row.childNodes.length !== 2) {
        problems.push(at(
          `has ${row.children.length} element child(ren) / ${row.childNodes.length} child node(s) ` +
          `[${Array.prototype.map.call(row.childNodes, describeNode).join(', ')}]; the contract is ` +
          'EXACTLY two <td> elements and nothing else',
        ));
        continue;
      }

      const td1 = row.children[0];
      const td2 = row.children[1];

      if (td1.tagName !== 'TD') {
        problems.push(at(`cell 1 is <${td1.tagName.toLowerCase()}>; the contract requires <td>`));
      } else {
        if (td1.className !== 'col-md-1') {
          problems.push(at(`cell 1 has class ${JSON.stringify(td1.className)}; the contract requires "col-md-1"`));
        }
        if (td1.children.length !== 0) {
          problems.push(at(
            `cell 1 contains ${td1.children.length} element(s) ` +
            `[${Array.prototype.map.call(td1.children, describeNode).join(', ')}]; the contract puts the ` +
            'id in a bare text node',
          ));
        }
      }

      if (td2.tagName !== 'TD') {
        problems.push(at(`cell 2 is <${td2.tagName.toLowerCase()}>; the contract requires <td>`));
        continue;
      }
      if (td2.className !== 'col-md-4') {
        problems.push(at(`cell 2 has class ${JSON.stringify(td2.className)}; the contract requires "col-md-4"`));
      }
      if (td2.children.length !== 1 || td2.childNodes.length !== 1) {
        problems.push(at(
          `cell 2 has ${td2.children.length} element child(ren) / ${td2.childNodes.length} child node(s) ` +
          `[${Array.prototype.map.call(td2.childNodes, describeNode).join(', ')}]; the contract requires ` +
          'EXACTLY one <a class="lbl"> holding the label, and nothing else. A label written straight into ' +
          'the <td> skips 1000 <a> elements per #run that the other framework builds.',
        ));
        continue;
      }
      const a = td2.children[0];
      if (a.tagName !== 'A') {
        problems.push(at(`cell 2 wraps the label in <${a.tagName.toLowerCase()}>; the contract requires <a>`));
      } else if (a.className !== 'lbl') {
        problems.push(at(`cell 2's <a> has class ${JSON.stringify(a.className)}; the contract requires "lbl"`));
      }
      if (a.children && a.children.length !== 0) {
        problems.push(at(`cell 2's <a> contains ${a.children.length} element(s); the contract requires text only`));
      }
    }
    return problems;
  }

  // -------------------------------------------------------------------------
  // C3 INSTRUMENT (a): framework-agnostic DOM-write counter.
  //
  // C3 asks for "exactly 1 DOM write per counter increment, 0 render-tree
  // allocation, VERIFIED BY INSTRUMENTATION". A runtime-reported counter
  // (`__filament.stats`) cannot verify that: it is the defendant's own account
  // of its own conduct, it cannot be wrong in any way the runtime does not
  // already know about, and — decisively — it does not exist on Blazor, so it
  // cannot say whether 1 write is good, ordinary, or worse than the baseline.
  //
  // A MutationObserver is the independent instrument. It is the browser's own
  // record of what happened to the DOM, it runs byte-identically on Blazor and
  // on Filament, and it turns C3 from a self-report into a COMPARISON.
  //
  // ROOT: document.body, subtree:true. Deliberately the widest root available
  // rather than #app or #counter-value. A narrow root is a way to not-see
  // writes, and the framework under test must not get to pick where we look. A
  // wider root can only ever count MORE, never less.
  //
  // "WRITE" is defined and reported two ways, because MutationRecord count and
  // DOM-write count are not the same number and conflating them would be its own
  // fabrication: one childList record can carry 50 addedNodes. So:
  //   records — MutationRecord objects delivered.
  //   writes  — childList: addedNodes + removedNodes; characterData: 1;
  //             attributes: 1. This is the C3 number.
  //
  // SETTLE: after the predicate holds we wait a rAF + a macrotask before taking
  // the final records. A framework that writes the value and then tidies up
  // afterwards has done those writes too, and stopping at the predicate would
  // hide exactly the writes C3 is about.
  // -------------------------------------------------------------------------
  function readFilamentStats() {
    const f = window.__filament;
    if (!f || typeof f !== 'object' || !f.stats || typeof f.stats !== 'object') return null;
    const s = f.stats;
    const out = { present: true, domWrites: null, raw: {} };
    for (const k of Object.keys(s)) {
      if (typeof s[k] === 'number') out.raw[k] = s[k];
    }
    if (typeof s.domWrites === 'number') out.domWrites = s.domWrites;
    return out;
  }

  async function countDomWrites(btnSelector, predSpec, rootSelector, timeoutMs, settleMs) {
    const predicate = makePredicate(predSpec);
    const btn = document.querySelector(btnSelector);
    if (!btn) throw new Error('button not found: ' + btnSelector);
    const root = document.querySelector(rootSelector);
    if (!root) throw new Error('DOM-write observe root not found: ' + rootSelector);
    if (predicate()) {
      throw new Error(
        'predicate already satisfied before click (' + predSpec.kind + '); refusing to report a vacuous 0 writes',
      );
    }

    const records = [];
    const mo = new MutationObserver((rs) => { for (const r of rs) records.push(r); });
    mo.observe(root, { childList: true, subtree: true, characterData: true, attributes: true });

    const statsBefore = readFilamentStats();
    let timedOut = false;
    try {
      btn.click();
      await waitForCondition(predicate, rootSelector, timeoutMs);
    } catch (e) {
      timedOut = true;
    }
    // Catch trailing writes the framework makes after the post-condition holds.
    await new Promise((r) => requestAnimationFrame(() => setTimeout(r, settleMs)));
    for (const r of mo.takeRecords()) records.push(r);
    mo.disconnect();
    const statsAfter = readFilamentStats();

    let writes = 0;
    const byType = { childList: 0, characterData: 0, attributes: 0 };
    const detail = [];
    for (const r of records) {
      byType[r.type] = (byType[r.type] || 0) + 1;
      const w = r.type === 'childList' ? (r.addedNodes.length + r.removedNodes.length) : 1;
      writes += w;
      if (detail.length < 25) {
        const t = r.target;
        detail.push({
          type: r.type,
          target: t.nodeType === 1
            ? t.tagName.toLowerCase() + (t.id ? '#' + t.id : '')
            : '#text in <' + (t.parentNode && t.parentNode.tagName
              ? t.parentNode.tagName.toLowerCase() + (t.parentNode.id ? '#' + t.parentNode.id : '')
              : '?') + '>',
          attributeName: r.attributeName || null,
          added: r.addedNodes.length,
          removed: r.removedNodes.length,
        });
      }
    }

    return { records: records.length, writes, byType, detail, timedOut, statsBefore, statsAfter };
  }

  /**
   * Drives N increments back-to-back, awaiting each one's DOM effect. Used by the
   * allocation probe, where the per-increment signal is only separable from noise
   * across many iterations.
   *
   * Deliberately allocation-frugal: no per-iteration closures beyond the one
   * Promise the await needs, no array building, no string templates. Whatever this
   * loop allocates is charged to the framework's number, so it is kept small and —
   * more importantly — kept IDENTICAL for every framework, which is what makes the
   * comparison survive the offset.
   */
  async function driveIncrements(n, timeoutMs) {
    const el = document.querySelector('#counter-value');
    const btn = document.querySelector('#increment');
    if (!el || !btn) throw new Error('counter app not found (#counter-value / #increment)');
    for (let i = 0; i < n; i++) {
      const want = String(Number(el.textContent.trim()) + 1);
      btn.click();
      // eslint-disable-next-line no-await-in-loop
      await new Promise((resolve, reject) => {
        const t0 = performance.now();
        const tick = () => {
          if (el.textContent.trim() === want) { resolve(); return; }
          if (performance.now() - t0 > timeoutMs) {
            reject(new Error('driveIncrements: increment ' + i + ' did not land within ' + timeoutMs + 'ms'));
            return;
          }
          setTimeout(tick, 0);
        };
        tick();
      });
    }
    return el.textContent.trim();
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
    checkRowMarkup,
    countDomWrites,
    readFilamentStats,
    driveIncrements,
    contract: { ROW_ID_CELL, ROW_LABEL_CELL, ROW_MARKUP_CONTRACT },
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
//
// ORDER: #run -> #update -> #swaprows -> #clear -> #run. The trailing
// #clear -> #run is the sequence create-warm TIMES, and the second #run is asserted
// against the second-run stream and ids 1001..2000 — so the headline scenario's click
// is gated on the same terms as everything else. #update/#swaprows sit in the middle
// rather than after, because they neither draw from the LCG nor touch the id counter:
// the state the second #run starts from is byte-identical either way, and running
// them first keeps the gate's coverage of state-dependent breakage (a #clear that
// only works once #swaprows has run is refused here rather than surviving to the
// stopwatch).
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

      // ---- STRICT row markup (fairness gate) --------------------------------
      // Sampled across the run, not just row 0: "correct first row, cheaper other
      // 999" is a cheat worth the five extra lookups. 998/999 are included because
      // #swaprows and the label oracle both read the tail.
      const MARKUP_INDICES = [0, 1, 2, 500, 998, 999];
      const markupProblems = B.checkRowMarkup(MARKUP_INDICES);
      out.observed.rowMarkup = {
        contract: B.contract.ROW_MARKUP_CONTRACT,
        checkedIndices: MARKUP_INDICES,
        row0outerHTML: tb.children[0] ? tb.children[0].outerHTML : null,
        conforms: markupProblems.length === 0,
      };
      if (markupProblems.length) {
        out.problems.push(
          `the row markup does not match the shared DOM contract. Required, EXACTLY:\n    ` +
          `${B.contract.ROW_MARKUP_CONTRACT}\n  Observed at row 0:\n    ` +
          `${out.observed.rowMarkup.row0outerHTML}\n  Violations:\n    ` +
          `${markupProblems.join('\n    ')}\n  Both frameworks must render byte-equivalent DOM or the ` +
          'comparison is void: every element and attribute one framework skips is work it is not doing ' +
          'and the other one is.',
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
      const checkStream = (which, stream, firstId) => {
        const first5 = [0, 1, 2, 3, 4].map((i) => B.rowLabel(i));
        const row1000 = B.rowLabel(999);
        for (let i = 0; i < 5; i++) {
          if (first5[i] !== stream.first5[i]) {
            out.problems.push(
              `on the ${which} #run, row ${i} label is ${JSON.stringify(first5[i])} but the deterministic ` +
              `sequence requires ${JSON.stringify(stream.first5[i])}. Every framework MUST run the ` +
              'Park-Miller LCG (seed 42.0, three draws per row: adjective/colour/noun) and emit the ' +
              'byte-identical stream. A different stream means a different amount of work, so the timings ' +
              'are not comparable.',
            );
          }
        }
        if (row1000 !== stream.row1000) {
          out.problems.push(
            `on the ${which} #run, row 999 (the 1000th) label is ${JSON.stringify(row1000)} but the ` +
            `deterministic sequence requires ${JSON.stringify(stream.row1000)}. The LCG must advance exactly ` +
            '3 draws per row across all 1000 rows.',
          );
        }
        // A single constant label satisfies "first5[0] matches" only by luck, but it
        // would fail the rest. Catch it explicitly for a clearer diagnostic.
        if (new Set(first5).size === 1 && first5[0] !== null) {
          out.problems.push(
            `on the ${which} #run, the first 5 rows all share the label ${JSON.stringify(first5[0])}; the ` +
            'label generator is constant, not the required pseudo-random stream',
          );
        }
        // Ids are a monotonic counter that is never reset, so the whole run is
        // checkable from its first id. Checked in full: an app that emits the right
        // id at index 0 and 999 but garbage between them has not built the rows the
        // contract describes.
        const base = Number(firstId);
        let idProblem = null;
        for (let i = 0; i < 1000 && !idProblem; i++) {
          const want = String(base + i);
          const got = B.rowId(i);
          if (got !== want) idProblem = { index: i, want, got };
        }
        if (idProblem) {
          out.problems.push(
            `on the ${which} #run, row ${idProblem.index} has id ${JSON.stringify(idProblem.got)} but the ` +
            `contract requires ${JSON.stringify(idProblem.want)}: ids are a monotonic counter starting at ` +
            `${firstId} for this run and are NEVER reset.`,
          );
        }
        return { first5, row1000, firstId: B.rowId(0), lastId: B.rowId(999) };
      };

      const run1 = checkStream('first', expected.firstRun, expected.firstRunFirstId);
      out.observed.first5 = run1.first5;
      out.observed.row1000 = run1.row1000;
      out.observed.firstRunFirstId = run1.firstId;
      out.observed.firstRunLastId = run1.lastId;
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
      if (out.problems.length) return out;

      // ---- SECOND #run: the click create-warm actually times -----------------
      // Everything above gates the FIRST #run, which only create-cold times — and
      // create-cold is explicitly not the headline. This gates the headline: the
      // tbody is empty, the runtime is warm, and the next #run is the one whose
      // median decides C4. Its stream is draws 3001..6000 and its ids are
      // 1001..2000, both of which an app that caches or hoists its labels cannot
      // produce — while a first-run-only gate would have waved it straight through.
      try {
        await B.measure('#run', { kind: 'rowCount', args: { count: 1000 } }, o, t);
      } catch (e) {
        out.problems.push(
          `the second #run (the click create-warm times) did not produce exactly 1000 rows within ${t}ms: ${e.message}`,
        );
        out.observed.secondRunRowCount = B.rowCount();
        return out;
      }
      out.observed.secondRunRowCount = B.rowCount();
      const run2 = checkStream('second', expected.secondRun, expected.secondRunFirstId);
      out.observed.secondRunFirst5 = run2.first5;
      out.observed.secondRunRow1000 = run2.row1000;
      out.observed.secondRunFirstId = run2.firstId;
      out.observed.secondRunLastId = run2.lastId;
      return out;
    }, [obs, T,
        {
          firstRun: { first5: expectedLabels.first5, row1000: expectedLabels.row1000 },
          firstRunFirstId: expectedLabels.firstRunFirstId,
          secondRun: expectedLabels.secondRun,
          secondRunFirstId: expectedLabels.secondRunFirstId,
        },
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
 * Refuse to start the clock unless the setup that actually ran is the setup the JSON
 * says ran.
 *
 * `untimedSetup` is not documentation — it is the harness's public claim about what
 * the number means, and the reader's only way to know a "warm" number was measured on
 * a warm runtime that had settled. A spec table and a hand-written switch statement
 * drift silently: the beat was once applied to two warm scenarios and declared for
 * two, while the other three warm scenarios were neither beaten nor claimed to be,
 * and nothing anywhere could notice. Comparing the two on every single iteration
 * makes that class of drift a loud failure instead of a quietly wrong median.
 */
function assertSetupMatchesSpec(scenario, declared, executed) {
  const same = declared.length === executed.length && declared.every((s, i) => s === executed[i]);
  if (!same) {
    throw new Error(
      `harness bug: scenario "${scenario}" executed the untimed setup ` +
      `[${executed.join(' -> ') || '(none)'}] but SCENARIO_SPECS declares ` +
      `[${declared.join(' -> ') || '(none)'}], which is what this run publishes as ` +
      'scenarios.' + scenario + '.untimedSetup. Refusing to time a click whose setup the result ' +
      'JSON would describe incorrectly.',
    );
  }
}

/**
 * One timed iteration. Every iteration starts from a fresh page load.
 * Returns milliseconds, or throws (which the caller records as a FAILURE).
 */
async function runScenarioIteration(ctx, app, scenario, url, opts) {
  const { page } = ctx;
  const obs = APPS[app].observeSelector;
  const T = opts.actionTimeoutMs;
  const spec = SCENARIO_SPECS[scenario];
  if (!spec) throw new Error(`unknown scenario: ${scenario}`);

  await loadAndSettleStrict(ctx, url, app, opts);

  // What the untimed setup ACTUALLY did, accumulated as it happens and checked
  // against SCENARIO_SPECS before the clock starts. See assertSetupMatchesSpec.
  const executed = [];

  /** Start the clock. Only reachable once the executed setup matches the declared one. */
  const timed = (button, predicate) => {
    assertSetupMatchesSpec(scenario, spec.setup, executed);
    return page.evaluate(
      ([btn, pred, o, t]) => window.__FILAMENT_BENCH__.measure(btn, pred, o, t),
      [button, predicate, obs, T],
    );
  };

  /** The settle beat, recorded. Every warm scenario gets one before its timed click. */
  const beat = async () => {
    await idleBeat(page, opts);
    executed.push(SETUP_STEP.beat);
  };

  // Untimed setup: create the 1000 rows and let any interaction-triggered lazy
  // loading finish, so update/swap/clear measure DOM work rather than network.
  //
  // The idle beat is part of the SETUP, not part of any one scenario. Every warm
  // scenario funnels through here, so all four are separated from their timed click
  // by the same barrier. It used to be applied only to the two new warm variants,
  // which meant create-warm got 2 rAFs + a >=50 ms macrotask of protection while
  // update/swap/clear went from the setup's #run straight into the timed click with
  // nothing but measure()'s own single rAF + setTimeout(0) between them — so the
  // 1000-row build's tail (post-render bookkeeping, disposal queues, GC of the
  // batch) landed inside their measured windows. Two settling regimes across four
  // scenarios the JSON calls equivalent is not a comparison.
  //
  // settleStrict is NOT a substitute: waitForNetworkQuiet returns on its first
  // iteration when inFlight===0 and the quiet window has already elapsed, and an
  // untimed #run generates no network traffic, so it returns in ~0 ms here.
  const setupRows = async () => {
    await actUntimed(page, '#run', RUN_PREDICATE, obs, T);
    executed.push(SETUP_STEP.run);
    await settleStrict(ctx, opts, 'the untimed #run setup');
    await beat();
  };

  switch (scenario) {
    case 'create-cold':
      // Fresh page load for EVERY iteration, and the timed click is the FIRST
      // interaction — so a runtime that initialises lazily boots inside this
      // window. Deliberately kept: it is a real cost. It is simply not the answer
      // to "how fast does this framework build 1000 rows", which is create-warm.
      return await timed('#run', RUN_PREDICATE);

    case 'create-warm': {
      // Untimed setup: build the rows once (this is what pays the runtime boot and
      // any interaction-triggered lazy load), then tear them back down, so the
      // timed #run starts from the same empty-tbody state as create-cold does.
      await setupRows();
      await actUntimed(page, '#clear', CLEAR_PREDICATE, obs, T);
      executed.push(SETUP_STEP.clear);
      // Cheap: no network activity since the #run settle, so the quiet window has
      // already elapsed. Present because #clear is an interaction like any other
      // and a framework is entitled to lazy-load on it.
      await settleStrict(ctx, opts, 'the untimed #clear setup');
      await beat();
      // measure() re-asserts non-vacuity: #tbody is empty here, so rowCount === 1000
      // is false at click time exactly as it is for create-cold. The two scenarios
      // time the identical button against the identical predicate from the identical
      // DOM state; the ONLY difference is whether the runtime is already booted.
      return await timed('#run', RUN_PREDICATE);
    }

    case 'update':
      await setupRows();
      // TOTAL predicate: every 10th row must carry the suffix and no other row may.
      // Gating on index 990 alone stopped the clock once 1 of ~100 mutations had
      // landed, so an app touching only the last row posted a ~100x "win".
      return await timed('#update', UPDATE_PREDICATE);

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
      return await timed('#swaprows', swapPredicate(ids));
    }

    case 'clear':
      await setupRows();
      return await timed('#clear', CLEAR_PREDICATE);

    case 'increment-cold':
      // First interaction on a cold runtime: boot + increment.
      return await timed('#increment', counterPredicate('1'));

    case 'increment-warm':
      // One untimed increment boots the runtime and warms the increment path...
      await actUntimed(page, '#increment', counterPredicate('1'), obs, T);
      executed.push(SETUP_STEP.increment);
      await settleStrict(ctx, opts, 'the untimed #increment setup');
      await beat();
      // ...then an identical click is timed. The predicate is "2" rather than "1"
      // purely because the counter is monotonic, exactly as the contract requires;
      // the WORK is the same single increment. It is non-vacuous at click time
      // (#counter-value reads "1"), which measure() re-asserts.
      return await timed('#increment', counterPredicate('2'));

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
// C3: "exactly 1 DOM write per counter increment, 0 render-tree allocation,
//      verified by instrumentation"
//
// Two independent instruments, plus a cross-check against the runtime's own
// self-report. Both instruments run byte-identically on Blazor and on Filament,
// which is the entire design goal: a criterion verified only by the runtime that
// claims to meet it has not been verified, and — because Blazor exposes no such
// counter — a self-report also cannot say whether the claimed number is any good.
// C3 is only meaningful as a comparison, so the instruments must be able to
// measure the framework that never heard of them.
// ---------------------------------------------------------------------------

/** Sampling interval for HeapProfiler. See ALLOCATION_SCOPE_CAVEAT for why 1024. */
export const ALLOC_SAMPLING_INTERVAL_BYTES = 1024;

/**
 * The honest limits of the allocation probe, emitted verbatim into every result.
 *
 * This is the caveat the brief asked for rather than a soft number, and it is not
 * a hedge — it is the finding. The probe is a ONE-SIDED test: rigorous for
 * Filament, structurally blind for Blazor. Anyone quoting a Filament-vs-Blazor
 * allocation ratio from this field is quoting an artifact of where the two
 * frameworks keep their heaps.
 */
export const ALLOCATION_SCOPE_CAVEAT = [
  'SCOPE: JavaScript heap only. This probe is V8\'s sampling allocation profiler, so it sees',
  'allocations made by JavaScript and nothing else.',
  '',
  'FOR FILAMENT this is complete and the claim is falsifiable. Filament\'s runtime IS JavaScript,',
  'so every allocation its increment path makes is a JS-heap allocation and appears here. At',
  'N=1000 increments and a 1024 B sampling interval, even 32 B/increment would surface as ~32 KB',
  '— far above the observed noise floor. A "0 tree allocation" claim that is false will show up.',
  '',
  'FOR BLAZOR IT IS NOT A MEASUREMENT OF THE RENDER TREE, AND MUST NEVER BE QUOTED AS ONE.',
  'Blazor\'s render tree — RenderTreeFrame buffers, the diff, the .NET object graph — is allocated',
  'inside the WebAssembly linear memory backing the .NET GC heap. To V8 that entire heap is ONE',
  'ArrayBuffer: the JS sampling profiler cannot see individual .NET allocations within it and',
  'reports none of them. What this probe measures on Blazor is only the JS-side interop glue',
  '(dotnet.runtime.js marshalling, UTF8ArrayToString, js-to-wasm thunks) — see topSites, which is',
  'reported precisely so this claim can be checked rather than believed.',
  '',
  'CONSEQUENCE: bytesPerIncrement UNDER-REPORTS Blazor by an unknown amount. "Filament 0 B vs',
  'Blazor N B" is therefore NOT a C3 result — it is a comparison between Filament\'s total',
  'allocation and Blazor\'s interop-glue-only subset. The correct reading: this probe can prove or',
  'refute FILAMENT\'s half of C3, and is silent on Blazor\'s. Quantifying Blazor\'s render-tree',
  'allocation needs a .NET-side instrument (GC.GetTotalAllocatedBytes via a [JSExport] probe, or',
  'dotnet-counters), which is not built here.',
  '',
  'The number also includes a constant per-iteration cost from the harness\'s own drive loop, which',
  'the two-point slope below cancels only insofar as that cost is constant in N. It is IDENTICAL',
  'for every framework, so it biases both the same way and cannot manufacture a difference.',
].join('\n');

/**
 * Sum selfSize across a HeapProfiler sampling profile, and attribute the bytes to
 * their allocation sites.
 *
 * topSites is not decoration: it is the evidence for ALLOCATION_SCOPE_CAVEAT. On
 * Blazor every heavy site is dotnet.runtime.js / UTF8ArrayToString / js-to-wasm —
 * i.e. interop glue, not a render tree. That is what "the profiler cannot see the
 * .NET heap" looks like from the outside, and it is checkable here rather than
 * taken on trust.
 */
export function summarizeSamplingProfile(profile) {
  let total = 0;
  const byFn = new Map();
  const walk = (node) => {
    const self = node.selfSize || 0;
    total += self;
    if (self > 0) {
      const cf = node.callFrame || {};
      const file = String(cf.url || '').split('/').pop() || '(unknown)';
      const key = `${cf.functionName || '(anonymous)'} @ ${file}:${cf.lineNumber ?? '?'}`;
      byFn.set(key, (byFn.get(key) || 0) + self);
    }
    for (const c of node.children || []) walk(c);
  };
  walk(profile.head);
  const topSites = [...byFn.entries()]
    .sort((a, b) => b[1] - a[1])
    .slice(0, 8)
    .map(([site, bytes]) => ({ site, bytes }));
  return { totalBytes: total, topSites };
}

/**
 * Bytes allocated per increment, by TWO-POINT SLOPE.
 *
 * A single N-increment measurement folds in every fixed cost of the sample —
 * enabling the profiler, the evaluate() round trip, the first-call tiering of the
 * drive loop. Measuring at two values of N and taking the slope
 *
 *     (bytes(nHigh) - bytes(nLow)) / (nHigh - nLow)
 *
 * cancels everything constant in N, which is the only reason the residual is small
 * enough to interpret. Repeated, and reported as a MEDIAN of slopes — never a mean
 * — for the same reason this file never means anything else: one GC-timing outlier
 * would otherwise carry the number.
 *
 * Measured spread on Blazor at N=1000 was 90.4 vs 111.8 B/increment across two
 * identical samples (~24%). That is the noise this instrument actually has, and it
 * is why a small non-zero reading here is not evidence of anything. A ~0 reading
 * against that floor, on the other hand, is a real result.
 */
async function measureAllocationPerIncrement(ctx, opts) {
  const { page, cdp } = ctx;
  const nLow = opts.c3AllocNLow;
  const nHigh = opts.c3AllocNHigh;
  const repeats = opts.c3AllocRepeats;

  await cdp.send('HeapProfiler.enable');
  const sampleAt = async (n) => {
    // Force a collection so the sample starts from a settled heap and cannot
    // inherit garbage from the previous one.
    await cdp.send('HeapProfiler.collectGarbage');
    await cdp.send('HeapProfiler.startSampling', {
      samplingInterval: ALLOC_SAMPLING_INTERVAL_BYTES,
      // LOAD-BEARING, AND NOT THE DEFAULT. Per the CDP contract: "By default, the
      // sampling heap profiler reports only objects which are still alive when the
      // profile is returned." Render-tree garbage is by definition NOT still alive
      // — it is allocated, used for one diff, and dropped. With these flags off,
      // this probe reports only what SURVIVED, which is the same retained-heap
      // quantity a snapshot delta gives, and it reports ~0 for a framework that
      // allocates hard and collects hard.
      //
      // That is not a hypothetical. Before these two flags were added, the probe
      // reported 88.0 B/increment for a fixture allocating ~2 KB/increment and
      // 81.7 B/increment for one allocating nothing — a 7% "difference" between
      // 2 KB and 0 B. It would have passed C3's "0 tree allocation" for ANY
      // framework, including one allocating megabytes, and it would have looked
      // completely reasonable doing it. The ground-truth fixtures are the only
      // reason it was caught.
      includeObjectsCollectedByMajorGC: true,
      includeObjectsCollectedByMinorGC: true,
    });
    await page.evaluate(
      ([k, t]) => window.__FILAMENT_BENCH__.driveIncrements(k, t),
      [n, opts.actionTimeoutMs],
    );
    const { profile } = await cdp.send('HeapProfiler.stopSampling');
    return summarizeSamplingProfile(profile);
  };

  // Discarded warm-up. The first sample carries the drive loop's own tiering and
  // reads ~5x the steady-state figure (549 B/incr vs ~100 B/incr, measured) — a
  // number that describes V8 warming up, not the framework.
  await sampleAt(nLow);

  const slopes = [];
  const samples = [];
  let lastTopSites = [];
  for (let i = 0; i < repeats; i++) {
    // eslint-disable-next-line no-await-in-loop
    const lo = await sampleAt(nLow);
    // eslint-disable-next-line no-await-in-loop
    const hi = await sampleAt(nHigh);
    lastTopSites = hi.topSites;
    const slope = (hi.totalBytes - lo.totalBytes) / (nHigh - nLow);
    slopes.push(slope);
    samples.push({ nLow, lowBytes: lo.totalBytes, nHigh, highBytes: hi.totalBytes, bytesPerIncrement: slope });
  }
  await cdp.send('HeapProfiler.disable').catch(() => {});

  const s = summarize(slopes.slice().sort((a, b) => a - b));
  return {
    method:
      'CDP HeapProfiler.startSampling (V8 sampling allocation profiler: allocation THROUGHPUT, ' +
      'including garbage later collected — not retained-heap growth). Two-point slope over N to ' +
      'cancel fixed per-sample cost; median of repeated slopes. HeapProfiler.collectGarbage between samples.',
    whyNotHeapSnapshotDelta:
      'A snapshot delta measures RETAINED growth. A framework that allocates a render tree per ' +
      'increment and drops it shows ~0 retained growth while allocating heavily — so a snapshot ' +
      'delta would report "0 allocation" for exactly the behaviour C3 exists to detect, and would ' +
      'report it as a PASS. Allocation throughput is the quantity C3 names.',
    samplingIntervalBytes: ALLOC_SAMPLING_INTERVAL_BYTES,
    nLow,
    nHigh,
    repeats,
    bytesPerIncrement: { median: s.median, min: s.min, max: s.max, samples: slopes },
    rawSamples: samples,
    topSites: lastTopSites,
    scope: 'javascript-heap-only',
    caveat: ALLOCATION_SCOPE_CAVEAT,
  };
}

/**
 * The C3 run. Counter app only — C3 is defined per counter increment.
 *
 * Sequence: fresh page, one UNTIMED warm-up increment (so a lazily-booted runtime
 * does its one-time DOM setup outside the counted window — Blazor's boot would
 * otherwise be charged as hundreds of "DOM writes" and the criterion would read as
 * a catastrophic fail for reasons that have nothing to do with per-increment
 * behaviour), an idle beat, then each counted increment.
 */
async function measureC3(browser, url, app, opts) {
  if (app !== 'counter') return null;

  return withFreshPage(browser, opts, async (ctx) => {
    await loadAndSettleStrict(ctx, url, app, opts);

    // Untimed warm-up: boot the runtime. Same rationale as every *-warm scenario.
    await actUntimed(
      ctx.page,
      '#increment',
      { kind: 'textEquals', args: { selector: '#counter-value', expected: '1' } },
      APPS.counter.observeSelector,
      opts.actionTimeoutMs,
    );
    await idleBeat(ctx.page, opts);

    // ---- instrument (a): MutationObserver, N consecutive increments ---------
    // N>1 because "1 write on the first increment, 3 on every one after" is a
    // real shape (first click populates, later clicks patch) and a single-sample
    // instrument would report the flattering one.
    const perIncrement = [];
    for (let i = 0; i < opts.c3Increments; i++) {
      // eslint-disable-next-line no-await-in-loop
      const expected = String(2 + i);
      // eslint-disable-next-line no-await-in-loop
      const r = await ctx.page.evaluate(
        ([root, want, t, settle]) => window.__FILAMENT_BENCH__.countDomWrites(
          '#increment',
          { kind: 'textEquals', args: { selector: '#counter-value', expected: want } },
          root,
          t,
          settle,
        ),
        [opts.c3ObserveRoot, expected, opts.actionTimeoutMs, opts.c3SettleMs],
      );
      perIncrement.push(r);
      // eslint-disable-next-line no-await-in-loop
      await idleBeat(ctx.page, opts);
    }

    const writes = perIncrement.map((r) => r.writes);
    const records = perIncrement.map((r) => r.records);
    const anyTimedOut = perIncrement.some((r) => r.timedOut);

    // ---- (b): cross-check against the runtime's self-report -----------------
    // Disagreement is a FINDING, reported loudly and never reconciled: if the two
    // instruments disagree, at least one of them is wrong, and silently preferring
    // either would destroy the only evidence that something is off.
    const statsSeen = perIncrement.some((r) => r.statsAfter && r.statsAfter.present);
    let statsCrossCheck = null;
    if (statsSeen) {
      const deltas = perIncrement.map((r) => {
        const a = r.statsBefore && typeof r.statsBefore.domWrites === 'number' ? r.statsBefore.domWrites : null;
        const b = r.statsAfter && typeof r.statsAfter.domWrites === 'number' ? r.statsAfter.domWrites : null;
        return a === null || b === null ? null : b - a;
      });
      const comparable = deltas.every((d) => d !== null);
      const agrees = comparable && deltas.every((d, i) => d === writes[i]);
      statsCrossCheck = {
        present: true,
        selfReportedDomWritesPerIncrement: deltas,
        observedDomWritesPerIncrement: writes,
        agrees,
        finding: comparable
          ? (agrees
            ? 'The runtime\'s self-reported __filament.stats.domWrites matches the independent '
              + 'MutationObserver count on every increment.'
            : 'DISAGREEMENT: __filament.stats.domWrites does NOT match the MutationObserver count. '
              + 'One of the two is wrong and this result does not say which. Either the runtime is '
              + 'under-counting its own writes (the self-report is not measuring what it claims), or '
              + 'it is writing the DOM by a path its counter does not instrument. NOT reconciled here '
              + 'on purpose: a harness that quietly picks a winner deletes the finding.')
          : 'INCONCLUSIVE: __filament is present but exposes no numeric stats.domWrites, so the '
            + 'self-report could not be compared. The MutationObserver count stands alone.',
      };
    }

    const first = perIncrement[0] || null;
    return {
      criterion: 'C3: exactly 1 DOM write per counter increment, 0 render-tree allocation',
      domWrites: {
        method:
          'MutationObserver on ' + opts.c3ObserveRoot + ' with {childList, subtree, characterData, ' +
          'attributes}, counted across ONE #increment click and a trailing rAF + ' + opts.c3SettleMs +
          'ms settle. Framework-agnostic: identical code runs on Blazor and on Filament.',
        observeRoot: opts.c3ObserveRoot,
        whyThisRoot:
          'The widest root available, deliberately. A narrow root (#app, #counter-value) is a way to ' +
          'not-see writes, and the framework under test must not choose where the instrument looks. ' +
          'A wider root can only over-count, never under-count.',
        writeDefinition:
          'childList: addedNodes + removedNodes; characterData: 1; attributes: 1. Reported ' +
          'separately from `records` because one MutationRecord can carry many added nodes — ' +
          'conflating the two would be its own fabrication.',
        increments: opts.c3Increments,
        writesPerIncrement: writes,
        recordsPerIncrement: records,
        medianWrites: summarize(writes.slice().sort((a, b) => a - b)).median,
        byType: first ? first.byType : null,
        firstIncrementDetail: first ? first.detail : null,
        timedOut: anyTimedOut,
        verdict: anyTimedOut
          ? 'INVALID: at least one increment did not land within the timeout; the counts below are not trustworthy.'
          : (writes.every((w) => w === 1)
            ? 'Exactly 1 DOM write on every counted increment.'
            : `NOT exactly 1 DOM write per increment: observed ${JSON.stringify(writes)}.`),
      },
      statsCrossCheck,
      allocation: opts.c3Alloc ? await measureAllocationPerIncrement(ctx, opts) : null,
    };
  });
}

// ---------------------------------------------------------------------------
// AOT verification.
//
// `--aot` is a SELF-DECLARATION typed by whoever launched the run. Recorded
// verbatim — which is all this harness used to do — it is worth nothing: the JSON
// would cheerfully state aot:true for a build where AOT silently failed to engage,
// and every downstream comparison would be against a mislabelled artifact. Nobody
// would ever catch it, because the flag agreed with the intent and the intent is
// what people re-read.
//
// So the harness now looks for INDEPENDENT evidence in the artifacts it actually
// serves, and records what it OBSERVED next to what was DECLARED. They are separate
// fields. A disagreement is a loud warning; the harness never silently "corrects"
// the flag, because the operator may be right and the signature table may be stale,
// and a harness that quietly rewrites its inputs is its own fabrication risk.
//
// ON FRAMEWORK-AGNOSTICISM. This is metadata verification, not measurement: it
// touches no predicate, no clock, no byte total, and no scenario's pass/fail, and
// it runs identically for every framework. It is a DATA table of signatures, not a
// branch on framework identity — and a framework with no matching signature yields
// observed:null ("no evidence available"), never a false confirmation and never a
// different measurement path. Filament will be driven by this exact code: it has no
// AOT signature here, so a --aot claim about it is reported UNVERIFIED rather than
// rubber-stamped. That is the correct, conservative outcome, and it is strictly
// more honest than today's behaviour of believing every claim from every framework.
// ---------------------------------------------------------------------------

/**
 * Signatures that can corroborate or refute an --aot claim from the served bytes.
 *
 * Thresholds are calibrated against this project's own publish output, measured on
 * the RAW (on-disk, pre-compression) artifact — compressed size is a much weaker
 * discriminator because AOT output is highly compressible:
 *
 *   blazor-rows-aot      dotnet.native.ogsd35n1u1.wasm   11,380,806 B
 *   blazor-counter-aot   dotnet.native.lz2nl4qo4f.wasm   11,362,554 B
 *   blazor-rows-nojit    dotnet.native.kllr7zg72l.wasm    1,494,734 B
 *   blazor-counter-nojit dotnet.native.kllr7zg72l.wasm    1,494,734 B
 *
 * A 7.6x separation. The thresholds below sit ~2x clear of each observed cluster,
 * leaving a wide indeterminate band in between: a build has to look like neither an
 * AOT nor an interpreted runtime to land there, and if it does the harness says
 * "indeterminate" rather than guessing.
 */
export const AOT_EVIDENCE_SIGNATURES = [
  {
    id: 'dotnet-wasm-native-runtime-size',
    // dotnet.native.wasm, with or without the publish fingerprint segment that
    // .NET 8+ injects (dotnet.native.<hash>.wasm). Compressed siblings are
    // excluded by the walker, which only ever stats the raw artifact.
    match: /^dotnet\.native(\.[0-9a-z]+)?\.wasm$/i,
    aotAtLeastBytes: 6_000_000,
    interpretedAtMostBytes: 3_000_000,
    rationale:
      'With RunAOTCompilation=true the .NET WASM toolchain compiles the application and framework ' +
      'assemblies to native WebAssembly and links them into dotnet.native.wasm; without it that file is ' +
      'just the interpreter runtime and the assemblies ship separately as .wasm/.dll payloads. Measured ' +
      'on this project: ~11.37 MB AOT vs ~1.49 MB interpreted (7.6x).',
  },
];

const AOT_WALK_MAX_ENTRIES = 20000;

/** Recursively collect files under `dir` (bounded), returning {relPath, name, size}. */
async function walkFiles(dir, limit = AOT_WALK_MAX_ENTRIES) {
  const out = [];
  const stack = [dir];
  while (stack.length && out.length < limit) {
    const cur = stack.pop();
    let entries;
    // eslint-disable-next-line no-await-in-loop
    try { entries = await fsp.readdir(cur, { withFileTypes: true }); } catch { continue; }
    for (const e of entries) {
      const full = path.join(cur, e.name);
      if (e.isDirectory()) { stack.push(full); continue; }
      if (!e.isFile()) continue;
      // eslint-disable-next-line no-await-in-loop
      const st = await fsp.stat(full).catch(() => null);
      if (!st) continue;
      out.push({ relPath: path.relative(dir, full), name: e.name, size: st.size });
      if (out.length >= limit) break;
    }
  }
  return out;
}

/**
 * Scan the served directory for artifacts matching a known AOT signature and read
 * their RAW sizes. Pure filesystem observation — no claim is interpreted here.
 */
export async function collectAotEvidence(dir, signatures = AOT_EVIDENCE_SIGNATURES) {
  const files = await walkFiles(dir);
  const evidence = [];
  for (const f of files) {
    for (const sig of signatures) {
      if (!sig.match.test(f.name)) continue;
      let verdict = 'indeterminate';
      if (f.size >= sig.aotAtLeastBytes) verdict = 'aot';
      else if (f.size <= sig.interpretedAtMostBytes) verdict = 'interpreted';
      evidence.push({
        signatureId: sig.id,
        path: f.relPath.split(path.sep).join('/'),
        rawBytes: f.size,
        verdict,
        thresholds: { aotAtLeastBytes: sig.aotAtLeastBytes, interpretedAtMostBytes: sig.interpretedAtMostBytes },
        rationale: sig.rationale,
      });
    }
  }
  // Largest first: if a publish output somehow contains several matches, the
  // runtime the app actually boots is the substantive one.
  evidence.sort((a, b) => b.rawBytes - a.rawBytes);
  return evidence;
}

/**
 * Combine the declared flag with the observed evidence. Never mutates either; the
 * result carries both plus an explicit agreement verdict and any warnings.
 *
 * `servedPaths` are the paths the server actually wrote during the weight run.
 * SERVEDNESS IS A REQUIREMENT, NOT A PREFERENCE. A file sitting in a publish
 * directory is not evidence about the build that was measured: --dir can hold a
 * leftover dotnet.native.old.wasm from a previous AOT build while the runtime the app
 * actually boots is something else entirely. Confirming an --aot claim from bytes the
 * browser never requested is the same class of error as trusting the flag itself,
 * which is the thing this code exists to stop — so `verified` requires the primary
 * evidence to carry servedInWeightRun === true, and omitting `servedPaths` means
 * servedness is UNKNOWN and therefore unverified rather than assumed.
 */
export function classifyAotEvidence(declared, evidence, servedPaths = null) {
  const served = servedPaths ? new Set(servedPaths.map((p) => p.replace(/^\/+/, ''))) : null;
  const annotated = evidence.map((e) => ({
    ...e,
    servedInWeightRun: served ? served.has(e.path) : null,
  }));

  // Servedness FIRST, verdict SECOND. Filtering on verdict first would let an
  // unserved artifact outrank a served one whenever the served one lands in the
  // indeterminate band: the served 4.5 MB runtime would be dropped as inconclusive,
  // the stale 11 MB leftover would become `primary`, and the AOT INDETERMINATE
  // warning about the runtime that was actually measured would be suppressed —
  // reporting aotObserved:true, verified:true from a file that never went over the
  // wire. If anything was served, only served evidence may speak.
  const servedEvidence = annotated.filter((e) => e.servedInWeightRun === true);
  const pool = servedEvidence.length ? servedEvidence : annotated;
  const conclusive = pool.filter((e) => e.verdict !== 'indeterminate');
  const primary = conclusive[0] ?? pool[0] ?? null;

  let observed = null;
  if (primary && primary.verdict === 'aot') observed = true;
  else if (primary && primary.verdict === 'interpreted') observed = false;

  const warnings = [];
  let basis;
  if (!annotated.length) {
    basis = 'no-signature-matched';
  } else if (observed === null) {
    basis = 'indeterminate';
  } else {
    basis = primary.signatureId;
  }

  const primaryServed = !!primary && primary.servedInWeightRun === true;

  if (declared !== null && observed !== null && declared !== observed) {
    warnings.push(
      `AOT MISMATCH: the run DECLARED --aot=${declared}, but the artifact ${primary.path} is ` +
      `${primary.rawBytes} B raw, which is ${primary.verdict === 'aot' ? 'far too large to be' : 'far too small to be'} ` +
      `anything but a${primary.verdict === 'aot' ? 'n AOT' : 'n interpreted'} build ` +
      `(thresholds: aot >= ${primary.thresholds.aotAtLeastBytes} B, interpreted <= ${primary.thresholds.interpretedAtMostBytes} B). ` +
      'Either the flag is wrong or the build is not what was intended. Every AOT-vs-interpreted ' +
      'comparison drawn from this file is suspect until that is resolved.',
    );
  }
  if (declared === true && observed === null) {
    warnings.push(
      'AOT UNVERIFIED: the run declared --aot=true but no served artifact could corroborate it ' +
      `(${basis}). The flag is recorded as declared-only; do not cite it as an observed property of ` +
      'the build.',
    );
  }
  // The evidence exists and is conclusive, but it is not evidence about THIS run.
  if (primary && observed !== null && !primaryServed) {
    warnings.push(
      primary.servedInWeightRun === false
        ? `AOT evidence NOT SERVED: ${primary.path} (${primary.rawBytes} B raw) is the artifact this ` +
          'verdict was derived from, but it was NEVER requested during the weight run — it may be a ' +
          'stale artifact left in the publish directory by an earlier build, in which case it says ' +
          'nothing about the runtime that was actually measured. aotObserved is reported for ' +
          'information only and the claim is NOT verified.'
        : `AOT SERVEDNESS UNKNOWN: no served-path list was supplied, so ${primary.path} cannot be ` +
          'confirmed to have gone over the wire. The claim is NOT verified.',
    );
  }
  if (primary && primary.verdict === 'indeterminate') {
    warnings.push(
      `AOT INDETERMINATE: ${primary.path} is ${primary.rawBytes} B raw, which falls between the ` +
      'interpreted and AOT thresholds. The signature table may need recalibrating for this toolchain.' +
      (annotated.length > pool.length
        ? ` (${annotated.length - pool.length} other matching artifact(s) in the publish directory were ` +
          'NOT served and are therefore not evidence about this build.)'
        : ''),
    );
  }

  return {
    declared,
    observed,
    agrees: declared === null || observed === null ? null : declared === observed,
    // Corroborated AND corroborated by something the browser actually loaded.
    verified: observed !== null && declared === observed && primaryServed,
    basis,
    evidence: annotated,
    warnings,
    note:
      'declared = the --aot CLI flag, self-reported by the operator and never trusted on its own. ' +
      'observed = an independent verdict derived from the RAW size of an artifact matching a known AOT ' +
      'signature (see evidence[].rationale). verified = observed agrees with declared AND the artifact ' +
      'it was derived from was SERVED during the weight run (evidence[].servedInWeightRun) — a file ' +
      'that was never requested may be a leftover from an earlier build and cannot confirm anything ' +
      'about the build that was measured. observed:null means no evidence was available for this ' +
      'framework, NOT that AOT is absent — the harness declines to guess rather than confirming a ' +
      'claim it cannot check.',
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
    // SELF-DECLARED. Kept under its original name for continuity, but it is the
    // operator's claim, not an observation — see aotObserved / aotVerification,
    // which are filled in from the served artifacts once the weight run has told us
    // what was actually on the wire.
    aot: opts.aot,
    aotDeclared: opts.aot,
    aotObserved: null,
    aotVerification: null,
    // Hand-maintained; kept for continuity and legibility. Known to have gone stale
    // across a breaking change. Not evidence — see `harness` for the checkable identity.
    harnessVersion: HARNESS_VERSION,
    // Computed from the bytes that actually ran. This is what makes "same harness"
    // a checkable claim rather than an assertion.
    harness: await computeHarnessIdentity(),
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
    // null = every scenario the app defines. A subset is only ever a narrowing;
    // the chosen names are recorded in config.scenarios so a partial run can never
    // be mistaken for a full one.
    scenarios: null,
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
    // Beat between a warm scenario's untimed setup and its timed click: two rAFs
    // (so the setup has been through layout+paint) then a macrotask of at least
    // this long, so post-render bookkeeping cannot land inside the measured window.
    idleBeatMs: 50,
    // ---- C3 -----------------------------------------------------------------
    // Off by default: C3 is a separate question from C1/C4 and the allocation
    // probe is slow. Never folded into a normal run, so a C4 timing can never be
    // taken on a page the profiler has been sampling.
    c3: false,
    // The allocation probe is the slowest part by far (~2 * repeats * nHigh
    // increments). Separately switchable so the cheap DOM-write instrument can be
    // run on its own.
    c3Alloc: false,
    c3Increments: 5,
    // document.body: the widest root available. See measureC3's whyThisRoot.
    c3ObserveRoot: 'body',
    // Trailing settle after the predicate holds, to catch writes a framework makes
    // AFTER the value lands. 60 ms > one 16.7 ms frame with room to spare.
    c3SettleMs: 60,
    c3AllocNLow: 200,
    c3AllocNHigh: 1000,
    c3AllocRepeats: 3,
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
      case '--scenarios':
        o.scenarios = next().split(',').map((s) => s.trim()).filter(Boolean);
        break;
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
      case '--idle-beat-ms': o.idleBeatMs = asInt(next(), '--idle-beat-ms'); break;
      case '--c3': o.c3 = true; break;
      case '--c3-alloc': o.c3 = true; o.c3Alloc = true; break;
      case '--c3-increments': o.c3Increments = asInt(next(), '--c3-increments'); break;
      case '--c3-observe-root': o.c3ObserveRoot = next(); break;
      case '--c3-alloc-n-low': o.c3AllocNLow = asInt(next(), '--c3-alloc-n-low'); break;
      case '--c3-alloc-n-high': o.c3AllocNHigh = asInt(next(), '--c3-alloc-n-high'); break;
      case '--c3-alloc-repeats': o.c3AllocRepeats = asInt(next(), '--c3-alloc-repeats'); break;
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

Scenarios:
  rows      create-cold create-warm update swap clear
  counter   increment-cold increment-warm

  *-cold times the FIRST click after a fresh load, so a lazily-initialised runtime
  boots inside the measured window: the number is boot + work. *-warm runs an
  identical interaction untimed first, so it measures the work alone — that is the
  one to compare frameworks on. update/swap/clear were always warm (untimed #run
  setup); the warm variants make create/increment consistent with them.

Optional:
  --port <n>          default 0 (ephemeral)
  --route <path>      app route, default /
  --scenarios <a,b>   comma-separated subset to run, default all for the app.
                      Recorded in config.scenarios; a partial run is always visible
                      as such in the output.
  --runs <n>          timed iterations per scenario, default 10
  --warmup <n>        discarded iterations before the timed ones, default 0
  --weight-runs <n>   cold-load byte measurements, default 3
  --aot[=true|false]  DECLARE whether AOT was enabled for this build. This is your
                      claim, not a measurement: the harness independently inspects
                      the served artifacts and records what it observed in
                      environment.aotObserved / environment.aotVerification, warning
                      loudly if the two disagree or if the claim cannot be checked.
  --headed            run Chrome headed (default: headless)
  --out <file>        output path, default bench/results/<label>.json
  --timeout <ms>      predicate timeout, default 10000
  --idle-beat-ms <ms> beat between a warm scenario's untimed setup and its timed
                      click, default 50
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

C3 (--app counter only; ignored otherwise):
  --c3                run the framework-agnostic DOM-write instrument: a
                      MutationObserver over --c3-observe-root counts the DOM writes
                      caused by ONE #increment. Identical code runs on Blazor and on
                      Filament, so C3 is reported as a COMPARISON rather than as a
                      framework's self-report. When the page exposes
                      __filament.stats.domWrites, the self-report is cross-checked
                      against the observed count and any disagreement is reported as
                      a finding, never silently reconciled.
  --c3-alloc          --c3 plus the allocation probe (V8 sampling allocation
                      profiler, two-point slope over N increments). SLOW.
                      READ c3.allocation.caveat BEFORE QUOTING ITS NUMBER: it
                      measures the JS heap only, so it is complete for Filament and
                      STRUCTURALLY BLIND to Blazor's .NET render tree, which lives in
                      WASM linear memory. It can refute Filament's "0 tree
                      allocation" claim; it cannot quantify Blazor's allocation, and
                      a ratio between the two is not a C3 result.
  --c3-increments <n> increments to count, default 5 (>1 because "1 write on the
                      first, 3 on every one after" is a real shape)
  --c3-observe-root <sel>
                      MutationObserver root, default body — deliberately the widest
                      root available. A narrow root is a way to not-see writes.
  --c3-alloc-n-low <n> / --c3-alloc-n-high <n> / --c3-alloc-repeats <n>
                      slope endpoints and repeat count, default 200 / 1000 / 3
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
  if (opts.c3 && opts.c3Increments < 1) {
    process.stderr.write('bench.mjs: --c3-increments must be >= 1\n');
    return 2;
  }
  // A non-positive span would divide by zero (or negative) in the slope and emit a
  // confident Infinity/NaN as "bytes per increment".
  if (opts.c3Alloc && opts.c3AllocNHigh <= opts.c3AllocNLow) {
    process.stderr.write(
      `bench.mjs: --c3-alloc-n-high (${opts.c3AllocNHigh}) must be > --c3-alloc-n-low (${opts.c3AllocNLow}); ` +
      'the allocation probe is a two-point slope over that span.\n',
    );
    return 2;
  }
  if (opts.c3Alloc && opts.c3AllocRepeats < 1) {
    process.stderr.write('bench.mjs: --c3-alloc-repeats must be >= 1\n');
    return 2;
  }

  const allScenarios = APPS[opts.app].scenarios;
  const selectedScenarios = opts.scenarios ?? allScenarios;
  const unknownScenarios = selectedScenarios.filter((s) => !allScenarios.includes(s));
  if (unknownScenarios.length) {
    process.stderr.write(
      `bench.mjs: unknown scenario(s) for --app ${opts.app}: ${unknownScenarios.join(', ')}\n` +
      `bench.mjs: available: ${allScenarios.join(', ')}\n`,
    );
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

  // Independent evidence for/against the --aot claim, read from the artifacts that
  // are about to be served. Collected here, interpreted after the weight run has
  // established which of them actually went over the wire.
  const aotEvidence = await collectAotEvidence(dir);

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

    // The --aot flag is a claim; this is the independent check of it, run against
    // the artifacts the server actually wrote during the weight run above.
    const aotVerification = classifyAotEvidence(opts.aot, aotEvidence, weight.servedPaths);
    environment.aotObserved = aotVerification.observed;
    environment.aotVerification = aotVerification;
    for (const w of aotVerification.warnings) {
      process.stderr.write(
        `\n[bench] ${'='.repeat(76)}\n[bench] WARNING: ${w}\n[bench] ${'='.repeat(76)}\n\n`,
      );
    }
    if (aotVerification.verified) {
      process.stderr.write(
        `[bench] AOT verified: declared=${aotVerification.declared} and the served artifacts agree ` +
        `(${aotVerification.basis})\n`,
      );
    }

    // C3 runs on its OWN page, before the timed scenarios and never during one:
    // the allocation probe leaves V8's sampling profiler enabled and forces GCs,
    // and a C4 median taken under either would be measuring the instrument.
    let c3 = null;
    if (opts.c3) {
      if (opts.app !== 'counter') {
        process.stderr.write(
          `[bench] --c3 ignored: C3 is defined per COUNTER increment and --app is "${opts.app}".\n`,
        );
      } else {
        c3 = await measureC3(browser, url, opts.app, opts);
        process.stderr.write(
          `\n[bench] C3 DOM writes/increment (MutationObserver on ${c3.domWrites.observeRoot}, ` +
          `framework-agnostic): ${JSON.stringify(c3.domWrites.writesPerIncrement)}\n` +
          `[bench]   ${c3.domWrites.verdict}\n`,
        );
        if (c3.statsCrossCheck && !c3.statsCrossCheck.agrees) {
          process.stderr.write(
            `[bench] ${'='.repeat(76)}\n` +
            '[bench] C3 CROSS-CHECK DISAGREEMENT: the runtime\'s self-reported\n' +
            `[bench]   __filament.stats.domWrites = ${JSON.stringify(c3.statsCrossCheck.selfReportedDomWritesPerIncrement)}\n` +
            `[bench]   MutationObserver observed   = ${JSON.stringify(c3.statsCrossCheck.observedDomWritesPerIncrement)}\n` +
            '[bench] One of the two is wrong. NOT reconciled — see c3.statsCrossCheck.finding.\n' +
            `[bench] ${'='.repeat(76)}\n`,
          );
        }
        if (c3.allocation) {
          process.stderr.write(
            `[bench] C3 allocation: ${c3.allocation.bytesPerIncrement.median} B/increment ` +
            `(JS heap ONLY — see c3.allocation.caveat; this does NOT see Blazor's .NET render tree)\n`,
          );
        }
      }
    }

    const scenarios = {};
    for (const scenario of selectedScenarios) {
      // eslint-disable-next-line no-await-in-loop
      scenarios[scenario] = await runScenario(browser, url, opts.app, scenario, opts);
    }

    const anyFailed = Object.values(scenarios).some((s) => s.status !== 'ok');

    // COMPUTED, never hand-counted. An upstream report claimed "40/40 timed
    // iterations across 10 scenarios" when the true figure was 100 (10 runs x 10
    // scenarios). That error was harmless in direction — it understated the work —
    // but it appeared in a document titled "ZERO fabricated numbers", and a number
    // nobody can recompute from the artifact is exactly how such things happen. So
    // the count now comes out of the same loop that produced the samples.
    const scenarioList = Object.values(scenarios);
    const totals = {
      scenarioCount: scenarioList.length,
      scenarioNames: Object.keys(scenarios),
      runsPerScenario: opts.runs,
      timedIterationsPlanned: scenarioList.length * opts.runs,
      timedIterationsRecorded: scenarioList.reduce((a, s) => a + s.n, 0),
      timedIterationFailures: scenarioList.reduce((a, s) => a + s.failureCount, 0),
      warmupIterationsDiscarded: scenarioList.length * opts.warmup,
      note:
        'timedIterationsRecorded is summed from the per-scenario sample counts actually retained, so it ' +
        'is derived from the same data the medians are, and cannot drift from it. planned - recorded == ' +
        'the iterations that failed and were refused a number.',
    };

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
      // null unless --c3 was passed. Its absence is not evidence about C3.
      c3,
      // The fixture that defines "the same workload". Recorded so a later edit to
      // expected-labels.json is visible in the output rather than silently
      // redefining what every framework is being held to.
      expectedLabels: expectedLabels
        ? { path: path.relative(HARNESS_DIR, expectedLabels.path), sha256: expectedLabels.sha256 }
        : null,
      environment,
      totals,
      config: {
        runs: opts.runs,
        warmup: opts.warmup,
        weightRuns: opts.weightRuns,
        scenarios: selectedScenarios,
        allScenariosForApp: allScenarios,
        scenariosComplete: selectedScenarios.length === allScenarios.length,
        actionTimeoutMs: opts.actionTimeoutMs,
        idleBeatMs: opts.idleBeatMs,
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
          'verifyContract drives #run -> #update -> #swaprows -> #clear -> #run untimed and asserts the ' +
          'full post-condition of each. BOTH #run clicks are gated against the deterministic Park-Miller ' +
          'label stream (expected-labels.json, hash recorded above): rows 0-4 and 999 of the FIRST run ' +
          '(LCG draws 1..3000, the stream create-cold times) and of the SECOND run on the same page (draws ' +
          '3001..6000, the stream create-warm — the headline — times), plus every row id in both runs ' +
          '(1..1000 then 1001..2000, the monotonic counter). Gating only the first run would leave the ' +
          'headline click asserted by nothing but "1000 rows exist", which an app that generates the ' +
          'stream once and reuses the interned strings satisfies while doing none of the LCG or ' +
          'concatenation work on the run that is timed. An app that emits constant labels, caches its ' +
          'labels after the first run, resets the id counter, updates fewer rows, or duplicates instead ' +
          'of swapping is refused with exit code 3 rather than reported as fast.',
        coldVsWarm:
          'Every scenario carries `runtime` ("cold" | "warm") and `headline` (whether it answers the ' +
          'question its name asks). A COLD scenario times the first click after a fresh page load, so a ' +
          'framework that initialises its runtime lazily boots INSIDE the measured window and the number ' +
          'is boot + work. For a Blazor build the boot term is the majority of cold `create`, which means ' +
          'a framework with no runtime to boot wins that scenario without rendering anything faster — the ' +
          'metric would be reporting startup and calling it rendering. A WARM scenario runs an identical ' +
          'interaction UNTIMED first (see scenarios[].untimedSetup), so the runtime is booted and the ' +
          'measured window is the work alone. update/swap/clear were always warm — they have always had an ' +
          'untimed #run setup — so create-warm/increment-warm make create/increment consistent with them ' +
          'rather than introducing a new kind of measurement. Every warm scenario is separated from its ' +
          'timed click by the SAME idle beat (two chained requestAnimationFrames, so the setup has been ' +
          'through style/layout/paint, plus a >= config.idleBeatMs macrotask to drain post-render ' +
          'bookkeeping), so the four are measured under one settling regime rather than the setup tail of ' +
          'some of them being billed to their timed click. Both variants are reported; cold is a real ' +
          'user-visible cost and is not deleted, it is simply not a rendering number. Compare frameworks ' +
          'on the warm variants, and only ever against the same variant.',
        monotonicState:
          'Per the DOM contract, row ids are monotonic and never reset, and the label LCG is seeded only ' +
          'at page load. A warm scenario\'s timed #run therefore emits ids 1001..2000 with the LCG ' +
          'continuing, and increment-warm\'s timed click goes 1 -> 2 rather than 0 -> 1. This is intended ' +
          'and the harness deliberately does NOT reset the seed or the id counter to make the timed run ' +
          'look identical to the setup run: the WORK is identical either way (1000 rows, 3000 LCG draws, ' +
          '1000 three-part concatenations; one increment), and resetting hidden state would be the harness ' +
          'manufacturing a condition no user is ever in.',
        statistics:
          'MEDIAN and IQR (p75 - p25) over `runs` iterations, using linear-interpolation quantiles ' +
          '(type 7, the numpy/R default). The mean is never reported. Per-run BREAKDOWNS in `weight` are ' +
          'quoted from weight.representativeRun — the run nearest the median, which for an even run count ' +
          'is not the median itself and no longer claims to be.',
        aot:
          'environment.aot / environment.aotDeclared is the operator\'s --aot claim and is never trusted ' +
          'on its own. environment.aotObserved is an INDEPENDENT verdict derived from the raw size of an ' +
          'artifact matching a known AOT signature, and environment.aotVerification carries the evidence, ' +
          'the thresholds and the reasoning. aotVerification.verified is true ONLY when the verdict ' +
          'agrees with the claim AND the artifact it came from was SERVED during the weight run ' +
          '(evidence[].servedInWeightRun): a file present in the publish directory but never requested ' +
          'may be a leftover from an earlier build, so it can corroborate nothing about the build that ' +
          'was measured, and evidence selection prefers served artifacts before it prefers conclusive ' +
          'ones. A disagreement is a loud warning and sets aotVerification.agrees=false; an unverifiable ' +
          'claim sets aotObserved=null and is reported as UNVERIFIED rather than confirmed. The harness ' +
          'never rewrites the declared flag to match the evidence. This is metadata verification only: it ' +
          'touches no timing, no byte total and no predicate, and a framework with no signature is ' +
          'measured identically, just not corroborated.',
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
        `[bench] ${name.padEnd(15)} ${s.status === 'ok' ? '' : '[' + s.status.toUpperCase() + '] '}` +
        `median=${s.median} ms  iqr=${s.iqr} ms  (n=${s.n}, failures=${s.failureCount})` +
        `  [${s.runtime}${s.headline ? '' : ', NOT the headline — includes runtime boot'}]` +
        `   [toPaint median=${s.toPaint.median} ms iqr=${s.toPaint.iqr} ms — includes the harness's vsync wait]\n`,
      );
    }
    process.stderr.write(
      `[bench] ${totals.timedIterationsRecorded}/${totals.timedIterationsPlanned} timed iterations recorded ` +
      `across ${totals.scenarioCount} scenario(s), ${totals.timedIterationFailures} failure(s)\n`,
    );
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

export {
  APPS,
  SCENARIO_SPECS,
  attachNetworkTracker,
  inPageHarness,
  waitForNetworkQuiet,
  UPDATE_INDICES,
  UPDATE_NOT_INDICES,
};
