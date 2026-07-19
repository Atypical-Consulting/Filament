/**
 * Entry point for the `filament-positionalrecord-gen` label — the positional-record correctness app.
 *
 * It mounts the JS the generator emits from baseline/PositionalRecord.Blazor/App.razor (a @foreach over a
 * List of positional records, #add appends one). Like rows it exists so the DOM-contract oracle can click
 * #add and assert #list goes from one <li> to two -- Blazor's own behaviour, produced here by the generator
 * compiling a positional `record Item(string Name, int Rank)` to an object literal and mapping its inline
 * construction (new Item("beta", 2) -> { name: 'beta', rank: 2 }) by constructor order (decision 111).
 *
 * App.g.js IS NOT COMMITTED AND IS NOT EDITABLE: build-filament.sh deletes and re-emits it every build.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
