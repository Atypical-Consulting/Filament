/**
 * Entry point for the `filament-foreachdict-gen` label — the @foreach-over-Dictionary correctness app.
 *
 * It mounts the JS the generator emits from baseline/ForeachDict.Blazor/App.razor (a @foreach over a reassigned
 * Dictionary). Like the other -gen labels it exists so the DOM-contract oracle can click #bump and assert #list
 * text "a=1b=2" -> "b=20c=3a=1" -- Blazor's own keyed diff AND value-refresh behaviour, produced here by the
 * generator spreading the Map signal (`() => [...scores.value]`) into list() and compiling @kvp.Value to the
 * reactive lookup `scores.value.get(kvp[0])` (decision 125). App.g.js is re-emitted every build, not committed.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
