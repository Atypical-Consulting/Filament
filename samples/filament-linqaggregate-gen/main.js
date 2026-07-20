/**
 * Entry point for the `filament-linqaggregate-gen` label — the LINQ-aggregate correctness app.
 *
 * It mounts the JS the generator emits from baseline/LinqAggregate.Blazor/App.razor (Where().Sum() and Max()).
 * Like counter it exists so the DOM-contract oracle can click #go and assert #sum -> "21" and #max -> "9" --
 * Blazor's own behaviour, produced here by the generator lowering the LINQ aggregates to JS array reductions
 * (decision 121). App.g.js is NOT committed and is re-emitted every build.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
