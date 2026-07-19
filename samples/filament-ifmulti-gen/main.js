/**
 * Entry point for the `filament-ifmulti-gen` label — the multi-node @if body correctness app.
 *
 * It mounts the JS the generator emits from baseline/IfMultiBody.Blazor/App.razor (a single-branch
 * @if whose body is two adjacent <span>s). Like divide/rootif it is NOT weighed or timed: it exists
 * only so the DOM-contract oracle can drive #toggle and assert BOTH spans mount/unmount together, in
 * order, as direct children of #w — the conditional DOM Blazor produces at runtime, produced here by
 * the same list(container, ..., anchor) mapping with one item per body node (decision 81/89).
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
