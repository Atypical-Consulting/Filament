// observe-filament.mjs -- the FILAMENT half of the S9 measurement (register A13/A15).
//
//   node observe-filament.mjs <bundled-module.mjs>
//
// Mounts the module Filament emitted from App.razor (bundled against the real runtime) in a happy-dom
// document and prints the four measured spans' text as JSON -- the SAME shape the Blazor oracle
// (Program.cs) prints. run.sh drives both and asserts byte-identity. This is the repo's no-Playwright
// measurement path (the reserve BENCH n°69 / decision 164 disclosed).
import { Window } from 'happy-dom';

const win = new Window();
globalThis.window = win;
globalThis.document = win.document;

const mod = await import(process.argv[2] + '?t=' + Date.now());
mod.mount(document.body);
await new Promise((r) => setTimeout(r, 20));

const t = (id) => document.querySelector('#' + id).textContent.trim();
const observed = { flag_t: t('flag_t'), flag_f: t('flag_f'), gen_a: t('gen_a'), gen_b: t('gen_b') };
console.log(JSON.stringify(observed));
