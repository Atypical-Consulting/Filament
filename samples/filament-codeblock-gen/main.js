/**
 * Entry point for the `filament-codeblock-gen` label — the root @{ } code-block correctness app.
 * Static (like compose): the DOM-contract oracle asserts #out renders "7" -- the one-time local read
 * (decision 109). App.g.js is re-emitted every build, never committed.
 */
import { mount } from './App.g.js';
const app = document.getElementById('app');
app.textContent = '';
mount(app);
