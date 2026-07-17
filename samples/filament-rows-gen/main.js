/**
 * Entry point for the `filament-rows-gen` / `filament-rows-gen-stats` labels.
 *
 * THE POINT OF THIS APP: it is samples/filament-rows/main.js with ONE line
 * changed -- the import. Everything else is byte-identical, on purpose. The
 * hand-written label mounts the Phase 1 answer key (samples/Rows/rows.js); this
 * label mounts the JS the generator EMITS from baseline/Rows.Blazor/RowsApp.razor
 * -- THE VERY FILE BLAZOR COMPILES, not a copy of it. Two labels, one host, one
 * runtime, one shell, one stylesheet: the only variable is who wrote the
 * component, so the C1 delta between them is the generator's cost and nothing
 * else's.
 *
 * WHY THIS LABEL EXISTS. Decisions 21/34/50: every weight and every timing this
 * repo has published describes hand-written JS somewhere. Phase 2 wired up the
 * -gen label for Counter only, and Counter has no @foreach, no list, no records
 * and no LCG -- so C4's ACTUAL target (create / update / swap / clear on 1000
 * rows, decisions 13/15) had never been measured on compiler output at all. This
 * app is what makes that measurable.
 *
 * Rows.g.js IS NOT COMMITTED AND IS NOT EDITABLE.
 * build-filament.sh deletes and re-emits it from RowsApp.razor on every build, so
 * it cannot go stale and a measurement can never be taken on a generated file
 * somebody touched by hand. Same reasoning as Counter.g.js; see build-filament.sh's
 * header.
 */

import { mount } from './Rows.g.js';

const app = document.getElementById('app');
// The shell ships <div id="app">Loading...</div> for parity with Blazor's, whose
// runtime replaces that text once the .NET runtime has booted. Filament has
// nothing to boot, so it clears the placeholder and mounts synchronously.
app.textContent = '';
mount(app);
