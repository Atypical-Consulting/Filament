/**
 * Entry point for the `filament-rows` / `filament-rows-stats` labels.
 *
 * The analogue of baseline/Rows.Blazor/Program.cs. See
 * samples/filament-counter/main.js for why this shim exists and why the
 * component lives at samples/Rows/rows.js rather than here.
 */

import { mount } from '../Rows/rows.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
