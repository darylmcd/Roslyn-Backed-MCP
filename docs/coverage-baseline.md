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
| Line coverage | **~86.2%** | Cobertura root: 33,105 / 38,387 lines (`line-rate="0.8624"`) |
| Branch coverage | **~71.9%** | Cobertura root: 13,230 / 18,394 branches (`branch-rate="0.7192"`) |

**Updated:** 2026-09-20 — measured at v4.2.1 with `verify-release.ps1` (3,156 discovered tests: 3,144 passed and 12 skipped).

Historical note: older docs cited ~50% line / ~34% branch from an earlier toolchain or partial collection; the **do not regress** rule applies to this **current** baseline.

## Priority areas for new tests (from Cobertura + risk)

Use this list when expanding `tests/RoslynMcp.Tests/` integration coverage. Prefer **behavior-heavy services** over DTO-only types (many DTOs show 0% line coverage because they are never constructed in isolation).

| Priority | Area | Rationale |
|----------|------|-----------|
| P1 | `TypeScaffolder`, `BatchTestScaffolder`, and `SingleTestScaffolder` | Current report leaves 371 source lines uncovered across behavior-heavy test-generation paths. |
| P1 | `ProjectMutationTools` and `UndoTools` | Both host tool surfaces report 0% line coverage; add protocol-level integration tests for mutation and rollback behavior. |
| P1 | `TestDiscoveryService`, `WorkspaceManager`, and `SymbolRefactorService` | Large workspace/refactoring surfaces account for 352 uncovered source lines and carry state-management risk. |
| P2 | `InterfaceMemberRemovalOrchestrator`, `SymbolSearchService`, and `InterfaceExtractionService` | Low-to-moderate line coverage (7.5%, 61.4%, and 66.9%) leaves orchestration and semantic-search branches exposed. |
| P3 | Generated regex code and core DTOs | Do not target generated or data-only code in isolation; cover it only through behavioral or serialization contracts. |

After adding tests, re-run `verify-release.ps1` and update the **Current baseline** table and `.github/copilot-instructions.md` if the aggregate moves materially.

## Related

- `CI_POLICY.md` — CI runs coverage as part of `verify-release.ps1`
- `ai_docs/references/testing.md` — primary test commands
- `docs/parity-gap-implementation-plan.md` — release verification context
