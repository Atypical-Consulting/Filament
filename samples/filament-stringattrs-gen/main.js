/**
 * Entry point for the `filament-stringattrs-gen` label — the reactive string attribute names app.
 *
 * It mounts the JS the generator emits from baseline/StringAttrs.Blazor/App.razor (an <a> whose
 * href/title/aria-label are reactive). Like the reactiveattr/mixedattr labels it is NOT weighed or
 * timed: it exists only so the DOM-contract oracle can click #toggle and assert the three attributes
 * track state, against Blazor's own DOM.
 *
 * App.g.js IS NOT COMMITTED AND IS NOT EDITABLE: build-filament.sh deletes and re-emits it from
 * App.razor on every build, so the app the oracle loads is always this run's generator output.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
