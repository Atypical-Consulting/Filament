/**
 * Entry point for the `filament-linqorder-gen` label — the LINQ ordering/paging correctness app.
 *
 * It mounts the JS the generator emits from baseline/LinqOrder.Blazor/App.razor (OrderBy/OrderByDescending +
 * Skip/Take + First/Last over a List). Like the other -gen labels it exists so the DOM-contract oracle can click
 * #go and assert #lo "0" -> "3" and #hi "0" -> "7" -- Blazor's own LINQ behaviour, produced here by the generator
 * mapping OrderBy to a stable `[...arr].sort((__a,__b) => key(__a) - key(__b))`, Skip/Take to slices, and First/
 * Last to index terminals (decision 126). App.g.js is re-emitted every build and is not committed.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
