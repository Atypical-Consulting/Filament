/**
 * Entry point for the `filament-mixedattr-gen` label — the mixed-`class` app.
 *
 * It mounts the JS the generator emits from baseline/MixedAttr.Blazor/App.razor (a counter whose
 * #status element carries class="badge @statusClass rounded"). Like the reactiveattr label it is NOT
 * weighed or timed: it exists only so the DOM-contract oracle can click #increment and assert the
 * composed `class` string tracks state, against Blazor's own DOM.
 *
 * App.g.js IS NOT COMMITTED AND IS NOT EDITABLE: build-filament.sh deletes and re-emits it from
 * App.razor on every build, so the app the oracle loads is always this run's generator output.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
