// THE SCANNER-COVERAGE PROOF (decision 155).
//
// Claim: because Filament emits class strings VERBATIM, Tailwind scanning the .razor sources
// covers every utility the emitted module can ever set. This script is the claim as a gate:
// extract every class token App.g.js sets, and require each one to appear as an escaped selector
// in the derived app.css. The day someone composes a class name dynamically (the thing Tailwind's
// own docs forbid) or the emission stops being verbatim, this exits 1 with the missing tokens.
//
// Token extraction reads BOTH shapes the compiler emits:
//   setAttr(el, 'class', 'flex gap-2')                       -- static values
//   effect(() => setAttr(el, 'class', 'a ' + (c ? 'b' : 'c')))  -- folds: every string literal part
//
// Not every token is a Tailwind utility ('toggle', 'remove' are the oracle's hooks); a token is
// only REQUIRED to resolve if Tailwind derived a rule for at least one token in the same module --
// i.e. we check tokens that LOOK like utilities by asking the stylesheet, and report the ones that
// are missing while being used in a class position with utility punctuation or a known prefix.
// Simpler and stricter: a token is exempt only if it contains no '-', ':', '/', '[' AND the
// stylesheet has no rule for it -- plain hook words; everything else must resolve.

import { readFileSync } from 'node:fs';

const [jsPath, cssPath] = process.argv.slice(2);
if (!jsPath || !cssPath) {
  console.error('usage: node check-css-coverage.mjs <App.g.js> <app.css>');
  process.exit(2);
}

const js = readFileSync(jsPath, 'utf8');
const css = readFileSync(cssPath, 'utf8');

// Every string literal that participates in a class value: the 3rd argument region of a
// setAttr(_, 'class', …) call, up to the closing paren of the setAttr call. Fold expressions keep
// their literal parts ('a ' + (cond ? 'b c' : 'd')) — each quoted run contributes its tokens.
const tokens = new Set();
const re = /setAttr\([^,]+,\s*'class',\s*([\s\S]*?)\);/g;
for (const m of js.matchAll(re)) {
  for (const lit of m[1].matchAll(/'([^']*)'/g)) {
    for (const t of lit[1].split(/\s+/)) if (t) tokens.add(t);
  }
}

if (tokens.size === 0) {
  console.error(`check-css-coverage: no class tokens found in ${jsPath} — the extraction regex no longer matches the emission.`);
  process.exit(1);
}

// Tailwind v4 escapes utility punctuation in selectors: . : / [ ] etc. Build the escaped selector
// text and look it up as a substring of the stylesheet (minified or not).
const escapeSelector = (t) =>
  '.' + t.replace(/[^a-zA-Z0-9_-]/g, (c) => '\\' + c);

const missing = [];
for (const t of tokens) {
  if (css.includes(escapeSelector(t))) continue;
  const looksUtility = /[:/[\]]/.test(t) || /^-?[a-z0-9]+(-[a-z0-9[\]/:.]+)+$/i.test(t);
  if (looksUtility) missing.push(t);
}

if (missing.length > 0) {
  console.error('check-css-coverage: utilities set by the module but ABSENT from the stylesheet:');
  for (const t of missing.sort()) console.error('  ' + t);
  console.error('Either a class name is composed dynamically (write full class names), or the @source scan does not cover the sources.');
  process.exit(1);
}

console.log(`check-css-coverage: ${tokens.size} class tokens checked, all utilities resolved in ${cssPath}.`);
