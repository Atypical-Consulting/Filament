/**
 * error-boundary-contract — the BEHAVIOURAL contract for <ErrorBoundary> (decision 164).
 *
 * WHY THIS EXISTS, WHEN THE CANON GATE ALREADY COMPARES BYTES. `canon` proves the generator emits
 * the module the answer key says it should. It cannot prove that module DOES the right thing, and
 * every claim decision 164 makes is a claim about behaviour — that the content's throw is CAUGHT
 * rather than propagated, that markup OUTSIDE the boundary survives it, that the latch is STICKY,
 * and that mount() itself does not throw. Those are run here, in a DOM, on the emitted bytes.
 *
 * It bundles through esbuild — the same root dependency canon.mjs pins — because the emitted module
 * imports the runtime as TypeScript, and drives the result under happy-dom.
 *
 *     node tools/error-boundary-contract.mjs <emitted.g.js> [--json]
 *
 * EVERY ASSERTION HAS A CONTROL THAT MAKES IT FAIL. A contract whose steps cannot fail measures
 * nothing. The controls here are applied to the bundled SOURCE — the guard removed, the latch made
 * non-sticky, the swap frozen — and each must break the step it is paired with. They run in the
 * same process, on the same bytes, immediately after the real run.
 *
 * Exit 0 = every step matched the contract AND every control failed the step it targets.
 */

import { build } from 'esbuild';
import { Window } from 'happy-dom';
import { mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const entry = process.argv[2];
const asJson = process.argv.includes('--json');
if (!entry) {
  process.stderr.write('usage: node tools/error-boundary-contract.mjs <emitted.g.js> [--json]\n');
  process.exit(2);
}

const work = mkdtempSync(path.join(tmpdir(), 'filament-eb-contract-'));

/** Bundle one module (optionally mutated first) and mount it into a fresh DOM. */
async function runIn(source, mutate) {
  const outfile = path.join(work, `app-${Math.abs(hash(mutate?.name ?? 'real'))}.mjs`);
  await build({
    entryPoints: [source], outfile, bundle: true, format: 'esm', target: 'es2022',
    logLevel: 'silent',
    // The same define src/filament-runtime/scripts/build.mjs gives dist/filament.js — WHAT SHIPS.
    define: { __FILAMENT_STATS__: 'false' },
    ...(mutate
      ? { plugins: [{
          name: 'control',
          setup(b) {
            b.onLoad({ filter: /\.g\.js$/ }, async (a) => {
              const fs = await import('node:fs/promises');
              return { contents: mutate.apply(await fs.readFile(a.path, 'utf8')), loader: 'js' };
            });
          },
        }] }
      : {}),
  });

  const window = new Window({ url: 'http://localhost/' });
  const { document } = window;
  document.body.innerHTML = '<div id="app"></div>';
  for (const n of ['document', 'Node', 'Event', 'MouseEvent', 'HTMLElement', 'CustomEvent', 'window'])
    globalThis[n] = window[n] ?? window;

  const { mount } = await import(pathToFileURL(outfile).href + `?v=${Date.now()}${Math.random()}`);
  const app = document.getElementById('app');
  let threw = null;
  try { mount(app); } catch (e) { threw = e && e.message ? e.message : String(e); }
  const text = (id) => document.getElementById(id)?.textContent ?? null;
  return { threw, text, html: app.innerHTML };
}

function hash(s) { let h = 0; for (const c of s) h = (h * 31 + c.charCodeAt(0)) | 0; return h; }

// ---- the contract ---------------------------------------------------------------------------
// Each step names what decision 164 claims, and the CONTROL that must break it.
const steps = [
  {
    name: 'the content\'s throw is CAUGHT, not propagated',
    check: (r) => r.threw === null,
    detail: (r) => `mount() threw ${JSON.stringify(r.threw)}`,
    control: {
      name: 'guard rethrows instead of latching',
      // The catch is kept (so the module still PARSES — a control that breaks the build measures
      // the regex, not the mapping) but it rethrows, which is what the boundary would do without
      // a latch: the throw escapes reconcile and mount() dies.
      apply: (s) => s.replace(
        /_ebErr0\.value \?\?= _e;\s*return document\.createTextNode\(''\);/, 'throw _e;'),
    },
  },
  {
    name: 'the content is NOT rendered (#in absent)',
    check: (r) => r.text('in') === null,
    detail: (r) => `#in was ${JSON.stringify(r.text('in'))}`,
    control: {
      name: 'content no longer throws',
      // NOT "freeze the swap": with the swap frozen the content branch still throws and still
      // returns the empty placeholder, so #in is absent either way and the step would be passed
      // by a control that changed nothing. Making the content SUCCEED is what discriminates it —
      // #in then renders "ok". The first control written here was the useless one, and this
      // contract is what said so.
      apply: (s) => s.replace(/if \(fail\) \{/, 'if (false) {'),
    },
  },
  {
    name: 'the boundary renders ErrorContent, carrying the caught message',
    check: (r) => r.text('err') === 'Sorry: the content could not be rendered',
    detail: (r) => `#err was ${JSON.stringify(r.text('err'))}`,
    control: {
      name: 'swap frozen (latch never consulted)',
      apply: (s) => s.replace(/_ebErr0\.value === null \? \[0\] : \[1\]/, '[0]'),
    },
  },
  {
    name: 'markup OUTSIDE the boundary survives it',
    check: (r) => r.text('outside') === 'outside',
    detail: (r) => `#outside was ${JSON.stringify(r.text('outside'))}`,
    control: {
      name: 'outside moved inside the guarded branch',
      // If #outside were built inside the content branch it would die with it — which is exactly
      // the divergence from Blazor this step exists to catch.
      apply: (s) => s.replace(/insert\(_el0, _el1\);/, ''),
    },
  },
  {
    name: 'the latch is STICKY (`??=`), so the FIRST exception is the one shown',
    check: (r) => r.text('err') === 'Sorry: the content could not be rendered',
    detail: (r) => `#err was ${JSON.stringify(r.text('err'))}`,
    control: {
      name: 'latch overwritten on every write',
      // `=` instead of `??=` — the shape Blazor's W5 refutes. It cannot change THIS witness's text
      // (one throw only), so the control is reported as INAPPLICABLE rather than passed off as a
      // proof. Naming that is the point: a control that cannot fail is not evidence.
      apply: (s) => s.replace(/_ebErr0\.value \?\?= _e;/, '_ebErr0.value = _e;'),
      expectInapplicable: true,
    },
  },
];

// ---- run ------------------------------------------------------------------------------------
const real = await runIn(entry, null);
const results = [];
let failures = 0;

for (const step of steps) {
  const ok = step.check(real);
  if (!ok) failures++;

  let controlVerdict = 'not run';
  if (ok) {
    let controlled;
    try {
      controlled = await runIn(entry, { name: step.control.name, apply: step.control.apply });
      const stillPasses = step.check(controlled);
      if (step.control.expectInapplicable) {
        controlVerdict = stillPasses
          ? 'INAPPLICABLE (declared) — this witness cannot distinguish it'
          : 'broke the step';
      } else if (stillPasses) {
        controlVerdict = 'DID NOT BREAK THE STEP — the step proves nothing';
        failures++;
      } else {
        controlVerdict = 'broke the step';
      }
    } catch (e) {
      controlVerdict = `broke the step (threw: ${e.message.slice(0, 60)})`;
    }
  }

  results.push({ step: step.name, ok, control: step.control.name, controlVerdict,
                 detail: ok ? undefined : step.detail(real) });
  process.stdout.write(`  ${ok ? 'ok  ' : 'FAIL'} ${step.name}\n`);
  process.stdout.write(`       control "${step.control.name}": ${controlVerdict}\n`);
  if (!ok) process.stdout.write(`       ${step.detail(real)}\n`);
}

process.stdout.write(`\n  rendered: ${real.html}\n`);
process.stdout.write(failures === 0
  ? '\nVERDICT: the emitted bytes behave as decision 164 claims, and every control breaks its step.\n'
  : `\nVERDICT: ${failures} failure(s).\n`);

if (asJson) process.stdout.write(JSON.stringify({ failures, results }, null, 2) + '\n');
process.exit(failures === 0 ? 0 : 1);
