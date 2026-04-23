# Next work and backlog

<!-- purpose: Open work only; contract for agents syncing backlog on ship. -->
<!-- scope: in-repo -->

**updated_at:** 2026-04-23T14:39:18Z

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

| id | pri | deps | do |
|----|-----|------|-----|
| `cc-18-to-19-residuals-post-top10-extraction` | P3 | — | `RoslynMcp.Roslyn` still has 13 methods in the cc=18-19 band after the top-17 hotspot sweep. Tackle them in roughly seven sessions using two-method batches; start with `ScriptingService.EvaluateAsync` and `RefactoringService.PersistDocumentSetChangesAsync`, and review `NuGetVulnerabilityJsonParser.AddPackagesFromFramework` first for possible defer/reject if it is a dense switch parser. Refs: PRs #304, #307, #308, #310, #312, #314, #316, #317, #318, #319. |

## P4 — open work

| id | pri | deps | do |
|----|-----|------|-----|
| `find-dead-fields-across-classes` | P4 | — | Add a read-side analysis tool for unused fields (`never-read`, `never-written`, `never-either`). Current `find_dead_locals` and `find_unused_symbols` miss field-level cases seen in audit follow-up work. |
| `host-parallel-mcp-reads` | P4 | `host` | Document and enable safe parallel read-only MCP calls where the host/client permits it; current serialization wastes the server's read-side concurrency. |
| `mcp-audit-rollup-2026-04-13-22` | P4 | — | Umbrella row for unresolved server-side follow-ons absorbed from the retired 2026-04-13/15/22 raw audits: preview-formatting defects, summary-mode gaps, resource/connection contract docs, large-solution payload reducers, warm-up follow-ons, and selected experimental-promotion fixes. Split child rows only when one item becomes active. |
| `workspace-process-pool-or-daemon` | P4 | `compilation-prewarm-on-load` | Reduce repeated `workspace_load` cost across multi-agent sessions by evaluating a long-running daemon or process-pool/shared-workspace model. |

## Refs

| Path | Role |
|------|------|
| `ai_docs/planning_index.md` | Planning router and scope boundary |
| `ai_docs/workflow.md` | Branch/PR workflow and backlog-closure rule |
| `ai_docs/reports/20260422T143323Z_deep-review-rollup.md` | Current synthesized audit rollup backing the umbrella P4 row |
| `ai_docs/procedures/deep-review-backlog-intake.md` | Intake procedure for future audit batches |
