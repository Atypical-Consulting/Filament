<div align="center">

# ⚡ Filament

### Blazor components, compiled to JavaScript.

**Write Blazor-style `.razor` — template *and* C#. Ship lean static HTML + JS and a sub-2 KB signals runtime. No .NET in the browser.**

[How it works](#how-it-works) · [The evidence](#the-evidence) · [What compiles](#what-compiles) · [Honest limits](#honest-limits) · [Quickstart](#quickstart)

</div>

---

A 1,000-row grid, gzipped, over the wire:

| | Blazor WebAssembly | **Filament** | |
|---:|:---:|:---:|:---|
| Bundle | `1.88 MB` | **`4.4 KB`** | **~432× lighter** |
| .NET in the browser | the whole runtime | **`0 bytes`** | |
| Per-row update | render tree + diff | **`1 DOM write`** | |

Every number on this page is **measured against Blazor**, on the same apps, and carries its disclosed reserve. Filament is a **research compiler testing a thesis**, not a shipping framework — and it says so plainly in [Honest limits](#honest-limits).

---

## The problem

Blazor's component model is excellent. Its *delivery* is the cost. To render a counter, a Blazor WebAssembly app downloads and boots the **.NET runtime in the browser** before a single pixel appears:

- **Weight** — the framework *is* the payload. A Blazor WASM counter is ~1.9 MB gzip on the wire; your code is a rounding error next to the runtime it rides in on.
- **Startup** — nothing paints until .NET is fetched, instantiated, and tiered up. First paint waits on a VM.
- **DOM churn** — a render tree re-diffs to find what changed. The machinery to compute one text update isn't free.

> **The question Filament asks:** what if the component model stayed, and the runtime left?

## How it works

Filament reads the **same `.razor` file Blazor compiles** and lowers it — with Roslyn — to plain, imperative JavaScript at **build time**. Static structure becomes create-once DOM calls. State becomes a **signal**. Each binding becomes exactly one **effect**. There is no template to parse at runtime and no virtual DOM to diff.

<table>
<tr>
<th>Counter.razor — what you write</th>
<th>mount() — what ships (abridged)</th>
</tr>
<tr>
<td>

```razor
<p>Current count:
  <span id="counter-value">@currentCount</span></p>

<button id="increment"
        @onclick="Increment">Click me</button>

@code {
    private int currentCount = 0;

    private void Increment()
    {
        currentCount++;
    }
}
```

</td>
<td>

```js
export function mount(target) {
  const currentCount = signal(0);

  const span = document.createElement('span');
  span.id = 'counter-value';
  const t = document.createTextNode('');
  insert(span, t);

  const button = document.createElement('button');
  insert(button, document.createTextNode('Click me'));

  // one binding point -> one effect
  effect(() => setText(t, currentCount.value));
  listen(button, 'click',
         () => { currentCount.value++; });

  insert(target, /* ...p, */ button);
}
```

</td>
</tr>
</table>

The pipeline behind every generated `mount(target)`:

1. **lift state** — `private int currentCount = 0` becomes `const currentCount = signal(0)`. **You never wrote that line — the compiler derived it.** This state lifting is the thesis, and for Counter the `@code` output is byte-identical to a hand-written reference.
2. **create** — the element tree is built once, imperatively.
3. **bind** — one `effect(() => setText(t, currentCount.value))` per binding point.
4. **listen** — `@onclick` maps to a single listener; the handler body is *translated*, not spliced.
5. **attach** — `insert` into the target.

Reactive lists compile to `list(parent, sourceFn, keyFn, bodyFn, anchor)` — a keyed reconcile with minimal (LIS-based) moves. `@if`/`@else` reuse that same `list()` primitive, keyed on the active branch index, so **the whole control-flow family adds zero new runtime code**.

## The evidence

Two apps — `Counter` and a 1,000-row `Rows` — compiled from **pure `.razor`** and benchmarked against the same apps built with Blazor WASM (interpreted and AOT). The project's grading criteria:

| # | Criterion | Result | Detail |
|---|-----------|:------:|--------|
| **C1** | Bundle weight (< 10 KB gzip) | ✅ **PASS** | `Counter` **2,987 B**, `Rows` **4,373 B** gzip. The signals runtime holds a **1,943 B / 2,048 B** budget with the runtime byte-frozen across all 47 subset slices. |
| **C2** | Lighter than Blazor (≥ 50×) | ✅ **PASS** | `Rows` is **~432× lighter** (gzip vs gzip, non-AOT baseline); Counter ~631×. C1 is the stricter gate, so C2 passes the moment C1 does. |
| **C3** | DOM writes | ✅ **PASS** | `Counter`: **1** `characterData` write per increment (identical to Blazor — a correctness bar, not a win). `Rows` `#update`: **100 writes, zero reconcile**; `#swap`: **exactly 2 node moves** (keyed, same node identity). |
| **C4** | Speed | ✅ **PASS** | `Rows` beats the *faster* Blazor config (AOT) on **all four** scenarios — create-warm **3.10 vs 7.90 ms**, and update/swap/clear likewise. |

### C4 in full (Rows, median ms, n=10)

| scenario | blazor-nojit | blazor-aot | **filament-gen** |
|----------|:---:|:---:|:---:|
| create-cold | 47.95 | 25.00 | **6.65** |
| **create-warm** | 16.15 | 7.90 | **3.10** |
| update | 14.70 | 4.35 | **0.40** |
| swap | 14.60 | 3.90 | **0.50** |
| clear | 4.10 | 3.25 | **1.80** |

> **⚠️ Read the caveats before quoting a speed number.** `update`/`swap` sit a few `performance.now()` quanta above the timer floor — the *verdict* is safe but the ratios are coarse (±~15%), **not** to be cited to three significant figures. Counter's `increment-warm` is floor-limited: "**at least ~11×**" is defensible; a bare "11×" or gzip-implied "20×" divides by a quantization artifact. Weight is judged vs non-AOT, speed vs AOT — never the reverse. See [`BENCH.md`](./BENCH.md) for every figure and its reserve.

**The honest ceiling:** C1 **and** C4 pass on generator output for both apps, so the architecture's **viability condition is met and measured**. That is *all* it establishes — see below.

## What compiles

Across **128 recorded decisions** and **47 measured slices**, the compilable C# subset covers most of everyday C#. Every widening was verified byte-for-byte against a Blazor-faithful answer key, then measured live in a real browser via a Playwright DOM-contract oracle. **411 tests** back it (333 generator · 60 subset · 18 analyzer, plus 214 runtime).

| Area | Covered |
|------|---------|
| **Control flow** | `@if` / `@else` / `@else if` (multi-node, nested, mixed, root-level); `@foreach` over `List<T>`, `T[]`, `Dictionary<K,V>` (incl. reassigned) |
| **Statements** | `for`, `while`, `do-while`, `switch`, `try`/`catch`/`throw`/`lock`, root `@{ }` blocks, int-vs-double division, positional records |
| **Numeric types** | `int`; `long`→BigInt (exact past 2⁵³); `float`→`Math.fround`; `decimal`→boxed `{mantissa, scale}`; `DateTime`→BigInt ticks |
| **Collections** | `List<T>`, `T[]`, `Dictionary<K,V>`→`Map` — read, reassign, **and element write** (copy-on-write signals) |
| **LINQ** | `Where`/`Select`/`Count`/`Any`/`All` · `Sum`/`Min`/`Max`/`Average`/`First`/`Last` · `OrderBy`/`Skip`/`Take`/`Reverse` · `GroupBy` |
| **Reactivity** | `@bind` two-way (string/int/bool/checkbox), inline lambda handlers, reactive attributes, `async`/`await`→`Promise` |
| **Composition** | static-leaf and bound-parameter (reactive) component composition |

Anything outside the subset raises a **located diagnostic and writes no file** — Filament never emits silently-wrong JavaScript.

**Developer experience:** `dotnet new filament` template · `Filament.Sdk` (auto-imports generator + runtime, `dotnet watch` hot-reload) · `Filament.Analyzer` (author-time FIL0001/FIL0002 in the IDE) · a runnable Rider example · a browser demo.

## Honest limits

Filament is a **thesis under test**. The verdict, stated the way the repo states it:

> **RADICAL is not eliminated, and not established.** The thesis (a standalone Razor→JS compiler on a tiny signals runtime can replace Blazor) is **not falsified** — but two demo apps over a deliberately narrow subset do not prove out a whole-framework architecture.

**Not implemented** (spec §3 non-goals — the named price of the C1/C4 numbers): routing (`@page`), DI (`@inject`), inheritance (`@inherits`), multi-component parameter fan-out, `RenderFragment`/`ChildContent`, `EventCallback`, `@ref`, `CascadingParameter`, forms, generics, JsInterop.

**Reserves** (all disclosed in [`BENCH.md`](./BENCH.md) / [`DECISIONS.md`](./DECISIONS.md)):
- ✅ *Banked (BENCH n°48).* The hand-written `Rows` bundle (post-#80) was re-measured on the wire at **4,373 B gzip — byte-identical to the generated bundle**.
- ✅ *Banked (BENCH n°48).* The comment-anchor node debt was re-measured **inside a conditional app**: Filament renders **4 nodes to Blazor's 5** (its one `@if` anchor is cheaper than Blazor's two `<!--!-->` markers). The debt never flips sign — it stays a create-time *advantage*.
- ⚠️ **EOL-Razor risk (#52), still open — now mitigated.** The generator pins `Microsoft.AspNetCore.Razor.Language` 6.0.36 (frozen, out of support). Contained to one 194-line seam, hardened to fail loud, and mapped for migration in [ADR 0001](./docs/adr/0001-eol-razor-mitigation.md). It bears asymmetrically against RADICAL and is part of its price.

## Quickstart

```bash
# Compile one component to JS
dotnet run --project src/Filament.Generator -- path/to/App.razor out/App.js

# Or scaffold a whole app (no repo paths needed)
dotnet new install ./templates/filament
dotnet new filament -o MyApp

# Run the browser demo (Counter, If, Rows)
(cd src/filament-runtime && npm ci) && ./demo/build.sh
```

An out-of-subset construct is refused, located, and writes nothing:

```bash
dotnet run --project src/Filament.Generator -- \
  tests/Filament.Generator.Tests/Unsupported/Foreach.razor samples/Counter/x.js
# → Foreach.razor(2,20): FIL0001: [unsupported-foreach]   (exit 1, no file written)
```

## Repository map

| Path | What it is |
|------|------------|
| `src/Filament.Generator/` | The Razor→JS compiler (console app). `CSharpFrontEnd.cs` does the `@code` lifting. |
| `src/Filament.Subset/` | The single-sourced C# subset definition, shared by generator + analyzer. |
| `src/Filament.Analyzer/` | Author-time Roslyn analyzer (FIL0001 / FIL0002). |
| `src/Filament.Sdk/` | SDK-style NuGet packaging + `dotnet watch` support. |
| `src/filament-runtime/` | The TypeScript signals runtime (`signal`/`effect`/`list`/`setText`/…). |
| `baseline/` · `samples/` | Blazor baselines and the Filament sources + answer keys. |
| `bench/` | The measurement harness — the source of every number. |
| `website/` | This project's marketing + evidence site (Astro). |
| [`ENGINEERING.md`](./ENGINEERING.md) | The full engineering log: phases, gates, and every measurement in context. |
| [`BENCH.md`](./BENCH.md) · [`DECISIONS.md`](./DECISIONS.md) | Every number and every call, append-only. |

---

<div align="center">
<sub>Numbers are provisional research measurements. Read <a href="./BENCH.md"><code>BENCH.md</code></a> before quoting any figure — each carries a reserve.</sub>
</div>
