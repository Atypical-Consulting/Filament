/**
 * Entry point for the `filament-datetimenow-gen` label — the DateTime.UtcNow app.
 *
 * It mounts the JS the generator emits from baseline/DateTimeNow.Blazor/App.razor. Like the other
 * feature labels it is NOT weighed or timed: it exists only so the DOM-contract oracle can click
 * #snap and assert #out's rendered ticks sit within the ticksNearNow tolerance of the harness's
 * own wall clock (decisions 29/30, "measure it"; decision 145).
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
