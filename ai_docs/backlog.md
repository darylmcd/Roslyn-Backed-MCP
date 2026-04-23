# Next work and backlog

<!-- purpose: Open work only; contract for agents syncing backlog on ship. -->
<!-- scope: in-repo -->

**updated_at:** 2026-04-23T22:01:33Z

## Agent contract

**Scope:** this file lists unfinished work only. It is not a changelog.

| | |
|---|---|
| **MUST** | Remove or update backlog rows when work ships; do it in the same PR or an immediate follow-up. |
| **MUST** | End implementation plans with a final todo: `backlog: sync ai_docs/backlog.md`. |
| **MUST** | Use stable, kebab-case `id` values per open row. |
| **MUST NOT** | Add completed tables, dated history sections, or narrative changelogs here. |
| **MUST NOT** | Leave done items in the open table. |

## Standing rules

- Reprioritize on each audit pass.
- Keep rows terse: summarize the current need, not the full session history.
- Keep rows planner-ready: name live anchors and the next concrete deliverable or investigation output.
- Replace stale umbrella rows with concrete follow-ons before planning against them.
- Put long-form audit evidence in the referenced reports, not in this file.

## P2 — open work

_No open P2 rows._

## P3 — open work

| id | pri | deps | do |
|----|-----|------|-----|
| `ci-test-parallelization-audit` | P3 | — | Refresh the old test-parallelization audit around the real residual issue: `eng/verify-release.ps1` still has 3 pre-existing cleanup-race flakes even though the `[DoNotParallelize]` sweep is already done. Start by reproducing and naming the failing tests, then localize whether the race sits in `tests/RoslynMcp.Tests/AssemblyCleanup.cs`, `tests/RoslynMcp.Tests/TestBase.cs`, or another teardown path before planning the fix plus a repeated-release stress gate (`eng/verify-release-stress.ps1` / CI). |

## P4 — open work

| id | pri | deps | do |
|----|-----|------|-----|
| `test-related-files-empty-result-explainability` | P4 | — | `test_related_files` currently returns only `tests` + `dotnetTestFilter` (`src/RoslynMcp.Core/Models/RelatedTestsForFilesDto.cs`), so an empty result cannot distinguish "no tests exist" from "heuristic miss." Extend the response, or add a companion diagnostic mode, to report scanned test projects, heuristics attempted, and miss reasons for each changed file. |
| `test-related-files-service-refactor-underreporting` | P4 | — | `src/RoslynMcp.Roslyn/Services/TestDiscoveryService.cs` still underreports service-layer refactors because its fallback path leans too heavily on type-name / filename affinity. Broaden the heuristic for multi-file service changes so it can surface likely integration or neighboring service tests before returning empty, and validate against the `MutationAnalysisService` + `ScaffoldingService` and `CodePatternAnalyzer` + `TestDiscoveryService` misses seen in the 2026-04-23 sweep. |
| `workspace-process-pool-or-daemon` | P4 | `large-solution profile` | Do not start a daemon/process-pool implementation blindly. First capture representative 50+ project timings per `docs/large-solution-profiling-baseline.md`; only if `workspace_load` / reload P95 still blocks daily use after `workspace_warm` should the next plan produce a bounded design note comparing daemon, process-pool, and shared-workspace approaches, including lifecycle and failure-isolation hooks. |

## Refs

| Path | Role |
|------|------|
| `ai_docs/planning_index.md` | Planning router and scope boundary |
| `ai_docs/workflow.md` | Branch/PR workflow and backlog-closure rule |
| `ai_docs/plans/20260422T170500Z_test-parallelization-audit/plan.md` | Dedicated phased plan for `ci-test-parallelization-audit` |
| `docs/large-solution-profiling-baseline.md` | Evidence gate for daemon/process-pool performance work |
| `ai_docs/procedures/deep-review-backlog-intake.md` | Intake procedure for future audit batches |
| `ai_docs/plans/20260422T223000Z_backlog-sweep/plan.md` | Backlog sweep (20260422T223000Z). Shipped 14 initiatives across 14 PRs; closed 9 backlog rows (P2: 0, P3: 4, P4: 5). |
