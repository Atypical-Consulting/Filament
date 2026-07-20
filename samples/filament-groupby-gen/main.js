/**
 * Entry point for the `filament-groupby-gen` label — the LINQ GroupBy correctness app.
 *
 * It mounts the JS the generator emits from baseline/GroupBy.Blazor/App.razor (GroupBy + IGrouping over a List).
 * Like the other -gen labels it exists so the DOM-contract oracle can click #go and assert #g "2" / #k "1" / #s "4"
 * -- Blazor's own GroupBy behaviour, produced here by the generator compiling GroupBy(x=>key) to a reduce into a
 * Map<K, group> (each group a JS array-with-.key) and g.Key to g.key (decision 128). App.g.js is re-emitted every
 * build and is not committed.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
