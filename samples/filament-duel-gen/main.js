/**
 * Entry point for the `filament-duel-gen` label — THE DUEL: the app-level head-to-head app.
 *
 * It mounts the GENERATED ROUTER (Router.g.js) over the two Duel pages compiled from
 * baseline/Duel.Blazor/Pages/ — the SAME .razor files the Blazor WASM twin compiles. The Board page
 * composes the register (EditForm/@bind-Value, keyed @foreach over a reassigned List of mutable
 * records, per-row captured-lambda handlers, sibling control-flow regions, LINQ); About carries
 * state so route re-entry proves the mounted-afresh contract. Router.g.js, Board.g.js and About.g.js
 * ARE NOT COMMITTED AND ARE NOT EDITABLE: build-filament.sh re-emits them from the .razor pages on
 * every build. The duel is measured on weight, time-to-interactive and memory (site page /benchmark).
 */

import { mount } from './Router.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
