# Filament — Phase 0 baseline + Phase 1 runtime + Phase 2/3 generator

This repository holds the **Phase 0 Blazor WebAssembly baseline**, the **Phase 1 hand-written
`filament-runtime`** measured against it, and the **Razor → JS generator**: the numbers
that the Filament criteria **C1** (bundle weight), **C2** (weight vs Blazor), **C3** (DOM writes) and
**C4** (speed) are judged on.

**`Counter` now compiles from PURE `.razor`** — template *and* `@code`, C# and all —
via `src/Filament.Generator/`, a console app. Read the scope statement before quoting anything:

> **🟢 THE PHASE 2 GATE ON `Counter` NOW PASSES, AND THE STATE LIFTING IS IN THE BYTES.** Phase 3
> compiles `@code` with Roslyn: `private int currentCount = 0` is **lifted by the compiler** to
> `const currentCount = signal(0)`, and `currentCount++` maps to `currentCount.value++`. The `@code`
> block of `samples/Counter/Counter.razor` is now **byte-identical to the baseline's**, and the
> generator compiles **`baseline/Counter.Blazor/App.razor` itself** — the file Blazor compiles — to a
> module **alpha-equivalent** to the Phase 1 answer key (670 B minified / 234 tokens, both sides).
> DECISIONS.md #71 records the control that proves the gate can still FAIL: neutralise the inlining
> and it goes NOT EQUIVALENT at token #39, which is #55's divergence reproduced.
>
> **🔴 PHASE 3 IS NOT PASSED.** Its gate is a conjunction — *"**both apps** compile from pure `.razor`,
> the measurements are unchanged, and 20 out-of-subset cases produce 20 correct diagnostics."*
> **`Rows` now COMPILES from pure `.razor`** — from `baseline/Rows.Blazor/RowsApp.razor`, the file
> Blazor compiles — and it runs: byte-exact label stream against the golden C# oracle, `#update` at
> **100 `characterData` writes and zero reconcile**, `#swap` at **exactly 2 moves** (DECISIONS.md #72,
> #76). But its **equivalence gate is RED**: three shape divergences from `samples/Rows/rows.js`
> remain, none of them a translation bug, and **all three are the owner's call** — the handler mapping
> (#68, the two keys disagree), `+=` on a signal, and four whitespace Text nodes **the answer key omits
> and BLAZOR SHIPS**. Neutralising exactly those three gives ALPHA-EQUIVALENT at 2 200 B / 887 tokens,
> dead on the key. The out-of-subset suite has **27** cases, all refused and all located — and auditing
> it found that its "cascading parameters" case had been passing **for the wrong reason** while the same
> non-goal on a *field* compiled at exit 0, plus that `private int x = "a string";` emitted a module
> (DECISIONS.md **#77**). Both are fixed and mutation-tested; **three §5 false positives stay OPEN and
> disclosed**. The "measurements unchanged" term still belongs to the Measure phase (#44).
>
> **⚠️ THE TWO ANSWER KEYS DISAGREE about the handler mapping**, and it is an OWNER's call before the
> `Rows` step: `counter.js` **inlines** a single-use handler body, `rows.js` emits `function update()`
> and references it though it is named by exactly one `@onclick`. No single rule reproduces both.
> DECISIONS.md #68.
>
> **⚠️ DECISION #20's DEBT IS STILL OPEN AND NOT BANKED.** Filament builds **5** child nodes where
> Blazor builds **7**. The 2 extra are `<!--!-->` markers — Blazor's own bookkeeping — but 5 < 7 is a
> real, free create-time advantage.

`src/Filament.Core/` and `src/Filament.Analyzer/` are still **empty directories**.

| File | What it is |
|---|---|
| `BENCH.md` | **Append-only** measurement register. **Entry #5 measures the GENERATOR's output** (C1/C3/C4, hand vs generated vs Blazor, one run). **Entry #4 holds the Phase 1 C1/C4 numbers** and is **not** superseded by #5 — #5 adds a label, it does not re-measure `Rows` or `create/update/swap/clear`. Entries #1–#3 are kept as archive. Never edit an entry; rectify with a new one. |
| `DECISIONS.md` | Why each number was produced the way it was, and every arbitrage. Read this before disputing a result. |
| `src/filament-runtime/` | The signals runtime (`signal`/`computed`/`effect`/`list`). `npm run verify` = build + typecheck + tests + **2 048 B size gate**. |
| `src/Filament.Generator/` | **The generator.** A console app (`dotnet run -- <in.razor> <out.js>`), **not** an `ISourceGenerator` and not an MSBuild target — spec §4.3 excludes Roslyn source generators, since they cannot emit non-C# (DECISIONS.md #58). `TemplateCompiler` compiles the template (Razor IR); `CSharpFrontEnd` compiles `@code` (Roslyn) and does the **state lifting**. |
| `tests/Filament.Generator.Tests/` | The generator's suite, **including the Phase 2 gate** (now GREEN on `Counter`) and Phase 3's out-of-subset suite: **27 cases — 26 out-of-subset constructs covering the 20 the gate names, + 1 disclosed non-C# case** — each **refused, located, no file written** (`GateSubsetTests`), against **negative controls** that must compile CLEAN (`NegativeControls`). **161 pass, 1 fails**, and the failure is **`Rows`' equivalence gate** (DECISIONS.md #76), committed RED on purpose. |
| `tools/canon.mjs` | The alpha-equivalence comparator that **decides** the gate (DECISIONS.md #51/#56). `node tools/canon.test.mjs` → 23 tests. Its limitations are in its own header. |
| `samples/Counter/Counter.razor` | The generator's **input**, now **pure `.razor`**. Its markup AND its `@code` are `baseline/Counter.Blazor/App.razor`'s, **verbatim** — both pinned by tests. The `@code` is **C#**, compiled by Roslyn (Phase 3). |
| `samples/Counter/counter.js` | The Phase 1 **answer key** — the hand-written reference the generator is judged against. **Never edited to make the gate pass** (DECISIONS.md #21/#51). |
| `bench/publish-baseline.sh` | Regenerates the four **Blazor** publish outputs. The only on-disk definition of those configs. |
| `bench/build-filament.sh` | Builds the six **Filament** labels (3 production + 3 `-stats`), and **invokes the generator** for the `-gen` labels. Static root is `<label>/` — **no `wwwroot/`**, unlike Blazor. |
| `bench/run-phase2-gen.sh` | Entry #5's measurement, in its **mirrored interleaved order**. The order is in the script on purpose; do not improvise it. |
| `bench/harness/bench.mjs` | Framework-agnostic Playwright/CDP measurement driver. Emits a **content hash** identifying the harness into every result. |
| `bench/harness/server.mjs` | Production-like static server with per-request content negotiation. |
| `bench/results/*.json` | **The measured evidence.** Tracked in git on purpose. |

> **The harness identifies itself by CONTENT HASH, not by a version string.** `HARNESS_VERSION` was
> hand-maintained and stayed `1.2.0` across a 701-line diff, so two materially different harnesses
> both claimed to be the same one and **nothing could detect it**. `computeHarnessIdentity()` now
> hashes `bench.mjs` + `server.mjs` + `expected-labels.json` at runtime into `environment.harness`,
> and **throws rather than degrade**. A cross-config comparison is **refused** unless all configs
> share one hash. The version string is kept as a **label, not evidence**. See DECISIONS.md #43.

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

## Step 1b — The generator (Phase 2 template + Phase 3 `@code`)

```bash
# Compile a Razor template to direct DOM calls. This is exactly what build-filament.sh
# runs, and the output path is gitignored (#59).
dotnet run --project src/Filament.Generator -- \
  samples/Counter/Counter.razor samples/filament-counter-gen/Counter.g.js

# The emitted module imports the runtime by a specifier resolved RELATIVE TO THE OUTPUT
# DIRECTORY, by walking up to src/filament-runtime/src/index.ts. Emitting OUTSIDE the
# repo therefore fails loudly -- `error FIL000: could not locate ... Pass --runtime` --
# rather than emitting a module with a dangling import. Override it explicitly:
dotnet run --project src/Filament.Generator -- \
  samples/Counter/Counter.razor /tmp/Counter.g.js --runtime 'filament-runtime'

# Inspect the Razor IR the generator sees (this is how #52/#54 were established).
# NOTE THE ORDER: --dump-ir comes FIRST. `<file> --dump-ir` is not an error -- it
# writes a file literally named "--dump-ir", which is exactly the kind of silent
# nonsense this repo refuses. Verified by running both forms.
dotnet run --project src/Filament.Generator -- --dump-ir samples/Counter/Counter.razor

# The suite, INCLUDING both gates. Expect 161 passed, 1 failed -- the failure is Rows'
# equivalence gate, RED on purpose (DECISIONS.md #76). Anything else failing is a regression.
dotnet test tests/Filament.Generator.Tests/Filament.Generator.Tests.csproj

# The comparator that decides the gate, and its own tests.
node tools/canon.mjs <generated.js> samples/Counter/counter.js   # exit 0 = alpha-equivalent
node tools/canon.test.mjs                                        # 23 passed
```

**The gate on `Counter` is GREEN, and it was closed by doing the work, not by moving the threshold.**
`Gate_GeneratedCounter_IsAlphaEquivalentToAnswerKey` was committed RED for a phase because the answer
key **inlines** `Increment`'s body, which requires translating `@code` — Phase 2's scope excluded that,
so the phase's scope contradicted its own gate (`DECISIONS.md` #55/#62). **Phase 3 translates `@code`,
so the divergence became reachable and is closed.** `samples/Counter/counter.js` was **NOT** edited: the
answer key is the **reference** and the generator is what is **judged** (#21/#51). The control that
proves the gate can still fail: neutralise the inlining and it reports NOT EQUIVALENT at token #39 —
#55's divergence, reproduced (#71).

**`Rows` compiles and runs; its equivalence gate is RED on three OWNER-level shape calls, so the
PHASE 3 gate — a conjunction — is NOT passed** (#76).

Things worth knowing before you run it:

- **Out-of-subset constructs raise a LOCATED diagnostic and write NO file** — never silently wrong JS
  (spec §10). The codes are **the spec's**: **`FIL0001`** out-of-subset C#, **`FIL0002`** out-of-subset
  type, **`FIL0003`** out-of-subset Razor — each with a `[reason]` tag; tool failures carry
  `FIL-WIRING`, which cannot be mistaken for a spec code (#61). Try it:
  `dotnet run --project src/Filament.Generator -- tests/Filament.Generator.Tests/Unsupported/If.razor samples/Counter/x.js`
  → **`If.razor(2,2): FIL0001: [control-flow-not-yet-implemented]`, exit 1, and NO file is written**.
  (The output path must be **inside the repo**: the generator computes the runtime's import specifier
  as a real relative path and refuses with `FIL-WIRING` if it cannot find it — which is the tool being
  misused, not your Razor being unsupported, and it says so.)
- **`@foreach` is RAW C# TEXT in Razor's IR** — unbalanced braces, and the element is a **sibling** of
  the loop header (#54). Razor emits no loop node because Blazor never needs one: it re-parses that
  text with Roslyn and calls `RenderTreeBuilder` at runtime. Filament emits JS, so it **RE-ASSEMBLES
  the spans and RE-PARSES them**, putting the markup back as a marker call inside the same class as
  `@code` — so `Row`, `_rows` and the loop local `row` all resolve to real symbols and nothing is ever
  spliced (#72). `--dump-ir` shows the shape it starts from.
- **The generator is a console app, not an `ISourceGenerator`.** Spec §4.3 says Roslyn cannot emit
  non-C# into a compilation, and JS is non-C#; the source-generator route is excluded **by the spec**,
  not by us. An MSBuild target is a **packaging** concern that changes no emitted byte (#58).
- **It pins `Microsoft.AspNetCore.Razor.Language` 6.0.36 — the last published version, frozen in 2021
  and out of support.** This is **not a preference**: every newer route is closed (the 10.x package
  does not restore; the SDK DLL's API is internal; the syntax tree is internal in all versions). This
  risk weighs **against** the RADICAL variant of spec §8 (#52).
- **The emitted `samples/filament-counter-gen/Counter.g.js` is gitignored and re-emitted on every
  build.** A committed generated file is one somebody eventually hand-edits, and C1 would then be
  measured on a bundle the generator did not emit while the label still claimed it did (#59).

---

## Step 2 — Measure

**The static root differs by framework, and this is a documented footgun:**

| Framework | Static root |
|---|---|
| **Blazor** (`blazor-*`) | `bench/publish/<label>/wwwroot` — **never** `bench/publish/<label>` |
| **Filament** (`filament-*`) | `bench/publish/<label>` — **no `wwwroot/`** |

`dotnet publish` interposes a `wwwroot/`; esbuild has no reason to invent one. Pointing `bench.mjs`
at `filament-counter/wwwroot` yields **ENOENT, not a wrong number**, so this cannot silently corrupt
a result — but it will waste your afternoon. `build-filament.sh` prints the exact `--dir` to use.

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

### Phase 2 — measuring the generator's output (entry #5)

```bash
bash bench/build-filament.sh            # 6 labels; re-emits Counter.g.js from Counter.razor
bash bench/publish-baseline.sh blazor-counter-nojit blazor-counter-aot
bash bench/run-phase2-gen.sh            # 8 configs C1/C4 in mirrored order, then 3 C3 passes
```

**The order is in the script, and it is the point.** Entry #4 had to disclose an order confound
(reserve F): all Blazor ran first, all Filament last, so thermal drift was perfectly confounded with
framework identity. `run-phase2-gen.sh` runs a **mirrored (counterbalanced)** order —
`nojit, hand, gen, aot` on gzip and `aot, gen, hand, nojit` on brotli — so every config appears once
in each half and **the two Filament labels sit adjacent in both passes**. **Do not improvise the
order**; `bash bench/run-phase2-gen.sh gzip|br|c3` splits the passes **without** changing it.

**C3 runs last and separately, deliberately.** The allocation probe leaves V8's sampling profiler on
and forces GCs — a C4 median taken under it would be measuring the instrument. C3 runs on the
`-stats` labels, which are the **only** bundles with instrumentation compiled in and **must never be
weighed**.

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
| `bench/results/phase1-clean/` | **Entry #4's evidence**: 12 per-config JSONs (both frameworks, one harness), `summary.json`, `run.log`. **Tracked in git.** |
| `bench/results/phase2-gen/` | **Entry #5's evidence**: 11 per-config JSONs — 8 C1/C4 (hand, **generated**, Blazor nojit, Blazor AOT × gzip/brotli) + 3 C3. All share harness hash `47e7e46f…`. **No `summary.json`** — see entry #5 reserve H. **Tracked in git.** |
| `bench/publish/<label>/` | Publish output. Gitignored; regenerate with the script. |

`--out` is the only thing that decides where a result is written; nothing is auto-discovered.

---

## Interpreting the results

**Read `BENCH.md` and `DECISIONS.md` before quoting any number.** Both carry reserves that change
what the numbers mean. **`BENCH.md` entry #5 holds the generator numbers**; **entry #4 holds the
Phase 1 C1/C4 numbers** — both frameworks re-measured on **one** harness, on the **bug-fixed**
runtime. Entry #2 holds the **Blazor baseline** (its weight figures reproduce byte-exactly in entry #4
and are **not** superseded). Entries #1 and #3 are kept as archive; **entry #3's C4 ratios are
superseded as quantities** — every Blazor timing moved on the clean harness, and the old ratios
**understated** Filament. **Entry #3's C3 numbers still stand**: C3 was **not** re-measured in
entry #4, and entry #5 measures C3 on `Counter` only.

### What entry #5 established about the generator — exactly this, and no more

| | |
|---|---|
| **C1** | ✅ **PASS. 2,994 B gzip on the wire, 70% of the 10,000 B budget unused.** |
| **Generator's cost vs the answer key** | 🟢 **+18 B gzip (+0.60%) · +19 B brotli (+0.73%)** — 0.18% of the budget. **IQR 0 both sides: resolved, not noise.** |
| **C3** | ✅ Generated output does **exactly 1 DOM write per increment** (`characterData`). **So does Blazor** — this is a correctness bar, **not** a win. |
| **C4** | Generated vs hand-written is **indistinguishable AT THE INSTRUMENT'S FLOOR**. **That is not a measurement of parity.** |
| **Phase 2 gate** | 🔴 **FAIL** *at the time of entry #5* — `Rows` not done, equivalence on `Counter` failed at token #42. **Phase 3 has since CLOSED the `Counter` half: it is now ALPHA-EQUIVALENT** (DECISIONS.md #71). `Rows` remains not done. |

**The +18 B is fully attributed, constructively.** Neutralising the two divergences of DECISIONS.md
#55 one at a time reproduces the answer key's bundle **byte-for-byte in SIZE — 2,986 B raw and 1,265 B
gzip on both sides — and `canon` rules the two ALPHA-EQUIVALENT.** The bundles are **not identical
byte-for-byte**, and the difference is exactly what alpha-equivalence exists to ignore: **12 of the
2,986 bytes differ (0.40%), every one of them a single-letter identifier esbuild's minifier assigned
differently** (`b`↔`h`, `_`↔`b`). No token, no string, no call differs. **The template compilation
costs ZERO bytes** over hand-written JS; the entire delta is those two named divergences (whitespace
nodes **11 B**, handler indirection **7 B**). *(Earlier wording here read "to the byte", which claimed
byte-identity the artifacts do not have. Corrected; see DECISIONS.md #66 and `BENCH.md` entry #6, which
rectifies entry #5. The measured claim is unchanged — only the word for it was too strong.)*

**Read the sign of that delta carefully — it was never a regression, and the answer key has since been
CORRECTED.** 11 of the 18 bytes were two `"\n\n"` text nodes **that Blazor also ships**. Measured
in-browser, `#app.childNodes` was **Blazor 7, generated 5, answer key 3**: the generator built a DOM
strictly CLOSER to Blazor's than the answer key did — **it was the answer key that diverged from the
baseline**, which nobody had noticed for `Counter`. The owner ruled the answer key **corrected**
(DECISIONS.md **#64**), because a DOM contract that is not actually shared invalidates every C4
comparison built on it. Re-measured in-browser after the correction: **Blazor 7, generated 5, answer
key 5**.

> ⚠️ **The +18 B row above is therefore SUPERSEDED and awaits re-measurement.** It was measured against
> the *old* 3-node answer key. Both sides now ship the two text nodes, so the whitespace component of
> the delta is gone and only the handler indirection remains. Build-time bundle preview (**not** the
> wire measurement that decides C1 — see DECISIONS.md #44): answer key **1,265 → 1,276 B gzip**,
> generated **1,283 B**, i.e. a delta of **+7 B gzip**, which is exactly the handler cost entry #5 had
> already isolated. **No published wire figure is hand-edited here; the Measure phase re-measures.**
>
> ⚠️ **SUPERSEDED AGAIN BY PHASE 3, AND THE DELTA IS NOW ZERO.** Phase 3 inlines the single-use handler
> body the way the answer key does, which removes the last of the two divergences. Build-time bundle
> preview (**not** the wire measurement — DECISIONS.md #44): `filament-counter` **3,056 raw / 1,276
> gzip** and `filament-counter-gen` **3,056 raw / 1,276 gzip** — **identical sizes, delta 0 B**. `cmp`
> still differs in **12 of 3,056 characters, every one a minified identifier letter** (`_`↔`h`, `b`↔`_`),
> so the claim is **equal size + alpha-equivalence, NOT byte-identity** (#66 rectified exactly that
> over-claim). **This now measures a generator that also lifts the state**, which the +18 B never did.
> **It still needs a real re-measurement on the wire.**

**The residual is still open, and is not banked.** Even corrected, Filament builds **5 nodes to Blazor's
7**. The 2 extra are `<!--!-->` **comment markers** — one per `AddMarkupContent` call, Blazor's own
bookkeeping for locating a raw-markup range later. Filament has no render tree and nothing to locate, so
it emits none; that is defensible, and **it is still a free create-time advantage**. `DECISIONS.md` #20's
debt is now **quantified for `Counter` (2 of 7 nodes)** and **still open**.

**RADICAL vs PRUDENT (spec §8) — updated by Phase 3 (`BENCH.md` entries n°7/8).** Phase 2's verdict
below ("Counter alone, logic hand-written") is **superseded**. Phase 3 compiles **both** apps from pure
`.razor`, `@code` included, and measures C1/C4 on that output: **C1 PASS** (counter-gen 2,987 B /
rows-gen 4,373 B gzip) and **C4 PASS** (rows-gen beats the faster Blazor/AOT on all four scenarios —
create-warm 3.10 vs 7.90 ms, update/swap/clear likewise). So the §8 **viability condition is met and
measured for `Counter` AND `Rows`** — including all of C4's 1,000-row target and the DOM-heavy work.
**That is the honest ceiling of the claim, and no more:** RADICAL is an architecture claim about a whole
framework, and two demo apps over a **narrow §5 subset** (the §3 non-goals — async, LINQ, generics,
inheritance, DI, routing, forms, `EventCallback`, `RenderFragment` — are entirely unexercised) do not
establish it. The thesis is **not falsified** and RADICAL is **not eliminated**; it is **not established
as an architecture** either. The EOL-package risk (#52: Razor `6.0.36` is frozen, out of support) bears
asymmetrically on RADICAL and is part of its price. The adversarial audit (`DECISIONS.md` #79) ran:
**0 blockers**, measurement confirmed, no fifth recurrence of the #41 splice bug.

**Two things in particular, before you quote a speed number:**

- **`increment-warm` is FLOOR-LIMITED. Never quote it as a speedup.** 3/10 samples read exactly
  0.0 ms — below the ~0.1 ms `performance.now()` quantum. **"At least ~11x" is defensible; "11x" and
  the gzip-implied "20x" divide by a quantization artifact and are not measurements.**
- **`update` and `swap` are resolved but COARSE** (3–4 quanta ⇒ ~±17% / ±13% on the *ratio*).
  Verdicts are safe; **do not cite these ratios to 3 significant figures.**

Five more things:

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
