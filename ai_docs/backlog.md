# Next work and backlog

<!-- purpose: Open work only; contract for agents syncing backlog on ship. -->
<!-- scope: in-repo -->

**updated_at:** 2026-04-26T16:00:00Z

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
| `ci-test-parallelization-audit` | Medium | — | Refresh the old test-parallelization audit around the real residual issue: `eng/verify-release.ps1` still has 3 pre-existing cleanup-race flakes even though the `[DoNotParallelize]` sweep is already done. Start by reproducing and naming the failing tests, then localize whether the race sits in `tests/RoslynMcp.Tests/AssemblyCleanup.cs`, `tests/RoslynMcp.Tests/TestBase.cs`, or another teardown path before planning the fix plus a repeated-release stress gate (`eng/verify-release-stress.ps1` / CI). |
| `pretooluse-apply-preview-token-invariant` | Medium | — | The `PreToolUse:mcp__roslyn__*_apply` hook at `hooks/hooks.json` still scans the session transcript for a matching `*_preview` and false-positive-blocks `*_apply` 8 times across 4 refactor sessions in the 2026-04-10→04-24 retro (`format_range_apply`, `extract_interface_apply`, `create_file_apply`, `fix_all_apply`, `move_type_to_file_apply`) — prior work (PR #234, PR #230) softened cold-subagent and composite-preview cases but the structural scan remains. Return a short-lived `previewToken` on every `*_preview` response and require it on every `*_apply`; rewrite the hook to validate by token presence instead of transcript grep. Anchors: `src/RoslynMcp.Roslyn/Services/PreviewStore.cs` (already issues tokens for most previews — extend to the remaining preview tools), `src/RoslynMcp.Host.Stdio/Tools/*Apply*.cs` (add required `previewToken` parameter on every `*_apply`), `hooks/hooks.json` (flip scan → token compare). Distinct from `format-range-apply-preview-token-lifetime` (that row tunes token lifetime; this row tightens the apply-gate invariant). |
| `find-type-consumers-file-granularity-rollup` | Medium | — | Agents looking for "which files touch this type" fall back to Grep (≥5 refactor sessions in the 2026-04-10→04-24 retro: 57a0d696, bfe7443f, ac8b4d93, 08adc1f1, b3e4cb62) because `find_references` returns per-site hits with no per-file rollup. Add `find_type_consumers(typeName, limit) → [{filePath, kinds: [using|ctor|inherit|field|local], count}]` on top of the existing reference index. Anchors: `src/RoslynMcp.Roslyn/Services/ReferenceService.cs` (reuse reference index), new tool in `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`. Shape mirrors what downstream rename/move/delete planning flows already want. |

## Low

| id | pri | deps | do |
|----|-----|------|-----|
| `skills-remove-server-heartbeat-polling` | Low | — | The `/roslyn-mcp:complexity` skill (and any wait-loop in sibling skills under `.claude/plugins/roslyn-mcp/skills/`) polls `mcp__roslyn__server_heartbeat` in a loop — one session in the 2026-04-10→04-24 retro (b5464409) logged 78 heartbeat calls inside a single `/complexity` invocation; two others logged 8 and 6. Audit the shipped skill prompts and replace poll-until-ready with a single `server_info` check (or `workspace_status` + bounded backoff) so transcripts aren't inflated by heartbeat noise. Anchors: `.claude-plugin/plugin.json` (plugin manifest at repo root) + `skills/complexity/SKILL.md`, cross-check with `skills/*/SKILL.md` for the same pattern. |
| `tool-annotation-population-audit` | Low | — | MCP best-practices: tools should declare `readOnlyHint` / `destructiveHint` / `idempotentHint` / `openWorldHint` annotations so clients (including the PreToolUse apply-gate hook) can route decisions off structured metadata instead of tool-name patterns. Verify via `server_info.surface` + a spot-check on `src/RoslynMcp.Host.Stdio/Tools/*Apply*.cs` and `*Preview*.cs` whether the `[McpServerTool]` attributes already carry these. If missing, populate: every `*_apply` → `destructiveHint: true`; every `*_preview` / read-shape tool → `readOnlyHint: true`; workspace-scoped tools → `openWorldHint: false`. Close-immediately if already populated. |
| `response-format-json-markdown-parameter` | Low | — | MCP best-practices: summary-shaped tools should support `response_format: "json" \| "markdown"` (default `json` preserves wire compatibility). Current state: all 161 tools return JSON only. Highest-value candidates (heavy context cost, human-readable reframing): `server_info`, `workspace_list`, `workspace_status`, `validate_workspace`, `symbol_impact_sweep`, `project_diagnostics(summary)`. Add the parameter + markdown formatter per tool; start with `validate_workspace` (typically 120-line JSON → ~30-line table). Anchors: `src/RoslynMcp.Host.Stdio/Tools/ServerTools.cs`, `WorkspaceTools.cs`, `ValidationBundleTools.cs`. |
| `posttool-verify-skills-on-shipped-skill-edits` | Low | — | `eng/verify-skills-are-generic.ps1` is gated by `just ci` + `verify-release.ps1`, so an `ai_docs/` / `eng/` / `backlog.md` leak inside a `./skills/**/SKILL.md` is caught at CI, not at edit-time — round-trip waste per retro observation. Add a PostToolUse hook on `Edit\|Write\|MultiEdit` matching `./skills/**` that runs `pwsh eng/verify-skills-are-generic.ps1` and surfaces violations the same turn. Timeout bounded (the script is <1s). |
| `semantic-grep-identifier-scoped-search` | Low | — | Agents want to grep inside C# code but exclude string/comment matches — current Grep returns hits inside CHANGELOG, markdown, and string literals (3 Roslyn-self-audit sessions in the 2026-04-10→04-24 retro: 57a0d696, 08adc1f1, cf2d0b85). Add `semantic_grep(pattern, scope=identifiers|strings|comments|all, projectFilter?) → [{filePath, line, column, tokenKind, snippet}]` as a thin wrapper on `SyntaxTree.DescendantTokens` + `SyntaxToken.Kind()` + regex match. Anchors: new service under `src/RoslynMcp.Roslyn/Services/`, new tool in `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs`. Weaker evidence — Low until an external-repo session reproduces the pattern. |

## Defer

<!-- Explicitly parked. Record WHY in the `do` cell. -->

| id | pri | deps | do |
|----|-----|------|-----|
| `workspace-process-pool-or-daemon` | Defer | `large-solution profile` | Do not start a daemon/process-pool implementation blindly. First capture representative 50+ project timings per `docs/large-solution-profiling-baseline.md`; only if `workspace_load` / reload P95 still blocks daily use after `workspace_warm` should the next plan produce a bounded design note comparing daemon, process-pool, and shared-workspace approaches, including lifecycle and failure-isolation hooks. Deferred until profile evidence justifies the work. |

## Refs

| Path | Role |
|------|------|
| `ai_docs/planning_index.md` | Planning router and scope boundary |
| `ai_docs/workflow.md` | Branch/PR workflow and backlog-closure rule |
| `ai_docs/prompts/backlog-sweep-plan.md` | Planner prompt; enforces per-initiative Rule 1 (bundle only on shared code path) / Rule 3 (≤4 prod files) / Rule 3b (toolPolicy) / Rule 4 (≤3 test files) / Rule 5 (≤80K context). Each row here is sized for one initiative. |
| `ai_docs/prompts/backlog-sweep-execute.md` | Executor companion; consumes the `state.json` the planner emits and vets each initiative against Rules 3/4/5 before starting work. |
| `ai_docs/bootstrap-read-tool-primer.md` | Self-edit session read-only tool primer (Roslyn-MCP read-side tools to prefer over Bash/Grep). |
| `ai_docs/runtime.md` | Bootstrap scope policy — distinguishes main-checkout self-edit (no `*_apply`) from worktree/parallel-subagent sessions. |
| `ai_docs/plans/20260422T170500Z_test-parallelization-audit/plan.md` | Dedicated phased plan for `ci-test-parallelization-audit`. |
| `docs/large-solution-profiling-baseline.md` | Evidence gate for daemon/process-pool performance work. |
| `ai_docs/procedures/deep-review-backlog-intake.md` | Intake procedure for future audit batches. |
| `ai_docs/plans/20260422T223000Z_backlog-sweep/plan.md` | Prior sweep (20260422T223000Z). Shipped 14 initiatives across 14 PRs; closed 9 backlog rows. |
| `ai_docs/plans/20260424T041204Z_backlog-sweep/plan.md` | Closed sweep against the 44-initiative audit-intake batch (5 P2 + 29 P3 + 10 P4). 42 merged / 2 deferred as of 2026-04-25 — `ci-test-parallelization-audit` (dedicated phased plan) and `workspace-process-pool-or-daemon` (heroic-last, profile-evidence gated). |
| `ai_docs/plans/20260426T025255Z_backlog-sweep/plan.md` | Active sweep plan against the open backlog after the 20260424T041204Z sweep finalized. |
| `review-inbox/` | Staging folder for the NEXT audit batch (flat directory; `/backlog-intake` reads here). |
| `review-inbox/archive/<batch-ts>/` | Processed audit/retro/promotion batches — one subdirectory per successful intake. Keep until every row sourced from a batch is closed or superseded, then delete that subdirectory. |
