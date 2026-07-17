/**
 * Counter — hand-written Filament app. Phase 1 reference.
 *
 * This file is the ANSWER KEY. Phase 2's generator consumes baseline's
 * Counter.Blazor/App.razor and its emitted JS is snapshot-tested against what is
 * written here, so every line below is written the way a COMPILER would emit it,
 * not the way a human would prefer it.
 *
 * The source it corresponds to (baseline/Counter.Blazor/App.razor), TRANSCRIBED
 * EXACTLY -- the blank lines between the three siblings are part of the source and
 * are NOT decoration here; see "THE WHITESPACE TEXT NODES" below:
 *
 *     <h1 id="title">Counter</h1>
 *
 *     <p>Current count: <span id="counter-value">@currentCount</span></p>
 *
 *     <button id="increment" @onclick="Increment">Click me</button>
 *
 *     @code {
 *         private int currentCount = 0;
 *         private void Increment() { currentCount++; }
 *     }
 *
 * THE SHAPE, and why it is this shape:
 *
 *   create()   builds the element tree ONCE, imperatively. Static structure is
 *              static code — there is no template string to parse at runtime, no
 *              vdom to allocate, and nothing to diff. A `<h1 id="title">Counter</h1>`
 *              whose content never varies compiles to createElement + a Text node
 *              and is then never looked at again.
 *
 *   effect()   one per BINDING POINT. There is exactly one here (@currentCount),
 *              so there is exactly one effect. It closes over `t` — the Text node
 *              inside #counter-value — at create time, which is what makes an
 *              increment cost one `t.data = v` and nothing else. (C3.)
 *
 * WHY THE INTERPOLATION GETS ITS OWN TEXT NODE.
 * `<p>Current count: <span id="counter-value">@currentCount</span></p>` has a
 * static prefix and a dynamic span. The span's child is a Text node created once
 * and handed to setText forever. Writing `span.textContent = v` instead would
 * destroy and rebuild the span's children on every increment — 2 DOM writes
 * (remove + add) where the contract allows 1, and C3 would fail on markup that
 * looks identical.
 *
 * THE WHITESPACE TEXT NODES -- A CORRECTION, AND WHY IT IS NOT THE FORBIDDEN EDIT.
 *
 * This file used to build THREE child nodes of #app: h1, p, button. Blazor builds
 * FIVE from the same source, because App.razor has blank lines between those three
 * siblings and Razor turns them into real "\n\n" text nodes. Verified twice, from
 * the artifact rather than from anyone's reading:
 *
 *   - .NET 10's own generated BuildRenderTree for App.razor calls
 *         AddMarkupContent(0, "<h1 id=\"title\">Counter</h1>\n\n")
 *         AddMarkupContent(6, "\n\n")
 *   - the served blazor-counter-nojit bundle, measured in Chrome:
 *         #app.childNodes = ["<!--!-->", "<h1#title>", "\n\n", "<p>", "<!--!-->",
 *                            "\n\n", "<button#increment>"]     -> 7
 *
 * Measured the same way: generator 5, this file (before the correction) 3. The
 * GENERATOR was right and THIS FILE was the artifact that diverged. The header
 * above used to transcribe the source WITHOUT its blank lines, which is almost
 * certainly how they were lost. Phase 1 already knew the rule -- RowsApp.razor
 * keeps its row markup on ONE LINE precisely so that no stray text node appears,
 * and its comment says so, naming the Blazor marker too. Counter never got the
 * same treatment.
 *
 * DECISIONS 21/51 SAY: NEVER EDIT THE ANSWER KEY TO MAKE THE GATE PASS. This edit
 * is made anyway, by the owner's explicit decision, and the distinction is the
 * whole point: the motive is THE CONTRACT, not the gate. A DOM contract that is not
 * actually shared invalidates every C4 comparison built on it, and a reference that
 * renders FEWER nodes than the baseline hands Filament a free create-time advantage
 * Blazor pays for. The gate narrowing is a SIDE EFFECT, and it does not even make
 * the gate pass -- divergence #1 (the handler) is untouched and the gate stays RED.
 * The honesty test this had to survive: it would be made even if it made the
 * generator look WORSE. It does make this file bigger (~+11 B gzip on the
 * filament-counter bundle, which the Measure phase re-measures; no published figure
 * is hand-edited).
 *
 * THE RESIDUAL, DISCLOSED AND NOT BANKED. Even corrected, this is 5 nodes against
 * Blazor's 7. The 2 extra are `<!--!-->` COMMENT markers (nodeType 8, data "!"),
 * one emitted per AddMarkupContent call -- Blazor's own bookkeeping for locating a
 * raw-markup range later. They are not content the source declares, and Filament
 * has no render tree, no diffing and nothing to locate later, so it has nothing to
 * mark: reproducing them would mean shipping two comment nodes whose only purpose
 * is to support the machinery this POC exists to do without. Filament COULD emit
 * them; it should not. But 5 < 7 is still a real, free, create-time advantage, and
 * it stays decision 20's OPEN debt -- disclosed here so it is not quietly banked.
 *
 * WHAT IS DELIBERATELY ABSENT: no diffing, no reconciliation, no render tree, no
 * component instance, no lifecycle. An increment is: flag walk over one edge,
 * one queue pop, one character-data write. That is the entire thesis.
 */

import { signal, effect, setText, listen, insert } from '../../src/filament-runtime/src/index.ts';

/**
 * Compiled render for App.razor.
 *
 * @param {Node} target the mount point (#app), standing in for
 *   Program.cs's `builder.RootComponents.Add<App>("#app")`.
 */
export function mount(target) {
  // -- @code { private int currentCount = 0; } -------------------------------
  // A private field READ by the template is reactive state; the compiler lifts it
  // to a Signal. `currentCount++` in C# maps to `currentCount.value++` — one node
  // in, one node out, no syntactic desugaring. (See core.ts's header on why the
  // public surface is `.value` and not `currentCount()`.)
  const currentCount = signal(0);

  // -- create(): the static tree ---------------------------------------------

  // <h1 id="title">Counter</h1>
  const h1 = document.createElement('h1');
  h1.id = 'title';
  insert(h1, document.createTextNode('Counter'));

  // <p>Current count: <span id="counter-value">@currentCount</span></p>
  const p = document.createElement('p');
  insert(p, document.createTextNode('Current count: '));
  const span = document.createElement('span');
  span.id = 'counter-value';
  // THE binding point. Created empty and never replaced; the effect below owns
  // its `.data` from here on.
  const t = document.createTextNode('');
  insert(span, t);
  insert(p, span);

  // <button id="increment" @onclick="Increment">Click me</button>
  const button = document.createElement('button');
  button.id = 'increment';
  insert(button, document.createTextNode('Click me'));

  // -- the binding: @currentCount --------------------------------------------
  // Runs once immediately (writing "0", which is why the initial DOM already
  // satisfies the contract without a separate initialisation path), then exactly
  // once per real change to currentCount.
  effect(() => setText(t, currentCount.value));

  // -- the handler: @onclick="Increment" -------------------------------------
  // `private void Increment() { currentCount++; }`. No batch(): the body performs
  // exactly one write, so there is nothing to coalesce and a batch would only add
  // a try/finally. The write flushes synchronously inside dispatchEvent, so the
  // harness's MutationObserver observes a DOM that is already final.
  listen(button, 'click', () => {
    currentCount.value++;
  });

  // -- attach ----------------------------------------------------------------
  // Last, and on purpose: the effect's first run above wrote into a DETACHED
  // tree, so it produced no MutationRecord. Everything the observer sees at
  // startup is these five inserts, and every increment thereafter is the single
  // characterData write. Attaching first would work identically but would make
  // the create-time write and the update-time write indistinguishable in a trace.
  //
  // The two "\n\n" nodes are the blank lines App.razor has between its three
  // siblings. Blazor ships them; so does the generator; so, now, does this file.
  // See "THE WHITESPACE TEXT NODES" in the header: they are the SHARED DOM
  // CONTRACT, not formatting, and they are easy to drop without noticing --
  // which is exactly what happened here.
  insert(target, h1);
  insert(target, document.createTextNode('\n\n'));
  insert(target, p);
  insert(target, document.createTextNode('\n\n'));
  insert(target, button);
}
