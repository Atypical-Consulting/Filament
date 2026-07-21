/**
 * Entry point for the `filament-httpjson-gen` label — the HttpClient-erased-to-fetch app.
 *
 * It mounts the JS the generator emits from baseline/HttpJson.Blazor/App.razor. Like the other
 * feature labels it is NOT weighed or timed: it exists only so the DOM-contract oracle can click
 * #load and assert the fetched rows render byte-identically vs Blazor -- both shells serve the SAME
 * static data/items.json, so the network is deterministic (decisions 29/30, "measure it";
 * decision 147).
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
