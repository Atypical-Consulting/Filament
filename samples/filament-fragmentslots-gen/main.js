/**
 * Entry point for the `filament-fragmentslots-gen` label — the NAMED-AND-NESTED-FRAGMENT app.
 *
 * It mounts the JS the generator emits from baseline/FragmentSlots.Blazor/App.razor: a child with
 * TWO fragment holes fed bare content, a named slot whose name collides with a real sibling .razor,
 * and a fragment forwarded two component levels. Like the fragment/cascade/forms labels it is NOT
 * weighed or timed: it exists only so the DOM-contract oracle can drive it (decisions 29/30,
 * "measure it"; decision 168).
 *
 * WHAT THE ORACLE IS WATCHING FOR IS PARTLY AN ABSENCE, and absences must be COUNTED, not looked
 * for: #head has to be empty, there must be exactly ONE #mark (the broken compiler emitted two, both
 * live), and #decoy must not exist at all. The presences — #slot inside #card2, #deep inside #inner —
 * are checked with containment, and all three counters must advance together on one #inc, which is
 * what proves the forwarded and the named content kept the GRANDPARENT's scope.
 *
 * App.g.js IS NOT COMMITTED AND IS NOT EDITABLE: build-filament.sh deletes and re-emits it from
 * App.razor on every build, so the app the oracle loads is always this run's generator output.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
// The shell ships <div id="app">Loading...</div> for parity with Blazor's; Filament has nothing
// to boot, so it clears the placeholder and mounts synchronously.
app.textContent = '';
mount(app);
