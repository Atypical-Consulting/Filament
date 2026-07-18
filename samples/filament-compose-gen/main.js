/**
 * Entry point for the `filament-compose-gen` label — the static-leaf composition correctness app.
 *
 * It mounts the JS the generator emits from baseline/Compose.Blazor/App.razor (which composes the
 * sibling Greeting.razor). Like the divide label it is NOT weighed or timed: it exists only so the
 * DOM-contract oracle can assert #greeting renders "Hello, World" — the composed DOM Blazor produces
 * at runtime, produced here at compile time (decisions 29/30, "measure it").
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
