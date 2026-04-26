# Next work and backlog

<!-- purpose: Open work only; contract for agents syncing backlog on ship. -->
<!-- scope: in-repo -->

**updated_at:** 2026-04-26T20:38:05Z

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
| `pretooluse-apply-preview-token-invariant` | Medium | — | Still valid after the 2026-04-26 plan cleanup: `hooks/hooks.json` still protects `mcp__roslyn__*_apply` through transcript-scanning prompt text, and the 2026-04-26 multi-session retro reconfirms valid preview-token flows blocked as `preview-token-apply-gate` across sessions 34ca7601, 7e2befc9, 1d93dd85, d8763f40, and aef3fb58. Re-review before implementation because many apply handlers already accept `previewToken`; next deliverable is a bounded design/plan deciding whether to replace the hook with deterministic token validation, remove the transcript hook in favor of existing tool-level token requirements, or split remaining gaps by tool family. Anchors: `hooks/hooks.json`, `src/RoslynMcp.Roslyn/Services/PreviewStore.cs`, `src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs`, `src/RoslynMcp.Host.Stdio/Tools/*Tools.cs`. Evidence: `ai_docs/reports/20260426T162740Z_roslyn-backed-mcp_roslyn-mcp-multisession-retro.md` §4. |

## Low

| id | pri | deps | do |
|----|-----|------|-----|

## Defer

<!-- Explicitly parked. Record WHY in the `do` cell. -->

| id | pri | deps | do |
|----|-----|------|-----|
| `workspace-process-pool-or-daemon` | Defer | `large-solution profile` | Still valid after the 2026-04-26 plan cleanup: `docs/large-solution-profiling-baseline.md` only contains the small sample-solution smoke and explicitly says it is not a substitute for 50+ project profiling. Do not start a daemon/process-pool implementation blindly. First capture representative 50+ project timings; only if `workspace_load` / reload P95 still blocks daily use after `workspace_warm` should the next plan produce a bounded design note comparing daemon, process-pool, and shared-workspace approaches, including lifecycle and failure-isolation hooks. Deferred until profile evidence justifies the work. |

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
| `ai_docs/reports/20260426T162740Z_roslyn-backed-mcp_roslyn-mcp-multisession-retro.md` | Multi-session retro source; §4 findings were deduped against current backlog and merged PR history on 2026-04-26, leaving only `preview-token-apply-gate` active via `pretooluse-apply-preview-token-invariant`. |
| `review-inbox/` | Staging folder for the NEXT audit batch (flat directory; `/backlog-intake` reads here). |
| `review-inbox/archive/<batch-ts>/` | Processed audit/retro/promotion batches — one subdirectory per successful intake. Keep until every row sourced from a batch is closed or superseded, then delete that subdirectory. |
