/**
 * Entry point for the `filament-intbind-gen` label — the int @bind correctness app.
 *
 * The DOM-contract oracle drives #set (field->input), a VALID entry, an INVALID entry, and an OVERFLOW
 * entry -- the last two must REVERT (keep the field), verifying the int.TryParse-mirroring parse against
 * Blazor's own BindConverter (decision 108). App.g.js is re-emitted every build, never committed.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
