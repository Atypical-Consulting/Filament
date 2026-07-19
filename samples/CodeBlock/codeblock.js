/**
 * CodeBlock — hand-written Filament answer key for baseline/CodeBlock.Blazor/App.razor.
 *
 * A root @{ } code block (decision 109): `int total = 3 + 4;` is a LOCAL declaration that runs ONCE in
 * mount() -- a one-time `const total = 3 + 4;` -- and @total reads it (static: total never changes, so no
 * effect, just createTextNode(total)). The initial render IS the measurement (like compose).
 */

import { insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  const total = 3 + 4;

  const p = document.createElement('p');
  insert(p, document.createTextNode('Total: '));
  const out = document.createElement('span');
  out.id = 'out';
  insert(out, document.createTextNode(total));
  insert(p, out);
  insert(target, p);
}
