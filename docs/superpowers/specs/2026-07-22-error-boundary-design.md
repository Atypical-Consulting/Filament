# `<ErrorBoundary>` — a latch and a conditional, and what it deliberately does not catch

**Status:** shipped · **Date:** 2026-07-22 · **Decision:** DECISIONS.md #164 · **Measurement:** BENCH n°70

> This slice was scoped by measurement twice, and both times the measurement contradicted the plan.
> The first contradiction reframed the feature; the second removed a capability from it. Both are
> recorded here, because the reasoning that survived is only trustworthy next to the reasoning that
> did not.

## The finding that reframed it

Error boundaries were a documented non-goal, on the argument that "the platform default *is* the
boundary". Before writing any mapping, the question *what does a Blazor boundary actually catch?* was
put to the real Blazor renderer — not to the documentation. `Renderer.HandleExceptionViaErrorBoundary`
is ordinary .NET, so `tools/error-boundary-oracle` hosts the real `Renderer`, the real `ErrorBoundary`
component and real event dispatch, headless. **No browser is needed**, which is what made this slice
measurable at all: Playwright is not installable in this environment (BENCH n°69's open reserve).

| witness | who throws | Blazor | reachable in Filament? |
|---|---|---|---|
| W1 | an event handler the **parent** owns, written inside the boundary | **NOT caught** | yes |
| W2 | a **child component's** event handler | caught | no — `[composition-out-of-subset]` |
| W3 | a **child component's** `OnInitialized` | caught | no — same |
| W4 | the parent, while **evaluating the content** | caught | **yes** |
| W5 | the same on **re-render** | caught; latch **sticky**; outside stays live | partly (see below) |
| W6 | nothing — no `ErrorContent` | `<div class="blazor-error-boundary">` | yes |

**W1 is the result that matters.** It is the shape every author expects a boundary to catch, and
Blazor does not catch it: a boundary catches what its **descendants** raise, and a handler written in
the boundary's child content belongs to the component that *wrote* the fragment — an ancestor. **W2 is
W1's control**: the same throw, the same place on screen, raised by a descendant, and caught. So the
harness distinguishes the outcomes; "caught" is not a verdict it returns unconditionally.

W2/W3 need a stateful child, which the composition subset refuses — the same argument that closed
`IDisposable`. **W4 is therefore the entirety of what a Filament boundary can faithfully catch**, and
a faithful boundary must *let W1 propagate*, because catching it would render an error page where
Blazor tears the app down.

## The mapping

Everything inlines into one `mount()`, so no boundary component survives. What remains is two things
that already existed:

1. `signal(null)` — the latch. `null` **is** Blazor's `CurrentException`.
2. one `list()` over a comment anchor, whose active key set is the content while the latch is null
   and the error UI once it is not — **`@if`/`@else`'s own shape** (decisions 81/82).

```js
const _ebErr0 = signal(null);
const _ebAnchor0 = document.createComment('');
insert(_el0, _ebAnchor0);

function _ebBody0_0() {
  try { /* …content… */ return _el2; }
  catch (_e) { _ebErr0.value ??= _e; return document.createTextNode(''); }
}
function _ebError0_1() { /* …ErrorContent… */ }

list(_el0, () => _ebErr0.value === null ? [0] : [1], (i) => i,
     (i) => i === 0 ? _ebBody0_0() : _ebError0_1(), _ebAnchor0);
```

**`??=`, not `=`.** Measured on the Blazor side (W5): with the content throwing a *different* message
each re-render, `ErrorContent` keeps showing the **first**, and the logger fires **once**. Stickiness
also falls out structurally — flipping the latch changes the key set, which unmounts the content rows
and disposes their effects, so the content is never evaluated again.

**The guard is inside the branch function, not around it.** The throw happens while `list()`'s
reconcile is calling `create()`, midway through rebuilding its row array; letting it escape would
corrupt the list. Catching inside keeps `create()`'s contract — it always returns a node.

### S16 is answered by keying, not by breaking the freeze

The non-goals register flagged **S16** as the one slice that might require changing the frozen
runtime, because "a fragment is N top-level nodes while a `list()` row owns exactly one". It did not
require it: **each top-level node of a slot becomes its own leaf key in the same `list()`**, which is
already how a multi-node `@if` body compiles (decision 120). `git diff -- src/filament-runtime` is
**empty**.

## The second contradiction — and the capability it removed

The guard was first placed **inside the effect closure**, so that a throw on **re-render** (W5) would
be caught too. It is not caught. A `computed` is refreshed by `checkDirty()` **from inside `flush()`**
— the runtime is still deciding whether the binding is dirty and has not entered it — so the throw
crosses no guard wrapped around that binding and is re-thrown at the write site (decision 38):

```
flush → checkDirty → refresh → recompute → Computed.fn → throw     (latch still null)
```

The unit test that "validated" the design passed because it had **no computed in the chain**. The
compiled app had one. Blazor **catches** this case, so shipping it would have been a boundary that
*looks* like a guard and is not one — section 10's silent mis-compile, in the one construct whose
entire purpose is to be trustworthy when something goes wrong.

**So content that reads a computed is refused**, with a located diagnostic that says why. Closing it
properly would need per-effect error ownership in the runtime — i.e. the byte freeze — which is a
separate decision and was not taken.

## Refused, each for a measured reason

| construct | why |
|---|---|
| content reading a `computed` | the throw never reaches the guard (above) |
| a nested boundary | "which boundary owns this throw" is an arbitration nothing measured |
| a second boundary in one component | `context` is bound to a latch named **before** translation; two would make one C# name mean two things by declaration order |
| a boundary at the template root | it emits no element, so its anchor is laid down with the tree while root-level siblings attach after — content would render ahead of markup written before it (the reordering `<CascadingValue>` already refuses there) |
| `MaximumErrorCount` | it counts errors across re-renders; this boundary latches the first and never re-evaluates, so there is no second error to count |
| `Context="…"` | the name is what binds the value at compile time here |
| a bare `@context` | Blazor renders `Exception.ToString()` — CLR type name, message, CLR stack — where a JS `Error` stringifies to `"Error: boom"`. Not the same text |
| `Exception` members other than `.Message` | only `.Message` has a direct JS twin (`Error.prototype.message`) |
| an author member named `context` | it would be read instead of the exception, silently, and only inside the boundary — refused at the declaration, like decision 163's route-parameter collision |

## Two gates, neither subsuming the other

- **`canon`** decides the bytes: **ALPHA-EQUIVALENT**, 760 B minified / 272 tokens both sides.
- **`tools/error-boundary-contract.mjs`** decides the behaviour: 5 steps run in a DOM on the emitted
  bytes, each with a control that must **break** it.

**The contract corrected two of its own controls**, which is what controls are for. One removed the
guard and broke the *syntax* — it "failed" the step by not compiling, so it measured the regex, not
the mapping. Another froze the swap to prove `#in` absent — but with the swap frozen the content still
throws and still returns the empty placeholder, so `#in` was absent either way and the step passed
without proving anything. A fifth control is declared **INAPPLICABLE** rather than counted: this
witness throws once, so it cannot distinguish `=` from `??=`.

## Open reserves, disclosed

1. **The computed path is refused, not supported.** Blazor catches it; Filament cannot without a
   runtime change.
2. **The oracle is not a browser.** What a WASM app *paints* after an uncaught W1, and the `site.css`
   that styles `.blazor-error-boundary`, are not measured.
3. **`@context.Message` is faithful only for an author-thrown exception.** A runtime-raised one
   carries a .NET message on one side and a JS message on the other — cited as a divergence, not an
   equivalence.
4. **No bench label is wired**, deliberately: the slice is generator-only and adds bytes only to an
   app that writes a boundary, so there is no C1/C4 delta to report.
