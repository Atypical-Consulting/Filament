/**
 * Entry point for the `filament-sizedarray-gen` label — the sized-array correctness app.
 *
 * It mounts the JS the generator emits from baseline/SizedArray.Blazor/App.razor (a `new int[3]` reassigned to
 * a literal array). Like counter it exists so the DOM-contract oracle can click #fill and assert #len "3"->"4"
 * and #first "0"->"7" -- Blazor's own behaviour, produced here by the generator mapping `new int[3]` to
 * `new Array(3).fill(0)` and the reassignment to a literal (decision 122). App.g.js is re-emitted every build.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
