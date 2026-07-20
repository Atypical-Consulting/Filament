/**
 * Entry point for the `filament-fragment-gen` label — the RenderFragment / ChildContent app.
 *
 * It mounts the JS the generator emits from baseline/Fragment.Blazor/App.razor (a parent handing a
 * child a fragment of its own markup, which the child places inside its own element). Like the
 * compose/bound/eventcb labels it is NOT weighed or timed: it exists only so the DOM-contract oracle
 * can click #inc and assert that #body — the PARENT's markup, rendered INSIDE the child's #card —
 * still tracks the parent's count (decisions 29/30, "measure it"; decision 131).
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
