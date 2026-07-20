# Contributing to Filament

Thanks for your interest. Filament is a **thesis under test, not a shipping framework** — that
framing drives everything below, so it's worth reading before you open a PR.

## The one rule

**Measure it. Don't reason about it.**

Filament's entire value is that its claims are backed by numbers taken against real Blazor, on
disclosed hardware, with the reserves stated. A change that is *probably* faithful, or *should be*
faster, has no place in the record until it has been run through the oracle and compared. Several
of the constructs the compiler supports today were originally refused on reasoning that turned out
to be wrong — the refusal survived until somebody measured it.

The corollary matters just as much: **a plausible-but-wrong lowering is worse than a refusal.**
A refusal is a located diagnostic the developer sees at build time. A wrong lowering is a silent
divergence they find in production. When in doubt, refuse.

## Getting set up

On a fresh clone, two npm steps come **before** the .NET build — the solution does not compile and
the test suite does not pass without them:

```bash
npm ci                                              # root: esbuild, for tools/canon.mjs
cd src/filament-runtime && npm ci && npm run build  # the runtime bundle
cd -

dotnet build Filament.sln
dotnet test  Filament.sln
```

Why each is needed, since neither is guessable from the error you'd otherwise get:

- **Node must be on your PATH.** 45 of the 46 generator test files shell out to `node` to run
  `tools/canon.mjs`, the alpha-equivalence checker that compares emitted JS against the approved
  answer key. Without it the generator suite fails wholesale.
- **The root `npm ci`** installs `esbuild`, which `canon.mjs` invokes as `npx --no-install esbuild`
  from the repo root to minify before comparing. It's pinned to exactly `0.28.1` because the
  minified token stream *is* what alpha-equivalence is computed over — a different esbuild is a
  different answer key, and `canon.mjs` warns if it sees one.
- **The runtime build** produces `src/filament-runtime/dist/filament.js`, which
  `examples/FilamentApp` copies into its `wwwroot` at build time. `dist/` is gitignored, being
  build output, so the solution won't compile until it exists.

The signals runtime has its own suite:

```bash
cd src/filament-runtime && npm ci && npm test
```

The benchmark harness (`bench/`) needs Playwright and a real browser, and publishes multi-megabyte
Blazor WASM baselines. It is **not** part of `dotnet test` and is not run in CI — its numbers are
calibrated to the machine they were taken on, which is why every `BENCH.md` entry records the CPU,
thermal state, load average, and Chrome build.

## Adding a subset widening

This is the most common kind of contribution: a C# or Razor construct that Filament refuses today
and that has a faithful JavaScript mapping. The workflow the project actually follows:

1. **Find the witness.** Every genuine refusal has a fixture under
   `tests/Filament.Generator.Tests/Unsupported/`. That's the thing you're flipping.
2. **Establish the faithful lowering.** Write the equivalent Blazor component, build it, and see
   what it actually does — including at the edges. Integer division truncates. `long` is exact past
   2^53. `float` round-trips through a specific shortest representation. A `Dictionary` value read
   inside a loop must stay reactive or it goes stale where Blazor would re-render. These are the
   kinds of details that separate a faithful mapping from a convincing one.
3. **Implement it, generator-side.** The runtime ships as written; prefer a lowering that uses what
   already exists. `git diff -- src/filament-runtime` being empty is the norm, not a coincidence.
4. **Measure it against Blazor** through the oracle. Both directions, and the edge cases.
5. **Move the fixture** — `git mv` it from `Unsupported/` into `Supported/`. The two directories are
   split by truth, and a witness in the wrong one is a lie about what the compiler does.
6. **Record it.** `DECISIONS.md` gets the decision and its rationale; `BENCH.md` gets the
   measurement. Both are append-only.

If the construct has no faithful mapping, that is a perfectly good outcome — document the refusal
and why, so the next person doesn't re-derive it.

## Disclosure

Where Filament diverges from Blazor, the divergence gets written down. An empty-sequence aggregate,
an uncaught exception's shape, a formatting edge — these are disclosed in `DECISIONS.md` rather
than quietly accepted. The project's credibility rests on the reserves being stated out loud, so
please don't smooth one over to make a table look better.

## Commits and PRs

- Conventional Commits (`feat:`, `fix:`, `docs:`, `bench:`, `refactor:`, `chore:`).
- PRs are squash-merged, so the **PR title becomes the commit on `main`** — write it accordingly.
- Fill in the PR template, particularly the runtime-firewall checkbox.
- CI must be green: the .NET suite on Linux, Windows, and macOS, plus the runtime's vitest suite.

## Where things live

| Path | What it is |
|---|---|
| `src/Filament.Generator` | The Razor → JS compiler |
| `src/Filament.Subset` | The single-sourced definition of what compiles, shared by generator and analyzer |
| `src/Filament.Analyzer` | Author-time `FIL####` diagnostics |
| `src/Filament.Sdk` | Packaging: auto-imports, `dotnet new filament`, `dotnet watch` |
| `src/filament-runtime` | The signals runtime — the one part not emitted |
| `bench/` | Measurement harness and the committed evidence in `bench/results/` |
| `baseline/` | The Blazor WASM apps every number is measured against |
| `docs/adr/` | Architectural decisions |
| `DECISIONS.md`, `BENCH.md` | The running record (French); `ENGINEERING.md` (English) |

## Code of Conduct

This project follows the [Contributor Covenant](CODE_OF_CONDUCT.md).
