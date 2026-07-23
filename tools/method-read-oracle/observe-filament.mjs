// observe-filament.mjs -- the FILAMENT half of the S10 measurement (register A14).
//
//   node observe-filament.mjs <bundled-module.mjs>
//
// Mounts the module Filament emitted from App.razor (bundled against the real runtime) in a happy-dom
// document, reads the two spans, dispatches a click on #inc (running the emitted listen() handler and
// its signal writes), and re-reads them. Prints the four texts as JSON -- the SAME shape the Blazor
// oracle (Program.cs) prints. run.sh drives both and asserts byte-identity. This is the repo's
// no-Playwright measurement path (the reserve BENCH n°69 / decision 164 disclosed).
import { Window } from 'happy-dom';

const win = new Window();
globalThis.window = win;
globalThis.document = win.document;

const mod = await import(process.argv[2] + '?t=' + Date.now());
mod.mount(document.body);
await new Promise((r) => setTimeout(r, 20));

const t = (id) => document.querySelector('#' + id).textContent.trim();

const nBefore = t('n');
const dBefore = t('d');

document.querySelector('#inc').click();
await new Promise((r) => setTimeout(r, 20));

const observed = {
  n_before: nBefore,
  d_before: dBefore,
  n_after: t('n'),
  d_after: t('d'),
};
console.log(JSON.stringify(observed));
