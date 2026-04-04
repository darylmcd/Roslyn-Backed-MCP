# Code coverage baseline

This document records **measured** line/branch coverage from **Coverlet** (XPlat Code Coverage) via `eng/verify-release.ps1`, and lists **priority areas** for additional integration tests.

## How to reproduce

1. Run `./eng/verify-release.ps1 -Configuration Release` from the repository root.
2. Open `artifacts/coverage/**/coverage.cobertura.xml` (path includes a test-run GUID folder).
3. Optional HTML summary: install ReportGenerator (`dotnet tool install --global dotnet-reportgenerator-globaltool`) then:
   ```bash
   reportgenerator -reports:"artifacts/coverage/**/coverage.cobertura.xml" -targetdir:artifacts/coverage/report -reporttypes:HtmlSummary
   ```

CI uploads the **`code-coverage`** artifact (Cobertura + HTML summary when the workflow runs ReportGenerator).

## Current baseline (root aggregate)

| Metric | Value | Source |
|--------|-------|--------|
| Line coverage | **~59.3%** | `<coverage line-rate="…">` in Cobertura (e.g. ~0.593) |
| Branch coverage | **~46.4%** | `<coverage branch-rate="…">` |

**Updated:** 2026-04-04 — measured after integrating coverage collection into `verify-release.ps1`.

Historical note: older docs cited ~50% line / ~34% branch from an earlier toolchain or partial collection; the **do not regress** rule applies to this **current** baseline.

## Priority areas for new tests (from Cobertura + risk)

Use this list when expanding `tests/RoslynMcp.Tests/` integration coverage. Prefer **behavior-heavy services** over DTO-only types (many DTOs show 0% line coverage because they are never constructed in isolation).

| Priority | Area | Rationale |
|----------|------|-----------|
| P1 | `CompileCheckService`, `CodeActionService` hot paths | Validation and code-action flows; previously under-covered async paths. |
| P1 | `DependencyAnalysisService`, `DeadCodeService` branches | Large surface; mutation-adjacent behavior. |
| P2 | `BoundedStore<T>` via preview stores | Eviction/TTL paths; exercise through preview/apply integration tests. |
| P2 | `ServiceCollectionExtensions` (DI registration) | Low direct coverage; optional smoke test that builds host `ServiceProvider`. |
| P3 | Core DTOs | Improve only when tied to a behavioral regression or serialization contract test. |

After adding tests, re-run `verify-release.ps1` and update the **Current baseline** table and `.github/copilot-instructions.md` if the aggregate moves materially.

## Related

- `CI_POLICY.md` — CI runs coverage as part of `verify-release.ps1`
- `ai_docs/references/testing.md` — primary test commands
- `docs/parity-gap-implementation-plan.md` — release verification context
