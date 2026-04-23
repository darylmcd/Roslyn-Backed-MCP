# Next work and backlog

<!-- purpose: Open work only; contract for agents syncing backlog on ship. -->
<!-- scope: in-repo -->

**updated_at:** 2026-04-23T20:26:14Z

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
- Put long-form audit evidence in the referenced reports, not in this file.

## P2 — open work

_No open P2 rows._

## P3 — open work

_No open P3 rows._

## P4 — open work

| id | pri | deps | do |
|----|-----|------|-----|
| `host-parallel-mcp-reads` | P4 | `host` | Document and enable safe parallel read-only MCP calls where the host/client permits it; current serialization wastes the server's read-side concurrency. |
| `mcp-audit-rollup-2026-04-13-22` | P4 | — | Umbrella row for unresolved server-side follow-ons absorbed from the retired 2026-04-13/15/22 raw audits: preview-formatting defects, summary-mode gaps, resource/connection contract docs, large-solution payload reducers, warm-up follow-ons, and selected experimental-promotion fixes. Split child rows only when one item becomes active. |
| `test-related-files-empty-result-explainability` | P4 | — | `test_related_files` currently returns only `tests: []` and `dotnetTestFilter: \"\"` on a miss. During the 2026-04-23 backlog-sweep wave-2 preflight this gave no clue whether no tests existed or the heuristic missed likely coverage for `MutationAnalysisService` and `ScaffoldingService`, then again for `CodePatternAnalyzer` and `TestDiscoveryService`. Extend the tool, or add a companion, to report scanned test projects, heuristics hit, and rejection reasons on empty results. Refs: retrospective 2026-04-23 Step 2a row 1, Step 2b row 1, Step 3 pattern 1. |
| `test-related-files-service-refactor-underreporting` | P4 | — | Harden `test_related_files` for service-layer refactors. In the 2026-04-23 backlog-sweep wave-2 preflight it returned empty results for `MutationAnalysisService.cs` plus `ScaffoldingService.cs` and for `CodePatternAnalyzer.cs` plus `TestDiscoveryService.cs`, despite those initiatives later landing targeted test updates in PRs #367 and #366. Add stronger fallback heuristics such as symbol-name affinity, broader integration-test ownership, or unioning with `test_related` before falling back to empty. Refs: retrospective 2026-04-23 Step 2a row 1, Step 3 pattern 1. |
| `workspace-process-pool-or-daemon` | P4 | `compilation-prewarm-on-load` | Reduce repeated `workspace_load` cost across multi-agent sessions by evaluating a long-running daemon or process-pool/shared-workspace model. |

## Refs

| Path | Role |
|------|------|
| `ai_docs/planning_index.md` | Planning router and scope boundary |
| `ai_docs/workflow.md` | Branch/PR workflow and backlog-closure rule |
| `ai_docs/reports/20260422T143323Z_deep-review-rollup.md` | Current synthesized audit rollup backing the umbrella P4 row |
| `ai_docs/procedures/deep-review-backlog-intake.md` | Intake procedure for future audit batches |
