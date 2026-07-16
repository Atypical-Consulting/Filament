# Filament — Phase 0 baseline

This repository holds the **Phase 0 Blazor WebAssembly baseline**: the measured numbers that the
Filament criteria **C2** (weight) and **C4** (speed) will later be judged against.

Nothing here is a Filament implementation yet. What is here is a baseline, the harness that measured
it, the raw evidence, and enough instruction to let a stranger reproduce all of it cold.

| File | What it is |
|---|---|
| `BENCH.md` | **Append-only** measurement register. **Entry #2 holds the numbers that count**; entry #1 is superseded but kept. Never edit an entry; rectify with a new one. |
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
cd bench/harness && npm run selftest    # 440 passed, 0 failed at entry #2
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
what the numbers mean. **`BENCH.md` entry #2 holds the numbers that count**; entry #1 is the archive of
what was measured that morning, and its headline figures are superseded. Five things in particular:

- **The warm numbers are the headline; the cold ones are context.** `create-cold` is the first click on
  a freshly loaded page, so it carries Blazor's boot. `create-warm` is a second timed `#run` in the same
  page, and it is what actually measures row building. C4 is judged on `create-warm` — **7.35 ms AOT /
  13.70 ms non-AOT** (`DECISIONS.md` #13). A framework that boots faster beats `create-cold` without
  rendering a single row faster.
- **Cold numbers do not reproduce across sessions. Never quote one as a stable reference.**
  `counter-nojit/increment-cold` was **25.55 ms** in round 1 (samples 21.4–27.0) and **17.15 ms** in
  round 2 (samples 13.1–18.0) — same machine, same Chrome, same SDK, same `n = 10`, and **zero sample
  overlap**. A 33% swing is not noise. Never compare an entry #1 number against an entry #2 one
  (`DECISIONS.md` #18).
- **The encoding basis moves the C2 target by up to 31%.** Rows non-AOT is 1,888,029 B gzip vs
  1,553,388 B brotli — a 50x target of 37,761 B vs **31,068 B**. **Brotli is the headline basis**
  (`DECISIONS.md` #14): a real static host serves brotli, and it is the harder target. **Filament must
  be measured under the same `--max-encoding` as the baseline it is compared to.** A brotli Filament
  against a gzip Blazor is not a 50x ratio, it is an encoding artifact.
- **C1 (< 10 KB gzip) is the binding weight gate, not C2.** C2's brotli target is 31,068 B — **~3x more
  permissive than C1** — so C2 passes automatically once C1 does. "Filament beats Blazor 50x on weight"
  is the *least* demanding weight result Filament will ever produce (`DECISIONS.md` #16). C1's value
  rests on the owner's authority at the Phase 0 gate, not on a spec file: no spec is on disk.
- **Weight is judged against non-AOT, speed against AOT — never the reverse.** AOT is 2.16x heavier in
  brotli but 1.86x faster at row building, so the loophole is symmetric and doubly tempting. Claiming a
  50x weight win against AOT (67,016 B — 2.16x softer) **while** comparing speed against non-AOT
  (13.70 ms — 1.86x softer) is picking the easy half of each criterion, and each half is invisible on
  its own (`DECISIONS.md` #15).

### The estimate this README used to publish, and why it was wrong

Earlier revisions of this file said, sourced to `BENCH.md` reserve #1:

> Boot-adjusted, real row building is ~9.85 ms interpreted / ~9.05 ms AOT.

**That estimate is refuted.** It was never measured. It was `create-cold` (Rows) minus `increment-cold`
(Counter). Directly measuring `create-warm` gives **13.70 ms interpreted / 7.35 ms AOT**.

The error was not noise — **it inverted a conclusion.** The estimate implied interpreted and AOT were
nearly tied at row building (9.85 vs 9.05 = **1.09x**). Direct measurement shows AOT is **1.86x faster**
(13.70 vs 7.35). Entry #1's reserve #2 held that the AOT win was not a rendering speed-up; it is one,
and entry #1 understated it.

Two flaws, and both are real:

1. **Its input does not reproduce.** The subtraction is only as stable as the boot term it subtracts,
   and that term moved 33% between sessions. The *same* subtraction yields **9.85 ms** on round 1's
   `increment-cold` (35.40 − 25.55) and **17.0 ms** on round 2's (34.15 − 17.15) — against a directly
   measured **13.70 ms**. Which answer you get depends only on which session supplied the boot term.
   This is what produced the 9.85 ms estimate.
2. **It subtracts a term measured on a different app.** `create-cold` is Rows, `increment-cold` is
   Counter. The two boot costs are not the same quantity, so the subtraction is not an identity — and
   fed clean, same-round inputs it is a *biased* estimator, overshooting in all four available pairings:

   | pairing | derived | measured | error |
   |---|---|---|---|
   | `rows-nojit`, br | 17.00 | 13.70 | **+24.1%** |
   | `rows-nojit`, gzip | 16.00 | 13.70 | +16.8% |
   | `rows-aot`, br | 8.05 | 7.35 | +9.5% |
   | `rows-aot`, gzip | 7.55 | 7.45 | +1.3% |

   Quoting only the +1.3% pairing — as an earlier draft of this file and of `DECISIONS.md` #13 did —
   flatters the method by picking the best of four.

The systematic sign is the tell: the derived value is **never low**. That is not noise, it has the cause
described below — `create-warm` inherits a warmed GC heap, so derived and warm do not measure the same
quantity. The lesson is therefore not "distrust the instrument, not the algebra". It is **do not derive
what you can measure**, which is what round 2 did.

**And `cold − warm` is not "boot".** This file also called ~61–72% of cold `create` "Blazor runtime
boot". That is a mislabel (`BENCH.md` entry #2, boot analysis). `cold − warm` is 20.45 ms for
`rows-nojit` (59.9% of cold) and 16.55 ms for `rows-aot` (69.2%), but the independently measured boot
proxy (`increment-cold − increment-warm`) is only 15.85 ms (**46.4%** of cold) and 14.85 ms (**62.1%**).
The ~4.6 ms residual on non-AOT is the interpreter tiering up, not boot.

`BENCH.md` entry #1 was written **before** the Phase 0 gate decisions on basis and on cold/warm
`create`. It is append-only and is not rewritten: it remains the true record of what was measured that
morning. **Entry #2 is the rectified headline** and supersedes it — including on the estimate above.

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
mismatch. Counter is unaffected. **`BENCH.md` entry #2 is that re-measurement**: it records 39 requests
for both apps and Rows non-AOT at 1,888,029 B gzip / 1,553,388 B brotli, against entry #1's
1,889,184 B gzip — a 1,155 B drop, consistent with the deleted favicon.
