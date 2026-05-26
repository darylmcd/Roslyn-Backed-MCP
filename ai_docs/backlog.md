# Next work and backlog

<!-- purpose: Open work only; contract for agents syncing backlog on ship. -->
<!-- scope: in-repo -->

**updated_at: 2026-05-26T01:00:00Z**

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
- **GitHub-issue cross-link.** When a row has a corresponding open GitHub Issue, surface the link at the start of the `do` cell so the backlog and issue stay paired. Two flavors:
  - **Reserved for contributor pickup** (`good first issue` / `help wanted` labels): prefix the `do` cell with `**Reserved — [gh #NNN](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/NNN) (good first issue|help wanted); skip in sweeps until contributor pickup.**` — `/backlog-sweep:plan` skips these per its Step 1 hard-skip rule. Remove the marker (or close the row) when a contributor PR lands or when the maintainer reclaims the work.
  - **Tracked-only** (auto-filed audit issues that aren't promoted to a contributor label): prefix the `do` cell with `[gh #NNN](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/NNN) — `. Sweep treats these as normal claimable rows; the implementing PR closes both the issue (via `Fixes #NNN`) and the backlog row.
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
| `release-cut-unreleased-fragments` | High | none | 8 changelog fragments staged in `changelog.d/` since v2.2.2 (workspace cache extraction PR #887, auto-reload decoupling #885, top-5 remediations #884/#883, dead-code cleanup, pragma-suppression fix, plan-dir cleanup, agent-prompt refresh). Cut **v2.3.0** (minor — includes WorkspaceCacheCoordinator extraction and dead-code removal, both behavior-preserving but architecturally meaningful) via `/release-cut minor`; consumes the 8 fragments and tags the release. Anchors: `changelog.d/*.md`, `CHANGELOG.md`, `Directory.Build.props`, `eng/version.json`, `.claude-plugin/server.json`. Regression test shape: `eng/verify-release.ps1` passes; post-tag `server_info` returns the new version. Evidence: `git log v2.2.2..HEAD --oneline` shows 9 unreleased commits; 8 fragments queued. Source: 2026-05-26 discovery-sweep work-search. |
| `workspace-manager-loadintosession-split` | High | none | `WorkspaceManager.LoadIntoSessionAsync` is 217 LOC / CC 17 inside a 1791 LOC class — single monolith hosting MSBuild init, atomic swap, diagnostics queue, and cache hooks. Extract `WorkspaceSessionLoader` (MSBuild creation + global-properties wiring + swap-on-success) and `WorkspaceDiagnosticsSink` (workspace failed-event handler + bounded queue); keep `WorkspaceManager` as orchestration only. Anchors: `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:167,765-984`. Regression test shape: `WorkspaceManagerEvictionTests` + `AutoReloadCascadeHostCrashTests` + `WorkspaceLoadRestoreRaceTests` stay green; new unit covers loader-throws path leaving `session.Workspace` pointing at old non-disposed workspace (autoreload-cascade invariant preserved). Evidence: `mcp__roslyn__get_complexity_metrics` ranks 1-2 (LoadAsync CC 17/156 + LoadIntoSessionAsync CC 17/217); already partial-extracted by PR #887 (WorkspaceCacheCoordinator). Source: 2026-05-26 discovery-sweep refactor audit. |
| `test-coverage-tools-runtestcoveragecore-split` | High | none | `TestCoverageTools.RunTestCoverageCore` is a 216 LOC / CC 20 / 8-param method bundling gating, dotnet invocation, cobertura parsing, and envelope shaping; `ParseAndAggregateCoberturaXml` is also CC 20. Move cobertura aggregation + envelope-with-deprecation construction into a `TestCoverageCoordinator` service under `RoslynMcp.Roslyn`, leaving the tool method as a ~30-line orchestrator. Anchors: `src/RoslynMcp.Host.Stdio/Tools/TestCoverageTools.cs:37-330,353-421`. Regression test shape: `TestCoveragePartialCoverletTests` + `Top10V2RegressionTests` stay green; new service unit covers cobertura aggregation independently from gate+runner orchestration. Evidence: `mcp__roslyn__get_complexity_metrics` rank #4 (CC 20, MI 25.54). Source: 2026-05-26 discovery-sweep refactor audit. |


## Medium

| id | pri | deps | do |
|----|-----|------|-----|
| `merge-or-close-pr-866-known-flakes` | Medium | none | PR #866 (`chore/known-flakes-external-edit-staleness-fs-watcher-flake`) has been open since 2026-05-20 with all CI checks SUCCESS, mergeable status `UNKNOWN`, 6 days stale. Review, rebase if needed, and merge — or close if superseded by intervening flake work. Anchors: `gh pr view 866`, `ai_docs/known-flakes.md`. Evidence: `gh pr list` returns one open PR; statusCheckRollup all-green snapshot from 2026-05-20. Source: 2026-05-26 discovery-sweep work-search. |
| `untracked-gh-issues-backlog-sync` | Medium | none | 4 open GitHub Issues are absent from `ai_docs/backlog.md` despite the Agent-contract `Reserved — [gh #NNN]` / tracked-only requirement: #769 (P3 polish list for firewallanalyzer audit), #611 (`test_run` timeout envelope), #608 (`add_project_reference_preview` self-reference), #606 (`workspace_load` prewarm bool??). Add reserved-for-pickup rows for #611, #608, #606 (good first issue) and a tracked-only row for #769 per the contract. Anchors: `ai_docs/backlog.md` High/Medium/Low tiers. Evidence: `gh issue list --state open` vs `Grep "gh #" ai_docs/backlog.md` returns no matches. Source: 2026-05-26 discovery-sweep work-search. |
| `promotion-scorecard-execution-batch` | Medium | none | Promotion scorecard (2026-05-08) lists 25 promote-ready experimental tools against server 1.34.2; current is 2.2.2 (18 days drift). Re-run scorecard against the 2.2.2 surface first; resolve the duplicate-source-of-truth between `audit-reports/_latest-promotion-scorecard.json` (serverVersion 1.38.1) and `ai_docs/audit-reports/_latest-promotion-scorecard.json` (serverVersion 1.34.2) by picking one canonical location and deleting the other; then ship tier promotions in bounded batches via the `/promote-tier` skill. Anchors: `audit-reports/_latest-promotion-scorecard.json`, `ai_docs/audit-reports/_latest-promotion-scorecard.json`, `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.*.cs`. Clusters: brainstorm BRAIN-007 (stable-surface promotion scorecard system). Evidence: scorecard `summary.promoteReady=25`; no recent commits matching "promote"/"tier" in last 6 weeks. Source: 2026-05-26 discovery-sweep work-search + brainstorm. |
| `namespace-addtype-helper-dedup` | Medium | none | `AddType` namespace-walker (recursive generic args + array element type extraction) is duplicated verbatim (similarity=1.0, 40 lines) between `CrossProjectRefactoringService` and `InterfaceExtractionService`. Extract to `TypeNamespaceWalker.Collect(HashSet<string>, ITypeSymbol, string? ownNamespace)` under `Helpers/`; both services delegate. Anchors: `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:899-939`; `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs:367-407`. Regression test shape: `ExtractInterfaceSemanticUsingsTests` + `TypeMoveTests` / cross-project namespace-recompute tests cover the walker; add a single unit for nested generics (`Task<List<Foo>>`). Evidence: `mcp__roslyn__find_duplicated_methods` normalizedHash 5ead23709ee96641 / 67e8c5784e6d0a3a. Source: 2026-05-26 discovery-sweep refactor audit. |
| `mutation-analysis-classify-extract` | Medium | none | `MutationAnalysisService.ClassifyTypeUsageAfterWalk` is CC 21 / 31 LOC in a 949 LOC file — branch-dense classifier ripe for table-driven dispatch. Replace inline branch tree with a `(Predicate, Classification)` table walked by a helper that returns the first match, or split per-kind classifier methods. Anchors: `src/RoslynMcp.Roslyn/Services/MutationAnalysisService.cs:635-680`. Regression test shape: `MutationAnalysisSideEffectsTests` + the existing `find_type_mutations` regressions stay green. Evidence: `mcp__roslyn__get_complexity_metrics` rank #3 (CC 21, MI 49.65); recent compound-scope fix touched this file (commit 649e1f7). Source: 2026-05-26 discovery-sweep refactor audit. |
| `registry-install-readiness-scorecard` | Medium | none | Implement MCP registry / install-readiness scorecard per brainstorm BRAIN-002 (implementation-ready, near horizon, first slice named — brainstorm's own §1 Recommended Next Move). First slice: a `verify-release.ps1` check (or sibling script) that validates `.claude-plugin/server.json` against the MCP registry schema, confirms plugin metadata round-trips through the Claude Code plugin loader, and reports install-readiness as a structured artifact consumable by `/publish-preflight`. Anchors: `.claude-plugin/server.json`, `eng/verify-release.ps1`, `audit-reports/application-brainstorm.md §BRAIN-002`. Regression test shape: existing publish-preflight stays green; new check fails closed on missing/malformed registry fields. Evidence: brainstorm `Recommended Next Move §1`. Source: 2026-05-26 discovery-sweep brainstorm BRAIN-002. |

## Low

| id | pri | deps | do |
|----|-----|------|-----|
| `tool-surface-pagination-or-tool-sets` | Low | none | 171 tools is approaching small-model discovery saturation, but current evidence points to a routing/steering problem more than a raw-count problem. Now that `recommend_workflow` exists, wait for fresh post-router evidence before adding tool-set catalog resources (for example `roslyn://server/catalog/tool-sets` and `roslyn://server/catalog/tools/{toolSet}/{offset}/{limit}`) that expose bounded subsets such as `navigation`, `refactoring`, `validation`, and `analysis` without hiding tools from clients that can handle the full surface. Do not change MCP tool registration or default `tools/list` visibility in this row unless the implementation note proves the client supports that safely. Anchors: `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs`, `src/RoslynMcp.Host.Stdio/Resources/ServerResources.cs`, `tests/RoslynMcp.Tests/SurfaceCatalogTests.cs`. Regression test shape: catalog summary advertises the tool-set resource; each named set returns only matching categories with offset/limit/hasMore metadata; unknown set returns structured `InvalidArgument`; existing full and paginated catalog resources remain unchanged. Evidence: surface count drift across recent versions (`server_info.surface.registered.tools` 151 -> 171 since v1.27); MCP spec tools/list pagination; 2026-05-20 no-context subagent probe showed correctly steered agents chose Roslyn primitives without needing a full catalog dump. **Weaker evidence - N until small-model discovery friction is reported after the router lands externally.** Source: 2026-05-05 MCP-best-practices comparison §3 rec J plus 2026-05-20 agent-view review. |
| `test-git-fixture-helper-dedup` | Low | none | `RunGit` + `StageFixtureBaseline` helpers are duplicated verbatim (similarity=1.0, 30+38 lines) across `ValidateRecentGitChangesTests` and `ValidateWorkspaceChangeTrackerReconcileTests`. Extract a `GitFixtureRunner` static under `tests/RoslynMcp.Tests/Support/`; both test classes delegate. Anchors: `tests/RoslynMcp.Tests/ValidateRecentGitChangesTests.cs:257-325`; `tests/RoslynMcp.Tests/ValidateWorkspaceChangeTrackerReconcileTests.cs:280-348`; new `tests/RoslynMcp.Tests/Support/GitFixtureRunner.cs`. Regression test shape: both originating test classes stay green after delegation. Evidence: `mcp__roslyn__find_duplicated_methods` normalizedHash 5157e720eefee8e1 + d4c1d337fbd2629d. Source: 2026-05-26 discovery-sweep refactor audit. |
| `brainstorm-report-refresh-shipped-overlaps` | Low | none | `audit-reports/application-brainstorm.md` cites 4 "exact open" backlog rows that are stale: BRAIN-001 cites shipped `workspace-fork-apply-primitive` (the `workspace_fork_apply` tool is in `ValidationBundleTools.cs`); BRAIN-003 cites shipped `plugin-package-files-allowlist` (per `changelog.d/workspace-fork-apply-tool.md`); BRAIN-004 cites absent `surface-test-resumability-cleanup-skill`; BRAIN-006 cites absent `initiative-executor-roslyn-tool-discovery-brief`. Mark BRAIN-001/003 as `implemented`; re-derive BRAIN-004/006 against the current backlog; refresh the Status / Backlog-overlap section. Anchors: `audit-reports/application-brainstorm.md:28-126`. Evidence: current backlog grep for those row IDs returns none. Source: 2026-05-26 discovery-sweep work-search. |

## Defer

<!-- Explicitly parked. Record WHY in the `do` cell. -->

| id | pri | deps | do |
|----|-----|------|-----|
| `http-streamable-host-project` | Defer | concrete remote-deployment driver (named users, auth/observability/tenancy plan approved and staffed) | Add `src/RoslynMcp.Host.Http/` as a sibling project that reuses `Core` + `Roslyn`, exposing the same surface over MCP Streamable HTTP transport. Roadmap-aligned but requires multi-week design (auth flows, per-tenant rate limiting, TLS, observability, deployment story). The current local-first product is healthy and the roadmap explicitly punts on these concerns. Re-evaluate when there is a concrete remote-deployment ask with named users / SLA expectations. Anchors: new `src/RoslynMcp.Host.Http/` project; `Core` + `Roslyn` boundaries are already transport-agnostic per `ai_docs/architecture.md`. Evidence: `docs/roadmap.md` § HTTP/SSE Hosting (deferred to a second host project); MCP spec § Streamable HTTP transport (2025-03 stable). Source: 2026-05-05 MCP-best-practices comparison §3 rec H. |
| `workspace-process-pool-or-daemon` | Defer | future worse-profile evidence | Representative 227-project OrchardCore profile captured on 2026-04-26 did not justify daemon/process-pool implementation: `workspace_load` P95 was 44.85s, `symbol_search` P95 was 1.18s, and `find_references` P95 was 997ms, all below `docs/large-solution-profiling-baseline.md` thresholds. Keep this deferred unless a larger/worse customer-scale profile or daily-use evidence shows `workspace_load` / reload P95 blocking work after `workspace_warm`; then produce a bounded design note comparing daemon, process-pool, and shared-workspace approaches, including lifecycle and failure-isolation hooks. Evidence: `docs/large-solution-profiling-baseline.md` recorded OrchardCore run; local raw artifacts under `artifacts/large-solution-profiling/20260426T212443Z/`. |

## Refs

| Path | Role |
|------|------|
| `ai_docs/planning_index.md` | Planning router and scope boundary |
| `ai_docs/workflow.md` | Branch/PR workflow and backlog-closure rule |
| `ai_docs/prompts/backlog-sweep-plan.md` | Planner prompt for batching backlog rows into shippable initiatives |
| `ai_docs/bootstrap-read-tool-primer.md` | Self-edit session read-only tool primer (Roslyn-MCP read-side tools to prefer over Bash/Grep) |
| `ai_docs/runtime.md` | Bootstrap scope policy — distinguishes main-checkout self-edit (no `*_apply`) from worktree/parallel-subagent sessions |
| `docs/large-solution-profiling-baseline.md` | Evidence gate for daemon/process-pool performance work |
| `ai_docs/procedures/deep-review-backlog-intake.md` | Intake procedure for future audit batches |
| `review-inbox/` | Staging folder for the NEXT audit batch (flat directory; `/backlog-intake` reads here) |
| `review-inbox/archive/<batch-ts>/` | Processed audit/retro/promotion batches; delete after all actionable items are shipped, rejected, or summarized |
| `ai_docs/plans/20260522T132800Z_top5-remediation/plan.md` | Shipped 1 code initiative and closed 1 measurement no-go |
