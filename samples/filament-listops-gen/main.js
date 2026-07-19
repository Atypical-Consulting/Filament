/**
 * Entry point for the `filament-listops-gen` label — the List.Clear() correctness app.
 *
 * It mounts the JS the generator emits from baseline/ListOps.Blazor/App.razor (@foreach over a List
 * with Add/Clear). Like rootforeach it exists only so the DOM-contract oracle can click #add then #clear
 * and assert #list goes 3 <li> -> 0 <li> -- Clear reconciling the list to empty (decision 106).
 *
 * App.g.js IS NOT COMMITTED AND IS NOT EDITABLE: build-filament.sh deletes and re-emits it every build.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
