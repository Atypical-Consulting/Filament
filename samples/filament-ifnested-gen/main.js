/**
 * Entry point for the `filament-ifnested-gen` label — the nested @if correctness app.
 *
 * It mounts the JS the generator emits from baseline/IfNested.Blazor/App.razor (an @if(other) inside
 * an @if(show) branch). Like divide/rootif/ifmulti it is NOT weighed or timed: it exists only so the
 * DOM-contract oracle can drive the two toggles and assert #a is present iff show && other — the
 * conditional DOM Blazor produces at runtime, produced here by the same list(container, ..., anchor)
 * mapping with a decision-tree source (decision 81/89).
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
