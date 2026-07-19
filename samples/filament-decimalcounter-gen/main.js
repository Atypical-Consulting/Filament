/**
 * Entry point for the `filament-decimalcounter-gen` label — the decimal correctness app.
 *
 * It mounts the JS the generator emits from baseline/DecimalCounter.Blazor/App.razor (a `decimal` accumulator
 * adding 1.05m per click). Like counter it exists so the DOM-contract oracle can click #add and assert #value
 * goes "1.10" -> "2.15" -> "3.20" -- Blazor's own behaviour, produced here by the generator boxing a decimal as
 * { m: BigInt mantissa, s: scale } and doing exact base-10 arithmetic through the emitted __dec helpers
 * (decision 114). A number-backed decimal would render "1.1" (trailing zero lost) then 3.2000000000000002.
 *
 * App.g.js IS NOT COMMITTED AND IS NOT EDITABLE: build-filament.sh deletes and re-emits it every build.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
