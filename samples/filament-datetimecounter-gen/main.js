/**
 * Entry point for the `filament-datetimecounter-gen` label — the DateTime correctness app.
 *
 * It mounts the JS the generator emits from baseline/DateTimeCounter.Blazor/App.razor (a DateTime advanced by 5
 * days per click). Like counter it exists so the DOM-contract oracle can click #add and assert #value goes
 * "07/20/2026 00:00:00" -> "07/25/..." -> "07/30/..." -- Blazor's own behaviour, produced here by the generator
 * representing a DateTime as BigInt ticks, computing construction/AddDays at generate-time, and formatting the
 * display through the emitted __dtStr helper (decision 115).
 *
 * App.g.js IS NOT COMMITTED AND IS NOT EDITABLE: build-filament.sh deletes and re-emits it every build.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
