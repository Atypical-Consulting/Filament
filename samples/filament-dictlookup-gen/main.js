/**
 * Entry point for the `filament-dictlookup-gen` label — the Dictionary correctness app.
 *
 * It mounts the JS the generator emits from baseline/DictLookup.Blazor/App.razor (a Dictionary looked up by a
 * reactive key). Like counter it exists so the DOM-contract oracle can click #next and assert #value goes "one"
 * -> "two" -> "three" -> "one" -- Blazor's own behaviour, produced here by the generator mapping a
 * Dictionary<K,V> to a JS Map and `@labels[key]` to `labels.get(key.value)` (decision 118).
 *
 * App.g.js IS NOT COMMITTED AND IS NOT EDITABLE: build-filament.sh deletes and re-emits it every build.
 */

import { mount } from './App.g.js';

const app = document.getElementById('app');
app.textContent = '';
mount(app);
