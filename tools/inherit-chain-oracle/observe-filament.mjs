// observe-filament.mjs -- the FILAMENT half of the S8 measurement (register A4).
//
//   node observe-filament.mjs <bundled-module.mjs>
//
// Mounts the module Filament emitted from App.razor (App : Mid : Grand, bundled against the real
// runtime) in a happy-dom document, reads #out, dispatches a click on #inc twice (running the emitted
// listen() handler -- the grandparent's Inc, lifted here -- and its signal write), re-reading each time.
// Prints the three texts as JSON -- the SAME shape the Blazor oracle (Program.cs) prints. run.sh drives
// both and asserts byte-identity. This is the repo's no-Playwright measurement path (the reserve
// BENCH n°69 / decision 164 disclosed).
import { Window } from 'happy-dom';

const win = new Window();
globalThis.window = win;
globalThis.document = win.document;

const mod = await import(process.argv[2] + '?t=' + Date.now());
mod.mount(document.body);
await new Promise((r) => setTimeout(r, 20));

const out = () => document.querySelector('#out').textContent.trim();

const initial = out();

document.querySelector('#inc').click();
await new Promise((r) => setTimeout(r, 20));
const afterFirst = out();

document.querySelector('#inc').click();
await new Promise((r) => setTimeout(r, 20));
const afterSecond = out();

console.log(JSON.stringify({ initial, afterFirst, afterSecond }));
