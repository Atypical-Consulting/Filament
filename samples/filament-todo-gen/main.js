/**
 * Entry point for the `filament-todo-gen` label — the Tailwind todo-list.
 *
 * It mounts the JS the generator emits from baseline/Todo.Blazor/App.razor (TodoShell and
 * TodoFooter inline into the one module). Like the other feature labels it is NOT weighed or
 * timed: it exists only so the DOM-contract oracle can drive add/toggle/clear/remove and assert
 * every Tailwind className renders byte-identically vs Blazor (decisions 29/30, "measure it";
 * decisions 151/152/154).
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
