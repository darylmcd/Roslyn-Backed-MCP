# find-duplicated-methods-no-byte-budget — add output-byte budget / summary mode to FindDuplicatedMethodsCore

**row:** `find-duplicated-methods-no-byte-budget` · **pri:** `Low` · **size:** `M` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs:407` (`find_duplicated_methods`) / `:462` (`find_duplicated_code` alias → shared `FindDuplicatedMethodsCore`)
- duplicate-method service under `src/RoslynMcp.Roslyn/Services/`

## Acceptance

- [ ] Output-byte budget / `summary` mode added to `FindDuplicatedMethodsCore` so large result sets degrade gracefully instead of paging out (affects canonical + alias equally via the shared core)
- [ ] Regression: fixture with many large clusters asserts byte-bounded/summary output (not a bare 50-group dump)

## Evidence

- Original `audit-reports/20260531T192823Z_roslyn-backed-mcp_mcp-server-surface-test.md` Phase 2 (premise corrected); shared-core confirmed at HEAD.

## Context

**Premise corrected 2026-06-05 (top-5 remediation, Directive #5).** Original row claimed the `find_duplicated_code` ALIAS lacked the canonical's cap and paged out at ~74KB while `find_duplicated_methods` returned cleanly. FALSE at HEAD: since #450 both delegate to the SAME `FindDuplicatedMethodsCore` with the SAME `limit: 50` (group cap) — the alias has no separate cap path to fix, and the surface-test's 74KB-vs-clean divergence was a transient measurement artifact, not a structural difference.

Real residual: the family caps by GROUP COUNT (50), not by OUTPUT BYTES, so even the CANONICAL tool can breach the MCP output cap when 50 clusters carry large bodies — same payload-budget family as `test-discover-no-autopagination` / `project-diagnostics-no-summary-pages-out`. Sweep-shaped if combined with the other payload-budget rows.
