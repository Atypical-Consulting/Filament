/**
 * Entry point for the `if` demo app (demo/build.sh).
 *
 * Mirrors samples/filament-rows-gen/main.js: it mounts the JS the generator
 * EMITS from samples/If/If.razor, not the hand-written answer key (samples/If/if.js).
 * One host, one runtime, one mount call -- the only variable is who wrote the
 * component, and here it is the compiler.
 *
 * If.g.js IS NOT COMMITTED AND IS NOT EDITABLE.
 * demo/build.sh deletes and re-emits it from If.razor on every build, so it can
 * never go stale and the demo can never mount a generated file somebody hand-edited.
 * Same reasoning as Counter.g.js / Rows.g.js; see bench/build-filament.sh's header.
 */

import { mount } from './If.g.js';

const app = document.getElementById('app');
// Parity with the other apps' shell: clear the "Loading…" placeholder, then mount.
app.textContent = '';
mount(app);
