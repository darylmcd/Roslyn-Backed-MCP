# Next work and backlog

<!-- purpose: Open work only; contract for agents syncing backlog on ship. -->
<!-- scope: in-repo -->

**updated_at: 2026-05-22T19:06:35Z**

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


## Medium

| id | pri | deps | do |
|----|-----|------|-----|

## Low

| id | pri | deps | do |
|----|-----|------|-----|
| `tool-surface-pagination-or-tool-sets` | Low | none | 171 tools is approaching small-model discovery saturation, but current evidence points to a routing/steering problem more than a raw-count problem. Now that `recommend_workflow` exists, wait for fresh post-router evidence before adding tool-set catalog resources (for example `roslyn://server/catalog/tool-sets` and `roslyn://server/catalog/tools/{toolSet}/{offset}/{limit}`) that expose bounded subsets such as `navigation`, `refactoring`, `validation`, and `analysis` without hiding tools from clients that can handle the full surface. Do not change MCP tool registration or default `tools/list` visibility in this row unless the implementation note proves the client supports that safely. Anchors: `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs`, `src/RoslynMcp.Host.Stdio/Resources/ServerResources.cs`, `tests/RoslynMcp.Tests/SurfaceCatalogTests.cs`. Regression test shape: catalog summary advertises the tool-set resource; each named set returns only matching categories with offset/limit/hasMore metadata; unknown set returns structured `InvalidArgument`; existing full and paginated catalog resources remain unchanged. Evidence: surface count drift across recent versions (`server_info.surface.registered.tools` 151 -> 171 since v1.27); MCP spec tools/list pagination; 2026-05-20 no-context subagent probe showed correctly steered agents chose Roslyn primitives without needing a full catalog dump. **Weaker evidence - N until small-model discovery friction is reported after the router lands externally.** Source: 2026-05-05 MCP-best-practices comparison §3 rec J plus 2026-05-20 agent-view review. |

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
| `ai_docs/plans/20260513T140000Z_backlog-sweep/plan.md` | Shipped 1 initiative, 1 PR, 2 rows closed |
| `ai_docs/plans/20260513T010000Z_backlog-sweep/plan.md` | Shipped 10 initiatives, 10 PRs (#716–#726), 10 rows closed |
| `ai_docs/plans/20260516T200033Z_backlog-sweep/plan.md` | Shipped 15 initiatives, 15 PRs (#779–#798), 15 rows closed |
| `ai_docs/plans/20260517T025647Z_backlog-sweep/plan.md` | Shipped 3 initiatives, 3 PRs (#803–#805), 3 rows closed |
| `ai_docs/plans/20260517T235058Z_backlog-sweep/plan.md` | Shipped 10 initiatives, 10 PRs (#810–#819), 11 rows closed |
| `ai_docs/plans/20260518T221744Z_backlog-sweep/plan.md` | Shipped 15 initiatives, 15 PRs (#823–#838), 15 rows closed |
| `ai_docs/plans/20260519T145945Z_backlog-sweep/plan.md` | Shipped 6 initiatives, 6 PRs (#842–#847), 6 rows closed |
| `ai_docs/plans/20260519T193650Z_backlog-sweep/plan.md` | Shipped 15 initiatives, 15 PRs (#852–#871), 15 rows closed |
| `ai_docs/plans/20260522T132800Z_top5-remediation/plan.md` | Shipped 1 code initiative and closed 1 measurement no-go |
