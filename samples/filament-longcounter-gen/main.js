/**
 * Entry point for the `filament-longcounter-gen` label — the long/BigInt correctness app.
 *
 * It mounts the JS the generator emits from baseline/LongCounter.Blazor/App.razor (a `long` counter whose
 * value crosses 2^53). Like counter it exists so the DOM-contract oracle can click #add and assert #value
 * goes 9007199254740990 -> 9007199254740993 -> 9007199254740996 -- Blazor's own behaviour, produced here by
 * the generator compiling `long` to BigInt (decision 112). A number-backed impl would render ...992/...994
 * (precision lost past 2^53); the >2^53 value is what PROVES the BigInt mapping.
 *
 * App.g.js IS NOT COMMITTED AND IS NOT EDITABLE: build-filament.sh deletes and re-emits it every build.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
