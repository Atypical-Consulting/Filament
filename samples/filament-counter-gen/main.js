/**
 * Entry point for the `filament-counter-gen` / `filament-counter-gen-stats` labels.
 *
 * THE POINT OF THIS APP: it is samples/filament-counter/main.js with ONE line
 * changed -- the import. Everything else is byte-identical, on purpose. The
 * hand-written label mounts the Phase 1 answer key; this label mounts the JS the
 * Phase 2 GENERATOR emitted from samples/Counter/Counter.razor. Two labels, one
 * host, one runtime, one shell, one stylesheet: the only variable is who wrote
 * the component. That is what makes the C1 delta between them attributable to
 * the generator and to nothing else.
 *
 * Decision 34/50 named this measurement as Phase 2's imposed deliverable: every
 * number the POC has published so far describes hand-written JS, and the claim
 * that carries the thesis -- "a C# generator emits this, under 10 ko, at these
 * times" -- had never been tested. This app is the thing being tested.
 *
 * Counter.g.js IS NOT COMMITTED AND IS NOT EDITABLE.
 * build-filament.sh deletes and re-emits it from Counter.razor on every build, so
 * it cannot go stale and a measurement can never be taken on a generated file
 * somebody touched by hand. It is gitignored for the same reason GateTests moves
 * its own output out of the tree: a generated file sitting in samples/ is a file
 * someone eventually edits.
 */

import { mount } from './Counter.g.js';

const app = document.getElementById('app');
// The shell ships <div id="app">Loading...</div> for parity with Blazor's, whose
// runtime replaces that text once the .NET runtime has booted. Filament has
// nothing to boot, so it clears the placeholder and mounts synchronously.
app.textContent = '';
mount(app);
