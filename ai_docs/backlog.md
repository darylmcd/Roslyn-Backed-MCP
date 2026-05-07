# Next work and backlog

<!-- purpose: Open work only; contract for agents syncing backlog on ship. -->
<!-- scope: in-repo -->

**updated_at:** 2026-05-07T19:15:00Z

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
| `change-signature-reorder-preview` | Low | none | Add first-class support for `change_signature_preview(op=reorder)` or prove that `replace_invocation_preview` plus staged remove/add is the intended permanent path and update the tool description accordingly. Agents still hit the explicit "Parameter reordering is not supported" refusal for callsite-sensitive signature migrations even though adjacent callsite rewrite primitives exist. Anchors: `src/RoslynMcp.Roslyn/Services/ChangeSignatureService.cs`, `src/RoslynMcp.Host.Stdio/Tools/ChangeSignatureTools.cs`, `tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs`, `src/RoslynMcp.Roslyn/Services/BulkRefactoringService.cs`. Regression test shape: reorder two parameters on a sample method and update positional/named callsites, or a no-code docs/test assertion that the permanent workaround is explicit and covered. Evidence: 2026-04-26 multi-session retro sessions `34ca7601`, `d8763f40`, `dd0a7e48`; `replace_invocation_preview` later covered new-method argument reorder but not same-method signature reorder. |
| `parameter-object-preview-tool` | Low | none | Implement the v1 `parameter_object_preview` MCP tool per the design note `ai_docs/items/parameter-object-preview-design.md` (read first — it is the canonical contract). v1 generates a positional `record` DTO and rewrites all `M(...)` call sites to wrap the grouped arguments in `new Dto(...)`. Refuses default-value call sites, `ref`/`out`/`in`/`params` parameters, the `this` parameter on extension methods, and local-function targets; warns on reflective sites. Cross-project rewrites supported only when every caller-project already references the DTO project (no auto-`<ProjectReference>` insertion). Anchors (4 structural units / 6 prod files): NEW `src/RoslynMcp.Core/Services/IParameterObjectService.cs`, NEW `src/RoslynMcp.Core/Models/ParameterObjectPreviewRequest.cs`, NEW `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs`, NEW `src/RoslynMcp.Host.Stdio/Tools/ParameterObjectTools.cs`, EDIT `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Refactoring.cs`, EDIT `src/RoslynMcp.Host.Stdio/ServiceCollectionExtensions.cs`. Mandatory addenda: `tests/RoslynMcp.Tests/TestBase.cs`, `tests/RoslynMcp.Tests/TestInfrastructure/TestServiceContainer.cs`, `README.md` surface count. `toolPolicy: edit-only`. `productionFilesTouched: 9`. Regression test shape: 1 file `tests/RoslynMcp.Tests/ParameterObjectPreviewTests.cs` covering: (1) positional grouping single-project, (2) named/mixed call sites resolved to semantic order, (3) default-value site refused with structured warning, (4) `ref`/`out` parameter refused with `InvalidArgument`, (5) cross-project rewrite when ProjectReference exists, (6) cross-project refused when reference missing, (7) `apply_refactoring` redeems token and writes new file plus rewritten call sites to disk. Evidence: 2026-05-07 design pass closing `parameter-object-preview-design`; original recurrence evidence 2026-04-26 multi-session retro sessions `34ca7601`, `d8763f40`, `dd0a7e48`. |
| `dry-run-preview-side-effect-audit` | Low | none | Audit whether any `_preview` tools mutate workspace caches or trigger reload-like side effects in plan-only use. Evidence is weak and may be a documentation issue, so the next deliverable is an investigation note, not a tool change. If no side effects are confirmed, update preview-tool docs/descriptions and close obsolete; if confirmed, split a targeted `dryRun` implementation row for the affected tool family. Anchors: `src/RoslynMcp.Roslyn/Services/RefactoringService.cs`, `src/RoslynMcp.Roslyn/Services/EditService.cs`, `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`, `src/RoslynMcp.Host.Stdio/Tools/RefactoringTools.cs`, `src/RoslynMcp.Host.Stdio/Tools/TypeMoveTools.cs`. Regression test shape: focused tests proving representative preview calls do not change workspace version/cache state, or one failing case pinned before a tool-specific fix. Evidence: 2026-05-04 multi-session retro sessions `28e53529`, `b8d60dae`, `eac7f094`. **Weaker evidence — N until audit confirms actual side effects.** |
| `promotion-scorecard-20260427-review` | Low | none | Review the 2026-04-27 promotion-only audit recommendations now that `/publish-preflight` emits machine-readable scorecards and `.claude/skills/promote-tier/` exists. The audit recommended promoting `workspace_warm`, `find_type_consumers`, `probe_position`, `trace_exception_flow`, `find_duplicate_helpers`, `find_dead_locals`, `find_dead_fields`, `symbol_impact_sweep`, `semantic_grep`, `validate_workspace`, `validate_recent_git_changes`, `test_reference_map`, `get_prompt_text`, `server_catalog_tools_page`, and `server_catalog_prompts_page`; current catalog entries remain experimental, so this batch needs an explicit accept/reject decision. Next deliverable: create a bounded decision note under `ai_docs/items/promotion-scorecard-20260427-review.md` and split any accepted tier flips into category-sized implementation rows; keep writer-side `needs-more-evidence` entries out of scope. Anchors: `docs/experimental-promotion-analysis.md`, `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Analysis.cs`, `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Resources.cs`, `.claude/skills/promote-tier/SKILL.md`. Regression test shape: docs-only decision note plus backlog split/no-go sync. Evidence: 2026-04-27 promotion-only audit §12 (review-inbox archive removed after this row captured the actionable set); later infrastructure shipped via PR #496 but did not apply or reject this exact candidate set. |
| `tool-surface-pagination-or-tool-sets` | Low | none | 167 tools is approaching small-model discovery saturation. MCP `tools/list` already supports cursor pagination per spec; layered on top, named "tool sets" (`navigation`, `refactoring`, `validation`, `analysis`) would let clients enable subsets via `server_info` user-config. Speculative — not yet causing observable friction in the retro window — but the surface continues to grow. Track only; act when (1) a small-model client reports tool-discovery friction, OR (2) surface count crosses a hard threshold (~200 tools). Anchors: `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs`, `src/RoslynMcp.Host.Stdio/Resources/ServerResources.cs`. Regression test shape: defer until acted on. Evidence: surface count drift across recent versions (`server_info.surface.registered.tools` 151 → 167 since v1.27); MCP spec § tools/list pagination. **Weaker evidence — N until small-model discovery friction reported externally.** Source: 2026-05-05 MCP-best-practices comparison §3 rec J. See `ai_docs/reports/20260505_mcp-best-practices-restored-rows.md` for provenance. |

## Defer

<!-- Explicitly parked. Record WHY in the `do` cell. -->

| id | pri | deps | do |
|----|-----|------|-----|
| `validate-locator-preflight-tool` | Defer | re-measurement window after `inv-arg-envelope-schema-hint` (merged 2026-05-05 via PR #483) | Deferred 2026-05-05 after `inv-arg-envelope-schema-hint` shipped because the schema hint may make the standalone preflight tool obsolete. Re-measure InvalidArgument-on-locator-tools rate over a 7-day window before deciding whether to ship this tool. If `schemaHint` reduces locator-shape errors >=80%, mark obsolete; otherwise re-plan and ship. Re-evaluate after 2026-05-12. Anchors: new tool surface in `src/RoslynMcp.Host.Stdio/Tools/SymbolLocatorTool.cs`, reuses existing `src/RoslynMcp.Host.Stdio/Tools/SymbolLocatorFactory.cs` helper from PR #474, catalog registration. Regression test shape: 1 fixture covering valid file/line, valid metadataName, malformed metadataName (parenthesized), unparseable symbolHandle, fully empty locator. Evidence: 2026-05-04 multi-session retro sessions `dd0a7e48`, `5687cbf9`, `f71cbc02`, `eac7f094`, `28e53529`, `b093b4b1` hit post-hoc locator-shape errors that could benefit from pre-flight validation. |
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
| `review-inbox/archive/<batch-ts>/` | Processed audit/retro/promotion batches — one subdirectory per successful intake. Delete a batch after every actionable item is either shipped, explicitly rejected as stale, or summarized in current backlog rows. |
| `ai_docs/reports/20260505_mcp-best-practices-restored-rows.md` | Provenance + recovery context for the 14 backlog rows added 2026-05-05 from the MCP-best-practices comparison (rec A–J + Tier 1 follow-on). Maps each row id → recommendation letter; lists cross-cutting evidence sources. Read this first when picking up any of the rec-A through rec-J rows. |
