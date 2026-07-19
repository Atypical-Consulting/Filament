/**
 * Entry point for the `filament-linq-gen` label — the LINQ correctness app.
 *
 * It mounts the JS the generator emits from baseline/Linq.Blazor/App.razor (`_nums.Where(x => x > 0).Count()`).
 * Like counter it exists so the DOM-contract oracle can click #go and assert #value goes "0" -> "2" -- Blazor's
 * own behaviour, produced here by the generator lowering the LINQ chain to `_nums.filter(x => x > 0).length`
 * (decision 116). The common LINQ operators are JS array methods; the source List is already an array.
 *
 * App.g.js IS NOT COMMITTED AND IS NOT EDITABLE: build-filament.sh deletes and re-emits it every build.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
