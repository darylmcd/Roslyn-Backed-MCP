# coverage-baseline-stale — re-measure and refresh the coverage baseline doc

**row:** `coverage-baseline-stale` · **pri:** `Medium` · **size:** `S`

## Anchors

- `docs/coverage-baseline.md` — the only production file; a pure-doc update once fresh numbers exist.
- `eng/verify-release.ps1` — run (not edited) to produce the fresh Cobertura measurement.
- `artifacts/coverage/**/coverage.cobertura.xml` — build output (not edited) read for the fresh line/branch rates.

## Acceptance

- [ ] Run `./eng/verify-release.ps1 -Configuration Release` (full, uncapped) and open the produced `artifacts/coverage/**/coverage.cobertura.xml`.
- [ ] Update the "Current baseline (root aggregate)" table's line/branch coverage percentages, test count, and `Updated:` stamp (date + version) to the fresh measurement.
- [ ] Re-check the "Priority areas for new tests" list against current Cobertura output — drop entries that are no longer 0%/low-coverage, add any newly-identified gaps.
- [ ] Update `.github/copilot-instructions.md` too if the aggregate moved materially (the doc's own closing instruction).

## Evidence

- `docs/coverage-baseline.md`'s baseline is stamped `2026-04-11 — measured after v1.9.0 (329 tests)`. The repo is now at v4.1.2 (2026-09-02 per `CHANGELOG.md`) — a ~5-month, 2-major-version gap. Found during a 2026-09-03 staleness check of the CI-policy doc cluster requested by the maintainer.

## Context

Not urgent/broken — this is stale reference data, not a functional defect. The doc's own instructions call for re-measurement "if the aggregate moves materially," which a gap this size (329 tests → current suite, several major versions) makes near-certain. No local SDK matches `global.json`'s pinned `10.0.400` on this machine (`dotnet` resolves 10.0.201), so this needs either a CI-side run (e.g. trigger the dispatch/schedule workflow, which already uploads the `code-coverage` artifact with Cobertura + HTML) or a machine with the matching SDK — not a plain local `dotnet` invocation.
