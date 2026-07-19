/**
 * Entry point for the `filament-bind-gen` label — the two-way binding correctness app.
 *
 * It mounts the JS the generator emits from baseline/Bind.Blazor/App.razor (@bind="text" on an input,
 * #echo showing @text, a button that sets text). Like stringattrs it is NOT weighed or timed: it exists
 * only so the DOM-contract oracle can drive BOTH directions of the binding -- #set changes text (input
 * value tracks it) and a change event on the input (text/#echo track it) -- against Blazor's own rendered
 * DOM (decision 104).
 *
 * App.g.js IS NOT COMMITTED AND IS NOT EDITABLE: build-filament.sh deletes and re-emits it from
 * App.razor on every build, so the app the oracle loads is always this run's generator output.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
