#!/usr/bin/env bash
#
# build-filament.sh — build the Filament demo apps into a measurable static root.
#
# The counterpart of publish-baseline.sh, and it deliberately mirrors that
# script's conventions: same label-driven CLI, same PUBLISH_ROOT override, same
# per-project mkdir lock, same "verify the artifact, never the flag" stance on
# post-build assertions. Read publish-baseline.sh first; everything structural
# here is borrowed from it on purpose, so that the two halves of the comparison
# are produced by two scripts a reviewer can diff.
#
# Six labels from THREE source trees, in TWO modes:
#
#   filament-counter           samples/filament-counter       production  (minified, stats DCE'd out)
#   filament-rows              samples/filament-rows          production  (minified, stats DCE'd out)
#   filament-counter-gen       samples/filament-counter-gen   production  (minified, stats DCE'd out)
#   filament-counter-stats     samples/filament-counter       instrumented (stats compiled IN)
#   filament-rows-stats        samples/filament-rows          instrumented (stats compiled IN)
#   filament-counter-gen-stats samples/filament-counter-gen   instrumented (stats compiled IN)
#
# ---------------------------------------------------------------------------
# THE -gen LABELS: THE GENERATOR'S OUTPUT, NOT THE ANSWER KEY
# ---------------------------------------------------------------------------
# filament-counter mounts samples/Counter/counter.js -- the Phase 1 ANSWER KEY,
# written by a human. filament-counter-gen mounts the JS that Filament.Generator
# EMITS from samples/Counter/Counter.razor. The two apps are otherwise identical
# down to the byte: same runtime, same host shim modulo one import line, same
# shell, same stylesheet. That is deliberate and it is the whole point -- the only
# variable between the two labels is who wrote the component, so the C1 delta
# between them is the generator's cost and nothing else's.
#
# DECISIONS.md #21/#34/#50 is why this label exists. Every weight and every timing
# the POC has published describes hand-written JS. The proposition that actually
# carries the thesis -- "a C# generator emits this, under 10 ko, at these times" --
# had never been measured; #50 records that the imposed Phase 2 deliverable is to
# write the generator for the counter and RE-MEASURE C1/C3/C4 ON ITS OUTPUT.
# Wiring the generator in here is what closes the debt #58 left open ("build-filament.sh
# n'appelle pas encore le generateur ... donc C1/C3/C4 n'ont PAS ete re-mesures sur
# la sortie du generateur").
#
# THE EMITTED FILE IS RE-EMITTED EVERY BUILD, AND IS NOT COMMITTED.
# samples/filament-counter-gen/Counter.g.js is deleted and regenerated below on
# every run, and it is gitignored. A committed generated file is one somebody
# eventually hand-edits, and then C1 is measured on an artifact the generator did
# not produce while the label's name still says it did. That failure would be
# silent, which is the class of failure this repo exists to refuse. Regenerating
# unconditionally costs ~1 s and makes staleness structurally impossible rather
# than merely unlikely. The generator is a console app (#58), so this is a plain
# `dotnet run` -- spec 4.3's MSBuild target is a packaging concern that changes no
# emitted byte.
#
# WHY THE STATS BUILD IS A SEPARATE LABEL, AND NOT A FLAG ON THE SAME OUTPUT.
# C1 (< 10 ko gzip) is measured on the production bundle; C3 (1 DOM write, 0 tree
# allocation) is measured on the instrumented one. If one directory could be
# either, a C1 number read from a stats build — or a C3 number read from a build
# with no stats in it — becomes a plausible accident rather than an impossible
# one. Separate labels make that confusion structurally impossible: the C1 bundle
# and the C3 bundle cannot be the same bytes, and their names say so.
#
# !!! THE STATIC ROOT IS bench/publish/<label>/ -- WITH NO wwwroot/ !!!
# This is the one place this script deliberately DIVERGES from publish-baseline.sh,
# and it is a documented footgun. `dotnet publish` interposes a wwwroot/ that
# esbuild has no reason to invent, so:
#
#   Blazor:   bench/publish/blazor-counter-nojit/wwwroot   <- static root
#   Filament: bench/publish/filament-counter               <- static root
#
# README.md's standing instruction ("the static root is <label>/wwwroot, NEVER
# <label>") is a BLAZOR rule. Pointing bench.mjs at filament-counter/wwwroot
# yields ENOENT, not a wrong number, so this cannot silently corrupt a result --
# but it will waste your afternoon. The script prints the exact --dir to use.
#
# ---------------------------------------------------------------------------
# THE RUNTIME CONTRACT THIS SCRIPT ENFORCES FROM THE ARTIFACT
# ---------------------------------------------------------------------------
# publish-baseline.sh refuses to believe -p:RunAOTCompilation=true and measures
# dotnet.native.wasm instead. The same principle applies here: passing
# --define:__FILAMENT_STATS__=false does not PROVE the stats code left the
# bundle, it only asks for it. Dead-code elimination silently fails to fire the
# moment stats state is reachable from a live path (e.g. an object literal built
# unconditionally and only *read* under the flag). A C1 number measured on a
# bundle that still carries its instrumentation is exactly the kind of quiet
# wrong answer this project exists to prevent.
#
# So src/filament-runtime/ MUST hold up two conventions, and this script fails
# the build if the artifact says otherwise:
#
#   1. Every stats-only statement sits behind `if (__FILAMENT_STATS__) { ... }`.
#      esbuild substitutes the define, then minification drops the dead branch.
#   2. Inside that gate, the literal string "filament:stats" appears at least
#      once. It is the marker this script greps for, and nothing else may emit it.
#
# The marker is the whole verification: absent from the production bundle proves
# the branch was eliminated; present in the instrumented bundle proves the
# instrumentation is actually compiled in and the C3 run is not measuring a
# no-op. A marker-free convention (e.g. "just trust the define") is unverifiable
# from the artifact, which is the failure mode DECISIONS.md #10 names.
#
# Usage:
#   ./bench/build-filament.sh                      # all four labels
#   ./bench/build-filament.sh filament-counter     # a subset, by label
#   ./bench/build-filament.sh --list               # show known labels
#
# Set PUBLISH_ROOT to build somewhere else, exactly as publish-baseline.sh does:
#
#   PUBLISH_ROOT=/tmp/scratch ./bench/build-filament.sh filament-counter
#
# ---------------------------------------------------------------------------
# COMPRESSION PARITY -- this is what makes C1 an honest number
# ---------------------------------------------------------------------------
# `dotnet publish` emits .gz and .br siblings at maximum compression, and
# server.mjs prefers a precompressed sibling when one exists. A Filament that
# shipped no siblings would be gzipped on the fly while Blazor served its
# max-level precompressed bytes -- Filament would be charged for the difference
# and C1 would be measured against a handicap it never earned.
#
# So this script emits both siblings, at the SAME settings server.mjs itself
# uses (GZIP_LEVEL = 9, BROTLI_QUALITY = 11) via node's zlib. Verified against
# the published Blazor artifacts on 2026-07-16:
#
#   index.html   2226 B raw | dotnet .br 713 B == node q11 713 B  (EXACT)
#   css/app.css   795 B raw | dotnet .br 360 B == node q11 360 B  (EXACT)
#
# Brotli is byte-identical, which matters because brotli is the headline basis
# (DECISIONS.md #14). Gzip lands within +-5 B (index.html: dotnet 928, node 933;
# app.css: dotnet 474, node 473) -- .NET's deflate and zlib's deflate are
# different implementations of the same maximum level, not different levels.
# The residual is ~0.5% on a ~1 KB file and it runs AGAINST Filament on the
# larger file, so it cannot manufacture a C1 pass.
#
# ---------------------------------------------------------------------------
# INDEX.HTML SHELL PARITY
# ---------------------------------------------------------------------------
# The shell is GENERATED here from a template rather than committed next to each
# app, because parity is only worth having if it cannot drift. An auditor found
# Blazor's two apps shipping different shells; the fix is not to ask two app
# authors to keep two files in sync, it is to have one template and one script.
#
# The template is Blazor's published shell with the Blazor-runtime-specific
# elements removed. Every removal is reported, with its byte cost, by
# --shell-parity. Elements that are NOT Blazor-specific are KEPT even when they
# cost Filament bytes it could trivially drop (the favicon comment being the
# clearest case): every retained byte makes C1 harder, and a weight result is
# only interesting if it was not helped along.

set -euo pipefail

REPO_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
# Overridable so the script can be exercised without clobbering measured output.
PUBLISH_ROOT="${PUBLISH_ROOT:-$REPO_ROOT/bench/publish}"

# The reference shell every Filament shell is diffed against. Counter and Rows
# ship the same shell modulo <title> and the dotnet.js fingerprint, so either
# works as the reference; counter is named for determinism.
BLAZOR_REFERENCE_SHELL="$PUBLISH_ROOT/blazor-counter-nojit/wwwroot/index.html"

# Must match server.mjs's GZIP_LEVEL / BROTLI_QUALITY, or the siblings this
# script writes are not the bytes the server would have produced on the fly and
# "precompressed sibling" stops meaning "identical, just earlier".
GZIP_LEVEL=9
BROTLI_QUALITY=11

# The artifact-level proof that the stats define did what it was asked to do.
STATS_MARKER='filament:stats'

# C1, both readings. The spec says "10 ko" and does not say which one it means,
# so the script refuses to pick for you and reports against both.
C1_DECIMAL=10000
C1_BINARY=10240

ALL_LABELS=(
  filament-counter
  filament-rows
  filament-counter-gen
  filament-rows-gen
  filament-divide-gen
  filament-compose-gen
  filament-rootforeach-gen
  filament-rootif-gen
  filament-boundcompose-gen
  filament-reactiveattr-gen
  filament-boolattr-gen
  filament-mixedattr-gen
  filament-stringattrs-gen
  filament-ifmulti-gen
  filament-ifelsemulti-gen
  filament-ifnested-gen
  filament-divideint-gen
  filament-loops-gen
  filament-moreattrs-gen
  filament-bind-gen
  filament-lambdahandler-gen
  filament-listops-gen
  filament-checkbind-gen
  filament-intbind-gen
  filament-codeblock-gen
  filament-trylock-gen
  filament-positionalrecord-gen
  filament-longcounter-gen
  filament-floatcounter-gen
  filament-decimalcounter-gen
  filament-datetimecounter-gen
  filament-linq-gen
  filament-counter-stats
  filament-rows-stats
  filament-counter-gen-stats
  filament-rows-gen-stats
)

project_for() {
  case "$1" in
    filament-counter|filament-counter-stats)         echo "samples/filament-counter" ;;
    filament-rows|filament-rows-stats)               echo "samples/filament-rows" ;;
    filament-counter-gen|filament-counter-gen-stats) echo "samples/filament-counter-gen" ;;
    filament-rows-gen|filament-rows-gen-stats)       echo "samples/filament-rows-gen" ;;
    filament-divide-gen)                             echo "samples/filament-divide-gen" ;;
    filament-divideint-gen)                          echo "samples/filament-divideint-gen" ;;
    filament-loops-gen)                              echo "samples/filament-loops-gen" ;;
    filament-compose-gen)                            echo "samples/filament-compose-gen" ;;
    filament-rootforeach-gen)                        echo "samples/filament-rootforeach-gen" ;;
    filament-rootif-gen)                             echo "samples/filament-rootif-gen" ;;
    filament-boundcompose-gen)                       echo "samples/filament-boundcompose-gen" ;;
    filament-reactiveattr-gen)                       echo "samples/filament-reactiveattr-gen" ;;
    filament-boolattr-gen)                           echo "samples/filament-boolattr-gen" ;;
    filament-mixedattr-gen)                          echo "samples/filament-mixedattr-gen" ;;
    filament-stringattrs-gen)                        echo "samples/filament-stringattrs-gen" ;;
    filament-moreattrs-gen)                          echo "samples/filament-moreattrs-gen" ;;
    filament-bind-gen)                               echo "samples/filament-bind-gen" ;;
    filament-lambdahandler-gen)                      echo "samples/filament-lambdahandler-gen" ;;
    filament-listops-gen)                            echo "samples/filament-listops-gen" ;;
    filament-checkbind-gen)                          echo "samples/filament-checkbind-gen" ;;
    filament-intbind-gen)                            echo "samples/filament-intbind-gen" ;;
    filament-codeblock-gen)                          echo "samples/filament-codeblock-gen" ;;
    filament-ifmulti-gen)                            echo "samples/filament-ifmulti-gen" ;;
    filament-ifelsemulti-gen)                        echo "samples/filament-ifelsemulti-gen" ;;
    filament-ifnested-gen)                           echo "samples/filament-ifnested-gen" ;;
    filament-trylock-gen)                            echo "samples/filament-trylock-gen" ;;
    filament-positionalrecord-gen)                   echo "samples/filament-positionalrecord-gen" ;;
    filament-longcounter-gen)                        echo "samples/filament-longcounter-gen" ;;
    filament-floatcounter-gen)                       echo "samples/filament-floatcounter-gen" ;;
    filament-decimalcounter-gen)                     echo "samples/filament-decimalcounter-gen" ;;
    filament-datetimecounter-gen)                    echo "samples/filament-datetimecounter-gen" ;;
    filament-linq-gen)                               echo "samples/filament-linq-gen" ;;
    *) return 1 ;;
  esac
}

# production | instrumented. Mirrors aot_for()'s label-suffix convention.
# filament-divide-gen is production-only: it has no C3, so no -stats variant exists.
mode_for() {
  case "$1" in
    *-stats) echo "instrumented" ;;
    filament-counter|filament-rows|filament-counter-gen|filament-rows-gen|filament-divide-gen|filament-compose-gen|filament-rootforeach-gen|filament-rootif-gen|filament-boundcompose-gen|filament-reactiveattr-gen|filament-boolattr-gen|filament-mixedattr-gen|filament-stringattrs-gen|filament-ifmulti-gen|filament-ifelsemulti-gen|filament-ifnested-gen|filament-divideint-gen|filament-loops-gen|filament-moreattrs-gen|filament-bind-gen|filament-lambdahandler-gen|filament-listops-gen|filament-checkbind-gen|filament-intbind-gen|filament-codeblock-gen|filament-trylock-gen|filament-positionalrecord-gen|filament-longcounter-gen|filament-floatcounter-gen|filament-decimalcounter-gen|filament-datetimecounter-gen|filament-linq-gen) echo "production" ;;
    *) return 1 ;;
  esac
}

# The Razor source this label's component is COMPILED FROM, or empty for the
# hand-written labels. Non-empty is what makes a label a generator label: it is
# the single switch that decides whether the generator runs, so a label cannot
# accidentally be half-generated.
#
# NOTE WHICH FILE THE ROWS LABEL NAMES: baseline/Rows.Blazor/RowsApp.razor is the
# file BLAZOR ITSELF COMPILES, not a copy kept in sync with it. There is no Rows
# analogue of samples/Counter/Counter.razor and there must not be one -- a copy is
# a thing that drifts, and "the generator compiles the same component Blazor does"
# would then be a claim about somebody's diligence instead of a fact about the
# bytes. Counter has a separate file only because its header documents the Phase 3
# mapping; a test (PureRazor_CounterRazor_IsTheBaselinesComponent) pins its markup
# and @code to the baseline's for exactly this reason.
razor_for() {
  case "$1" in
    filament-counter-gen|filament-counter-gen-stats) echo "$REPO_ROOT/samples/Counter/Counter.razor" ;;
    filament-rows-gen|filament-rows-gen-stats)       echo "$REPO_ROOT/baseline/Rows.Blazor/RowsApp.razor" ;;
    filament-divide-gen)                             echo "$REPO_ROOT/baseline/Divide.Blazor/App.razor" ;;
    filament-divideint-gen)                          echo "$REPO_ROOT/baseline/DivideInt.Blazor/App.razor" ;;
    filament-loops-gen)                              echo "$REPO_ROOT/baseline/Loops.Blazor/App.razor" ;;
    filament-compose-gen)                            echo "$REPO_ROOT/baseline/Compose.Blazor/App.razor" ;;
    filament-rootforeach-gen)                        echo "$REPO_ROOT/baseline/RootForeach.Blazor/App.razor" ;;
    filament-rootif-gen)                             echo "$REPO_ROOT/baseline/RootIf.Blazor/App.razor" ;;
    filament-boundcompose-gen)                       echo "$REPO_ROOT/baseline/BoundCompose.Blazor/App.razor" ;;
    filament-reactiveattr-gen)                       echo "$REPO_ROOT/baseline/ReactiveAttr.Blazor/App.razor" ;;
    filament-boolattr-gen)                           echo "$REPO_ROOT/baseline/BoolAttr.Blazor/App.razor" ;;
    filament-mixedattr-gen)                          echo "$REPO_ROOT/baseline/MixedAttr.Blazor/App.razor" ;;
    filament-stringattrs-gen)                        echo "$REPO_ROOT/baseline/StringAttrs.Blazor/App.razor" ;;
    filament-moreattrs-gen)                          echo "$REPO_ROOT/baseline/MoreAttrs.Blazor/App.razor" ;;
    filament-bind-gen)                               echo "$REPO_ROOT/baseline/Bind.Blazor/App.razor" ;;
    filament-lambdahandler-gen)                      echo "$REPO_ROOT/baseline/LambdaHandler.Blazor/App.razor" ;;
    filament-listops-gen)                            echo "$REPO_ROOT/baseline/ListOps.Blazor/App.razor" ;;
    filament-checkbind-gen)                          echo "$REPO_ROOT/baseline/CheckBind.Blazor/App.razor" ;;
    filament-intbind-gen)                            echo "$REPO_ROOT/baseline/IntBind.Blazor/App.razor" ;;
    filament-codeblock-gen)                          echo "$REPO_ROOT/baseline/CodeBlock.Blazor/App.razor" ;;
    filament-ifmulti-gen)                            echo "$REPO_ROOT/baseline/IfMultiBody.Blazor/App.razor" ;;
    filament-ifelsemulti-gen)                        echo "$REPO_ROOT/baseline/IfElseMultiBody.Blazor/App.razor" ;;
    filament-ifnested-gen)                           echo "$REPO_ROOT/baseline/IfNested.Blazor/App.razor" ;;
    filament-trylock-gen)                            echo "$REPO_ROOT/baseline/TryLock.Blazor/App.razor" ;;
    filament-positionalrecord-gen)                   echo "$REPO_ROOT/baseline/PositionalRecord.Blazor/App.razor" ;;
    filament-longcounter-gen)                        echo "$REPO_ROOT/baseline/LongCounter.Blazor/App.razor" ;;
    filament-floatcounter-gen)                       echo "$REPO_ROOT/baseline/FloatCounter.Blazor/App.razor" ;;
    filament-decimalcounter-gen)                     echo "$REPO_ROOT/baseline/DecimalCounter.Blazor/App.razor" ;;
    filament-datetimecounter-gen)                    echo "$REPO_ROOT/baseline/DateTimeCounter.Blazor/App.razor" ;;
    filament-linq-gen)                               echo "$REPO_ROOT/baseline/Linq.Blazor/App.razor" ;;
    *) echo "" ;;
  esac
}

# The file the generator writes, which main.js imports. Per-label rather than
# hardcoded: this was 'Counter.g.js' for every label, which would have had the
# Rows generator emit a file called Counter.g.js that samples/filament-rows-gen/
# main.js does not import -- esbuild would then resolve the import to whatever
# stale Rows.g.js happened to exist, or fail. The rm-then-emit below only proves
# freshness for the file it actually names.
generated_js_for() {
  case "$1" in
    filament-counter-gen|filament-counter-gen-stats) echo "Counter.g.js" ;;
    filament-rows-gen|filament-rows-gen-stats)       echo "Rows.g.js" ;;
    filament-divide-gen)                             echo "Divide.g.js" ;;
    filament-divideint-gen)                          echo "DivideInt.g.js" ;;
    filament-loops-gen)                              echo "Loops.g.js" ;;
    filament-compose-gen)                            echo "App.g.js" ;;
    filament-rootforeach-gen)                        echo "App.g.js" ;;
    filament-rootif-gen)                             echo "App.g.js" ;;
    filament-boundcompose-gen)                       echo "App.g.js" ;;
    filament-reactiveattr-gen)                       echo "App.g.js" ;;
    filament-boolattr-gen)                           echo "App.g.js" ;;
    filament-mixedattr-gen)                          echo "App.g.js" ;;
    filament-stringattrs-gen)                        echo "App.g.js" ;;
    filament-moreattrs-gen)                          echo "App.g.js" ;;
    filament-bind-gen)                               echo "App.g.js" ;;
    filament-lambdahandler-gen)                      echo "App.g.js" ;;
    filament-listops-gen)                            echo "App.g.js" ;;
    filament-checkbind-gen)                          echo "App.g.js" ;;
    filament-intbind-gen)                            echo "App.g.js" ;;
    filament-codeblock-gen)                          echo "App.g.js" ;;
    filament-ifmulti-gen)                            echo "App.g.js" ;;
    filament-ifelsemulti-gen)                        echo "App.g.js" ;;
    filament-ifnested-gen)                           echo "App.g.js" ;;
    filament-trylock-gen)                            echo "App.g.js" ;;
    filament-positionalrecord-gen)                   echo "App.g.js" ;;
    filament-longcounter-gen)                        echo "App.g.js" ;;
    filament-floatcounter-gen)                       echo "App.g.js" ;;
    filament-decimalcounter-gen)                     echo "App.g.js" ;;
    filament-datetimecounter-gen)                    echo "App.g.js" ;;
    filament-linq-gen)                               echo "App.g.js" ;;
    *) echo "" ;;
  esac
}

title_for() {
  case "$1" in
    filament-counter|filament-counter-stats)         echo "Counter" ;;
    filament-rows|filament-rows-stats)               echo "Rows" ;;
    filament-counter-gen|filament-counter-gen-stats) echo "Counter" ;;
    filament-rows-gen|filament-rows-gen-stats)       echo "Rows" ;;
    filament-divide-gen)                             echo "Divide" ;;
    filament-divideint-gen)                          echo "DivideInt" ;;
    filament-loops-gen)                              echo "Loops" ;;
    filament-compose-gen)                            echo "Compose" ;;
    filament-rootforeach-gen)                        echo "RootForeach" ;;
    filament-rootif-gen)                             echo "RootIf" ;;
    filament-boundcompose-gen)                       echo "BoundCompose" ;;
    filament-reactiveattr-gen)                       echo "ReactiveAttr" ;;
    filament-boolattr-gen)                           echo "BoolAttr" ;;
    filament-mixedattr-gen)                          echo "MixedAttr" ;;
    filament-stringattrs-gen)                        echo "StringAttrs" ;;
    filament-moreattrs-gen)                          echo "MoreAttrs" ;;
    filament-bind-gen)                               echo "Bind" ;;
    filament-lambdahandler-gen)                      echo "LambdaHandler" ;;
    filament-listops-gen)                            echo "ListOps" ;;
    filament-checkbind-gen)                          echo "CheckBind" ;;
    filament-intbind-gen)                            echo "IntBind" ;;
    filament-codeblock-gen)                          echo "CodeBlock" ;;
    filament-ifmulti-gen)                            echo "IfMultiBody" ;;
    filament-ifelsemulti-gen)                        echo "IfElseMultiBody" ;;
    filament-ifnested-gen)                           echo "IfNested" ;;
    filament-trylock-gen)                            echo "TryLock" ;;
    filament-positionalrecord-gen)                   echo "PositionalRecord" ;;
    filament-longcounter-gen)                        echo "LongCounter" ;;
    filament-floatcounter-gen)                       echo "FloatCounter" ;;
    filament-decimalcounter-gen)                     echo "DecimalCounter" ;;
    filament-datetimecounter-gen)                    echo "DateTimeCounter" ;;
    filament-linq-gen)                               echo "Linq" ;;
    *) return 1 ;;
  esac
}

# The Blazor app whose published output this label must match byte-for-byte on
# every shared asset. Used to locate the reference stylesheet AND, after the
# build, to prove the copy really is what that app ships.
blazor_label_for() {
  case "$1" in
    filament-counter|filament-counter-stats)         echo "blazor-counter-nojit" ;;
    filament-rows|filament-rows-stats)               echo "blazor-rows-nojit" ;;
    filament-counter-gen|filament-counter-gen-stats) echo "blazor-counter-nojit" ;;
    filament-rows-gen|filament-rows-gen-stats)       echo "blazor-rows-nojit" ;;
    filament-divide-gen)                             echo "blazor-divide" ;;
    filament-divideint-gen)                          echo "blazor-divideint" ;;
    filament-loops-gen)                              echo "blazor-loops" ;;
    filament-compose-gen)                            echo "blazor-compose" ;;
    filament-rootforeach-gen)                        echo "blazor-rootforeach" ;;
    filament-rootif-gen)                             echo "blazor-rootif" ;;
    filament-boundcompose-gen)                       echo "blazor-boundcompose" ;;
    filament-reactiveattr-gen)                       echo "blazor-reactiveattr" ;;
    filament-boolattr-gen)                           echo "blazor-boolattr" ;;
    filament-mixedattr-gen)                          echo "blazor-mixedattr" ;;
    filament-stringattrs-gen)                        echo "blazor-stringattrs" ;;
    filament-moreattrs-gen)                          echo "blazor-moreattrs" ;;
    filament-bind-gen)                               echo "blazor-bind" ;;
    filament-lambdahandler-gen)                      echo "blazor-lambdahandler" ;;
    filament-listops-gen)                            echo "blazor-listops" ;;
    filament-checkbind-gen)                          echo "blazor-checkbind" ;;
    filament-intbind-gen)                            echo "blazor-intbind" ;;
    filament-codeblock-gen)                          echo "blazor-codeblock" ;;
    filament-ifmulti-gen)                            echo "blazor-ifmulti" ;;
    filament-ifelsemulti-gen)                        echo "blazor-ifelsemulti" ;;
    filament-ifnested-gen)                           echo "blazor-ifnested" ;;
    filament-trylock-gen)                            echo "blazor-trylock" ;;
    filament-positionalrecord-gen)                   echo "blazor-positionalrecord" ;;
    filament-longcounter-gen)                        echo "blazor-longcounter" ;;
    filament-floatcounter-gen)                       echo "blazor-floatcounter" ;;
    filament-decimalcounter-gen)                     echo "blazor-decimalcounter" ;;
    filament-datetimecounter-gen)                    echo "blazor-datetimecounter" ;;
    filament-linq-gen)                               echo "blazor-linq" ;;
    *) return 1 ;;
  esac
}

# THE STYLESHEET IS PER-APP, NOT GLOBAL.
#
# This was hardcoded to Counter's app.css for all four labels, which shipped
# Counter's 795 B stylesheet to the Rows app. The two are NOT the same file:
# Rows' 917 B sheet carries the table styling the rows benchmark is rendered
# under -- border-collapse:collapse (materially more expensive to lay out in
# Blink than the default `separate`), td padding, .col-md-1's fixed width,
# a.lbl's colour, #main's padding. Blazor's rows app laid out 1000 rows under
# all of that; Filament's laid them out under none of it, and was 122 B lighter
# for it. That is a layout asymmetry on create-warm/update/swap plus a weight
# understatement, both pointing Filament's way, on the app C4 turns on.
css_for() {
  case "$1" in
    filament-counter|filament-counter-stats)         echo "$REPO_ROOT/baseline/Counter.Blazor/wwwroot/css/app.css" ;;
    filament-rows|filament-rows-stats)               echo "$REPO_ROOT/baseline/Rows.Blazor/wwwroot/css/app.css" ;;
    filament-counter-gen|filament-counter-gen-stats) echo "$REPO_ROOT/baseline/Counter.Blazor/wwwroot/css/app.css" ;;
    filament-rows-gen|filament-rows-gen-stats)       echo "$REPO_ROOT/baseline/Rows.Blazor/wwwroot/css/app.css" ;;
    filament-divide-gen)                             echo "$REPO_ROOT/baseline/Divide.Blazor/wwwroot/css/app.css" ;;
    filament-divideint-gen)                          echo "$REPO_ROOT/baseline/DivideInt.Blazor/wwwroot/css/app.css" ;;
    filament-loops-gen)                              echo "$REPO_ROOT/baseline/Loops.Blazor/wwwroot/css/app.css" ;;
    filament-compose-gen)                            echo "$REPO_ROOT/baseline/Compose.Blazor/wwwroot/css/app.css" ;;
    filament-rootforeach-gen)                        echo "$REPO_ROOT/baseline/RootForeach.Blazor/wwwroot/css/app.css" ;;
    filament-rootif-gen)                             echo "$REPO_ROOT/baseline/RootIf.Blazor/wwwroot/css/app.css" ;;
    filament-boundcompose-gen)                       echo "$REPO_ROOT/baseline/BoundCompose.Blazor/wwwroot/css/app.css" ;;
    filament-reactiveattr-gen)                       echo "$REPO_ROOT/baseline/ReactiveAttr.Blazor/wwwroot/css/app.css" ;;
    filament-boolattr-gen)                           echo "$REPO_ROOT/baseline/BoolAttr.Blazor/wwwroot/css/app.css" ;;
    filament-mixedattr-gen)                          echo "$REPO_ROOT/baseline/MixedAttr.Blazor/wwwroot/css/app.css" ;;
    filament-stringattrs-gen)                        echo "$REPO_ROOT/baseline/StringAttrs.Blazor/wwwroot/css/app.css" ;;
    filament-moreattrs-gen)                          echo "$REPO_ROOT/baseline/MoreAttrs.Blazor/wwwroot/css/app.css" ;;
    filament-bind-gen)                               echo "$REPO_ROOT/baseline/Bind.Blazor/wwwroot/css/app.css" ;;
    filament-lambdahandler-gen)                      echo "$REPO_ROOT/baseline/LambdaHandler.Blazor/wwwroot/css/app.css" ;;
    filament-listops-gen)                            echo "$REPO_ROOT/baseline/ListOps.Blazor/wwwroot/css/app.css" ;;
    filament-checkbind-gen)                          echo "$REPO_ROOT/baseline/CheckBind.Blazor/wwwroot/css/app.css" ;;
    filament-intbind-gen)                            echo "$REPO_ROOT/baseline/IntBind.Blazor/wwwroot/css/app.css" ;;
    filament-codeblock-gen)                          echo "$REPO_ROOT/baseline/CodeBlock.Blazor/wwwroot/css/app.css" ;;
    filament-ifmulti-gen)                            echo "$REPO_ROOT/baseline/IfMultiBody.Blazor/wwwroot/css/app.css" ;;
    filament-ifelsemulti-gen)                        echo "$REPO_ROOT/baseline/IfElseMultiBody.Blazor/wwwroot/css/app.css" ;;
    filament-ifnested-gen)                           echo "$REPO_ROOT/baseline/IfNested.Blazor/wwwroot/css/app.css" ;;
    filament-trylock-gen)                            echo "$REPO_ROOT/baseline/TryLock.Blazor/wwwroot/css/app.css" ;;
    filament-positionalrecord-gen)                   echo "$REPO_ROOT/baseline/PositionalRecord.Blazor/wwwroot/css/app.css" ;;
    filament-longcounter-gen)                        echo "$REPO_ROOT/baseline/LongCounter.Blazor/wwwroot/css/app.css" ;;
    filament-floatcounter-gen)                       echo "$REPO_ROOT/baseline/FloatCounter.Blazor/wwwroot/css/app.css" ;;
    filament-decimalcounter-gen)                     echo "$REPO_ROOT/baseline/DecimalCounter.Blazor/wwwroot/css/app.css" ;;
    filament-datetimecounter-gen)                    echo "$REPO_ROOT/baseline/DateTimeCounter.Blazor/wwwroot/css/app.css" ;;
    filament-linq-gen)                               echo "$REPO_ROOT/baseline/Linq.Blazor/wwwroot/css/app.css" ;;
    *) return 1 ;;
  esac
}

die() { printf '\nFAIL: %s\n' "$1" >&2; exit 1; }
log() { printf '\n==> %s\n' "$1"; }

# ---- per-project mutex ------------------------------------------------------
# Lifted verbatim from publish-baseline.sh, for the same reason: two labels share
# one source tree (filament-counter and filament-counter-stats are both
# samples/filament-counter), and the purge below deletes paths the other label
# also builds through. `mkdir` is the portable atomic test-and-set; macOS has no
# flock(1). The lock lives in TMPDIR keyed by a hash of the project's absolute
# path so it survives the purge and two checkouts never share one.
#
# Cheaper here than it is there -- an esbuild bundle is milliseconds, not the
# minutes an AOT publish takes -- but the race it closes is identical, and a lock
# that is never contended costs one mkdir.

LOCK_WAIT_SECONDS=300    # esbuild is fast; a long wait here means something hung.
HELD_LOCK=""

release_project_lock() {
  # Plain `if`, not `[[ ... ]] && ...`: under `set -e` a false one-liner AND-list
  # is itself a failing statement, and this runs from the EXIT trap.
  if [[ -n "$HELD_LOCK" ]]; then
    rm -rf "$HELD_LOCK"
  fi
  HELD_LOCK=""
}
trap release_project_lock EXIT INT TERM

acquire_project_lock() {
  local project_dir="$1"
  local key lock_dir waited=0 owner
  key="$(printf '%s' "$project_dir" | shasum | cut -c1-16)"
  lock_dir="${TMPDIR:-/tmp}/filament-build-$key.lock"

  while ! mkdir "$lock_dir" 2>/dev/null; do
    owner="$(cat "$lock_dir/pid" 2>/dev/null || true)"

    # Reap a lock whose holder died (crash / SIGKILL), rather than hang forever.
    if [[ -n "$owner" ]] && ! kill -0 "$owner" 2>/dev/null; then
      printf '    stale lock from dead pid %s; reclaiming\n' "$owner"
      rm -rf "$lock_dir"
      continue
    fi

    if (( waited == 0 )); then
      printf '    waiting for lock on %s (held by pid %s)\n' \
        "$(basename "$project_dir")" "${owner:-?}"
    fi
    if (( waited >= LOCK_WAIT_SECONDS )); then
      die "timed out after ${LOCK_WAIT_SECONDS}s waiting for the lock on
     $project_dir (held by pid ${owner:-unknown}).
     Another build-filament.sh is building this project. If nothing is
     running, remove the stale lock: rm -rf '$lock_dir'"
    fi
    sleep 1
    waited=$((waited + 1))
  done

  printf '%s' "$$" > "$lock_dir/pid"
  HELD_LOCK="$lock_dir"
  if (( waited > 0 )); then
    printf '    acquired lock after %ss\n' "$waited"
  fi
}

file_size() {
  # macOS stat and GNU stat disagree on flags; try BSD first.
  stat -f%z "$1" 2>/dev/null || stat -c%s "$1"
}

# ---- the shell template -----------------------------------------------------
# Blazor's published shell, minus the Blazor-runtime-specific elements. Kept as a
# function rather than a file so the two app shells cannot drift apart: there is
# exactly one of these, and $1/$2 are the only permitted variation.
#
# What is KEPT, and why, element by element:
#   <!DOCTYPE>, <html lang>, meta charset, meta viewport, <base href="/">
#       Not framework-specific. Byte-identical to Blazor's.
#   <link rel="stylesheet" href="css/app.css">
#       Blazor's apps ship it and pay for it. Filament ships the SAME file,
#       copied byte-for-byte from the baseline source, so neither side is
#       styling-subsidised. PER-APP, because the two Blazor apps do NOT ship the
#       same stylesheet: Counter's is 795 B raw / 473 B gzip, Rows' is 917 B raw
#       / 484 B gzip (it carries the table rules the rows benchmark lays out
#       under). css_for() picks by label and a post-build cmp against the Blazor
#       label's PUBLISHED sheet proves the copy landed -- see css_for().
#   <link rel="icon" href="data:,"> and its 3-line comment
#       The comment is pure prose and Filament could delete it for free. Kept:
#       Blazor pays those bytes, so Filament pays them too. It costs Filament
#       ~55 B gzip out of a 10,000 B budget, in the direction that makes C1
#       harder. See --shell-parity for the exact figure.
#   <div id="app">Loading...</div>
#       The mount point. Both frameworks need one; Blazor's says "Loading..."
#       and so does Filament's, including when Filament has nothing to load.
#
# What is REMOVED, and why each removal is forced rather than chosen:
#   <link rel="preload" href="_framework/dotnet.<hash>.js" ...>
#       Preloads the .NET runtime. There is no .NET runtime (C5). Keeping the tag
#       would emit a real HTTP request for a file that does not exist: a 404 on
#       every page load, counted in the network trace, charged to Filament.
#   <script type="importmap">
#       Maps four _framework/dotnet*.js module specifiers. All four are the .NET
#       runtime. An importmap resolving to nothing is not parity, it is litter.
#   <div id="blazor-error-ui">
#       Blazor's runtime error surface, driven by blazor.webassembly.js. Inert
#       markup without it.
#   <script src="_framework/blazor.webassembly.<hash>.js">
#       Replaced by the Filament bundle. This is the "script src" the brief
#       names as a mandatory app-specific difference.
#
# Every one of those removals deletes a Blazor RUNTIME dependency. None of them
# deletes a byte Filament would otherwise have had to carry, and none of them is
# an aesthetic choice. That distinction is the whole claim of shell parity.
filament_shell() {
  local title="$1" script_src="$2"
  cat <<EOF
<!DOCTYPE html>
<html lang="en">

<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>$title</title>
    <base href="/" />
    <link rel="stylesheet" href="css/app.css" />
    <!-- Empty data: URI suppresses the browser's automatic /favicon.ico request,
         which would otherwise 404 and add noise to the benchmark network trace.
         The template's favicon.png/icon-192.png were decorative sample assets. -->
    <link rel="icon" href="data:," />
</head>

<body>
    <div id="app">Loading...</div>
    <script src="$script_src"></script>
</body>

</html>
EOF
}

# ---- compression ------------------------------------------------------------
# node's zlib rather than gzip(1)/brotli(1): macOS ships neither brotli nor a
# gzip whose output matches .NET's, and node is already a hard dependency of the
# harness. Using the same engine the server uses is the point -- see the
# compression-parity note in the header.
emit_siblings() {
  local file="$1"
  node -e '
    const zlib = require("zlib"), fs = require("fs");
    const f = process.argv[1];
    const gzLevel = Number(process.argv[2]), brQuality = Number(process.argv[3]);
    const raw = fs.readFileSync(f);
    fs.writeFileSync(f + ".gz", zlib.gzipSync(raw, { level: gzLevel }));
    fs.writeFileSync(f + ".br", zlib.brotliCompressSync(raw, {
      params: {
        [zlib.constants.BROTLI_PARAM_QUALITY]: brQuality,
        // dotnet publish hints the size; without it brotli picks a smaller
        // window for small inputs and lands a few bytes wide of the .NET output.
        [zlib.constants.BROTLI_PARAM_SIZE_HINT]: raw.length,
      },
    }));
  ' "$file" "$GZIP_LEVEL" "$BROTLI_QUALITY"

  # A sibling LARGER than the raw file would be served in preference to it and
  # would make Filament heavier than shipping nothing. Cheap to check, and the
  # failure is silent otherwise.
  local raw_size gz_size br_size
  raw_size="$(file_size "$file")"
  gz_size="$(file_size "$file.gz")"
  br_size="$(file_size "$file.br")"
  if (( gz_size >= raw_size )); then
    die "$(basename "$file").gz ($gz_size B) is not smaller than the raw file ($raw_size B)"
  fi
  if (( br_size >= raw_size )); then
    die "$(basename "$file").br ($br_size B) is not smaller than the raw file ($raw_size B)"
  fi
}

# ---- CLI --------------------------------------------------------------------

if [[ "${1:-}" == "--list" ]]; then
  printf '%s\n' "${ALL_LABELS[@]}"
  exit 0
fi

if [[ "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then
  # Print the whole header block, exactly as publish-baseline.sh does: from the
  # title to the last comment line before the code. Never a hardcoded line range;
  # that truncates silently as soon as the header grows.
  awk 'NR < 3 { next } !/^#/ { exit } { sub(/^#{1,2} ?/, ""); print }' \
    "${BASH_SOURCE[0]}"
  exit 0
fi

# --shell-parity: print the Blazor-vs-Filament shell delta, with byte costs, and
# exit. This is a REPORT, not a build step, so it must be runnable without
# building anything -- a reviewer checking the parity claim should not need
# esbuild, node_modules, or an app that compiles.
if [[ "${1:-}" == "--shell-parity" ]]; then
  [[ -f "$BLAZOR_REFERENCE_SHELL" ]] || die "no Blazor reference shell at
     $BLAZOR_REFERENCE_SHELL
     Run ./bench/publish-baseline.sh blazor-counter-nojit first."

  tmp="$(mktemp -d)"
  trap 'rm -rf "$tmp"; release_project_lock' EXIT INT TERM

  filament_shell "Counter" "app.js" > "$tmp/filament.html"
  cp "$BLAZOR_REFERENCE_SHELL" "$tmp/blazor.html"
  for f in "$tmp/filament.html" "$tmp/blazor.html"; do
    node -e '
      const zlib = require("zlib"), fs = require("fs");
      const raw = fs.readFileSync(process.argv[1]);
      fs.writeFileSync(process.argv[1] + ".gz", zlib.gzipSync(raw, { level: 9 }));
    ' "$f"
  done

  b_raw="$(file_size "$tmp/blazor.html")";   b_gz="$(file_size "$tmp/blazor.html.gz")"
  f_raw="$(file_size "$tmp/filament.html")"; f_gz="$(file_size "$tmp/filament.html.gz")"

  printf '\n=== index.html shell parity: Blazor (reference) vs Filament ===\n\n'
  printf '  %-28s %8s %8s\n' '' 'raw B' 'gzip B'
  printf '  %-28s %8s %8s\n' 'blazor-counter-nojit' "$b_raw" "$b_gz"
  printf '  %-28s %8s %8s\n' 'filament-counter' "$f_raw" "$f_gz"
  printf '  %-28s %8s %8s\n' 'delta (Filament - Blazor)' "$((f_raw - b_raw))" "$((f_gz - b_gz))"

  printf '\n--- unified diff (reference -> Filament) ---\n'
  diff -u "$tmp/blazor.html" "$tmp/filament.html" || true

  printf '\n--- what differs, and why ---\n'
  printf '  MANDATORY, app-specific (the brief permits exactly these two):\n'
  printf '    <title>            Counter | Rows        -- app identity\n'
  printf '    <script src>       app.js vs _framework/blazor.webassembly.<hash>.js\n'
  printf '\n  FORCED by C5 (no .NET runtime in the browser) -- each would 404 or be inert:\n'
  printf '    - <link rel="preload" href="_framework/dotnet.<hash>.js">   preloads the .NET runtime\n'
  printf '    - <script type="importmap">                                  maps 4 dotnet*.js specifiers\n'
  printf '    - <div id="blazor-error-ui">                                 driven by blazor.webassembly.js\n'
  printf '\n  KEPT even though dropping them is free, because Blazor pays them:\n'
  printf '    + the 3-line favicon comment      pure prose; kept for parity\n'
  printf '    + <link rel="icon" href="data:,"> suppresses the /favicon.ico 404\n'
  printf '    + <link rel="stylesheet" href="css/app.css">  and the file itself, copied byte-for-byte\n'
  printf '    + <base href="/">, meta charset, meta viewport, <div id="app">Loading...</div>\n'
  printf '\n  Filament is %s B raw / %s B gzip lighter, and 100%% of that is\n' \
    "$((b_raw - f_raw))" "$((b_gz - f_gz))"
  printf '  Blazor runtime plumbing. No shared element was trimmed.\n\n'
  exit 0
fi

# ---- preflight: fail loudly and early, never half-build ---------------------

command -v node >/dev/null 2>&1 || die "node not on PATH. The harness needs it too; see README.md."
command -v npx  >/dev/null 2>&1 || die "npx not on PATH (ships with node)."

# Only the -gen labels need the SDK, but checking here means the failure lands
# before anything is deleted rather than half way through a rebuild.
if [[ $# -eq 0 || "$*" == *-gen* ]]; then
  command -v dotnet >/dev/null 2>&1 || die "dotnet not on PATH, and the -gen labels
     compile samples/Counter/Counter.razor with src/Filament.Generator. Build only the
     hand-written labels if you have no SDK:
       ./bench/build-filament.sh filament-counter filament-rows"
fi

# Pin the toolchain the way publish-baseline.sh pins the SDK. esbuild's minifier
# is part of the result: a different version can emit different bytes, and C1 is
# a byte gate decided in the last few hundred of them.
EXPECTED_ESBUILD="0.28.1"
ESBUILD_VERSION="$(npx --no-install esbuild --version 2>/dev/null || echo 'MISSING')"
if [[ "$ESBUILD_VERSION" == "MISSING" ]]; then
  die "esbuild is not installed. From bench/harness: npm ci"
fi
if [[ "$ESBUILD_VERSION" != "$EXPECTED_ESBUILD" ]]; then
  printf '\nWARNING: esbuild is %s; Phase 1 was built with %s.\n' \
    "$ESBUILD_VERSION" "$EXPECTED_ESBUILD" >&2
  printf 'The minifier is part of the byte count, and C1 is a byte gate. Any weight\n' >&2
  printf 'produced here belongs in a NEW BENCH.md entry, not compared against an old one.\n' >&2
fi

# Resolve requested labels.
if [[ $# -gt 0 ]]; then
  REQUESTED=("$@")
  for label in "${REQUESTED[@]}"; do
    project_for "$label" >/dev/null 2>&1 \
      || die "unknown config label '$label'. Known labels:
     $(printf '%s ' "${ALL_LABELS[@]}")"
  done
else
  REQUESTED=("${ALL_LABELS[@]}")
fi

log "Building ${#REQUESTED[@]} config(s) with esbuild $ESBUILD_VERSION"

for label in "${REQUESTED[@]}"; do
  project_rel="$(project_for "$label")"
  project_dir="$REPO_ROOT/$project_rel"
  mode="$(mode_for "$label")"
  title="$(title_for "$label")"
  out_dir="$PUBLISH_ROOT/$label"
  entry="$project_dir/main.js"

  # The apps are another agent's deliverable. Say precisely what is missing and
  # what shape it must have, rather than letting esbuild fail with a bare ENOENT.
  [[ -d "$project_dir" ]] || die "app source not found: $project_dir
     build-filament.sh expects each Filament demo app at:
       samples/filament-counter/main.js
       samples/filament-rows/main.js
     Each main.js is an ES module entry point that imports from
     src/filament-runtime/ and renders the shared DOM contract into <div id=\"app\">.
     The shell (index.html) is GENERATED by this script -- do not commit one."
  [[ -f "$entry" ]] || die "no entry point at $entry
     Each Filament app's entry MUST be main.js in its sample directory."

  acquire_project_lock "$project_dir"

  log "$label  <-  $project_rel  ($mode)"

  # Idempotency. Same reasoning as publish-baseline.sh: a second run must
  # reproduce the first byte-for-byte, so nothing may survive from the last one.
  # ${var:?} so an empty out_dir can never turn this into `rm -rf /`.
  rm -rf "${out_dir:?}"
  mkdir -p "$out_dir/css"

  # ---- generate: Razor -> JS, for the -gen labels only ---------------------
  # Unconditional delete-then-emit. See the header: a generated file that can
  # survive a build is a file that can be hand-edited, and then the label's name
  # asserts "the generator produced this" while the bytes say otherwise -- with
  # nothing to catch it. The rm is what makes the assertion below meaningful:
  # after it, the file exists IF AND ONLY IF this run's generator wrote it.
  razor_src="$(razor_for "$label")"
  if [[ -n "$razor_src" ]]; then
    [[ -f "$razor_src" ]] || die "'$label' is a generator label but its Razor source
     is missing: $razor_src"

    generated_basename="$(generated_js_for "$label")"
    [[ -n "$generated_basename" ]] || die "'$label' has a Razor source but no entry in
     generated_js_for(). Every generator label must name the file it emits, or the
     rm-then-emit below proves freshness for a file nothing imports."
    generated_js="$project_dir/$generated_basename"
    rm -f "$generated_js"

    # The emitted file must be the one main.js imports, or esbuild silently bundles
    # a stale/absent module and the label's name asserts something the bytes do not.
    grep -qF "./$generated_basename" "$entry" || die "'$label' emits $generated_basename
     but $entry does not import './$generated_basename'. The label would be weighed on
     JS this run's generator did not produce."

    printf '    dotnet run --project src/Filament.Generator -- %s %s\n' \
      "${razor_src#"$REPO_ROOT"/}" "${generated_js#"$REPO_ROOT"/}"
    # Release, like every other artifact this project measures. --no-build would
    # measure whatever was last compiled; the build is seconds and this runs once
    # per label, so it is built here rather than assumed.
    dotnet run --project "$REPO_ROOT/src/Filament.Generator" -c Release \
      -- "$razor_src" "$generated_js" \
      || die "the generator refused to emit for '$label'.
     Section 10: a construct outside the subset MUST produce a diagnostic rather
     than silently wrong JS, so this exit code is the generator working, not
     failing. Read the diagnostic above; do not work around it."

    # Verify from the ARTIFACT, never from the exit code -- the same stance the
    # stats-marker and AOT checks take. An exit-0 generator that wrote nothing
    # would otherwise leave the previous label's bundle in place and be weighed.
    [[ -f "$generated_js" ]] || die "the generator exited 0 for '$label' but wrote no
     file at $generated_js. C1 would have been measured on a bundle the generator
     did not produce."
    grep -q 'GENERATED by Filament.Generator' "$generated_js" || die "'$label' has a
     $generated_js that does not carry the generator's banner. Something other than
     Filament.Generator wrote it, and this label's whole purpose is to measure what
     Filament.Generator emits."
    printf '        emitted %s B of JS from %s\n' \
      "$(file_size "$generated_js")" "$(basename "$razor_src")"
  fi

  # ---- bundle -------------------------------------------------------------
  esbuild_args=(
    "$entry"
    --bundle
    --format=iife
    --target=es2022
    --outfile="$out_dir/app.js"
    --log-level=warning
  )
  if [[ "$mode" == "production" ]]; then
    # --define + --minify is what performs the DCE. --minify alone would keep the
    # branch; --define alone would leave `if (false) {...}` in readable form.
    esbuild_args+=(--define:__FILAMENT_STATS__=false --minify --drop:console --legal-comments=none)
  else
    # Instrumented: stats ON. NOT minified -- when the C3 cross-check disagrees
    # with the MutationObserver, the next question is always "which line wrote
    # that?", and a minified stats build cannot answer it. This bundle is never
    # weighed, so its size costs nothing.
    esbuild_args+=(--define:__FILAMENT_STATS__=true --sourcemap=inline)
  fi

  printf '    npx esbuild %s\n' "${esbuild_args[*]}"
  npx --no-install esbuild "${esbuild_args[@]}" \
    || die "esbuild failed for '$label'."

  # ---- shell + shared assets ----------------------------------------------
  filament_shell "$title" "app.js" > "$out_dir/index.html"

  # The SAME app.css Blazor ships, copied from the baseline source rather than
  # re-authored, so "the two apps are styled identically" is a fact about the
  # bytes and not a claim about someone's diligence. Per-app: see css_for().
  blazor_css="$(css_for "$label")"
  [[ -f "$blazor_css" ]] || die "no baseline app.css at $blazor_css
     The Filament shell links css/app.css for parity with Blazor's; without the
     file the link 404s and Filament is charged for a request Blazor never makes."
  cp "$blazor_css" "$out_dir/css/app.css"

  # ---- post-build assertions: prove the output is what the label claims ----
  # publish-baseline.sh asserts AOT from dotnet.native.wasm rather than from the
  # flag it passed. Everything below is the same move.

  [[ -f "$out_dir/index.html" ]] || die "'$label' produced no index.html"
  [[ -f "$out_dir/app.js" ]]     || die "'$label' produced no app.js"

  # (0) STYLING PARITY, asserted from the ARTIFACT rather than from the cp above.
  #     Same move as the AOT and stats-marker checks: the question is not "which
  #     file did we mean to copy" but "do the two apps the harness actually loads
  #     render under the same stylesheet". A mismatch voids both the msToPaint
  #     comparison (different layout work for the same 1000 rows) and the weight
  #     comparison (different transfer size), so it is fatal, not a warning.
  blazor_ref="$PUBLISH_ROOT/$(blazor_label_for "$label")/wwwroot/css/app.css"
  if [[ -f "$blazor_ref" ]]; then
    if ! cmp -s "$out_dir/css/app.css" "$blazor_ref"; then
      die "'$label' ships a css/app.css that differs from the stylesheet its
     Blazor counterpart publishes ($blazor_ref).
     The two apps must be styled IDENTICALLY or the layout comparison (create-warm,
     update, swap all lay out under these rules) and the weight comparison are void.
     Usual cause: the source stylesheet in css_for() is not the one that Blazor app
     actually ships. Fix css_for(), do not fix this assertion."
    fi
  else
    # Not fatal: the Filament labels can legitimately be built before the Blazor
    # baseline is published. But say so, because the strongest form of this check
    # did not run and silence would read as a pass.
    printf '    NOTE: %s not published; styling parity vs the Blazor artifact was\n' "$(blazor_label_for "$label")"
    printf '          NOT verified. Run publish-baseline.sh, then rebuild, before\n'
    printf '          trusting any weight or msToPaint figure from this label.\n'
  fi

  # (1) The stats define did what it was asked to. See the header.
  if grep -qF "$STATS_MARKER" "$out_dir/app.js"; then
    marker_present="yes"
  else
    marker_present="no"
  fi
  if [[ "$mode" == "production" && "$marker_present" == "yes" ]]; then
    die "'$label' is a PRODUCTION build but its bundle still contains the
     stats marker '$STATS_MARKER'. Dead-code elimination did NOT remove the
     instrumentation, so this bundle's weight is not the C1 bundle's weight.
     Usual cause: stats state is reachable from a live path (an object literal
     built unconditionally, read only under the flag), so esbuild cannot prove
     the branch is dead. All stats-only code must sit INSIDE
     'if (__FILAMENT_STATS__) { ... }'. Do not measure C1 on this output."
  fi
  if [[ "$mode" == "instrumented" && "$marker_present" == "no" ]]; then
    die "'$label' is an INSTRUMENTED build but its bundle does NOT contain the
     stats marker '$STATS_MARKER', so there is no instrumentation in it and a
     C3 run against it would measure a no-op and report it as a pass.
     src/filament-runtime/ must emit the literal string '$STATS_MARKER' inside
     its 'if (__FILAMENT_STATS__)' gate. See this script's header."
  fi

  # (2) The define was substituted at all. A surviving identifier means the flag
  # never reached the bundler, and BOTH modes above would then be lying.
  if grep -qF '__FILAMENT_STATS__' "$out_dir/app.js"; then
    die "'$label' still contains the identifier __FILAMENT_STATS__; the esbuild
     --define did not substitute it. The stats state of this bundle is whatever
     the runtime decides at load time, which is exactly what the two-label split
     exists to prevent."
  fi

  # (3) C5: no .NET runtime in the browser. Cheap to assert, and it is a
  #     criterion -- so assert it from the artifact rather than from the fact
  #     that nobody meant to ship one.
  if [[ -d "$out_dir/_framework" ]]; then
    die "'$label' contains a _framework/ directory -- that is a .NET publish
     artifact and C5 forbids a .NET runtime in the browser."
  fi
  stray_wasm="$(find "$out_dir" -name '*.wasm' -o -name 'dotnet*.js' | head -n1)"
  if [[ -n "$stray_wasm" ]]; then
    die "'$label' ships $stray_wasm, which looks like a .NET runtime artifact (C5)."
  fi

  # ---- compressed siblings, at dotnet-publish parity ----------------------
  for f in "$out_dir/index.html" "$out_dir/app.js" "$out_dir/css/app.css"; do
    emit_siblings "$f"
  done

  # ---- weigh it: C1 is the gate that matters -------------------------------
  # The sum of the gzip siblings IS the transfer for a cold load: the shell, the
  # bundle and the stylesheet are the only three requests this app makes, and the
  # server serves each from its .gz sibling. This is a build-time preview, not
  # the measurement -- bench.mjs weighs what crossed the wire, and that number is
  # the one that counts. They should agree; if they ever do not, the server's
  # negotiation is the thing to distrust, not this sum.
  total_gz=0
  total_br=0
  total_raw=0
  for f in "$out_dir/index.html" "$out_dir/app.js" "$out_dir/css/app.css"; do
    total_raw=$(( total_raw + $(file_size "$f") ))
    total_gz=$((  total_gz  + $(file_size "$f.gz") ))
    total_br=$((  total_br  + $(file_size "$f.br") ))
  done

  printf '    OK  %s\n' "$label"
  printf '        app.js      %8s B raw  %8s B gzip  %8s B br\n' \
    "$(file_size "$out_dir/app.js")" "$(file_size "$out_dir/app.js.gz")" "$(file_size "$out_dir/app.js.br")"
  printf '        index.html  %8s B raw  %8s B gzip  %8s B br\n' \
    "$(file_size "$out_dir/index.html")" "$(file_size "$out_dir/index.html.gz")" "$(file_size "$out_dir/index.html.br")"
  printf '        css/app.css %8s B raw  %8s B gzip  %8s B br\n' \
    "$(file_size "$out_dir/css/app.css")" "$(file_size "$out_dir/css/app.css.gz")" "$(file_size "$out_dir/css/app.css.br")"
  printf '        TOTAL       %8s B raw  %8s B gzip  %8s B br\n' "$total_raw" "$total_gz" "$total_br"

  if [[ "$mode" == "production" ]]; then
    # Report against BOTH readings of "10 ko". The spec is ambiguous and this
    # script does not get to resolve it by picking the flattering one.
    printf '        C1 (build-time preview, gzip): %s B\n' "$total_gz"
    if (( total_gz < C1_DECIMAL )); then
      printf '           vs 10,000 B (10 ko decimal): PASS  (%s B of headroom)\n' "$((C1_DECIMAL - total_gz))"
    else
      printf '           vs 10,000 B (10 ko decimal): FAIL  (%s B over)\n' "$((total_gz - C1_DECIMAL))"
    fi
    if (( total_gz < C1_BINARY )); then
      printf '           vs 10,240 B (10 KiB binary): PASS  (%s B of headroom)\n' "$((C1_BINARY - total_gz))"
    else
      printf '           vs 10,240 B (10 KiB binary): FAIL  (%s B over)\n' "$((total_gz - C1_BINARY))"
    fi
    printf '        NOT the measurement. bench.mjs weighs the wire; this is a preview.\n'
  else
    printf '        (instrumented build -- NEVER weigh this one; it is the C3 bundle)\n'
  fi

  release_project_lock
done

log "Done. ${#REQUESTED[@]} config(s) built under $PUBLISH_ROOT/"
printf '\nThe static root is %s/<label>  -- with NO wwwroot/.\n' "$PUBLISH_ROOT"
printf 'That differs from the Blazor labels, which DO have one. For example:\n\n'
printf '  node bench/harness/bench.mjs --dir bench/publish/filament-counter \\\n'
printf '    --app counter --label filament-counter --runs 10 --weight-runs 3 \\\n'
printf '    --max-encoding gzip --headless --no-aot --out bench/results/filament-counter.json\n\n'
printf 'C1 is measured on the production labels (gzip basis).\n'
printf 'C3 is measured on the -stats labels, via bench.mjs --c3.\n'
printf 'Run ./bench/build-filament.sh --shell-parity for the shell delta and its byte cost.\n'
