# Real-World I/O & Shipping Readiness — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** implement the spec at `docs/superpowers/specs/2026-07-21-real-world-io-design.md` — time, randomness, HTTP+JSON, analyzer call coverage, release pipeline, real-apps guide.

**Architecture:** three generator slices (each: baseline-first, refusal-flip TDD, canon answer key, approval snapshot, oracle contract, French DECISIONS/BENCH), then analyzer + pipeline + docs. Runtime stays frozen; every new primitive is an emitted helper.

**Tech Stack:** existing repo stack (Roslyn generator, xUnit, canon.mjs, Playwright oracle, Astro site, GH Actions).

## Global Constraints

- Runtime firewall: `git diff -- src/filament-runtime` empty at every commit.
- Baseline MUST `dotnet build` before any generator design step (RZ9979 lesson).
- BENCH.md is append-only French; DECISIONS.md entries in French; HARNESS bumps disclosed in the entry.
- A slice that flips an Unsupported witness must `git mv` it into Supported/.
- Commit style: `type(scope): summary (DECISIONS #N, BENCH n°N)`.

---

### Task 1 — Wave A: DateTime.UtcNow/Now/Today + .Ticks (DECISIONS #145, BENCH n°62)

**Files:** Create `baseline/DateTimeNow.Blazor/` (copy Counter.Blazor shell), `samples/DateTimeNow/datetimenow.js` (answer key), `tests/Filament.Generator.Tests/DateTimeNowTests.cs`, `tests/Filament.Generator.Tests/Unsupported/Code/DateTimeKind.razor`. Modify `src/Filament.Generator/CSharpFrontEnd.cs` (static-member classification + DateTimeMethod area + helper emission), `bench/harness/bench.mjs` (contract `datetimenow`, predicate `ticksNearNow`, HARNESS 1.55.0), `bench/publish-baseline.sh` + `bench/build-filament.sh` labels, `DECISIONS.md`, `BENCH.md`.

- [ ] Witness (build it FIRST): `#out` renders `@stamp.Ticks`, `#snap` sets `stamp = DateTime.UtcNow;` — `dotnet build baseline/DateTimeNow.Blazor` green.
- [ ] Failing tests: emission contains `__dtUtcNow`, `.Ticks` is identity, `DateTime.Kind` refused with wording naming the ticks erasure.
- [ ] Implement: admit static property access `DateTime.UtcNow/Now/Today` → helper calls; `.Ticks` → identity; helpers emitted on use (pattern: __dtStr).
- [ ] Canon key + approval; full suite green.
- [ ] Oracle: `ticksNearNow` predicate (BigInt parse, |v − now| ≤ 90 s at assert time, per side); measure vs Blazor; JSON committed if weight-bearing, else correctness-only like recent slices.
- [ ] DECISIONS #145 (français), BENCH n°62 (append-only), commit.

### Task 2 — Wave B: Random seeded/unseeded (DECISIONS #146, BENCH n°63)

**Files:** Create `baseline/Rand.Blazor/`, `samples/Rand/rand.js`, `tests/Filament.Generator.Tests/RandTests.cs`, `tests/.../Unsupported/Code/RandomNextBytes.razor`. Modify generator (type admission for `System.Random` fields, `new Random(...)` construction, method dispatch, `Random.Shared`, `__rnd` helper emission), bench.mjs (`random` contract + `intInRange` predicate, HARNESS 1.56.0), publish/build scripts, DECISIONS/BENCH.

- [ ] Witness FIRST: `rng = new Random(42)`, `#roll` does `sum += rng.Next(1, 7)`; `#shared` does `last = Random.Shared.Next(10)`; builds green. Compute the expected seeded values with a throwaway `dotnet run` and hardcode them in the contract.
- [ ] Failing tests: seeded emission carries the Knuth table init; unseeded carries Math.random; NextBytes refused.
- [ ] Implement `__rnd(seed)`: exact Net5CompatSeedImpl (161803398 init, 55-slot table, inext/inextp 0/21, InternalSample with the ==MaxValue decrement, Sample scale 1/2147483647, large-range double-sample path).
- [ ] Canon + approvals + suite green.
- [ ] Oracle: seeded sequence must be BYTE-EQUAL to Blazor's DOM (that is the C# faithfulness proof); unseeded via `intInRange`.
- [ ] DECISIONS #146, BENCH n°63, commit.

### Task 3 — Wave C: HttpClient + JSON (DECISIONS #147, BENCH n°64)

**Files:** Create `baseline/HttpJson.Blazor/` (Program.cs registers HttpClient with BaseAddress; `wwwroot/data/items.json` camelCase), `samples/HttpJson/httpjson.js`, `tests/.../HttpJsonTests.cs`, `tests/.../Unsupported/Code/HttpJsonLong.razor` + `HttpDelete.razor`. Modify `TemplateCompiler.cs` (@inject admits HttpClient), `CSharpFrontEnd.cs` (dispatch branches for HttpClientJsonExtensions + HttpClient.GetStringAsync; `__getJson`/`__getText`/`__camel` helpers; the T shape gate refusing long/decimal/DateTime/Dictionary members), bench.mjs (`httpjson` contract, HARNESS 1.57.0), scripts, DECISIONS/BENCH.

- [ ] Witness FIRST: `#load` async handler `items = await Http.GetFromJsonAsync<List<Item>>("data/items.json") ?? new();` rendered by `@foreach` (#140 signal); builds green.
- [ ] Failing tests: emission has `__getJson` + `__camel`; long-membered T refused with BigInt guidance; DeleteAsync refused.
- [ ] Implement + canon + approvals + suite green.
- [ ] Oracle: static JSON served by both shells → byte-identical DOM after the async load (continuation wait exists since #119).
- [ ] DECISIONS #147, BENCH n°64, commit.

### Task 4 — Wave D: analyzer call coverage (DECISIONS #148)

**Files:** Create `src/Filament.Subset/CallSubset.cs`; modify `src/Filament.Analyzer/ConstructSubsetAnalyzer.cs` (+ its tests project) to flag FIL0001 on invocations neither same-component nor in CallSubset; point the generator's dispatcher at the same table where it is a pure membership test.

- [ ] Failing analyzer tests (admitted: same-component, IJSRuntime, LINQ, Task.Delay, DateTime methods, Random, HttpClient JSON; flagged: Console.WriteLine, File.ReadAllText).
- [ ] Implement; both test suites green; DECISIONS #148; commit.

### Task 5 — Wave E: release pipeline (DECISIONS #149)

**Files:** Create `.github/workflows/release.yml`; modify `src/Filament.Sdk/Filament.Sdk.csproj` (+ template package) for version 0.2.0; a local `dotnet pack` + scaffold-from-pack proof run.

- [ ] Pack locally, scaffold `dotnet new filament` from the packed nupkg in a temp dir, build it — green.
- [ ] release.yml: tag `v*` → 3-OS tests → pack → Release artifacts → guarded nuget push.
- [ ] DECISIONS #149; commit.

### Task 6 — Wave F: the guide (DECISIONS #150)

**Files:** Create `docs/REAL-APPS.md`; modify `README.md` (Real apps section + link).

- [ ] Write the guide per spec section F (escape hatch, storage, title recipe, fetch, time, random, testing generated modules with vitest, browser floor ES2023, perf disclosure, debugging story, watch loop, non-goals with rationale).
- [ ] DECISIONS #150; commit; memory files updated.
