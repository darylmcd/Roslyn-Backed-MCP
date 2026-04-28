# Next work and backlog

<!-- purpose: Open work only; contract for agents syncing backlog on ship. -->
<!-- scope: in-repo -->

**updated_at:** 2026-04-28T15:25:57Z

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
| `apply-composite-preview-name-friction` | Low | none | Tool name `apply_composite_preview` reads as a preview but the tool is destructive (it APPLIES a previously-staged composite preview). Either rename to `composite_apply` (catalog-version bump required) OR strengthen the catalog `summary` text to lead with "DESTRUCTIVE — applies a previously-previewed orchestration operation" so agents see the warning in the catalog/discover_capabilities output. Anchors: registration in `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs`, tool wrapper in `src/RoslynMcp.Host.Stdio/Tools/OrchestrationTools.cs`, prompt body in `ai_docs/prompts/deep-review-and-refactor.md` Phase 10 (already documents the friction). Regression test shape: assert `ServerSurfaceCatalog["apply_composite_preview"].summary` starts with the destructive warning marker. Evidence: `review-inbox/archive/20260427T202515Z/20260427T202515Z_roslyn-backed-mcp_mcp-server-audit.md` §15 S-02. |
| `coupling-metrics-p50-borderline-on-15s-solution-budget` | Low | none | `get_coupling_metrics` p50 was 12807ms on this 7-project / 571-doc solution (Phase 2 of 2026-04-27 self-audit), against the 15s solution-scan budget. No regression vs prior runs but trending against budget; likely cause is per-type symbol-walk re-enumerating the full solution. Profile and either cache the per-project compilation walk or scope the SymbolFinder pass to one project at a time when `projectName` is supplied. Anchors: service `src/RoslynMcp.Roslyn/Services/CodeMetricsService.cs` (coupling section, look for `GetCouplingMetricsAsync`), tool registration `src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs` (`GetCouplingMetrics`). Regression test shape: BenchmarkDotNet test under `tests/RoslynMcp.Tests/Benchmarks/` asserting per-project coupling on a small workspace completes in <2s. Evidence: `review-inbox/archive/20260427T202515Z/20260427T202515Z_roslyn-backed-mcp_mcp-server-audit.md` §6 + §15 S-04. |
| `dead-logger-fields-roslyn-services-batch-1` | Low | none | Drop never-read `private readonly ILogger<T> _logger` primary-ctor parameter from 4 services in `RoslynMcp.Roslyn.Services` whose constructor injects a logger that no method ever calls (write count=1 from the ctor, read count=0). Either restore actual logging OR drop the parameter; this batch picks the drop path and removes the parameter from the constructor signature (DI auto-resolves; no `ServiceCollectionExtensions` change needed since `AddLogging()` registers `ILogger<T>` globally). Anchors: `src/RoslynMcp.Roslyn/Services/BulkRefactoringService.cs:16`, `src/RoslynMcp.Roslyn/Services/CodeMetricsService.cs:14`, `src/RoslynMcp.Roslyn/Services/CompletionService.cs:13`, `src/RoslynMcp.Roslyn/Services/ConsumerAnalysisService.cs:14`. Regression test shape: one xunit test in `tests/RoslynMcp.Tests/` reflects on the 4 service types and asserts no `ILogger<T>` field is declared. Evidence: `review-inbox/archive/20260427T202515Z/20260427T202515Z_roslyn-backed-mcp_mcp-server-audit.md` §14.1 (split per Rule 3). |
| `dead-logger-fields-roslyn-services-batch-2` | Low | `dead-logger-fields-roslyn-services-batch-1` | Same fix as batch-1 applied to the next 4 services in `RoslynMcp.Roslyn.Services`. Anchors: `src/RoslynMcp.Roslyn/Services/DiagnosticService.cs:18`, `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs:13`, `src/RoslynMcp.Roslyn/Services/FlowAnalysisService.cs:12`, `src/RoslynMcp.Roslyn/Services/MutationAnalysisService.cs:16`. Regression test shape: extend the batch-1 reflection assertion to cover these 4 service types. Evidence: `review-inbox/archive/20260427T202515Z/20260427T202515Z_roslyn-backed-mcp_mcp-server-audit.md` §14.1 (split per Rule 3). |
| `dead-logger-fields-roslyn-services-batch-3` | Low | `dead-logger-fields-roslyn-services-batch-2` | Last service in the dead-`_logger` cluster: `DuplicateMethodDetectorService` declares both a never-read `_logger` AND a never-read `_compilationCache` (both written once by the primary constructor). Drop both parameters from the constructor signature. Single-file change. Anchors: `src/RoslynMcp.Roslyn/Services/DuplicateMethodDetectorService.cs:24` (`_compilationCache`) + `:25` (`_logger`). Regression test shape: extend the batch-1/2 reflection assertion to cover the 9th service AND assert `_compilationCache` is gone. Evidence: `review-inbox/archive/20260427T202515Z/20260427T202515Z_roslyn-backed-mcp_mcp-server-audit.md` §14.1 (split per Rule 3). |
| `di-graph-triple-registration-cleanup` | Low | none | `get_di_registrations(showLifetimeOverrides=true, summary=true)` reports 9 service types each registered 3 times across composition-root layers (only the last write wins; `lifetimesDiffer=false` so no functional bug, just 18 dead lines of DI bootstrap). Examples: `ILatestVersionProvider`, `NuGetVersionChecker`, `ExecutionGateOptions`, `PreviewStoreOptions`, `ScriptingServiceOptions`. Audit `src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs` against `src/RoslynMcp.Host.Stdio/Program.cs` (and any other composition fragments) and de-dup. Anchors: `src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs`, `src/RoslynMcp.Host.Stdio/Program.cs`. Regression test shape: existing `tests/RoslynMcp.Tests/DependencyInjectionTests.cs` (or similar) asserts each service type has registrationCount≤1 in the final IServiceCollection. Evidence: `review-inbox/archive/20260427T202515Z/20260427T202515Z_roslyn-backed-mcp_mcp-server-audit.md` §14.2 + §15 S-03. |
| `symbol-info-not-found-message-locator-vs-location` | Low | none | When `symbol_info` is called with `metadataName` (no file/line/column) and resolution fails, the error message reads "No symbol found at the specified location" — but the caller didn't supply a location. Branch the message text on which locator field was supplied: "No symbol found for metadata name '<name>'" / "No symbol found at <file>:<line>:<col>" / "No symbol found for symbol handle". Anchors: tool registration `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs` (`Name = "symbol_info"`), service `src/RoslynMcp.Roslyn/Services/SymbolNavigationService.cs` (resolver for `symbol_info`). Regression test shape: 3 tests calling with each locator shape and asserting the error message names the supplied field. Evidence: `review-inbox/archive/20260427T202515Z/20260427T202515Z_roslyn-backed-mcp_mcp-server-audit.md` §7 row 2 + §15 S-06. |

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
