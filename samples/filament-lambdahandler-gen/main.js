/**
 * Entry point for the `filament-lambdahandler-gen` label — the inline lambda-handler correctness app.
 *
 * It mounts the JS the generator emits from baseline/LambdaHandler.Blazor/App.razor (@onclick is an
 * inline `() => count++`). Like counter it exists so the DOM-contract oracle can click #inc and assert
 * #count goes 0 -> 1 -> 2 -- Blazor's own behaviour, produced here by the generator TRANSLATING the
 * lambda body (count -> count.value) and emitting an arrow, not a splice (decision 105).
 *
 * App.g.js IS NOT COMMITTED AND IS NOT EDITABLE: build-filament.sh deletes and re-emits it every build.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
