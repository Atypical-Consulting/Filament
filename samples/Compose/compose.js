/**
 * Compose — hand-written Filament app. The ANSWER KEY for baseline/Compose.Blazor/App.razor.
 *
 * Transcribed from the generator's own faithful output (readable local names here; the name-blind
 * canon gate treats wrap/greeting as alpha-equivalent to the generator's _el0/_el1).
 *
 * WHY THIS APP EXISTS: <Greeting Name="World" /> is STATIC-LEAF composition. The child Greeting.razor
 * (<span id="greeting">Hello, @Name</span>, [Parameter] string Name) is resolved as a same-directory
 * sibling, its parameter is folded to a compile-time CONSTANT ('World'), and its single static root is
 * INLINED into the parent's <div>. There is no child function, no import of a child, and no runtime
 * component instance -- Filament does at compile time what Blazor does at runtime. The composed DOM
 * (#greeting = "Hello, World") is what the DOM-contract oracle measures against Blazor.
 *
 * "Hello, " is the static prefix; the folded @Name is a second text node ("World"). Neither is
 * reactive: a static parameter never changes, so there is no signal and no effect.
 */

import { insert } from '../../src/filament-runtime/src/index.ts';

export function mount(target) {
  // <div id="wrap"> ... </div>
  const wrap = document.createElement('div');
  wrap.id = 'wrap';

  // <Greeting Name="World" /> -> the child's <span id="greeting">Hello, @Name</span>, inlined with
  // Name folded to the constant 'World'.
  const greeting = document.createElement('span');
  greeting.id = 'greeting';
  insert(greeting, document.createTextNode('Hello, '));
  insert(greeting, document.createTextNode('World'));
  insert(wrap, greeting);

  insert(target, wrap);
}
