/**
 * Entry point for the `filament-trylock-gen` label — the try/catch/throw/lock correctness app.
 *
 * It mounts the JS the generator emits from baseline/TryLock.Blazor/App.razor (the #go handler throws
 * inside a try, catches it, then runs a lock body). Like counter it exists so the DOM-contract oracle
 * can click #go and assert #count goes 0 -> 6 -> 12 -- Blazor's own behaviour, produced here by the
 * generator mapping try/catch to the JS namesake, `throw new Exception` to `throw new Error`, and lock
 * to a bare block (decision 110).
 *
 * App.g.js IS NOT COMMITTED AND IS NOT EDITABLE: build-filament.sh deletes and re-emits it every build.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
