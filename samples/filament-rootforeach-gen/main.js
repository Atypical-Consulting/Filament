/**
 * Entry point for the `filament-rootforeach-gen` label — the root-level @foreach correctness app.
 *
 * It mounts the JS the generator emits from baseline/RootForeach.Blazor/App.razor (a root-level
 * @foreach with no wrapping element). Like the divide/compose labels it is NOT weighed or timed:
 * it exists only so the DOM-contract oracle can click #add and assert three <li> reconcile
 * directly INTO #app (the mount target) — the composed DOM Blazor produces at runtime, produced
 * here by the same list(target, ...) mapping (decisions 29/30, "measure it"; decision 89).
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
