/**
 * Entry point for the `filament-loops-gen` label — the loop/switch statement correctness app.
 *
 * It mounts the JS the generator emits from baseline/Loops.Blazor/App.razor (handlers using while,
 * switch and do-while). Like counter/divide it exists so the DOM-contract oracle can click each button
 * and assert #v goes 0 -> 5 -> 9 -> 3 — the values Blazor's own while/switch/do-while produce, produced
 * here by the generator emitting the JS namesakes (decision 102).
 *
 * Loops.g.js IS NOT COMMITTED AND IS NOT EDITABLE: build-filament.sh deletes and re-emits it from
 * App.razor on every build, so the app the oracle loads is always this run's generator output.
 */

import { mount } from './Loops.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
