/**
 * Entry point for the `filament-asyncclick-gen` label — the async/await correctness app.
 *
 * It mounts the JS the generator emits from baseline/AsyncClick.Blazor/App.razor (an async Task handler that
 * awaits a delay then increments). Like counter it exists so the DOM-contract oracle can click #go, WAIT for the
 * awaited delay, and assert #count goes 0 -> 1 -> 2 -- Blazor's own behaviour, produced here by the generator
 * emitting an `async function` + `await new Promise(setTimeout)` (decision 119).
 *
 * App.g.js IS NOT COMMITTED AND IS NOT EDITABLE: build-filament.sh deletes and re-emits it every build.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
