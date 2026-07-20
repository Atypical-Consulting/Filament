<!-- Thanks for contributing to Filament! Please read CONTRIBUTING.md if you haven't. -->

## Summary

<!-- What does this PR do, and why? -->

Closes #

## Type of change

- [ ] Subset widening (a construct that used to be refused now compiles)
- [ ] Bug fix in the generator's lowering
- [ ] Runtime change (signals, effects, list reconciliation)
- [ ] Analyzer / diagnostics
- [ ] Tooling / SDK / template
- [ ] Docs / website
- [ ] Chore / refactor

## Checklist

- [ ] `dotnet test Filament.sln` is green locally
- [ ] New behaviour is **measured against Blazor** via the oracle, not asserted from reasoning
- [ ] `DECISIONS.md` records the decision and `BENCH.md` the measurement (append-only)
- [ ] A witness fixture that flipped refused → supported was `git mv`d from `Unsupported/` into `Supported/`
- [ ] Any divergence from C# semantics is **disclosed**, not left implicit

## Runtime firewall

Most changes should be generator-only — the runtime ships as written and is the one part the
compiler does not emit.

- [ ] `git diff -- src/filament-runtime` is empty
- [ ] …or this PR deliberately changes the runtime, and says why below

## Notes for reviewers

<!-- Anything tricky, trade-offs made, or follow-ups deferred. -->
