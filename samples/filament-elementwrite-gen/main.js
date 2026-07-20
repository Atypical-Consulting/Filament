/**
 * Entry point for the `filament-elementwrite-gen` label — the mutable-element-write correctness app.
 *
 * It mounts the JS the generator emits from baseline/ElementWrite.Blazor/App.razor (a reactive array + Dictionary
 * whose elements are written). Like the other -gen labels it exists so the DOM-contract oracle can click #go and
 * assert #a "20" -> "25" and #m "2" -> "102" -- Blazor's own behaviour, produced here by the generator compiling
 * xs[1] = ... to `xs.value = xs.value.with(1, ...)` and scores["b"] = ... to
 * `scores.value = new Map(scores.value).set("b", ...)` (copy-on-write so the signal fires, decision 127).
 * App.g.js is re-emitted every build and is not committed.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
