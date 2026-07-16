/**
 * Entry point for the `filament-counter` / `filament-counter-stats` labels.
 *
 * This is the analogue of baseline/Counter.Blazor/Program.cs, and it is
 * deliberately as small as that file is:
 *
 *     builder.RootComponents.Add<App>("#app");
 *
 * The component itself lives at samples/Counter/counter.js, colocated with where
 * Counter.razor will live in Phase 2/3 so the generator's emitted JS can be
 * snapshot-compared against it in place. The split is the same one Blazor makes:
 * Program.cs is the host, App.razor is the component, and only the component is
 * something a compiler emits.
 *
 * build-filament.sh requires the entry at samples/filament-<app>/main.js, which
 * is why this shim exists rather than the app being built directly.
 */

import { mount } from '../Counter/counter.js';

const app = document.getElementById('app');
// The shell ships <div id="app">Loading...</div> for parity with Blazor's, whose
// runtime replaces that text once the .NET runtime has booted. Filament has
// nothing to boot, so it clears the placeholder and mounts synchronously.
app.textContent = '';
mount(app);
