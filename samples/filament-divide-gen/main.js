/**
 * Entry point for the `filament-divide-gen` label — the double-division correctness app.
 *
 * It mounts the JS the generator emits from baseline/Divide.Blazor/App.razor. Unlike the
 * counter/rows -gen labels, this one is NOT weighed or timed: it exists only so the DOM-contract
 * oracle can drive #halve and assert #divide-value goes 7 -> 3.5 (see bench.mjs's `divide` app).
 * 3.5 is a value integer division (== 3) could never produce, so a generator that emitted the
 * wrong `/` renders the wrong number and the oracle catches it (decisions 29/30, "measure it").
 *
 * Divide.g.js IS NOT COMMITTED AND IS NOT EDITABLE: build-filament.sh deletes and re-emits it from
 * App.razor on every build, so the app the oracle loads is always this run's generator output.
 */

import { mount } from './Divide.g.js';

const app = document.getElementById('app');
// The shell ships <div id="app">Loading...</div> for parity with Blazor's; Filament has nothing
// to boot, so it clears the placeholder and mounts synchronously.
app.textContent = '';
mount(app);
