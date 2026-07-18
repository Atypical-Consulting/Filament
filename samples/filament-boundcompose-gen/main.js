/**
 * Entry point for the `filament-boundcompose-gen` label — the bound-parameter composition app.
 *
 * It mounts the JS the generator emits from baseline/BoundCompose.Blazor/App.razor (a parent counter
 * passing count to a composed child as a BOUND parameter). Like the compose/divide labels it is NOT
 * weighed or timed: it exists only so the DOM-contract oracle can click #inc and assert the composed
 * child's #out tracks the parent's count — the reactive plumbing crossing the composition boundary
 * (decisions 29/30, "measure it"; decision 90).
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
