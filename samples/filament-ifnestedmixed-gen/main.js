/**
 * Entry point for the `filament-ifnestedmixed-gen` label — the mixed-@if-branch correctness app.
 *
 * It mounts the JS the generator emits from baseline/IfNestedMixed.Blazor/App.razor (a branch mixing markup with
 * a nested @if). Like counter it exists so the DOM-contract oracle can toggle #s/#o and assert #w mounts/unmounts
 * the right nodes -- Blazor's own behaviour, produced here by the generator spreading the nested @if's active
 * indices beside the constant markup leaf (decision 120).
 *
 * App.g.js IS NOT COMMITTED AND IS NOT EDITABLE: build-filament.sh deletes and re-emits it every build.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
