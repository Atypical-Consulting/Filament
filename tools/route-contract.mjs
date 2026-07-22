/**
 * route-contract — the BEHAVIOURAL contract for a routed app with route parameters (decision 163).
 *
 * WHY THIS EXISTS, WHEN THE GATE ALREADY COMPARES BYTES. `canon` decides alpha-equivalence against a
 * hand-written answer key: it proves the generator emits the module the spec says it should. It cannot
 * prove that module DOES the right thing, and every claim decision 163 makes is a claim about behaviour
 * — that /item/new beats /item/{Id:int}, that /item/2147483648 does NOT match, that navigating
 * /item/7 -> /item/8 keeps the page's state instead of resetting it. Those are run here, in a DOM, on
 * the emitted bytes.
 *
 * It bundles through esbuild — the same root dependency canon.mjs pins — because the emitted modules
 * import the runtime as TypeScript, and drives the result under happy-dom.
 *
 *     node tools/route-contract.mjs <dir-with-Router.g.js> [--json]
 *
 * Exit 0 = every step matched the contract. Non-zero = a step diverged, and the divergence is printed
 * with what was expected and what was rendered.
 */

import { build } from 'esbuild';
import { Window } from 'happy-dom';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const dir = process.argv[2];
const asJson = process.argv.includes('--json');
if (!dir) {
  process.stderr.write('usage: node tools/route-contract.mjs <dir-with-Router.g.js> [--json]\n');
  process.exit(2);
}

// ---- bundle -------------------------------------------------------------------------------------
// One module out of the router and every page it imports, with the TypeScript runtime resolved and
// stripped. Nothing is minified: this step is about RUNNING the emitted code, not about comparing it.
const work = mkdtempSync(path.join(tmpdir(), 'filament-route-contract-'));
const bundlePath = path.join(work, 'app.mjs');
await build({
  entryPoints: [path.join(dir, 'Router.g.js')],
  outfile: bundlePath,
  bundle: true,
  format: 'esm',
  target: 'es2022',
  logLevel: 'silent',
  // The same define src/filament-runtime/scripts/build.mjs gives dist/filament.js — WHAT SHIPS. The
  // contract must run the production runtime, not the instrumented one.
  define: { __FILAMENT_STATS__: 'false' },
});

// ---- a DOM --------------------------------------------------------------------------------------
const window = new Window({ url: 'http://localhost/' });
const { document } = window;
document.body.innerHTML = '<div id="app"></div>';

// The emitted router reaches for BARE globals — addEventListener, location, history, document, URL —
// exactly as it does in a browser. They are published here rather than injected, so what runs is the
// shipped code and not a variant of it.
for (const name of ['document', 'location', 'history', 'URL', 'Node', 'Event', 'MouseEvent',
                    'HTMLElement', 'CustomEvent', 'window']) {
  // Node defines a few of these itself (URL) and makes others getter-only (navigator), so each one
  // is defined rather than assigned — the point is that the emitted code finds the DOM's version.
  Object.defineProperty(globalThis, name, { value: window[name], configurable: true, writable: true });
}
globalThis.addEventListener = window.addEventListener.bind(window);
globalThis.removeEventListener = window.removeEventListener.bind(window);

const { mount } = await import(pathToFileURL(bundlePath).href);
mount(document.getElementById('app'));

// ---- driving ------------------------------------------------------------------------------------
const problems = [];
const steps = [];

const text = (id) => document.getElementById(id)?.textContent ?? null;

/** Where the app thinks it is: the page's own #where marker, or null when nothing mounted. */
const where = () => text('where');

// A THROW IS A CONTRACT FAILURE, not a crash. A router whose converter table is keyed wrongly raises a
// TypeError on the first navigation, and an unhandled rejection there would report as a broken harness
// when it is in fact the emitted code being broken -- which is exactly what this file exists to tell
// apart. Everything that touches the app is funnelled through here.
let fatal = null;
function guard(what, fn) {
  if (fatal) return undefined;
  try {
    return fn();
  } catch (e) {
    fatal = `${what} threw: ${e && e.message ? e.message : e}`;
    problems.push(fatal);
    return undefined;
  }
}

/** A real link click — bubbling, button 0 — which is what the router's interceptor listens for. */
function clickLink(id) {
  return guard(`click #${id}`, () => {
    const a = document.getElementById(id);
    if (!a) { problems.push(`link #${id} is not in the DOM (page is "${where()}")`); return false; }
    a.dispatchEvent(new window.MouseEvent('click', { bubbles: true, cancelable: true, button: 0 }));
    return true;
  });
}

/** A direct address-bar navigation: set the URL and fire popstate, the way Back/Forward does. */
function go(url) {
  guard(`navigate to ${url}`, () => {
    window.history.pushState(null, '', url);
    window.dispatchEvent(new window.Event('popstate'));
  });
}

function check(name, actual, expected) {
  if (fatal) { steps.push({ step: name, expected, actual: null, ok: false, skipped: true }); return; }
  const ok = JSON.stringify(actual) === JSON.stringify(expected);
  steps.push({ step: name, expected, actual, ok });
  if (!ok) problems.push(`${name}: expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`);
}

// 1. The home page is what "/" mounts.
check('/ mounts home', where(), 'home');

// 2. A parameterised route matches, and the captured value is RENDERED. This is the whole slice: on
//    decision 139's string-equality table this step rendered nothing at all.
clickLink('to-item');
check('/item/7 mounts item', where(), 'item');
check('/item/7 captures Id=7', text('id'), '7');

// 3. INSTANCE REUSE. Bump the page's own state three times, then navigate to a route that differs ONLY
//    in its parameter. Blazor reuses the component: #seen survives and OnInitialized does not re-run.
//    A router that re-mounts unconditionally shows 0 here.
guard('bump x3', () => {
  for (let i = 0; i < 3; i++) document.getElementById('bump').dispatchEvent(
    new window.MouseEvent('click', { bubbles: true, cancelable: true, button: 0 }));
});
check('state accumulates', text('seen'), '3');

clickLink('to-8');
check('/item/8 re-renders Id', text('id'), '8');
check('/item/8 REUSES the page (state survives)', text('seen'), '3');

// 4. PRECEDENCE. A literal segment outranks a parameter, independently of declaration order.
//
//    /tag/all is the step that BITES: `{Slug}` is unconstrained, so it matches "all" perfectly well and
//    the only thing that decides is rank. /item/new is kept alongside it as the WEAKER case, and it is
//    labelled as such -- ':int' rejects "new", so that one passes even with the ranking removed. Both
//    are here because the difference is the whole reason the sort has to be tested with the right pair.
go('/tag/all');
check('/tag/all beats /tag/{Slug} (literal outranks a parameter)', where(), 'all-tags');
go('/item/new');
check('/item/new beats /item/{Id:int} (weak: the constraint also rejects)', where(), 'new');

// 5. The int constraint REJECTS what Blazor rejects, and the rejection is a non-match rather than a
//    match with a wrong value. 2147483648 is Int32.MaxValue + 1: Blazor does not route it.
go('/item/2147483648');
check('/item/2147483648 does not match (Int32 range)', where(), null);
go('/item/abc');
check('/item/abc does not match', where(), null);
go('/item/+5');
check('/item/+5 matches (NumberStyles.Integer allows a sign)', [where(), text('id')], ['item', '5']);

// 6. A bare parameter is a string, and it is URL-DECODED.
go('/tag/hello%20world');
check('/tag/hello%20world decodes', [where(), text('slug')], ['tag', 'hello world']);

// 7. `:long` is BigInt, and the point is the digit a double cannot hold. 9007199254740993 is 2^53 + 1;
//    a number-backed converter renders ...992 here.
go('/big/9007199254740993');
check('/big/{N:long} is exact past 2^53', [where(), text('n')], ['big', '9007199254740993']);

// 8. `:bool` is bool.TryParse — case-insensitive, and nothing else matches.
go('/flag/true');
check('/flag/true', [where(), text('state')], ['flag', 'on']);
go('/flag/FALSE');
check('/flag/FALSE (case-insensitive)', [where(), text('state')], ['flag', 'off']);
go('/flag/yes');
check('/flag/yes does not match', where(), null);

// 9. Blazor's segment rules: a trailing slash and a doubled slash are the same path, and a literal
//    segment is compared case-insensitively.
go('/item/7/');
check('trailing slash still matches', [where(), text('id')], ['item', '7']);
go('/ITEM/7');
check('literal segments match case-insensitively', where(), 'item');

// 10. Nothing matches -> nothing mounted, and the target is CLEARED rather than left showing the last
//     page. A router that leaves the previous page up is lying about where the user is.
go('/nope');
check('/nope mounts nothing, and clears', [where(), document.getElementById('app').innerHTML], [null, '']);

// 11. Back still works after all of that.
go('/');
check('back to /', where(), 'home');

// ---- report -------------------------------------------------------------------------------------
rmSync(work, { recursive: true, force: true });

if (asJson) {
  process.stdout.write(JSON.stringify({ ok: problems.length === 0, steps, problems }, null, 2) + '\n');
} else {
  for (const s of steps) {
    process.stdout.write(`${s.skipped ? ' SKIP ' : s.ok ? '  ok  ' : ' FAIL '} ${s.step}\n`);
    if (!s.ok && !s.skipped) {
      process.stdout.write(`        expected ${JSON.stringify(s.expected)}, got ${JSON.stringify(s.actual)}\n`);
    }
  }
  if (fatal) process.stdout.write(`\nABORTED: ${fatal}\n`);
  process.stdout.write(`\n${steps.filter((s) => s.ok).length}/${steps.length} steps matched the contract\n`);
}

process.exit(problems.length === 0 ? 0 : 1);
