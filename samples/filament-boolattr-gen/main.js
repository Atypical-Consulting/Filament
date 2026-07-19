/**
 * Entry point for the `filament-boolattr-gen` label — the boolean-`disabled` app.
 *
 * It mounts the JS the generator emits from baseline/BoolAttr.Blazor/App.razor (two buttons; #target
 * carries disabled="@locked"). Like the compose/divide/boundcompose/reactiveattr labels it is NOT
 * weighed or timed: it exists only so the DOM-contract oracle can click #toggle and assert the boolean
 * `disabled` attribute goes present->absent in lockstep with Blazor's own DOM.
 *
 * App.g.js IS NOT COMMITTED AND IS NOT EDITABLE: build-filament.sh deletes and re-emits it from
 * App.razor on every build, so the app the oracle loads is always this run's generator output.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
