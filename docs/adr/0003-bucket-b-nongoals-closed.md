# ADR 0003 — Closing the spec §3 non-goals: what fell, what did not, and what it cost

**Status:** Accepted · **Date:** 2026-07-20 · **Scope:** Bucket B · **Supersedes:** the "hold" of [ADR 0002](./0002-bucket-b-framework-roadmap.md) §3

> ADR 0002 sequenced `EventCallback` and `RenderFragment` and **held** the directive-level non-goals
> behind the RADICAL/PRUDENT question, on the argument that they "imply runtime services a static
> module has no home for" and are "not a natural extension of the compile-time model — they are new
> subsystems." That hold was lifted by directive. This ADR records the result, including the part of
> ADR 0002's argument that turned out to be **wrong**, and the part that turned out to be **right**.

## The result

**All eleven** items on README's "Not implemented" list are now **implemented and measured** against
Blazor through the DOM-contract oracle.

> **This ADR was revised once, and the revision is part of the record.** It first concluded at nine of
> eleven, holding forms and routing back with their blockers specified. Both were then closed — forms
> once the reactivity question below was answered, routing by generating a router into the app. The
> original diagnosis is kept verbatim because it was *correct*: what it predicted each would cost is
> exactly what each cost.

| # | Item | Status | Decision / BENCH |
|---|------|:------:|---|
| 1 | multi-component parameter fan-out | ✅ **already worked** | #129 (audit) |
| 2 | `EventCallback` | ✅ closed | #130 · n°49 |
| 3 | `RenderFragment` / `ChildContent` | ✅ closed | #131 · n°50 |
| 4 | `@ref` | ✅ closed | #132 · n°51 |
| 5 | DI (`@inject`) | ✅ closed, **narrowly** | #133 · n°52 |
| 6 | JsInterop | ✅ closed | #133 · n°52 |
| 7 | `CascadingParameter` | ✅ closed | #134 · n°53 |
| 8 | generics (`@typeparam`) | ✅ closed | #135 · n°54 |
| 9 | inheritance (`@inherits`) | ✅ closed, **narrowly** | #136 · n°55 |
| 10 | **forms** | ✅ closed | #137/#138 · n°56 |
| 11 | **routing (`@page`)** | ✅ closed, **and it cost bytes** | #139 · n°57 |

**Ten of the eleven cost ZERO runtime bytes.** `git diff -- src/filament-runtime` was empty at every
slice, and the runtime is still **byte-frozen at 1,943 / 2,048 B**. Routing is the single exception, and
it is an exception in the right place: its code is generated **into the app**, measured at **425 B
gzip**, so an app that does not route still pays nothing. C1 and C4 are untouched.

## What ADR 0002 got wrong

ADR 0002 treated the directive-level items as a single class — "new subsystems" — and it was wrong
about seven of the nine. The reason is a distinction it did not draw:

> **Blazor needs a runtime service for these because it discovers things at RUNTIME. Filament resolves
> composition at BUILD time, so most of them are lookups the compiler performs and then ERASES.**

Stated per item, that is not a slogan but a mechanism:

- **`@ref`** — Blazor needs `ElementReference` to carry an opaque id across the .NET/JS boundary. A
  module that *is* JS already holds the node, so `@ref` is a **naming decision**: the element is
  emitted into `const box`. Zero lines.
- **JsInterop** — the bridge exists to marshal a call across that same boundary. There is no boundary,
  so the dotted identifier is resolved at compile time into the same dotted path, which is legal JS as
  written. The bridge is **erased, not implemented**.
- **`CascadingParameter`** — Blazor needs a cascading value object because a descendant is discovered
  at render time and may be arbitrarily deep. Everything inlines into one `mount()`, so an ancestor's
  expression is literally in scope: a cascade **IS lexical scope**.
- **generics** — a type parameter constrains what may be substituted at compile time; the substitution
  happens at compile time; JS has no type to carry. Generics **erase**, and there is not even
  monomorphisation to do, because the child is inlined per use site.
- **`@inherits`** — inheritance is a question about *where a member's text lives*. Merge the base's
  members before state lifting and nothing remains.
- **`EventCallback`** — an alias for the parent's method, resolved at the composition site.
- **`RenderFragment`** — a compile-time splice of one subtree into another's position.

Seven features, zero runtime bytes. The compile-time model absorbed them rather than being extended by
them, which is a stronger result than "they were implementable."

## What ADR 0002 got right — and what it cost to close anyway

Two items resisted, and they resisted for the reason ADR 0002 named — they are **not lookups**. Both
have since been closed, and *how* they closed confirms that diagnosis rather than overturning it. The
original analysis is preserved; the outcome follows each.

### Forms — blocked on a reactivity question, not on wiring

`<EditForm Model="@m"><InputText @bind-Value="m.Name" /></EditForm>`. Probed through the real
generator, the blocker is not the components:

- `<EditForm>`/`<InputText>` are not even resolved today (the `Microsoft.AspNetCore.Components.Forms`
  namespace is not in the default imports), so `@bind-Value` arrives as a raw
  `TagHelperDirectiveAttributeIntermediateNode` carrying the text `model.Name` — *not* Blazor's
  `FormatValue`/`CreateBinder` lowering. Adding the import so Razor lowers it properly is the correct
  route, and is mechanical.
- **The real blocker is that `@bind-Value="model.Name"` binds a record PROPERTY.** Filament's
  reactivity is defined over *fields* (decision 67's conjunction: read by the template AND assigned
  outside construction). A record is an object literal with no reactivity on its members. Two-way
  binding to `model.Name` therefore needs a reactivity model for member access — a per-property signal,
  or copy-on-write over the record, in the manner of #127's element writes.

That is a genuine extension to the thesis machinery, of the same kind and size as #127, and it deserves
its own baseline, answer key and measurement. **Validation** (DataAnnotations, `EditContext`,
`ValidationMessage`) is a further subsystem beyond it and is not in scope even then.

> **OUTCOME (#137/#138, BENCH n°56) — closed, and the diagnosis was exact.** Both predicted costs were
> paid. The Forms namespace was imported so Razor lowers `@bind-Value` itself (mechanical, as expected).
> The reactivity question was answered by making **the template's write** the thing that marks a bound
> target reactive — which also closed decision **104**'s named deferral. It also surfaced a **silent
> mis-compile** (#137): a record field's initialiser was translated in phase 1, *before* the reactivity
> marking of phase 2, so the emitted literal `{ name: 'a' }` disagreed with every read of it
> (`model.name.value`). The page rendered "undefined" and the click threw `TypeError` — verified in node,
> not reasoned about. Validation stayed out of scope and is **refused, not ignored**.

### Routing — the one item that genuinely needs new code

`@page` needs URL matching, mount/unmount on navigation, `popstate` handling, and link interception.
None of that is a lookup the compiler can erase: it is behaviour that must exist while the page runs.
It also breaks an assumption the generator is built on — **its unit of work is exactly one `.razor`
file**, whereas a router needs the *set* of page components and a project-level entry point.

The design that would keep the numbers honest is known and is written down here so the next attempt
does not have to rediscover it:

- generate the router **into the app module**, never into the shared runtime, so the 2,048 B budget is
  untouched and the cost lands where it is visible;
- **measure and disclose the app-side byte cost** in BENCH — routing is the one item that must show up
  as weight, and a routing slice that reports no weight change is a slice that measured the wrong thing.

> **OUTCOME (#139, BENCH n°57) — closed exactly on those terms.** The router is generated into the app
> and *imports* each page, so a page module is byte-identical routed or standalone. The shared runtime did
> not move (still 1,943 B). The cost was measured and disclosed rather than absorbed: **425 B gzip** for
> the router, **1,641 B gzip** for the whole routed app — 6.1× under C1's budget. The generator gained a
> `--router` mode, because a router genuinely needs the *set* of pages: the structural assumption ADR 0002
> correctly identified.

## Decision

1. **Record all eleven as closed and measured** (#130–#139, BENCH n°49–n°57), each with its boundary
   witnesses kept refused, and each flipped witness moved into `Supported/` so no folder name lies.
2. **Correct README §"Honest limits"**, which listed shipped features as missing.
3. **Keep the distinction the flat list obscures.** Ten features cost zero runtime bytes because the
   compile-time model absorbed them; routing cost 425 B because it could not be absorbed. Reporting both
   as "implemented" without that difference would hide the most interesting result here.
4. **§8 is unchanged.** RADICAL remains *"ni éliminée ni établie."* Eleven features closing is evidence
   that the compile-time model absorbs a framework's *surface*. It is **not** evidence that a real
   application fits this subset — a different and larger claim. Routing is precisely where the model
   reached its limit: code had to be emitted. What remains untested is **scale, not surface**.
