#!/usr/bin/env node
/**
 * canon.test.mjs — tests for the Phase 2 equivalence comparator.
 *
 * A comparator that only ever says PASS is not a gate, it is a rubber stamp.
 * Decision 51 validated its prototype in the POSITIVE direction only (it accepts
 * a renaming). The NEGATIVE direction is the half that decides whether the gate
 * means anything, so most of what is below is negative.
 *
 *   node tools/canon.test.mjs
 */

import { mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { compareFiles, canon, tokenize, hasObjectKeys } from './canon.mjs';

const dir = mkdtempSync(join(tmpdir(), 'canon-test-'));
let pass = 0, fail = 0;

const file = (name, src) => { const p = join(dir, name.replace(/[^\w.-]/g, '_')); writeFileSync(p, src); return p; };

function check(name, got, want) {
  if (got === want) { pass++; console.log(`  ok    ${name}`); }
  else { fail++; console.log(`  FAIL  ${name}\n          got  ${got}\n          want ${want}`); }
}

/** assert that two SOURCES are / are not alpha-equivalent through the full pipeline */
function equiv(name, a, b, expected) {
  const r = compareFiles(file(`${name}.a.js`, a), file(`${name}.b.js`, b));
  check(name, r.equivalent, expected);
  return r;
}

// The shape under test: a reduced Counter that exercises every construct the real
// gate depends on (import clause, element creation, property write, text node,
// binding, event, attach).
const HAND = `
import { signal, effect, setText, listen, insert } from '../rt.js';
export function mount(target) {
  const currentCount = signal(0);
  const button = document.createElement('button');
  button.id = 'increment';
  const t = document.createTextNode('');
  insert(button, t);
  effect(() => setText(t, currentCount.value));
  listen(button, 'click', () => { currentCount.value++; });
  insert(target, button);
}`;

console.log('\n--- POSITIVE: renamings must be accepted ---');

// This is decision 51's own validation case: same structure, compiler-style names.
equiv('pos/compiler-names', HAND, `
import { signal, effect, setText, listen, insert } from '../rt.js';
export function mount(_target) {
  const _s0 = signal(0);
  const _el0 = document.createElement('button');
  _el0.id = 'increment';
  const _tx0 = document.createTextNode('');
  insert(_el0, _tx0);
  effect(() => setText(_tx0, _s0.value));
  listen(_el0, 'click', () => { _s0.value++; });
  insert(_target, _el0);
}`, true);

// Aliasing at the import site must not matter; the EXTERNAL name is what binds.
equiv('pos/import-aliased-vs-bare', `
import { signal as s } from '../rt.js';
export const x = () => s(0);`, `
import { signal } from '../rt.js';
export const x = () => signal(0);`, true);

// Formatting/comments are the minifier's problem, not canon's.
equiv('pos/whitespace-and-comments', HAND, HAND.replace(/\n/g, '\n\n  // noise\n'), true);

console.log('\n--- NEGATIVE: genuinely different programs must be REJECTED ---');
console.log('    (each of these is accepted by the crude prototype canon replaced)');

// 1. THE DOM CONTRACT. The prototype renamed string literals, so it could not
//    tell a <button> from a <div>. This is the shared DOM contract.
equiv('neg/different-element-tag', HAND, HAND.replace(`'button'`, `'div'`), false);

// 2. Wrong id => wrong DOM contract, same shape.
equiv('neg/different-attribute-value', HAND, HAND.replace(`'increment'`, `'decrement'`), false);

// 3. Wrong event name.
equiv('neg/different-event-name', HAND, HAND.replace(`'click'`, `'mousedown'`), false);

// 4. THE RUNTIME PRIMITIVE. The prototype renamed imported names, so a module
//    calling listen() where the answer key calls setText() PASSED. It throws on
//    the first click.
equiv('neg/swapped-runtime-primitives', HAND, `
import { signal, effect, listen, setText, insert } from '../rt.js';
export function mount(target) {
  const currentCount = signal(0);
  const button = document.createElement('button');
  button.id = 'increment';
  const t = document.createTextNode('');
  insert(button, t);
  effect(() => listen(t, currentCount.value));
  setText(button, 'click', () => { currentCount.value++; });
  insert(target, button);
}`, false);

// 5. THE PROPERTY NAME. `.id` vs `.className` is a different DOM contract.
equiv('neg/different-property-name', HAND, HAND.replace('button.id', 'button.className'), false);

// 6. `.value` vs `.data` — the signal API vs the Text node API.
equiv('neg/different-member-access', HAND, HAND.replace('currentCount.value++', 'currentCount.data++'), false);

// 7. Structure: the answer key inlines the handler; a named binding + reference
//    is a DIFFERENT program. This is the exact shape question the Phase 2 seam
//    turns on, so the comparator had better be able to see it.
equiv('neg/inlined-vs-named-handler', HAND, `
import { signal, effect, setText, listen, insert } from '../rt.js';
export function mount(target) {
  const currentCount = signal(0);
  const Increment = () => { currentCount.value++; };
  const button = document.createElement('button');
  button.id = 'increment';
  const t = document.createTextNode('');
  insert(button, t);
  effect(() => setText(t, currentCount.value));
  listen(button, 'click', Increment);
  insert(target, button);
}`, false);

// 8. A dropped statement must not pass.
equiv('neg/missing-insert', HAND, HAND.replace('  insert(target, button);\n', ''), false);

// 9. Reordered statements are a different program (create order is observable).
equiv('neg/reordered-inserts', HAND, HAND.replace(
  `  effect(() => setText(t, currentCount.value));\n  listen(button, 'click', () => { currentCount.value++; });`,
  `  listen(button, 'click', () => { currentCount.value++; });\n  effect(() => setText(t, currentCount.value));`), false);

// 10. Different initial state.
equiv('neg/different-literal-number', HAND, HAND.replace('signal(0)', 'signal(1)'), false);

// 11. A global is not a free rename.
equiv('neg/global-vs-local', HAND, HAND.replace(/document\.createElement/g, 'doc.createElement'), false);

console.log('\n--- unit: tokenizer & helpers ---');

check('tokenize/string-not-split', tokenize(`x('a b c')`).length, 4);
check('tokenize/keeps-string-literal', tokenize(`x('a b')`)[2].value, `'a b'`);
check('canon/reserved-stay-literal', canon('const a = 1;').startsWith('const'), true);
check('canon/property-stays-literal', canon('a.value;').includes('. value'), true);
check('canon/global-stays-literal', canon('document.x;').startsWith('document'), true);
check('canon/first-appearance-order', canon('const zz = 1; const aa = zz;'), 'const V0 = 1 ; const V1 = V0 ;');
check('hasObjectKeys/true', hasObjectKeys('({a:1})'), true);
check('hasObjectKeys/false-on-ternary', hasObjectKeys('x ? y : z'), false);
check('hasObjectKeys/false-on-counter-shape', hasObjectKeys(`insert(a,b);x.id='q';`), false);

rmSync(dir, { recursive: true, force: true });
console.log(`\n${pass} passed, ${fail} failed\n`);
process.exit(fail === 0 ? 0 : 1);
