/**
 * Entry point for the `filament-reactiveattr-gen` label — the reactive-`class` app.
 *
 * It mounts the JS the generator emits from baseline/ReactiveAttr.Blazor/App.razor (a counter whose
 * #status element carries class="@statusClass"). Like the compose/divide/boundcompose labels it is NOT
 * weighed or timed: it exists only so the DOM-contract oracle can click #increment and assert the
 * reactive `class` attribute tracks state in lockstep with the text binding, against Blazor's own DOM.
 *
 * App.g.js IS NOT COMMITTED AND IS NOT EDITABLE: build-filament.sh deletes and re-emits it from
 * App.razor on every build, so the app the oracle loads is always this run's generator output.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
