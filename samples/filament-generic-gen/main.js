/**
 * Entry point for the `filament-generic-gen` label — the generic-component app.
 *
 * It mounts the JS the generator emits from baseline/Generic.Blazor/App.razor (a child declaring
 * @typeparam T and a [Parameter] of that type, used by the parent at T = int). Like the other
 * feature labels it is NOT weighed or timed: it exists only so the DOM-contract oracle can click
 * #inc and assert the child's #out advances 1 -> 2 -> 3 — i.e. that the erased type left a LIVE
 * binding behind (decisions 29/30, "measure it"; decision 135).
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
