# Next work and backlog

<!-- purpose: Open work only; contract for agents syncing backlog on ship. -->
<!-- scope: in-repo -->

**updated_at:** 2026-05-05T15:35:00Z

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
| `audit-deep-skill-migration` | Medium | none | Convert the user-global slash-command `~/.claude/commands/audit-deep.md` into a plugin-shipped skill at `skills/audit-deep/`. Today the skill points at `<repo>/ai_docs/prompts/deep-review-and-refactor.md` so each consuming C# repo must own a current copy of an 872-line prompt; that drifts. Move the prompt into the skill so it ships with the plugin. Apply the open skill bugs during migration: (B1) soften the read-only hard rule to "read-only against audited repo's main; Phase 6 mutations confined to a disposable worktree the prompt creates"; (B2) align the `mode` vocabulary with the prompt's `full|promotion-only|read-only` (drop `focused`, or document it as fallback-only); (B3) prune the dead `no-holds-barred-audit.md` resolution branch; (B4/B5) require the Roslyn MCP server (`mcp__roslyn__server_info`) — halt instead of running a generic non-MCP fallback; (S2) delegate Phase 0's drift-detection step to `/surface-audit` when available; (S5) add `scripts/archive-old-reports.ps1` to move `ai_docs/audit-reports/*.md` older than N days into `archive/<YYYY>/`. Anchors: new `skills/audit-deep/SKILL.md`, new `skills/audit-deep/prompts/{full,promotion-only,read-only}.md` (split the 872-line monolith), new `skills/audit-deep/scripts/archive-old-reports.ps1`, plus deprecation note in `~/.claude/commands/audit-deep.md` (consumer-side concern; not edited in this repo). Regression test shape: 1 fixture verifying SKILL.md frontmatter parity + tool-reference validity against the live catalog (matches Phase 16b's per-skill checks). Evidence: 2026-05-05 audit-deep deep-dive (this session); identified 10 guidance gaps fixed in PR #494 via Wave 1 and 6 architectural issues queued here for Wave 2. **Rule 3 structural-unit exemption: 4 units (skill + 3 mode prompts).** |
| `audit-deep-subagent-orchestration` | Medium | `audit-deep-skill-migration` | Refactor the Phase-runner inside `audit-deep` to offload context-heavy phases to subagents, leaving the main agent as orchestrator. Today the 872-line prompt runs inline; a `mode=full` run can take 90–180min and consume large chunks of the orchestrator's context window for raw tool output. Per the prompt's principle #1 ("delegate long-running/log-heavy validation to subagents"), formalize: Phase 1 (broad diagnostics scan), Phase 2 (metrics), Phase 8 (build/test), Phase 8b (concurrency stress) become subagent tasks returning structured summaries to the orchestrator. Phase 6 (refactoring) and the preview/apply chains stay inline (workspace-version-sensitive per principle #3). Anchors: new `.claude/agents/audit-phase-runner.md` for the subagent definition, plus orchestration logic in `skills/audit-deep/SKILL.md` (post-migration). Regression test shape: 1 fixture with a small fixture solution exercising the orchestrator-subagent handoff for a single phase, asserting the orchestrator receives a structured-summary message back, not raw tool output. Evidence: 2026-05-05 audit-deep deep-dive (this session) — context-budget concern flagged as principle #12 in the existing prompt; mature offload pattern not yet implemented. **Depends on `audit-deep-skill-migration` because the subagent invocation needs to live in the post-migration skill structure.** |

## Low

| id | pri | deps | do |
|----|-----|------|-----|
| `audit-deep-release-cut-promotion-gate` | Low | `audit-deep-skill-migration` | Wire `/audit-deep mode=promotion-only` into the `/release-cut` pipeline as an optional promotion gate. (1) Have `audit-deep` write a machine-readable `ai_docs/audit-reports/_latest-promotion-scorecard.json` alongside the human-readable report, with `generated_at`, per-tool `recommendation`, and evidence counts. (2) `/publish-preflight` (consumed by `/release-cut`) reads the scorecard: if older than 30 days → WARN "scorecard stale; consider re-running"; if any `promote` recommendations → prompt user to promote in this release; otherwise silent. (3) Add a new `/roslyn-mcp:promote-tier <tool> stable` skill that flips the catalog tier marker per accepted recommendation. Anchors: new write logic in `skills/audit-deep/SKILL.md`, integration hook in `.claude/skills/publish-preflight/SKILL.md`, new `skills/promote-tier/SKILL.md`. Regression test shape: 2 tests — (a) scorecard JSON shape parses cleanly; (b) `publish-preflight` correctly flags a stale scorecard and skips silently when fresh-with-no-promotions. Evidence: 2026-05-05 audit-deep deep-dive (this session) S6 discussion; today promotion is implicit with no paper trail. |

## Defer

<!-- Explicitly parked. Record WHY in the `do` cell. -->

| id | pri | deps | do |
|----|-----|------|-----|
| `validate-locator-preflight-tool` | Defer | re-measurement window after `inv-arg-envelope-schema-hint` (merged 2026-05-05 via PR #483) | Deferred 2026-05-05 by `/backlog-sweep:execute` per the plan-time recommendation in `ai_docs/plans/20260504T203132Z_backlog-sweep/plan.md` §6. After `inv-arg-envelope-schema-hint` shipped, re-measure InvalidArgument-on-locator-tools rate over a 7-day window before deciding whether to ship this tool. If `schemaHint` reduces locator-shape errors ≥80%, mark obsolete; otherwise re-plan and ship. Re-evaluate after 2026-05-12. Anchors: new tool surface in `src/RoslynMcp.Host.Stdio/Tools/SymbolLocatorTool.cs`, reuses existing `src/RoslynMcp.Host.Stdio/Tools/SymbolLocatorFactory.cs` helper from PR #474, catalog registration. Regression test shape: 1 fixture covering valid file/line, valid metadataName, malformed metadataName (parenthesized), unparseable symbolHandle, fully empty locator. Evidence: `ai_docs/reports/20260504T200153Z_roslyn-backed-mcp_roslyn-mcp-multisession-retro.md` §2b row 1 / §4#4 — 6 sessions hit the post-hoc shape error and would have benefited from pre-flight validation. |
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
