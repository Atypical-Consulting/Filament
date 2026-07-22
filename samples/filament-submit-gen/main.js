/**
 * Entry point for the `filament-submit-gen` label — the SUBMIT-CONTRACT app.
 *
 * It mounts the JS the generator emits from baseline/Submit.Blazor/App.razor: four forms, each a
 * different path to a submit listener — a plain <form @onsubmit> on a @code method, an <EditForm
 * Model> with NO OnValidSubmit, a form carrying @onsubmit AND @onkeydown on the same element, and a
 * form whose handler is an inline lambda. Like the fragment/cascade/forms labels it is NOT weighed or
 * timed: it exists only so the DOM-contract oracle can drive it (decisions 29/30, "measure it";
 * decision 165).
 *
 * WHAT THE ORACLE IS WATCHING FOR HERE IS AN ABSENCE. Three of these four forms used to NAVIGATE on
 * submit, so the page reloaded and this very file re-ran from scratch — which is why the contract
 * plants a marker on `window` before each click and asserts it survives. A DOM assert alone reads the
 * right values off a document that is already on its way out.
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
