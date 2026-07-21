# Building real apps with Filament

This is the practical guide: what a production Filament app can do today, how to reach the
platform when you need something the subset does not map, and the exact limits — each one
disclosed with its reason, because a limit you can predict is a design constraint and a limit
you discover in production is a bug report.

The mental model in one paragraph: **your `.razor` is Blazor-authored source, and the compiler
either maps it faithfully or refuses with a located diagnostic** (`FIL0001`/`FIL0002`/`FIL0003`,
also surfaced in the IDE by `Filament.Analyzer` — including out-of-subset *calls*). It never
emits silently-wrong JavaScript. Everything below is measured against real Blazor by the
repo's DOM-contract oracle; the decision numbers refer to [`DECISIONS.md`](../DECISIONS.md).

## Real-world I/O

A real app reads the clock, rolls dice, and fetches JSON. All three are in the subset:

**Time** (decision 145). `DateTime.UtcNow`, `DateTime.Now`, `DateTime.Today`, and `.Ticks` —
the wall clock is the same wall clock on both sides, emitted as BigInt-tick helpers into your
module. `DateTimeKind` does not survive the ticks model (`.Kind` is refused, with the reason).

**Randomness** (decision 146). `new Random(seed)` reproduces the **exact** .NET seeded
sequence (the Knuth-subtractive generator, proven against the real BCL through the oracle);
`new Random()` / `Random.Shared` ride `Math.random` behind the same interface —
`Next()`, `Next(max)`, `Next(min, max)`, `NextDouble()`.

**Network** (decision 147). `@inject HttpClient Http`, then:

```razor
@using System.Net.Http.Json
@inject HttpClient Http

@code {
    private List<Item> items = new List<Item>();

    private async Task Load()
    {
        var data = await Http.GetFromJsonAsync<List<Item>>("api/items");
        if (data != null) { items = data; }
    }

    private record Item(string Name, int Rank);
}
```

compiles to `fetch` — because in a browser, Blazor's own HttpClient *is* fetch; Filament just
erases the bridge. `GetStringAsync` and `PostAsJsonAsync(url, value)` work the same way.
Non-success statuses throw (catchable with `try`/`catch`), and relative URLs resolve against
the page origin, exactly as Blazor's `BaseAddress` convention does.

**The JSON shape gate** — the part that keeps this honest. `GetFromJsonAsync<T>` admits `T`
only where the JSON shape and the Filament shape coincide: `int`, `double`, `bool`, `string`,
records of those, `List<T>`/`T[]` of those. A `long` member is refused (a JSON number arrives
as a JS number, not the BigInt `long` maps to), as are `float`, `decimal`, `DateTime` and
`Dictionary` members — each with its reason in the diagnostic. Serve camelCase JSON (the
System.Text.Json Web default); PascalCase keys are normalized on their leading character.

**Storage.** `localStorage`/`sessionStorage` need no feature at all — they are one escape-hatch
call away (next section), and the repo's JS-interop witness is exactly a localStorage round trip.

## The escape hatch — calling JavaScript

`@inject IJSRuntime JS` is admitted, and the bridge is *erased*: the call becomes the call
(decision 133). This is the official boundary for everything the subset does not map:

```razor
@inject IJSRuntime JS

@code {
    private string saved = "";

    private async Task Save()
    {
        await JS.InvokeVoidAsync("localStorage.setItem", "draft", saved);
        saved = await JS.InvokeAsync<string>("localStorage.getItem", "draft");
    }
}
```

The identifier must be a literal dotted path (`"localStorage.setItem"`, `"myLib.doThing"`) —
that is what lets the compiler emit it as a direct call instead of shipping a runtime resolver.
Three consequences worth spelling out:

- **Your own JS / npm libraries:** attach functions to `globalThis` in your `index.html` (or a
  script you bundle) and call them by name. That is the interop contract.
- **Page title:** `document.title = x` is an assignment, not a call — expose a one-line helper
  (`window.setTitle = (t) => { document.title = t; }`) and `InvokeVoidAsync("setTitle", ...)`.
- **Anything genuinely dynamic** (module imports, .NET object references, streams) stays
  refused, because it would require shipping the bridge the compiler exists to erase.

## The dev loop

```bash
dotnet new install Filament.Templates     # once (or from a local .nupkg)
dotnet new filament -o MyApp
cd MyApp && dotnet watch                  # edit App.razor -> recompiles the JS on save
```

`Filament.Sdk` auto-imports the compile step; `Filament.Analyzer` gives you the refusals as
IDE squiggles while you type — members, statements, expressions and calls all match the
generator's verdicts, because both read the same `Filament.Subset` tables.

**Testing your components:** the emitted module is a plain ES module with one export —
`mount(target)`. Any DOM test runner works:

```js
// vitest + happy-dom (or jsdom)
import { mount } from './wwwroot/App.g.js';

it('increments', () => {
  document.body.innerHTML = '<div id="app"></div>';
  mount(document.getElementById('app'));
  document.querySelector('#add').click();
  expect(document.querySelector('#value').textContent).toBe('1');
});
```

No test host, no renderer shim: the component *is* the JS you ship.

**Debugging:** the output is deliberately small and readable — named signals, one `effect` per
binding, your method names preserved. Read it; that is the debugging story, and it is a better
one than sourcemapping a megabyte of framework. Refusals never reach the browser: they stop
the build, located in your `.razor`.

## The floor and the ceiling

**Browser floor: ES2023.** The emitted code uses `Array.prototype.with`, `.at()`, spread,
`BigInt` and (only if you use `Random`) `Int32Array` — i.e. every evergreen browser since
roughly 2023. There are no polyfills and no transpilation; that is part of why the runtime is
1,943 bytes.

**Performance envelope.** The Duel (BENCH n°58–60) measured the same task-board app both ways:
~400× less to download than Blazor WASM, interactive ~6.7× sooner, ~50× smaller memory
footprint. Know the one deliberate trade: **element writes are copy-on-write**
(`arr[i] = v` → `arr.with(i, v)`, decision 127) so the signal graph sees a new reference —
O(n) per write. UI lists are fine; a 10,000-row grid edited cell-by-cell in a hot loop is not
this compiler's use case, and we would rather tell you that here than have you measure it in
production.

## Deliberate non-goals (and why)

- **SSR / prerendering** — not implemented. The compile-time model is well placed for it
  (emit HTML + hydrate), but it is unproven here, so it is not claimed.
- **CSS isolation (`.razor.css`)** — style with plain CSS or Tailwind. Scoping exists in Blazor
  because components render through a runtime; here your markup is the markup you wrote, and
  ordinary selectors reach it.
- **HMR beyond `dotnet watch`** — the rebuild is the reload; modules this small make watch
  loops fast enough that finer-grained HMR has not earned its complexity.
- **Error boundaries** — an uncaught exception in a JS event handler logs to the console and
  the app keeps running; the platform default *is* the boundary (unlike a torn-down .NET
  circuit). Wrap risky work in `try`/`catch` (in the subset) where you want recovery logic.
- **Form validation** — refused, not ignored: without validators every submit *is* valid, and
  silently accepting an invalid model would be the wrong answer dressed as the right one.
- **A general DI container** — `@inject` admits the two services with a compile-time meaning
  (`IJSRuntime`, `HttpClient`). A container that resolves implementations at runtime has no
  home in a static module; your own services live in `.cs` files this compiler never reads.

Everything else that is refused says so at build time, with a location and a reason. If a
refusal names something you need, that is exactly the feedback the project wants — the last
sixty entries in [`BENCH.md`](../BENCH.md) are the record of refusals becoming features, each
one measured against Blazor before it shipped.
