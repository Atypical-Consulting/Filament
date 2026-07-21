# Real-World I/O & Shipping Readiness — Design

**Date:** 2026-07-21
**Goal:** close the gap between "research artifact" and "usable for real projects": the
subset's deliberate determinism firewall (no clock, no randomness, no network) is lifted
with faithful mappings, the analyzer learns the last refusal kind (calls), a release
pipeline makes the SDK shippable, and a "building real apps" guide documents the whole
production story (escape hatch, storage, testing, browser floor, perf limits).

## Why now

Every distinct Unsupported witness with a faithful mapping is closed (#98–#139) and the
app-level objection is closed by the Duel. What remains between Filament and a real
project is not template coverage — it is **I/O**: a real app reads the clock, rolls
dice, and fetches JSON. Those three were refused *by methodological choice* (the oracle
demanded determinism), not because no faithful mapping exists. This program implements
the mappings and — where the value is genuinely non-deterministic — teaches the oracle
tolerance and range predicates instead of abandoning measurement.

The escape hatch already exists and is load-bearing for this claim: decision 133 erased
`@inject IJSRuntime` + `InvokeVoidAsync/InvokeAsync<T>` into direct JS calls, which
covers `localStorage`, `document` APIs and any global the host page defines. This
program does not rebuild it; it documents it as the official boundary.

## Wave A — Time: `DateTime.UtcNow` / `DateTime.Now` / `DateTime.Today` (slice, BENCH)

The DateTime model is already BigInt ticks (decision 115); only the *sources* were
refused. The wall clock is the same wall clock on both sides:

- `DateTime.UtcNow` → emitted helper `__dtUtcNow()` =
  `621355968000000000n + BigInt(Date.now()) * 10000n` (Unix epoch offset in ticks +
  ms→ticks). Faithful to C#'s value within clock resolution.
- `DateTime.Now` → `__dtNow()` = `__dtUtcNow() - BigInt(new Date().getTimezoneOffset()) * 600000000n`
  (getTimezoneOffset is UTC−local in minutes; one minute = 600,000,000 ticks).
- `DateTime.Today` → local midnight: `(__dtNow() / 864000000000n) * 864000000000n`
  (BigInt division truncates; ticks are positive).
- `.Ticks` on a DateTime value → the BigInt itself (identity; typed long, which the
  subset already renders faithfully via #112).

**Disclosed erasure:** `DateTimeKind` does not exist in the ticks model. `.Kind` is
refused (boundary witness), as are `ToUniversalTime`/`ToLocalTime`. Comparisons and
tick arithmetic are Kind-blind in C# too, so nothing admitted lies.

**Measurement:** witness `DateTimeNow.Blazor` renders `@stamp.Ticks` (default 0 → "0",
byte-equal), a `#snap` click stores `DateTime.UtcNow`. New harness predicate
`ticksNearNow` parses the rendered ticks and asserts |value − harness clock at assert
time| ≤ tolerance (90 s), evaluated per side at its own run time — robust to the
mirrored passes running minutes apart. HARNESS bump, disclosed.

## Wave B — Randomness: `Random` (slice, BENCH)

Two regimes, one emitted factory `__rnd(seed)` returning `{ next, nextTo, nextIn, nextDouble }`:

- **Seeded `new Random(seed)` — deterministic and PROVABLE.** .NET's seeded Random is
  the Knuth subtractive generator (Net5CompatSeedImpl, stable by compat guarantee).
  ~35 lines of int32 arithmetic, implemented exactly (including the large-range
  two-sample path of `Next(min,max)`). The oracle then proves faithfulness against the
  real BCL: Blazor renders C#'s sequence, Filament renders the JS sequence, the DOM
  must be byte-identical.
- **Unseeded `new Random()` / `Random.Shared` — arbitrary on both sides.** Backed by
  `Math.random()` with the same API surface. C#'s unseeded sequence (xoshiro) is not
  reproducible across runs either; range and distribution are the observable contract.
  Measured with a new `intInRange` predicate. Divergence (different arbitrary
  sequences) disclosed.

Mapping: `.Next()` → `.next()`, `.Next(max)` → `.nextTo(max)`, `.Next(min,max)` →
`.nextIn(min,max)`, `.NextDouble()` → `.nextDouble()`. `Random.Shared` → one
module-level `const` instance. Refused (boundary): `NextBytes` (byte[] not in subset),
`NextInt64` (deferred).

## Wave C — Network: `HttpClient` + JSON (slice, BENCH)

**The honesty argument mirrors decision 133:** Blazor WASM's HttpClient is implemented
*on top of fetch* — the bridge exists because .NET must marshal across a boundary.
Filament IS the platform, so the bridge erases:

- `@inject HttpClient Http` becomes the SECOND honest injectable (decision 133's
  narrowness widens by exactly one service, same argument).
- `await Http.GetFromJsonAsync<T>(url)` → `await __getJson(url)`:
  fetch, throw on `!r.ok` (GetFromJsonAsync's EnsureSuccess semantics — catchable with
  #110's try/catch), parse JSON, then `__camel` normalizes each key's FIRST character
  to lower case. Faithful to System.Text.Json Web defaults (camelCase +
  case-insensitive) for the Pascal↔camel case that covers real APIs; full
  case-insensitivity is disclosed as not reproduced.
- `await Http.GetStringAsync(url)` → `__getText(url)` (fetch + throw on !ok + text()).
- `await Http.PostAsJsonAsync(url, value)` (statement form, response discarded) →
  `fetch(url, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(value) })`.
  Our object literals are already camelCase — `JSON.stringify` IS Web-defaults
  serialization for the admitted shapes.

**The type gate is the honesty core:** `GetFromJsonAsync<T>` admits T only where the
JSON shape and the Filament shape coincide — `int`/`double`/`bool`/`string`, records
of those, `List<T>`/`T[]` of those. It REFUSES members typed `long` (JSON number →
JS number, not BigInt), `decimal` (not `{m,s}`), `DateTime` (JSON string, not ticks),
`Dictionary` — each with guidance. Relative URLs are faithful: Blazor's BaseAddress is
the host, fetch resolves against the document base.

**Measurement:** witness `HttpJson.Blazor` fetches a static camelCase
`data/items.json` served by both shells — deterministic, byte-identical DOM via the
existing predicates (async continuation waiting exists since #119).

## Wave D — Analyzer: the missing refusal kind (calls)

FIL0001 covers statements/members/expressions; calls were left "low-value" — wrong for
a shipping library, where every generator refusal must have an author-time squiggle.
A shared `Filament.Subset.CallSubset` single-sources the admitted-call table
(containing-type display string + method names, plus the same-component rule and the
admitted service types). `ConstructSubsetAnalyzer` gains invocation analysis over it;
generator and analyzer agree because both read the same table.

## Wave E — Release pipeline

`Filament.Sdk` (+ template) versioned and `dotnet pack`-proven; a `release.yml`
workflow on tag `v*`: test (3 OS) → pack → GitHub Release with `.nupkg` artifacts →
`dotnet nuget push` guarded on the `NUGET_API_KEY` secret being present. Publishing
credentials are the one human step; the pipeline is the deliverable.

## Wave F — The "Building Real Apps" guide

`docs/REAL-APPS.md` + README section: the escape hatch (IJSRuntime patterns —
localStorage, `document.title` recipe, calling your own JS/npm globals), fetch + JSON,
time, random, testing generated modules (they are plain ES modules — vitest example),
the browser floor (ES2023: `Array.prototype.with`, BigInt), performance disclosure
(copy-on-write element writes are O(n) — fine for UI lists, not for 10k-row editors),
the debugging story (refusals are author-time with .razor positions; the output is
small, readable JS by design), `dotnet watch` dev loop. Deliberate non-goals with
rationale: SSR/prerendering, CSS isolation (style with plain CSS/Tailwind — there is
no runtime to scope against), HMR beyond `dotnet watch`, error boundaries (an uncaught
handler exception in JS logs and continues — the platform default IS the boundary,
unlike Blazor's circuit teardown).

## Invariants (unchanged from the whole program)

Runtime FROZEN (`git diff -- src/filament-runtime` empty; all helpers are emitted into
the module). Every slice: baseline `dotnet build` FIRST, refusal-flip tests, canon +
approval snapshots, oracle-measured vs Blazor, French DECISIONS + append-only BENCH,
HARNESS bumps disclosed. Commit style `type(scope): summary (DECISIONS #N, BENCH n°N)`.
