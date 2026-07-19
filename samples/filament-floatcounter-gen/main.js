/**
 * Entry point for the `filament-floatcounter-gen` label — the float correctness app.
 *
 * It mounts the JS the generator emits from baseline/FloatCounter.Blazor/App.razor (a `float` accumulator
 * adding 0.2f per click). Like counter it exists so the DOM-contract oracle can click #add and assert #value
 * goes "0.1" -> "0.3" -> "0.5" -- Blazor's own behaviour, produced here by the generator rounding every float
 * op to single precision (Math.fround) and formatting the display through the emitted __f32 helper (decision
 * 113). A number-backed float would render "0.30000000000000004"; the fround + formatter is what makes it "0.3".
 *
 * App.g.js IS NOT COMMITTED AND IS NOT EDITABLE: build-filament.sh deletes and re-emits it every build.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
