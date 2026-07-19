/**
 * Entry point for the `filament-arrayindex-gen` label — the array correctness app.
 *
 * It mounts the JS the generator emits from baseline/ArrayIndex.Blazor/App.razor (an int[] indexed by a
 * reactive index). Like counter it exists so the DOM-contract oracle can click #next and assert #value goes
 * "10" -> "20" -> "30" -> "10" -- Blazor's own behaviour, produced here by the generator mapping a T[] to a JS
 * array literal and `@items[i]` to `items[i.value]` (decision 117).
 *
 * App.g.js IS NOT COMMITTED AND IS NOT EDITABLE: build-filament.sh deletes and re-emits it every build.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
