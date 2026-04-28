# Next work and backlog

<!-- purpose: Open work only; contract for agents syncing backlog on ship. -->
<!-- scope: in-repo -->

**updated_at:** 2026-04-28T19:18:02Z

## Agent contract

| | |
|---|---|
| **Scope** | This file lists unfinished work only. It is not a changelog. |
| **MUST** | Remove or update backlog rows when work ships; do it in the same PR or an immediate follow-up. |
| **MUST** | End implementation plans with a final todo: `backlog: sync ai_docs/backlog.md`. |
| **MUST** | Use stable, kebab-case `id` values per open row. |
| **MUST** | Every row's `do` cell summarizes the current need + the concrete next deliverable. Include `Anchors:` (specific source file paths) when the row references code, and evidence (audit/retro/CI signal) when one exists. |
| **MUST** | Size every row to a single bounded initiative — ≤4 production files, ≤3 test files, one regression-test shape. Split heroic multi-bug rows into per-bug children before planning against them. |
| **MUST NOT** | Add `Completed`, `Shipped`, `Done`, `History`, or `Changelog` sections. Git is the archive. |
| **MUST NOT** | Leave done items in the open table. |
| **MUST NOT** | Use `### <id>` body sections per item. The table row IS the canonical form. Items needing long-form depth (more than ~10 lines) link to `ai_docs/items/<id>.md` from the `do` cell. |

## Standing rules

- **Reprioritize on each audit pass.** Stale priority order is a finding.
- **Keep rows planner-ready.** A row is ready when an agent can read it cold and start a plan: name the live anchors and the next concrete deliverable or investigation output.
- **Replace stale umbrella rows with concrete follow-ons** before planning against them.
- **Long-form audit evidence belongs in referenced reports**, not in this file. The `do` cell carries a one-line evidence summary plus the report path.
- **Weak-evidence flag.** When a row's signal is thin (single retro session, self-audit only, etc.) say so explicitly in the `do` cell ("Weaker evidence — N until external session reproduces").
- **Priority tiers:** Critical > High > Medium > Low > Defer.
- See `workflow.md` → **Backlog closure** for close-in-PR expectations.

---

## Critical

<!-- Production-breaking or blocking work. Empty section is fine; keep the header. -->

| id | pri | deps | do |
|----|-----|------|-----|

## High

| id | pri | deps | do |
|----|-----|------|-----|

## Medium

| id | pri | deps | do |
|----|-----|------|-----|

## Low

| id | pri | deps | do |
|----|-----|------|-----|
| `coupling-metrics-p50-borderline-on-15s-solution-budget` | Low | none | `get_coupling_metrics` p50 was 12807ms on this 7-project / 571-doc solution (Phase 2 of 2026-04-27 self-audit), against the 15s solution-scan budget. No regression vs prior runs but trending against budget; likely cause is per-type symbol-walk re-enumerating the full solution. Profile and either cache the per-project compilation walk or scope the SymbolFinder pass to one project at a time when `projectName` is supplied. Anchors: service `src/RoslynMcp.Roslyn/Services/CodeMetricsService.cs` (coupling section, look for `GetCouplingMetricsAsync`), tool registration `src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs` (`GetCouplingMetrics`). Regression test shape: BenchmarkDotNet test under `tests/RoslynMcp.Tests/Benchmarks/` asserting per-project coupling on a small workspace completes in <2s. Evidence: `review-inbox/archive/20260427T202515Z/20260427T202515Z_roslyn-backed-mcp_mcp-server-audit.md` §6 + §15 S-04. |
| `dead-logger-fields-roslyn-services-batch-2` | Low | `dead-logger-fields-roslyn-services-batch-1` | Same fix as batch-1 applied to the next 4 services in `RoslynMcp.Roslyn.Services`. Anchors: `src/RoslynMcp.Roslyn/Services/DiagnosticService.cs:18`, `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs:13`, `src/RoslynMcp.Roslyn/Services/FlowAnalysisService.cs:12`, `src/RoslynMcp.Roslyn/Services/MutationAnalysisService.cs:16`. Regression test shape: extend the batch-1 reflection assertion to cover these 4 service types. Evidence: `review-inbox/archive/20260427T202515Z/20260427T202515Z_roslyn-backed-mcp_mcp-server-audit.md` §14.1 (split per Rule 3). |
| `dead-logger-fields-roslyn-services-batch-3` | Low | `dead-logger-fields-roslyn-services-batch-2` | Last service in the dead-`_logger` cluster: `DuplicateMethodDetectorService` declares both a never-read `_logger` AND a never-read `_compilationCache` (both written once by the primary constructor). Drop both parameters from the constructor signature. Single-file change. Anchors: `src/RoslynMcp.Roslyn/Services/DuplicateMethodDetectorService.cs:24` (`_compilationCache`) + `:25` (`_logger`). Regression test shape: extend the batch-1/2 reflection assertion to cover the 9th service AND assert `_compilationCache` is gone. Evidence: `review-inbox/archive/20260427T202515Z/20260427T202515Z_roslyn-backed-mcp_mcp-server-audit.md` §14.1 (split per Rule 3). |
| `di-graph-triple-registration-cleanup` | Low | none | `get_di_registrations(showLifetimeOverrides=true, summary=true)` reports 9 service types each registered 3 times across composition-root layers (only the last write wins; `lifetimesDiffer=false` so no functional bug, just 18 dead lines of DI bootstrap). Examples: `ILatestVersionProvider`, `NuGetVersionChecker`, `ExecutionGateOptions`, `PreviewStoreOptions`, `ScriptingServiceOptions`. Audit `src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs` against `src/RoslynMcp.Host.Stdio/Program.cs` (and any other composition fragments) and de-dup. Anchors: `src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs`, `src/RoslynMcp.Host.Stdio/Program.cs`. Regression test shape: existing `tests/RoslynMcp.Tests/DependencyInjectionTests.cs` (or similar) asserts each service type has registrationCount≤1 in the final IServiceCollection. Evidence: `review-inbox/archive/20260427T202515Z/20260427T202515Z_roslyn-backed-mcp_mcp-server-audit.md` §14.2 + §15 S-03. |
| `navigation-tools-misnamed-locator-error` | Low | none | After PR #474 fixed `symbol_info`'s misnamed-error message branch, sibling navigation tools still return the legacy *"No symbol found at the specified location"* text regardless of which locator field was supplied. Apply the same locator-aware branching at the resolver layer (or via the new `SymbolLocatorFactory` helper from PR #474) so all navigation tools surface the supplied-field name. Affected sites cited by PR #474 author: `symbol_relationships`, `goto_type_definition`, `find_consumers`; `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs` lines 232/258/308/358. Anchors: `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs`, plus the resolvers behind `symbol_relationships`, `goto_type_definition`, `find_consumers`. Regression test shape: 3 tests per affected tool (one per locator shape) asserting message names supplied field. Evidence: PR #474 (closed `symbol-info-not-found-message-locator-vs-location` with deliberate containment to symbol_info per Rule 1; sibling tools deferred to this row). |

## Defer

<!-- Explicitly parked. Record WHY in the `do` cell. -->

| id | pri | deps | do |
|----|-----|------|-----|
| `workspace-process-pool-or-daemon` | Defer | future worse-profile evidence | Representative 227-project OrchardCore profile captured on 2026-04-26 did not justify daemon/process-pool implementation: `workspace_load` P95 was 44.85s, `symbol_search` P95 was 1.18s, and `find_references` P95 was 997ms, all below `docs/large-solution-profiling-baseline.md` thresholds. Keep this deferred unless a larger/worse customer-scale profile or daily-use evidence shows `workspace_load` / reload P95 blocking work after `workspace_warm`; then produce a bounded design note comparing daemon, process-pool, and shared-workspace approaches, including lifecycle and failure-isolation hooks. Evidence: `docs/large-solution-profiling-baseline.md` recorded OrchardCore run; local raw artifacts under `artifacts/large-solution-profiling/20260426T212443Z/`. |

## Refs

| Path | Role |
|------|------|
| `ai_docs/planning_index.md` | Planning router and scope boundary |
| `ai_docs/workflow.md` | Branch/PR workflow and backlog-closure rule |
| `ai_docs/prompts/backlog-sweep-plan.md` | Planner prompt; enforces per-initiative Rule 1 (bundle only on shared code path) / Rule 3 (≤4 prod files) / Rule 3b (toolPolicy) / Rule 4 (≤3 test files) / Rule 5 (≤80K context). Each row here is sized for one initiative. |
| `ai_docs/prompts/backlog-sweep-execute.md` | Executor companion; consumes the `state.json` the planner emits and vets each initiative against Rules 3/4/5 before starting work. |
| `ai_docs/bootstrap-read-tool-primer.md` | Self-edit session read-only tool primer (Roslyn-MCP read-side tools to prefer over Bash/Grep). |
| `ai_docs/runtime.md` | Bootstrap scope policy — distinguishes main-checkout self-edit (no `*_apply`) from worktree/parallel-subagent sessions. |
| `docs/large-solution-profiling-baseline.md` | Evidence gate for daemon/process-pool performance work. |
| `ai_docs/procedures/deep-review-backlog-intake.md` | Intake procedure for future audit batches. |
| `review-inbox/` | Staging folder for the NEXT audit batch (flat directory; `/backlog-intake` reads here). |
| `review-inbox/archive/<batch-ts>/` | Processed audit/retro/promotion batches — one subdirectory per successful intake. Keep until every row sourced from a batch is closed or superseded, then delete that subdirectory. |
