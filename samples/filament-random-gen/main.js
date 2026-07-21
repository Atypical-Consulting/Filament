/**
 * Entry point for the `filament-random-gen` label — the System.Random app.
 *
 * It mounts the JS the generator emits from baseline/Rand.Blazor/App.razor. Like the other feature
 * labels it is NOT weighed or timed: it exists only so the DOM-contract oracle can drive #roll --
 * the SEEDED side, whose sums 5 -> 6 -> 7 must match the real BCL's Random(42) byte-for-byte, which
 * IS the faithfulness proof of the emitted Knuth-subtractive __rnd -- and #shared, the unseeded
 * Random.Shared side, asserted by RANGE (decisions 29/30, "measure it"; decision 146).
 *
 * App.g.js IS NOT COMMITTED AND IS NOT EDITABLE: build-filament.sh deletes and re-emits it from
 * App.razor on every build, so the app the oracle loads is always this run's generator output.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
// The shell ships <div id="app">Loading...</div> for parity with Blazor's; Filament has nothing
// to boot, so it clears the placeholder and mounts synchronously.
app.textContent = '';
mount(app);
