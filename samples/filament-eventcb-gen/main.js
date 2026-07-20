/**
 * Entry point for the `filament-eventcb-gen` label — the EventCallback (child→parent) app.
 *
 * It mounts the JS the generator emits from baseline/EventCb.Blazor/App.razor (a parent whose state
 * is changed by a button its CHILD owns, via an EventCallback parameter). Like the compose/bound
 * labels it is NOT weighed or timed: it exists only so the DOM-contract oracle can click the child's
 * #bump and assert the parent's #out advances — the event crossing the composition boundary UPWARD
 * (decisions 29/30, "measure it"; decision 130).
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
