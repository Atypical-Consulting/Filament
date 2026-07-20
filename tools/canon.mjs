#!/usr/bin/env node
/**
 * canon.mjs — the Phase 2 equivalence comparator. Decision 51.
 *
 *     canon(minify(generated)) === canon(minify(hand-written))
 *
 * `minify` is EXACTLY build-filament.sh's esbuild (--minify --target=es2022), at the
 * version pinned in the root package.json, because a minifier that renames differently
 * is a comparator that decides differently. It runs through esbuild's node API rather
 * than the CLI: same engine and flags, but no subprocess whose executable is named
 * differently on every platform. See the note above minify().
 *
 * `canon` renames each IDENTIFIER by ORDER OF FIRST APPEARANCE, so two programs
 * that differ only in naming collapse onto the same token stream. That is the
 * whole point: the generator names an element `_el0`, a human names it `h1`, and
 * that difference says NOTHING about the thesis.
 *
 * ---------------------------------------------------------------------------
 * WHAT "IDENTIFIER" MEANS HERE, AND WHY THE CRUDE PROTOTYPE WAS NOT USABLE
 * ---------------------------------------------------------------------------
 * The prototype this tool replaces renamed every identifier-shaped word match in
 * the file. That is not alpha-equivalence, it is erasure, and it was validated only
 * in the POSITIVE direction (it accepts a renaming). Measured in the NEGATIVE
 * direction it accepts these too — all three verified, see canon.test.mjs:
 *
 *   - It renames INSIDE STRING LITERALS. `createElement("button")` and
 *     `createElement("div")` both canonicalise to `V(V)`. The comparator that
 *     guards the SHARED DOM CONTRACT could not see the DOM contract.
 *   - It renames IMPORTED NAMES. `import{setText as i}` and `import{listen as i}`
 *     are the same token, so a module that calls `listen` where the answer key
 *     calls `setText` — a program that throws on the first click — PASSES.
 *   - It renames PROPERTY NAMES. `.value` vs `.data`, `.id` vs `.className`
 *     are invisible.
 *
 * So this tool renames only what alpha-renaming is actually allowed to touch —
 * BOUND identifiers — and leaves literal everything whose spelling carries
 * meaning:
 *
 *   renamed   : local bindings and references (const/let/var/function/class/
 *               params/catch), import aliases, and non-allowlisted free names.
 *   LITERAL   : reserved words; property names after `.` / `?.`; the EXTERNAL
 *               names in import/export clauses; every string, template and
 *               number literal; all punctuation; allowlisted globals (document,
 *               Math, ...) since renaming a free variable is not alpha-renaming.
 *
 * Bare specifiers are normalised (`import{signal}` is treated as
 * `import{signal as signal}`) so that a minifier's choice to alias or not cannot
 * decide the gate.
 *
 * ---------------------------------------------------------------------------
 * NAMED LIMITATIONS — read these before trusting a verdict
 * ---------------------------------------------------------------------------
 * L1. RENAMES BY NAME, NOT BY SCOPE. There is no scope analysis: one name is one
 *     token, file-wide. Two distinct variables in disjoint scopes that reuse a
 *     letter collide into one token. esbuild reuses letters across disjoint
 *     scopes CONSTANTLY, so this is not hypothetical. It cuts BOTH WAYS: a false
 *     PASS (two programs collapse onto one stream) and a false FAIL (one program
 *     reuses `e`, the other does not). This is the limitation decision 51 names,
 *     and it is why decision 51 does NOT let this tool carry the gate alone —
 *     "les mesures sont inchangées" is an independent control, and a disagreement
 *     between the two is a REPORT, not a rounding.
 * L2. FREE NAMES OUTSIDE THE ALLOWLIST ARE RENAMED. A program that consistently
 *     called a global by a different spelling would pass. GLOBALS below is the
 *     mitigation, not a proof.
 * L3. NO AST, SO OBJECT-LITERAL KEYS AND LABELS ARE TREATED AS RENAMEABLE.
 *     `{value:1}` vs `{data:1}` is invisible. `--strict-keys` reports whether the
 *     compared programs contain any `{key:` construct at all; the Counter gate
 *     asserts they do not, which is what makes L3 inert THERE and nowhere else.
 * L4. REGEX-vs-DIVISION is disambiguated by the standard previous-token
 *     heuristic, not by a parser.
 *
 * Usage:
 *   node tools/canon.mjs <a.js> <b.js>     compare; exit 0 equivalent, 1 different
 *   node tools/canon.mjs --print <file.js> print the canonical token stream
 *   node tools/canon.mjs --json <a> <b>    machine-readable verdict
 */

import { transformSync, version as esbuildVersionString } from 'esbuild';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const REPO_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');

// Must equal build-filament.sh's EXPECTED_ESBUILD. The minifier IS part of the
// comparison: a version that mangles differently is a different `canon`.
const EXPECTED_ESBUILD = '0.28.1';

// ---------------------------------------------------------------------------
// minify — exactly build-filament.sh's esbuild flags for the minified path.
// ---------------------------------------------------------------------------

// Call esbuild through its node API rather than spawning its CLI.
//
// The CLI was never portable here. It was reached via `npx`, which on Windows is `npx.cmd`,
// and since CVE-2024-27980 node refuses to spawn a .cmd without a shell — EINVAL there while
// working everywhere else. `shell: true` would fix the spawn and break the arguments, which
// include generated absolute paths that cmd.exe would reinterpret. Spawning the package's
// own bin doesn't help either: it is a platform-native executable, named differently per
// platform, not something node can run.
//
// transformSync is the same engine, same version, and the same two flags the CLI path used
// (esbuild only transforms here — there is no --bundle), so the token stream it produces is
// the one the answer keys were minified with. It also removes a subprocess per comparison,
// and the last dependency on npx's resolution, which silently meant "whatever happens to be
// in ~/.npm/_npx" rather than the version pinned in the root package.json.
export function esbuildVersion() {
  return esbuildVersionString;
}

export function minify(file) {
  return transformSync(readFileSync(resolve(file), 'utf8'), {
    minify: true,
    target: 'es2022',
  }).code;
}

// ---------------------------------------------------------------------------
// tokenizer
// ---------------------------------------------------------------------------

const RESERVED = new Set([
  'await', 'break', 'case', 'catch', 'class', 'const', 'continue', 'debugger',
  'default', 'delete', 'do', 'else', 'enum', 'export', 'extends', 'false',
  'finally', 'for', 'function', 'if', 'import', 'in', 'instanceof', 'new',
  'null', 'return', 'super', 'switch', 'this', 'throw', 'true', 'try', 'typeof',
  'var', 'void', 'while', 'with', 'yield', 'let', 'static', 'get', 'set',
]);

// L2's mitigation. Renaming a FREE variable is not alpha-renaming, so the names
// that are free-by-construction stay literal.
const GLOBALS = new Set([
  'globalThis', 'window', 'document', 'console', 'undefined', 'NaN', 'Infinity',
  'Object', 'Array', 'String', 'Number', 'Boolean', 'Symbol', 'Math', 'JSON',
  'Date', 'RegExp', 'Error', 'TypeError', 'RangeError', 'Promise', 'Set', 'Map',
  'WeakMap', 'WeakSet', 'Reflect', 'Proxy', 'parseInt', 'parseFloat', 'isNaN',
  'isFinite', 'queueMicrotask', 'requestAnimationFrame', 'cancelAnimationFrame',
  'setTimeout', 'clearTimeout', 'setInterval', 'clearInterval', 'performance',
  'Node', 'Text', 'Element', 'HTMLElement', 'Event', 'CustomEvent',
  'MutationObserver', 'DocumentFragment', 'Comment',
]);

const ID_START = /[A-Za-z_$ª-￿]/;
const ID_PART = /[A-Za-z0-9_$ª-￿]/;

// Tokens after which a `/` starts a REGEX rather than a division (L4).
const REGEX_OK_PUNCT = new Set([
  '(', ',', '=', ':', '[', '!', '&', '|', '?', '{', '}', ';', '=>', '+', '-',
  '*', '%', '<', '>', '===', '!==', '==', '!=', '<=', '>=', '&&', '||', '??',
  '+=', '-=', '*=', '/=', 'return', 'typeof', 'instanceof', 'in', 'of', 'new',
  'delete', 'void', 'throw', 'case', 'do', 'else', 'yield', 'await',
]);

const PUNCTUATORS = [
  '>>>=', '...', '===', '!==', '**=', '<<=', '>>=', '>>>', '&&=', '||=', '??=',
  '=>', '==', '!=', '<=', '>=', '&&', '||', '??', '?.', '++', '--', '+=', '-=',
  '*=', '/=', '%=', '&=', '|=', '^=', '**', '<<', '>>',
  '{', '}', '(', ')', '[', ']', ';', ',', '<', '>', '+', '-', '*', '/', '%',
  '&', '|', '^', '!', '~', '?', ':', '=', '.', '#', '@',
];

export function tokenize(src) {
  const out = [];
  let i = 0;
  const prevSig = () => (out.length ? out[out.length - 1] : null);

  while (i < src.length) {
    const c = src[i];

    // whitespace
    if (/\s/.test(c)) { i++; continue; }

    // comments
    if (c === '/' && src[i + 1] === '/') { while (i < src.length && src[i] !== '\n') i++; continue; }
    if (c === '/' && src[i + 1] === '*') { i += 2; while (i < src.length && !(src[i] === '*' && src[i + 1] === '/')) i++; i += 2; continue; }

    // strings
    if (c === '"' || c === "'") {
      let j = i + 1;
      while (j < src.length && src[j] !== c) { if (src[j] === '\\') j++; j++; }
      out.push({ type: 'string', value: src.slice(i, j + 1) });
      i = j + 1; continue;
    }

    // template literals (kept whole; substitutions inside are NOT canonicalised —
    // named as part of L3's family. Counter's output has none, asserted by the gate.)
    if (c === '`') {
      let j = i + 1, depth = 0;
      while (j < src.length) {
        if (src[j] === '\\') { j += 2; continue; }
        if (src[j] === '`' && depth === 0) break;
        if (src[j] === '$' && src[j + 1] === '{') { depth++; j += 2; continue; }
        if (src[j] === '}' && depth > 0) { depth--; j++; continue; }
        j++;
      }
      out.push({ type: 'template', value: src.slice(i, j + 1) });
      i = j + 1; continue;
    }

    // numbers
    if (/[0-9]/.test(c) || (c === '.' && /[0-9]/.test(src[i + 1] ?? ''))) {
      let j = i;
      while (j < src.length && /[0-9a-fA-FxXoObBeE._n]/.test(src[j])) {
        if ((src[j] === 'e' || src[j] === 'E') && /[+-]/.test(src[j + 1] ?? '')) j++;
        j++;
      }
      out.push({ type: 'number', value: src.slice(i, j) });
      i = j; continue;
    }

    // identifiers
    if (ID_START.test(c)) {
      let j = i;
      while (j < src.length && ID_PART.test(src[j])) j++;
      out.push({ type: 'ident', value: src.slice(i, j) });
      i = j; continue;
    }

    // regex vs division (L4)
    if (c === '/') {
      const p = prevSig();
      const regexOk = !p
        || (p.type === 'punct' && REGEX_OK_PUNCT.has(p.value))
        || (p.type === 'ident' && REGEX_OK_PUNCT.has(p.value));
      if (regexOk) {
        let j = i + 1, inClass = false;
        while (j < src.length) {
          if (src[j] === '\\') { j += 2; continue; }
          if (src[j] === '[') inClass = true;
          else if (src[j] === ']') inClass = false;
          else if (src[j] === '/' && !inClass) break;
          j++;
        }
        j++;
        while (j < src.length && ID_PART.test(src[j])) j++;
        out.push({ type: 'regex', value: src.slice(i, j) });
        i = j; continue;
      }
    }

    // punctuation
    const punct = PUNCTUATORS.find((p) => src.startsWith(p, i));
    if (punct) { out.push({ type: 'punct', value: punct }); i += punct.length; continue; }

    throw new Error(`canon: cannot tokenize at offset ${i}: ${JSON.stringify(src.slice(i, i + 20))}`);
  }
  return out;
}

// ---------------------------------------------------------------------------
// canon — rename bound identifiers by order of first appearance
// ---------------------------------------------------------------------------

export function canonTokens(src) {
  const toks = tokenize(src);
  const names = new Map();
  const rename = (n) => {
    if (!names.has(n)) names.set(n, `V${names.size}`);
    return names.get(n);
  };

  // Mark, in a first pass, which identifier occurrences are EXTERNAL names in an
  // import/export clause (literal) and which are the local bindings (renamed).
  // `import {A as B}` -> A external, B local.  `export {B as A}` -> B local, A external.
  const external = new Set();   // token indices whose spelling is meaningful
  const bareSpec = new Set();   // token indices to expand into `Name as <rename>`

  for (let i = 0; i < toks.length; i++) {
    const t = toks[i];
    if (t.type !== 'ident' || (t.value !== 'import' && t.value !== 'export')) continue;
    const isImport = t.value === 'import';
    // dynamic import(...) / import.meta are not clauses
    if (isImport && toks[i + 1] && toks[i + 1].type === 'punct' && (toks[i + 1].value === '(' || toks[i + 1].value === '.')) continue;
    // only `import {...}` / `export {...}` clauses carry external names
    if (!toks[i + 1] || toks[i + 1].type !== 'punct' || toks[i + 1].value !== '{') continue;

    let j = i + 2;
    while (j < toks.length && !(toks[j].type === 'punct' && toks[j].value === '}')) {
      if (toks[j].type === 'ident') {
        const hasAs = toks[j + 1] && toks[j + 1].type === 'ident' && toks[j + 1].value === 'as';
        if (hasAs) {
          // import: first is external. export: second is external.
          external.add(isImport ? j : j + 2);
          j += 3;
        } else {
          bareSpec.add(j);
          j += 1;
        }
        continue;
      }
      j++;
    }
  }

  const parts = [];
  for (let i = 0; i < toks.length; i++) {
    const t = toks[i];
    if (t.type !== 'ident') { parts.push(t.value); continue; }

    if (external.has(i)) { parts.push(t.value); continue; }

    if (bareSpec.has(i)) {
      // Normalise `import{x}` to `import{x as x}` so a minifier's choice to alias
      // or not cannot decide the gate. Export direction is <local> as <external>.
      const isImportSide = (() => {
        for (let k = i; k >= 0; k--) {
          if (toks[k].type === 'ident' && (toks[k].value === 'import' || toks[k].value === 'export')) return toks[k].value === 'import';
        }
        return true;
      })();
      parts.push(isImportSide ? `${t.value} as ${rename(t.value)}` : `${rename(t.value)} as ${t.value}`);
      continue;
    }

    if (RESERVED.has(t.value)) { parts.push(t.value); continue; }

    // property name after `.` or `?.` — spelling carries meaning, keep literal
    const prev = toks[i - 1];
    if (prev && prev.type === 'punct' && (prev.value === '.' || prev.value === '?.')) { parts.push(t.value); continue; }

    // contextual keywords inside a clause we already handled
    if (t.value === 'as' || t.value === 'from') {
      const prevTok = toks[i - 1];
      if (prevTok && (prevTok.type === 'ident' || prevTok.type === 'punct')) { parts.push(t.value); continue; }
    }

    if (GLOBALS.has(t.value)) { parts.push(t.value); continue; }

    parts.push(rename(t.value));
  }
  return parts;
}

export const canon = (src) => canonTokens(src).join(' ');

/** Does the stream contain an object-literal-ish `key:`? Gates L3. */
export function hasObjectKeys(src) {
  const toks = tokenize(src);
  for (let i = 1; i < toks.length; i++) {
    if (toks[i].type !== 'punct' || toks[i].value !== ':') continue;
    const p = toks[i - 1];
    const pp = toks[i - 2];
    if (p && p.type === 'ident' && pp && pp.type === 'punct' && (pp.value === '{' || pp.value === ',')) return true;
  }
  return false;
}

// ---------------------------------------------------------------------------
// compare
// ---------------------------------------------------------------------------

export function compareFiles(a, b) {
  const minA = minify(a), minB = minify(b);
  const tokA = canonTokens(minA), tokB = canonTokens(minB);
  const canA = tokA.join(' '), canB = tokB.join(' ');
  const equivalent = canA === canB;

  let firstDiff = null;
  if (!equivalent) {
    const n = Math.max(tokA.length, tokB.length);
    for (let i = 0; i < n; i++) {
      if (tokA[i] !== tokB[i]) {
        const lo = Math.max(0, i - 8), hi = Math.min(n, i + 9);
        firstDiff = {
          index: i,
          a: tokA[i] ?? '<end>',
          b: tokB[i] ?? '<end>',
          contextA: tokA.slice(lo, hi).join(' '),
          contextB: tokB.slice(lo, hi).join(' '),
        };
        break;
      }
    }
  }
  return {
    equivalent,
    a: { file: a, minBytes: Buffer.byteLength(minA), canonBytes: Buffer.byteLength(canA), tokens: tokA.length, canon: canA },
    b: { file: b, minBytes: Buffer.byteLength(minB), canonBytes: Buffer.byteLength(canB), tokens: tokB.length, canon: canB },
    objectKeys: hasObjectKeys(minA) || hasObjectKeys(minB),
    firstDiff,
  };
}

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------

function main(argv) {
  const args = argv.filter((x) => !x.startsWith('--'));
  const flags = new Set(argv.filter((x) => x.startsWith('--')));

  const v = esbuildVersion();
  if (v !== EXPECTED_ESBUILD && !flags.has('--json')) {
    process.stderr.write(
      `\nWARNING: esbuild is ${v}; canon's minify step is pinned to ${EXPECTED_ESBUILD}.\n` +
      `The minifier IS part of the comparison. A verdict produced here is not\n` +
      `comparable with one produced under the pinned version.\n\n`);
  }

  if (flags.has('--print')) {
    process.stdout.write(canon(minify(args[0])) + '\n');
    return 0;
  }

  if (args.length !== 2) {
    process.stderr.write('usage: canon.mjs <a.js> <b.js> | --print <file.js>\n');
    return 2;
  }

  const r = compareFiles(args[0], args[1]);
  if (flags.has('--json')) {
    process.stdout.write(JSON.stringify({ ...r, esbuild: v }, null, 2) + '\n');
    return r.equivalent ? 0 : 1;
  }

  const row = (label, s) => `  ${label.padEnd(34)} ${String(s.minBytes).padStart(6)} B  ${String(s.canonBytes).padStart(6)} B  ${String(s.tokens).padStart(5)}`;
  process.stdout.write(`canon — decision 51 alpha-equivalence  (esbuild ${v})\n\n`);
  process.stdout.write(`  ${'file'.padEnd(34)} ${'minified'.padStart(6)}    ${'canon'.padStart(6)}    tokens\n`);
  process.stdout.write(row(r.a.file, r.a) + '\n');
  process.stdout.write(row(r.b.file, r.b) + '\n\n');

  if (r.objectKeys) {
    process.stdout.write('  NOTE: object-literal keys present — limitation L3 is LIVE for this pair.\n\n');
  }

  if (r.equivalent) {
    process.stdout.write('VERDICT: ALPHA-EQUIVALENT\n');
    return 0;
  }
  process.stdout.write('VERDICT: NOT EQUIVALENT\n\n');
  process.stdout.write(`  first divergence at canonical token #${r.firstDiff.index}\n`);
  process.stdout.write(`    ${r.a.file}: ${r.firstDiff.a}\n`);
  process.stdout.write(`    ${r.b.file}: ${r.firstDiff.b}\n\n`);
  process.stdout.write(`  context A: ...${r.firstDiff.contextA}...\n`);
  process.stdout.write(`  context B: ...${r.firstDiff.contextB}...\n`);
  return 1;
}

if (process.argv[1] && resolve(process.argv[1]) === resolve(fileURLToPath(import.meta.url))) {
  process.exit(main(process.argv.slice(2)));
}
