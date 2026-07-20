/**
 * Entry point for the `filament-inherits-gen` label — the @inherits app.
 *
 * It mounts the JS the generator emits from baseline/Inherits.Blazor/App.razor (a component whose
 * state and behaviour live in a sibling base component). Like the other feature labels it is NOT
 * weighed or timed: it exists only so the DOM-contract oracle can click #inc — the BASE's method —
 * and assert #out, the BASE's field, advances 0 -> 1 -> 2 (decisions 29/30, "measure it";
 * decision 136).
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
