/**
 * Entry point for the `filament-moreattrs-gen` label — the attribute-allowlist-widening correctness app.
 *
 * It mounts the JS the generator emits from baseline/MoreAttrs.Blazor/App.razor: a boolean `hidden`
 * (present/absent) plus reactive string `role`, `style`, and `data-count` (data-* prefix) on #s. Like
 * stringattrs it is NOT weighed or timed: it exists only so the DOM-contract oracle can drive #toggle and
 * assert all four attributes track state, against Blazor's own rendered DOM (decision 103).
 *
 * App.g.js IS NOT COMMITTED AND IS NOT EDITABLE: build-filament.sh deletes and re-emits it from
 * App.razor on every build, so the app the oracle loads is always this run's generator output.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
