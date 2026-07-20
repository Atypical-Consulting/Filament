/**
 * Entry point for the `filament-foreacharray-gen` label — the @foreach-over-array correctness app.
 *
 * It mounts the JS the generator emits from baseline/ForeachArray.Blazor/App.razor (a @foreach over a
 * reassigned int[]). Like the other -gen labels it exists so the DOM-contract oracle can click #add and
 * assert #list text "123" -> "34152" and its <li> count 3 -> 5 -- Blazor's own keyed-diff behaviour,
 * produced here by the generator mapping a @foreach over a signal array to list() with the source
 * `() => items.value` (decision 124). App.g.js is re-emitted every build and is not committed.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
