/**
 * Entry point for the `filament-asyncresult-gen` label — the value-returning-async correctness app.
 *
 * It mounts the JS the generator emits from baseline/AsyncResult.Blazor/App.razor (an async Task<int> awaited
 * for its result). Like counter it exists so the DOM-contract oracle can click #go, WAIT for the awaited delay,
 * and assert #count goes 0 -> 42 -> 84 -- Blazor's own behaviour, produced here by the generator emitting an
 * async function that returns its value and `count.value = await compute()` (decision 123). Re-emitted every build.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
