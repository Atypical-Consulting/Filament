/**
 * Entry point for the `filament-routing-gen` label — the routed, multi-page app.
 *
 * It mounts the GENERATED ROUTER (Router.g.js), which imports each page's own generated module. Unlike
 * every other label, routing is the one feature that needs code at run time, so this app is where that
 * code's cost becomes visible — see BENCH n°57, which measures it as WEIGHT rather than only asserting
 * a DOM contract (decision 139).
 *
 * Router.g.js, Home.g.js and About.g.js ARE NOT COMMITTED AND ARE NOT EDITABLE: build-filament.sh
 * deletes and re-emits them from the .razor pages on every build.
 */

import { mount } from './Router.g.js';

const app = document.getElementById('app');
// The shell ships <div id="app">Loading...</div> for parity with Blazor's; Filament has nothing
// to boot, so it clears the placeholder and mounts synchronously.
app.textContent = '';
mount(app);
