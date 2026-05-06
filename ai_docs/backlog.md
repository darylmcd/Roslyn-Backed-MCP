# Next work and backlog

<!-- purpose: Open work only; contract for agents syncing backlog on ship. -->
<!-- scope: in-repo -->

**updated_at:** 2026-05-06T21:42:46Z

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
| `scaffold-test-preview-sampled-test-names` | Medium | none | Plumb `IMcpServer` into `ScaffoldingTools.PreviewScaffoldTest` (and the underlying `IScaffoldingService.PreviewScaffoldTestAsync`) and add a sampled `SuggestTestNameAsync` step gated behind a `useSampling: bool = false` parameter so non-sampling clients see no behavior change. When `useSampling=true` and the client declares the `sampling` capability, replace the placeholder `<TargetMethodName>_Needs_Test` with a model-suggested Given/When/Then test method name (e.g. `LoadIntoSessionAsync_WhenCacheMiss_FallsThroughToColdLoad`) computed from the target method's signature + sibling-test-class examples. Collapses N rename round-trips per N-test scaffold pass to 1 sampled call. Anchors: `src/RoslynMcp.Host.Stdio/Tools/ScaffoldingTools.cs`, `src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs` (`PreviewScaffoldTestAsync` + `_Needs_Test` placeholder site at line 1887). Regression test shape: 2 — (a) `useSampling: false` returns `_Needs_Test` placeholder unchanged (default behavior preserved); (b) `useSampling: true` against a mock client that returns a string returns a non-placeholder name. Evidence: `ai_docs/items/sampling-spike.md` § Verdicts — only `go` candidate from the 2026-05-05 spike (cost $0.001–$0.003/call, latency 0.6–1.2s, equivalent-to-better quality vs outer-loop). Source: spike's "GO" verdict; sibling candidates 1+2 (XML-doc gen, refactor-summary) ruled NO-GO. |

## Low

| id | pri | deps | do |
|----|-----|------|-----|
| `tool-surface-pagination-or-tool-sets` | Low | none | 167 tools is approaching small-model discovery saturation. MCP `tools/list` already supports cursor pagination per spec; layered on top, named "tool sets" (`navigation`, `refactoring`, `validation`, `analysis`) would let clients enable subsets via `server_info` user-config. Speculative — not yet causing observable friction in the retro window — but the surface continues to grow. Track only; act when (1) a small-model client reports tool-discovery friction, OR (2) surface count crosses a hard threshold (~200 tools). Anchors: `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs`, `src/RoslynMcp.Host.Stdio/Resources/ServerResources.cs`. Regression test shape: defer until acted on. Evidence: surface count drift across recent versions (`server_info.surface.registered.tools` 151 → 167 since v1.27); MCP spec § tools/list pagination. **Weaker evidence — N until small-model discovery friction reported externally.** Source: 2026-05-05 MCP-best-practices comparison §3 rec J. See `ai_docs/reports/20260505_mcp-best-practices-restored-rows.md` for provenance. |

## Defer

<!-- Explicitly parked. Record WHY in the `do` cell. -->

| id | pri | deps | do |
|----|-----|------|-----|
| `validate-locator-preflight-tool` | Defer | re-measurement window after `inv-arg-envelope-schema-hint` (merged 2026-05-05 via PR #483) | Deferred 2026-05-05 by `/backlog-sweep:execute` per the plan-time recommendation in `ai_docs/plans/20260504T203132Z_backlog-sweep/plan.md` §6. After `inv-arg-envelope-schema-hint` shipped, re-measure InvalidArgument-on-locator-tools rate over a 7-day window before deciding whether to ship this tool. If `schemaHint` reduces locator-shape errors ≥80%, mark obsolete; otherwise re-plan and ship. Re-evaluate after 2026-05-12. Anchors: new tool surface in `src/RoslynMcp.Host.Stdio/Tools/SymbolLocatorTool.cs`, reuses existing `src/RoslynMcp.Host.Stdio/Tools/SymbolLocatorFactory.cs` helper from PR #474, catalog registration. Regression test shape: 1 fixture covering valid file/line, valid metadataName, malformed metadataName (parenthesized), unparseable symbolHandle, fully empty locator. Evidence: `ai_docs/reports/20260504T200153Z_roslyn-backed-mcp_roslyn-mcp-multisession-retro.md` §2b row 1 / §4#4 — 6 sessions hit the post-hoc shape error and would have benefited from pre-flight validation. |
| `http-streamable-host-project` | Defer | concrete remote-deployment driver (named users, auth/observability/tenancy plan approved and staffed) | Add `src/RoslynMcp.Host.Http/` as a sibling project that reuses `Core` + `Roslyn`, exposing the same surface over MCP Streamable HTTP transport. Roadmap-aligned but requires multi-week design (auth flows, per-tenant rate limiting, TLS, observability, deployment story). The current local-first product is healthy and the roadmap explicitly punts on these concerns. Re-evaluate when there is a concrete remote-deployment ask with named users / SLA expectations. Anchors: new `src/RoslynMcp.Host.Http/` project; `Core` + `Roslyn` boundaries are already transport-agnostic per `ai_docs/architecture.md`. Evidence: `docs/roadmap.md` § HTTP/SSE Hosting (deferred to a second host project); MCP spec § Streamable HTTP transport (2025-03 stable). Source: 2026-05-05 MCP-best-practices comparison §3 rec H. See `ai_docs/reports/20260505_mcp-best-practices-restored-rows.md` for provenance. |
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
| `ai_docs/plans/20260428T124405Z_backlog-sweep/plan.md` | Backlog sweep (20260428T124405Z). Shipped 7 initiatives across 7 PRs (#467, #468, #471, #473, #474, #476, #477); closed 7 backlog rows. |
| `ai_docs/plans/20260504T203132Z_backlog-sweep/plan.md` | Backlog sweep (20260504T203132Z). Shipped 5 initiatives across 5 PRs (#483, #485, #486, #488, #490); closed 5 backlog rows. Initiative #6 (`validate-locator-preflight-tool`) deferred pending re-measurement of locator-shape error rate after #1's `schemaHint` lands — re-evaluate after 2026-05-12. |
| `ai_docs/plans/20260505T191730Z_backlog-sweep/plan.md` | Backlog sweep (20260505T191730Z). Shipped 16 initiatives across 16 PRs (#502, #503, #504, #505, #508, #509, #510, #511, #515, #516, #517, #519, #520, #526, #528, #530); closed 15 unique backlog rows. Reconcile PRs #527 and #529 kept plan state current. |
| `ai_docs/reports/20260505_mcp-best-practices-restored-rows.md` | Provenance + recovery context for the 14 backlog rows added 2026-05-05 from the MCP-best-practices comparison (rec A–J + Tier 1 follow-on). Maps each row id → recommendation letter; lists cross-cutting evidence sources. Read this first when picking up any of the rec-A through rec-J rows. |
