# Next work and backlog

<!-- purpose: Open work only; contract for agents syncing backlog on ship. -->

**updated_at:** 2026-04-07T22:09:00Z

## Agent contract

**Scope:** This file lists **unfinished work only**. It is not a changelog.

| | |
|---|---|
| **MUST** | Remove or update backlog rows when work ships; do it in the **same PR** as the implementation (or an immediate follow-up). |
| **MUST** | End implementation plans with a final todo: `backlog: sync ai_docs/backlog.md` when closing backlog-related items. |
| **MUST** | Use stable, kebab-case `id` values per open row (grep-friendly). |
| **MUST NOT** | Add “Completed” tables, dated history sections, or narrative changelogs here. |
| **MUST NOT** | Leave done items in the open table. |

## Standing rules (ongoing)

These are **not** backlog rows; do not delete them when clearing completed work.

- Reprioritize on each audit pass.
- Bug and tool items: put reproduction and session context in the PR or linked issue when it helps reviewers.
- Broader planning and complexity notes stay in this file; `ai_docs/archive/` holds policy only (see `archive/README.md`).

## Open work

| id | blocker | deps | do |
|----|---------|------|-----|
| `testbase-srp-split` | none | — | **P3 / refactor.** `TestBase` is a 318-line god class owning: 40+ static service properties, MSBuild init, filesystem helpers, path resolution, fixture copy logic, and now workspace caching. The recent fix made it idempotent and added a workspace cache, but the class still violates SRP. Plan: extract `TestServiceContainer` (services + lifecycle), `TestFixtures` (path resolution + copy helpers), and keep `TestBase` as a thin pass-through. Defer until a touch-the-class change is needed — the current split would touch 30+ test classes. |
| `vuln-scan-network-mock` | none | — | **P3 / test isolation.** `ScanNuGetVulnerabilitiesAsync_ReturnsStructuredResult` shells out to `dotnet list package --vulnerable`, which hits `api.nuget.org`. Now passes reliably with the workspace-cache fix, but still fails in air-gapped environments. Consider mocking the NuGet client at the gated-executor boundary, or marking the test `[Category("Network")]` so it can be filtered. |
| `verify-release-test-output-policy` | none | — | **P3 / process.** `eng/verify-release.ps1` runs `dotnet test` with default verbosity. Add `--logger "console;verbosity=normal"` and `$ErrorActionPreference = 'Stop'` so failing tests are not masked by tail/pipe operations in any wrapper script. |
| `semantic-search-async-modifier-doc` | none | — | **P3 / docs.** `semantic_search` query `async methods returning Task<bool>` returns 0 because Roslyn `IMethodSymbol.IsAsync` requires the `async` modifier on the declaration; pass-through `Task.FromResult` methods are not matched. Document this gotcha in `CodePatternAnalyzer` tool description and the `/roslyn-mcp:review` skill so users know to query for "methods returning Task<bool>" without "async" if they want all Task-returning methods. |
| `workspace-rw-lock-phase3-cleanup` | one full release of default-on soak | `workspace-rw-lock-flag-flip` (phase-2 default flip shipped 2026-04-07) | **P2 / perf rollout.** Phase-2 flipped `ExecutionGateOptions.UseReaderWriterLock` and `Program.BindExecutionGateOptions` defaults to `true`; the env-var escape hatch `ROSLYNMCP_WORKSPACE_RW_LOCK=false` remains for one more release. Phase-3 follow-up after soak: drop the env-var binding from `Program.BindExecutionGateOptions`, delete the legacy `_workspaceGates` / `_useRwLock` branch in `WorkspaceExecutionGate`, remove the `RunAsync` compat shim if all callers have migrated, drop the `SemaphoreSlim`-backed gate dictionary, and remove this row. **Dual-mode audit pair** (`20260407T211317Z_itchatbot_rw-lock_...` + `20260407T213736Z_itchatbot_legacy-mutex_...`) confirmed FLAG-021 (`workspace_close` `ObjectDisposedException`) is legacy-mutex-specific — phase-3 cleanup permanently closes that bug. |
| `dependency-service-srp-split` | none | — | **P3 / refactor.** `DependencyAnalysisService` (530 LOC) owns four responsibilities: namespace dependency graphs, DI registration scanning, NuGet package listing, and NuGet vulnerability scanning. Split into `NamespaceDependencyService`, `DiRegistrationService`, and `NuGetDependencyService`; `DependencyAnalysisService` becomes a façade or is deleted. Also covers `ParseNuGetVulnerabilityJson` (cyclomatic 27, 86 LOC with nested local function capturing 4 outer variables) — extract `NuGetVulnerabilityJsonParser` static class and eliminate the nested function. Defer until a touch-the-class change is needed. |
| `orchestration-migrate-package-extract` | none | — | **P3 / refactor.** `OrchestrationService.PreviewMigratePackageAsync` (102 LOC, cyclomatic 17) handles project-file and `Directory.Packages.props` mutation logic inline. Extract `BuildPackageReferenceEdit`, `BuildCentralVersionEdit`, and `BuildRemoveVersionAttributeEdit` helpers to make control flow readable and allow isolated testing of each mutation shape. Defer until a touch-the-method change is needed. |
| `compile-check-emit-validation-regression-check` | none | — | **P3 / investigation.** The 2026-04-07 firewallanalyzer rw-lock audit reported `compile_check` with `emitValidation=true` returning byte-identical output (1 char shorter) to the default call, with no measurable wall-clock difference. The 2026-04-06 prior audit reported `emitValidation=true` was "~94× slower" with additional findings. Either (a) `emitValidation` regressed to a no-op when the base compilation has unresolved package references, or (b) the prior measurement was incidental. Re-test on a clean workspace with restored packages to determine which. If regressed, restore the emit path; if not, document the precondition. |

## Refs

| Path | Role |
|------|------|
| `ai_docs/workflow.md` | Branch/PR workflow; **Backlog closure** |
| `ai_docs/backlog.md` | This file (open work + contract) |
