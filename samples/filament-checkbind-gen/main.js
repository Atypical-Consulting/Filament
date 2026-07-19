/**
 * Entry point for the `filament-checkbind-gen` label — the checkbox @bind correctness app.
 *
 * It mounts the JS the generator emits from baseline/CheckBind.Blazor/App.razor (@bind on a checkbox).
 * The DOM-contract oracle drives both directions -- #set flips the bool (the checkbox tracks it) and a
 * change on the checkbox flips the bool (the #status class tracks it) -- against Blazor (decision 107).
 *
 * App.g.js IS NOT COMMITTED AND IS NOT EDITABLE: build-filament.sh deletes and re-emits it every build.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
