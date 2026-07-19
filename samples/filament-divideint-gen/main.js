/**
 * Entry point for the `filament-divideint-gen` label — the integer-division correctness app.
 *
 * It mounts the JS the generator emits from baseline/DivideInt.Blazor/App.razor. Like divide it is NOT
 * weighed or timed: it exists only so the DOM-contract oracle can click #halve and assert #divide-value
 * goes 7 -> 3 (integer division truncates), the value Blazor's own int division produces — produced here
 * by the generator emitting Math.trunc(value / 2), not a bare `/` (which would render 3.5).
 *
 * DivideInt.g.js IS NOT COMMITTED AND IS NOT EDITABLE: build-filament.sh deletes and re-emits it from
 * App.razor on every build, so the app the oracle loads is always this run's generator output.
 */

import { mount } from './DivideInt.g.js';

const app = document.getElementById('app');
// The shell ships <div id="app">Loading...</div> for parity with Blazor's; Filament has nothing
// to boot, so it clears the placeholder and mounts synchronously.
app.textContent = '';
mount(app);
