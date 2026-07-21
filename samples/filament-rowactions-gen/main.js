/**
 * Entry point for the `filament-rowactions-gen` label — the per-row-handler correctness app.
 *
 * It mounts the JS the generator emits from baseline/RowActions.Blazor/App.razor (a keyed @foreach whose
 * rows each carry `@onclick="() => Del(r.Id)"` — the captured-lambda row handler, decision 141). The
 * DOM-contract oracle adds two rows then clicks the FIRST row's own .del and asserts THAT row alone is
 * removed ("task 1xtask 2x"/2 -> "task 2x"/1) — Blazor's captured-lambda behaviour, produced here by
 * wiring the arrow inside the row create function, where the loop variable is the function's parameter.
 * App.g.js is re-emitted every build and is not committed.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
