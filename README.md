# Filament — Phase 0 baseline

This repository holds the **Phase 0 Blazor WebAssembly baseline**: the measured numbers that the
Filament criteria **C2** (weight) and **C4** (speed) will later be judged against.

Nothing here is a Filament implementation yet. What is here is a baseline, the harness that measured
it, the raw evidence, and enough instruction to let a stranger reproduce all of it cold.

| File | What it is |
|---|---|
| `BENCH.md` | **Append-only** measurement register. Entry #1 is the Phase 0 baseline. Never edit an entry; rectify with a new one. |
| `DECISIONS.md` | Why each number was produced the way it was. Read this before disputing a result. |
| `bench/publish-baseline.sh` | Regenerates the four publish outputs. The only on-disk definition of the four configs. |
| `bench/harness/bench.mjs` | Framework-agnostic Playwright/CDP measurement driver. |
| `bench/harness/server.mjs` | Production-like static server with per-request content negotiation. |
| `bench/results/*.json` | **The measured evidence.** Tracked in git on purpose. |

---

## Prerequisites

| Requirement | Version measured on | Check |
|---|---|---|
| .NET SDK | **10.0.301** | `dotnet --version` |
| `wasm-tools` workload | 10.0.109 | `dotnet workload list` |
| Node.js | v26.5.0 | `node --version` |
| Google Chrome | 150.0.7871.124 (`chrome` channel) | driven by Playwright 1.61.1 |

Install the workload if missing:

```bash
dotnet workload install wasm-tools
```

Install the harness dependencies (this also fetches the Playwright browser):

```bash
cd bench/harness
npm ci
npx playwright install chrome
```

**The SDK version is part of the result, not an environment detail.** The C2 and C4 targets are
pinned to .NET 10.0.301 (`DECISIONS.md` #1). A different SDK may move the Blazor payload; if yours
differs, `publish-baseline.sh` warns, and any number you produce belongs in a **new** `BENCH.md`
entry rather than compared against entry #1.

---

## Step 1 — Publish the four baseline configs

```bash
./bench/publish-baseline.sh              # all four configs
./bench/publish-baseline.sh --list       # show the labels
./bench/publish-baseline.sh blazor-rows-nojit blazor-counter-nojit   # a subset
```

Four configs from two source trees:

| Label | Project | AOT |
|---|---|---|
| `blazor-counter-nojit` | `baseline/Counter.Blazor` | no |
| `blazor-counter-aot` | `baseline/Counter.Blazor` | yes |
| `blazor-rows-nojit` | `baseline/Rows.Blazor` | no |
| `blazor-rows-aot` | `baseline/Rows.Blazor` | yes |

Output lands in `bench/publish/<label>/` (gitignored — regenerable build output).

Things worth knowing before you run it:

- **`RunAOTCompilation` is passed on the command line, never in a `.csproj`.** One source tree must
  produce both the AOT and the non-AOT config, or the two are not comparable (`DECISIONS.md` #9).
- **The script purges `obj/`, `bin/` and the output dir per config.** This is load-bearing, not
  hygiene: `wasm-opt` rewrites `dotnet.native.wasm` in place, and toggling AOT on a shared project
  directory poisons the static-web-assets cache. Both failures were hit for real during Phase 0
  (`MSB3073`, `MSB3030`). This makes the script idempotent — a second run reproduces the first.
- **The AOT configs are slow** (they compile 33 assemblies to native). The two non-AOT configs take
  well under a minute each.
- **The script verifies AOT from the artifact, not the flag.** It asserts `dotnet.native.wasm` is
  >4 MiB for an AOT label and <4 MiB for a non-AOT one (measured: 11,362,554 B vs 1,494,734 B). A
  silent AOT fallback fails the publish instead of quietly producing a mislabelled baseline.

---

## Step 2 — Measure

The static root is **`bench/publish/<label>/wwwroot`**, never `bench/publish/<label>`.

`bench.mjs` starts the server itself; you do not run `server.mjs` by hand.

### gzip basis

```bash
node bench/harness/bench.mjs \
  --dir bench/publish/blazor-rows-nojit/wwwroot \
  --app rows --label blazor-rows-nojit \
  --runs 10 --weight-runs 3 --max-encoding gzip --headless --no-aot \
  --out bench/results/blazor-rows-nojit.json

node bench/harness/bench.mjs \
  --dir bench/publish/blazor-rows-aot/wwwroot \
  --app rows --label blazor-rows-aot \
  --runs 10 --weight-runs 3 --max-encoding gzip --headless --aot \
  --out bench/results/blazor-rows-aot.json

node bench/harness/bench.mjs \
  --dir bench/publish/blazor-counter-nojit/wwwroot \
  --app counter --label blazor-counter-nojit \
  --runs 10 --weight-runs 3 --max-encoding gzip --headless --no-aot \
  --out bench/results/blazor-counter-nojit.json

node bench/harness/bench.mjs \
  --dir bench/publish/blazor-counter-aot/wwwroot \
  --app counter --label blazor-counter-aot \
  --runs 10 --weight-runs 3 --max-encoding gzip --headless --aot \
  --out bench/results/blazor-counter-aot.json
```

### brotli basis

Identical, with `--max-encoding br` and a distinct `--out`:

```bash
node bench/harness/bench.mjs \
  --dir bench/publish/blazor-rows-nojit/wwwroot \
  --app rows --label blazor-rows-nojit \
  --runs 10 --weight-runs 3 --max-encoding br --headless --no-aot \
  --out bench/results/blazor-rows-nojit-br.json
```

`--max-encoding` **caps** negotiation; it does not force it. Chrome always sends
`gzip, deflate, br, zstd`, and `dotnet publish` emits both `.br` and `.gz` siblings, so an uncapped
run serves brotli. Capping to gzip serves real, verified gzip bytes rather than brotli bytes wearing
a gzip label (`DECISIONS.md` #3).

Useful flags: `--runs`, `--weight-runs`, `--warmup`, `--headed` (debug), `--route`, `--timeout`,
`--quiet-ms`, `--port`. `--help` lists them all.

Harness selftest:

```bash
cd bench/harness && npm run selftest    # 249 assertions
```

---

## Where results land

| Path | Contents |
|---|---|
| `bench/results/<label>.json` | Per-config protocol result: weight, per-scenario median/IQR, environment, config, contract check. **Tracked in git.** |
| `bench/results/brotli-weight-reference.json` | Hand-assembled brotli-vs-gzip summary. Derived from brotli runs, not emitted directly by `bench.mjs`. |
| `bench/publish/<label>/` | Publish output. Gitignored; regenerate with the script. |

`--out` is the only thing that decides where a result is written; nothing is auto-discovered.

---

## Interpreting the results

**Read `BENCH.md` and `DECISIONS.md` before quoting any number.** Both carry reserves that change
what the numbers mean. Two in particular:

- **`create` is mostly first-interaction cost, not row building.** `create` is the first click on a
  freshly loaded page, so ~61–72% of its 35.40 ms is Blazor runtime boot. Boot-adjusted, real row
  building is ~9.85 ms interpreted / ~9.05 ms AOT. A framework that boots faster beats 35.40 ms
  without rendering a single row faster (`BENCH.md`, reserve #1).
- **The encoding basis moves the C2 target by up to 31%.** Rows non-AOT is 1,889,184 B gzip vs
  1,554,591 B brotli — a 50x target of 37,784 B vs 31,092 B. **Filament must be measured under the
  same `--max-encoding` as the baseline it is compared to.** A brotli Filament against a gzip Blazor
  is not a 50x ratio, it is an encoding artifact (`DECISIONS.md` #3).

`BENCH.md` entry #1 was written **before** the Phase 0 gate decisions on basis and on cold/warm
`create`. Where the gate and entry #1 disagree, entry #1 is the *measurement* and the gate is the
*interpretation*; entry #1 is append-only and is not rewritten. A later entry records the rectified
headline.

---

## Note on reproducing entry #1 byte-for-byte

The baseline configuration was made self-consistent **after** entry #1 was measured, to keep the
future Filament comparison clean:

- `Rows.Blazor.csproj` now declares `PublishTrimmed` explicitly instead of inheriting the SDK Release
  default (verified no-op today — both apps were already trimmed).
- Both apps now ship a byte-identical `index.html` shell apart from `<title>`. Rows previously
  shipped a decorative `wwwroot/favicon.png` (1,148 B, served `identity`, the 40th request); it has
  been deleted.

So **a publish from today's source will not reproduce entry #1's Rows bytes exactly.** Expect Rows to
make **39** requests rather than 40 and to weigh roughly 1.2 KB less. This is ~0.06% of total weight
and changes no Phase 0 conclusion, but a reproducer should know it rather than discover it as a
mismatch. Counter is unaffected. The re-measured numbers belong in a new `BENCH.md` entry.
