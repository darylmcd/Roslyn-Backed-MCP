# Promotion Scorecard Review - 2026-04-27 Candidate List

<!-- purpose: Reconcile the 2026-04-27 promotion-only candidate list with the current catalog and quorum workflow. -->

## Decision

Do not flip any catalog tiers from the 2026-04-27 candidate list in this initiative.

The newer promotion path is quorum-based: `/publish-preflight` Step 8 consumes
the aggregate from `eng/aggregate-promotion-scorecards.ps1`, and `/promote-tier`
is only the mechanical tier-flip helper after a candidate has either:

- an aggregated `verdict: "promote: ready"` from at least two sibling repos with
  zero blockers, or
- an explicit maintainer override with a written rationale.

The 2026-04-27 list was useful intake, but it is no longer a direct work queue.
The current live surface check for this review was `server_info` on
2026-05-08: `roslyn-mcp` version
`1.34.2+e01c2f97dfc80e6c1a9888aed70c71191d8666c0`, catalog version
`2026.04`, with 168 registered tools/resources/prompts and catalog parity OK.
The worktree `RoslynMcp.slnx` also loaded for read-side inspection as workspace
`59bd4cc5796a4ed7baf4d3be4837ac8a`.

## Candidate Reconciliation

| Candidate | Current catalog state | Decision | Rationale |
|---|---:|---|---|
| `get_operations` | stable tool | Superseded | Already stable in `ServerSurfaceCatalog.Analysis.cs`; no follow-up row. |
| `workspace_warm` | experimental tool | Accepted only if quorum later confirms | Current single-repo scorecard says `promote-ready`, but the tool is not purely read-only and should wait for aggregate quorum or a maintainer override. |
| `find_type_consumers` | experimental tool | Accepted only if quorum later confirms | Still useful as a file-level rollup even though stable `find_consumers` and `find_type_usages` cover adjacent workflows. Do not promote from the old single-list evidence alone. |
| `trace_exception_flow` | experimental tool | Accepted only if quorum later confirms | Read-only and current single-repo scorecard is positive; promotion is appropriate only through aggregated quorum. |
| `find_duplicate_helpers` | experimental tool | Accepted only if quorum later confirms | Specialized dead-code signal with positive current local evidence; needs aggregate agreement. |
| `find_dead_locals` | experimental tool | Accepted only if quorum later confirms | Positive current local evidence, but still one workspace. Split with other read-only analysis flips only after aggregate quorum. |
| `find_dead_fields` | experimental tool | Rejected for this pass | Current scorecard says `keep-experimental` because the latest audit produced only a clean zero-result path and lacks broader positive evidence. |
| `symbol_impact_sweep` | experimental tool | Accepted only if quorum later confirms | Useful read-only planning bundle with positive current local evidence; wait for aggregate quorum. |
| `semantic_grep` | experimental tool | Accepted only if quorum later confirms | Positive current local evidence for bounded results and invalid-regex handling; still needs aggregate quorum. |
| `validate_workspace` | experimental tool | Accepted only if quorum later confirms | Positive current local evidence, but promotion should be batched with validation-surface review because it is a composite validator. |
| `validate_recent_git_changes` | experimental tool | Rejected until fixed | Current backlog already tracks `validate-recent-git-changes-timeout`; the latest scorecard says `needs-fix-before-promotion`. |
| `test_reference_map` | experimental tool | Accepted only if quorum later confirms | Positive current local evidence for pagination and coverage data; wait for aggregate quorum. |
| `get_prompt_text` | experimental tool | Accepted only if quorum later confirms | Positive current local evidence, but it fronts experimental prompts; promote only after aggregate quorum or explicit maintainer override. |
| `server_catalog_tools_page` | experimental resource | Accepted only if quorum later confirms | Paginated catalog resource is a good stable-surface candidate, but it still needs quorum-backed promotion. |
| `server_catalog_prompts_page` | experimental resource | Accepted only if quorum later confirms | Same as `server_catalog_tools_page`; promote with the catalog pagination pair only after quorum. |
| `probe_position` | experimental tool | Rejected for this pass | Narrow fixture-authoring helper and not present in the latest local scorecard entries reviewed here; keep experimental until fresh cross-repo evidence asks for it. |

## Follow-on Row Recommendations

Do not add a tier-flip implementation row from this note unless a future
aggregate scorecard produces `verdict: "promote: ready"` for the target names,
or the maintainer explicitly chooses an override.

If quorum appears, split the follow-up work into category-sized rows:

| Proposed row id | Include only when | Scope |
|---|---|---|
| `promote-quorum-analysis-readonly-tools` | Aggregate scorecard marks the included names `promote: ready` with zero blockers. | Flip eligible read-only analysis/validation tools from this review: `find_type_consumers`, `trace_exception_flow`, `find_duplicate_helpers`, `find_dead_locals`, `symbol_impact_sweep`, `semantic_grep`, `validate_workspace`, and `test_reference_map`. Keep the final row small enough to satisfy release validation and split further if the aggregate names are too broad. |
| `promote-quorum-prompt-catalog-surfaces` | Aggregate scorecard marks all included names `promote: ready` with zero blockers. | Flip `get_prompt_text`, `server_catalog_tools_page`, and `server_catalog_prompts_page` together only if their prompt/catalog behavior is accepted as part of the stable support contract. |
| `promote-quorum-workspace-warm` | Aggregate scorecard marks `workspace_warm` `promote: ready` with zero blockers, or a maintainer records a performance-oriented override. | Flip `workspace_warm` separately because it is long-running, progress-bearing, and not classified as read-only in the catalog. |

No follow-on row is recommended for `get_operations`, `find_dead_fields`,
`validate_recent_git_changes`, or `probe_position` from this pass.

## Validation Notes

- Manual catalog cross-check used `server_info` plus catalog source under
  `src/RoslynMcp.Host.Stdio/Catalog/`.
- The current local scorecard reviewed was
  `ai_docs/audit-reports/_latest-promotion-scorecard.json`, generated
  2026-05-08T16:15:25Z from `roslyn-backed-mcp`.
- This note intentionally does not edit `docs/experimental-promotion-analysis.md`
  or flip any catalog tiers; those changes belong to a future release-quality
  promotion row.
