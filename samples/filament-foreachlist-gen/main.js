/**
 * Entry point for the `filament-foreachlist-gen` label — the @foreach-over-reassigned-List correctness app.
 *
 * It mounts the JS the generator emits from baseline/ForeachList.Blazor/App.razor (a @foreach over a
 * List<int> that is never mutated in place, only reassigned wholesale). Like the other -gen labels it
 * exists so the DOM-contract oracle can click #add and assert #list text "123" -> "3415" and its <li>
 * count 3 -> 4 (key 2 removed, 4/5 inserted, 1/3 moved) -- Blazor's own keyed-diff behaviour, produced
 * here by the generator mapping the reassigned List to list() with the same collapsed source a
 * reassigned T[] gets, `() => items.value` (decision 140, the List twin of 124). App.g.js is re-emitted
 * every build and is not committed.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
