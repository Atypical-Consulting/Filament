/**
 * Entry point for the `filament-forms-gen` label — the forms app.
 *
 * It mounts the JS the generator emits from baseline/Forms.Blazor/App.razor (an <EditForm> with an
 * <InputText> bound to a model property, and a submit that reads the model back). Like the other
 * feature labels it is NOT weighed or timed: it exists only so the DOM-contract oracle can type into
 * #name, assert #live follows while #out stays empty, then submit and assert #out holds the value —
 * without the page navigating (decisions 29/30, "measure it"; decision 138).
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
