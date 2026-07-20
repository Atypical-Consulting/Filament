/**
 * Entry point for the `filament-jsinterop-gen` label — the JS-interop app.
 *
 * It mounts the JS the generator emits from baseline/JsInterop.Blazor/App.razor (a component that
 * writes to localStorage and reads it back through `@inject IJSRuntime`). Like the other feature
 * labels it is NOT weighed or timed: it exists only so the DOM-contract oracle can click #go and
 * assert #out becomes "hello" AND that localStorage actually holds it (decisions 29/30, "measure
 * it"; decision 133).
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
