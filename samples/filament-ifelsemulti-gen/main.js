/**
 * Entry point for the `filament-ifelsemulti-gen` label — the multi-node @else body correctness app.
 *
 * It mounts the JS the generator emits from baseline/IfElseMultiBody.Blazor/App.razor (an @if/@else
 * where the @if branch is one <span> and the @else branch is two). Like divide/rootif/ifmulti it is
 * NOT weighed or timed: it exists only so the DOM-contract oracle can drive #toggle and assert the
 * whole branch swaps — one node (a) out, two nodes (b, c) in, in order, as direct children of #w —
 * the conditional DOM Blazor produces at runtime, produced here by the same list(container, ...,
 * anchor) mapping with per-branch global-index ranges (decision 82/98).
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
