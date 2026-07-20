# Bucket B program — closing the spec §3 non-goals

**Date:** 2026-07-20 · **Supersedes:** the "hold" decision of [ADR 0002](../../adr/0002-bucket-b-framework-roadmap.md) §3

## The directive

Implement the full README §3 "Not implemented" list — the named price of the C1/C4 numbers:

> routing (`@page`), DI (`@inject`), inheritance (`@inherits`), multi-component parameter fan-out,
> `RenderFragment`/`ChildContent`, `EventCallback`, `@ref`, `CascadingParameter`, forms, generics, JsInterop.

ADR 0002 sequenced the first two and **held** the rest behind the RADICAL/PRUDENT question, on the
argument that they "imply runtime services a static module has no home for." That hold is now lifted
by directive. This plan records how each item is closed *without* surrendering the property that makes
the numbers real.

## Ground truth (probed, not assumed)

The repo's most-repeated lesson is *measure, don't pre-defer*. Each item was run through the real
generator before planning:

| Item | Probe verdict |
|---|---|
| Multi-parameter fan-out, nested composition | ✅ **already compiles** — banked by ADR 0002 audit / DECISIONS #129. The README line is **stale** and must be corrected. |
| `@page`, `@inject`, `@inherits`, `@typeparam` | ❌ `FIL0003 [unsupported-directive]` — a directive allowlist refusal |
| `@ref` | ❌ `FIL0003` **and** `FIL0002` on `ElementReference` (type subset) |
| `EventCallback` | ❌ `FIL0001 [unresolved-name]` |
| `RenderFragment` | ❌ `FIL0002 [unsupported-type]` |

So the blockers are three: a **directive allowlist**, the **type subset**, and **name resolution** —
not, in most cases, a missing runtime subsystem.

## The invariant this program must not break

C1 (< 10 KB gzip) and C4 (speed) are the project's load-bearing results, and the runtime has stayed
**byte-frozen at 1,943 / 2,048 B across all 47 slices**. Therefore:

> **Every slice below is generator-only unless it is impossible.** `git diff -- src/filament-runtime`
> must be empty. Where a capability genuinely needs new code, it is **generated into the app module**,
> not added to the shared runtime — and its byte cost is measured and disclosed in BENCH.

This is the honest reframing of ADR 0002's objection. Blazor needs a router/DI container/form system
*at runtime* because it discovers components at runtime. Filament resolves composition at **build
time**, so most of these features are lookups the compiler can perform and erase. That claim is a
hypothesis per slice, and each slice tests it.

## Sequence

Ordered by value-per-effort and dependency. Each is a full slice: baseline Blazor app → hand-written
answer key → TDD generator work → fixture migration → oracle run both ways → BENCH entry → DECISIONS
entry (French) → HARNESS bump disclosed.

### Wave 1 — complete composition (the missing half)

1. **`EventCallback`** — child → parent. The child's `[Parameter] EventCallback OnX` binds the parent's
   method at the composition site; inside the inlined child `@onclick="OnX"` lowers to a call to the
   parent's translated method. Boundary kept refused until measured: `EventCallback<T>`, async callbacks.
2. **`RenderFragment` / `ChildContent`** — the parent's markup subtree is inlined at the child's
   `@ChildContent` slot. `RenderFragment` admitted only in the `[Parameter]` position.

### Wave 2 — the cheap directive-level items (compile-time lookups)

3. **`@ref`** — the element `const` already exists in `mount()`; `@ref="box"` only *names* it.
   `ElementReference` admitted in the field position.
4. **JsInterop** — the target language *is* the host. `IJSRuntime.InvokeVoidAsync("alert", x)` → `alert(x)`.
   Gated to a constant identifier path and subset argument types.
5. **`CascadingParameter`** — because composition inlines every child into one `mount()`, a cascade is
   **lexical scope**. `<CascadingValue Value="@t">` binds descendants' `[CascadingParameter]` by type.

### Wave 3 — structural

6. **`@inherits`** — merge the base component's members before state lifting.
7. **Generics (`@typeparam`)** — monomorphize at the composition site, where the type argument is known.
8. **`@inject` / DI** — compile-time resolution: a registered service type is instantiated once in
   `mount()`, its methods translated by the same `@code` machinery. Singleton scope only.

### Wave 4 — the subsystems

9. **Forms** — `<EditForm Model>` → a `form` element; `<InputText @bind-Value>` reuses the existing
   `@bind` machinery; DataAnnotations validation is generated, not interpreted.
10. **Routing (`@page`)** — the only item that plausibly needs new *code* (match `location.pathname`,
    mount/unmount, `popstate`). Generated per-app, **not** added to the shared runtime, so the
    2,048 B budget holds; the app-side byte cost is measured and disclosed.

## Closing tasks

- Correct README §153 (it lists already-shipped fan-out as missing) and restate the honest limits.
- **ADR 0003** recording that ADR 0002's hold was lifted, what it cost, and what it did to the C1/C4
  numbers — including any capability that turned out *not* to be closable generator-only.
