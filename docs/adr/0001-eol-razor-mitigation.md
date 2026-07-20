# ADR 0001 — Mitigating the EOL-Razor dependency

**Status:** Accepted · **Date:** 2026-07-20 · **Scope:** Bucket C (structural risk)

> This ADR is the mitigation plan for the risk recorded as **DECISIONS #52**: the generator's Razor
> front end depends on a **dead NuGet package**. It inventories the exact dead-API surface, contains
> the blast radius, and names the trigger that forces migration. It does not remove the dependency —
> that is a RADICAL-commitment decision, sketched under *Options* below.

## Context

`src/Filament.Generator` parses `.razor` with two packages pinned to a single version:

```xml
<PackageReference Include="Microsoft.AspNetCore.Razor.Language" Version="6.0.36" />
<PackageReference Include="Microsoft.CodeAnalysis.Razor"        Version="6.0.36" />
```

**6.0.36 is the last published version, frozen in 2021, out of support.** This is not a preference —
it is the only reusable route (DECISIONS #52, verified by *compiling*, not by reading docs):

| Route | Result |
|-------|--------|
| `Razor.Language` **6.0.36** | Last published (no 7/8/9/10). Restores from a `net10.0` TFM. `GetDocumentIntermediateNode()` is **public**. |
| `Microsoft.CodeAnalysis.Razor.Compiler` 10.0-preview | **Does not restore** — its dependency `Razor.Utilities.Shared` was never published (`NU1101`). |
| SDK 10.0.301 DLL, direct reference | Compiles, but the API **closed**: `GetDocumentIntermediateNode()` → renamed `GetDocumentNode()`, now an **internal member on a public type** (`CS1061`). |
| Razor *syntax* tree (`RazorSyntaxTree.Root`) | **Dead in every version** — `Syntax.SyntaxNode` is `internal` (`CS0122`). |

The risk is **asymmetric**: it bears on the **RADICAL** variant (a standalone compiler built on a dead
package) far more than on **PRUDENT** (signals as a Blazor render mode, which stays inside the living
toolchain). It is part of RADICAL's price.

## The exact dead-API surface (the migration map)

The whole dependency is isolated in **one file** — `RazorFrontEnd.cs` (194 lines). Anything that ever
replaces Razor must reproduce exactly this surface and nothing more:

| API used | Where | Fragility |
|----------|-------|-----------|
| `RazorProjectFileSystem.Create(dir)` | `CreateEngine` | public, stable within 6.0.x |
| `RazorProjectEngine.Create(config, fs, builder ⇒ …)` | `CreateEngine` | public |
| `RazorConfiguration.Default` | `CreateEngine` | public |
| `CompilerFeatures.Register(builder)` | `CreateEngine` | public — registers component passes + tag-helper providers |
| `DefaultMetadataReferenceFeature`, `CompilationTagHelperFeature`, `ITagHelperFeature` | `CreateEngine` | public; **required** (DECISIONS #53 — without it `@onclick` silently degrades to a literal attribute) |
| Remove `ComponentMarkupBlockPass` + `ComponentMarkupEncodingPass` by `GetType().Name` | `CreateEngine` | **internal types, matched by string** — DECISIONS #52 debt #1 |
| `GetDocumentIntermediateNode()` | `Parse` | **public in 6.0.36, internal (`GetDocumentNode`) afterward** — the single load-bearing method |
| IR node types (`MarkupElementIntermediateNode`, `HtmlAttributeIntermediateNode`, `HtmlContentIntermediateNode`, `CSharpCodeIntermediateNode`, …) | `TemplateCompiler`, `TemplatePlan`, `IrDumper` | public IR shape — the contract the rest of the generator consumes |

**Two named debts (DECISIONS #52):**
1. The two markup passes are removed by `GetType().Name` string match. A silent no-op if a rename ever
   makes them vanish would collapse static subtrees into opaque markup strings — re-parsing which
   *is the entire project*. **Hardened by this ADR** (see *Decision*).
2. `GetDocumentIntermediateNode()` is the one method whose relocation to `internal` closes every newer
   version. It is the migration's single hardest point.

## Current containment (already true)

- **One seam.** All Razor-package use is behind `RazorFrontEnd`. The rest of the generator consumes the
  public IR, not the package.
- **Outcome guard.** `FrontEndInvariantTests.MarkupPasses_AreRemoved_SoTheIrHasRealStructure` asserts no
  `MarkupBlockIntermediateNode` survives — so debt #1 breaking is caught by an *observable outcome*, not
  by trusting the string.
- **Exact-version pin.** `6.0.36` is pinned exactly; there is no floating range to drift.

## Options considered

| Option | What it is | Verdict |
|--------|-----------|---------|
| **A — Pin + isolate + inventory** (this ADR) | Keep 6.0.36, contain it to one seam, harden the string-match, document the migration map. | **Chosen now.** Blast radius is one 194-line file; cost is zero; it buys time without committing. |
| **B — Vendor the parser** | Copy the needed `Razor.Language` source (MIT) into the repo. | Removes the dead-package dependency; adds real maintenance of ~a compiler subsystem. Reconsider only if NuGet ever stops serving 6.0.36. |
| **C — Write a subset parser** | A hand-written parser for *Filament's narrow subset* (far smaller than full Razor). | **The honest RADICAL exit.** §1's premise ("Razor already exists") is true for the POC and fragile beyond it; since Filament only supports a narrow subset, its parser is small. This is the Svelte path §1 tried to avoid — and #52 shows it is where RADICAL leads. |
| **D — PRUDENT pivot** | Signals as a Blazor render mode, staying inside the living toolchain. | Sidesteps the risk entirely — the named fallback if RADICAL is abandoned (spec §8). |

## Decision

1. **Adopt Option A now.** The dependency stays pinned and contained; the inventory above is the
   migration map for whoever later takes Option B or C.
2. **Harden debt #1 at the seam.** `RazorFrontEnd.CreateEngine` now **throws** if either markup pass is
   not found to remove, instead of silently no-op'ing. On the pinned 6.0.36 both passes are always
   registered, so behavior is unchanged today; the throw only fires if the toolchain is bumped
   underfoot — turning a silent, project-defeating collapse into a loud, located failure that points
   back here.
3. **Defer B/C to a RADICAL commitment.** Neither is worth its cost while RADICAL is *not established*
   (spec §8). Option **C** (a subset parser) is the recommended exit **if and when** RADICAL is chosen
   as the architecture.

## Trigger — when this must be revisited

Revisit (and likely execute Option C) if **any** of these becomes true:

- NuGet stops serving `Razor.Language` 6.0.36, or restore breaks on a future `net` TFM.
- A `net` SDK drops the runtime/BCL 6.0.36 needs to load.
- Filament commits to RADICAL as a shipping architecture — at which point a dead-package foundation is
  no longer acceptable, and the subset parser is the exit.

Until then: **contained, guarded, and documented** — the most a POC should spend on a risk it has
deliberately deferred.
