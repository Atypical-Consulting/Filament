/**
 * Entry point for the `filament-contentregion-gen` label — the CONTENT-REGION app.
 *
 * It mounts the JS the generator emits from baseline/ContentRegion.Blazor/App.razor: a page whose
 * three component child contents each hold TEMPLATE C# (a RenderFragment's, a cascade's and an
 * EditForm's), with the cascade sitting at the template ROOT between two siblings so its attach
 * ORDER is observable. Like the fragment/cascade/forms labels it is NOT weighed or timed: it exists
 * only so the DOM-contract oracle can drive it (decisions 29/30, "measure it"; decision 162).
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
