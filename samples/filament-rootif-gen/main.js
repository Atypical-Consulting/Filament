/**
 * Entry point for the `filament-rootif-gen` label — the root-level @if correctness app.
 *
 * It mounts the JS the generator emits from baseline/RootIf.Blazor/App.razor (a root-level @if
 * with no wrapping element). Like the divide/compose labels it is NOT weighed or timed: it exists
 * only so the DOM-contract oracle can drive #toggle and assert #cond mounts/unmounts directly on
 * #app (the mount target) — the conditional DOM Blazor produces at runtime, produced here by the
 * same list(target, ..., anchor) mapping (decisions 29/30, "measure it"; decision 89).
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
