---
generated_at: 2026-05-21T04:39:18Z
window: "last 14 days (2026-05-07T04:39:18Z → 2026-05-21T04:39:18Z)"
host_repo: roslyn-backed-mcp
host_repo_path: C:/Code-Repo/Roslyn-Backed-MCP
sessions_scanned: 2203
sessions_included: 40
relevant_sessions_in_window_total: 1011
repos_covered:
  - Roslyn-Backed-MCP
  - DotNet-Network-Documentation
  - DotNet-Firewall-Analyzer
  - IT-Chat-Bot
  - TradeWise
phase_mix:
  refactoring: 5
  release_operational: 11
  planning_docs: 6
  audit_test: 15
  mixed: 3
truncated: true
truncation_note: "1011 sessions with >=3 Roslyn MCP mentions were in window; budget capped at top 40 by mention count plus selected cross-repo and subagent samples. Notable repos with relevant sessions NOT sampled: SysLog-Server (42 parent sessions), BioRemote (38), BioFileTransfer (3). Sample is heavily skewed to (a) audit/test surface-test runs across the C# fleet and (b) backlog-sweep:execute orchestration in this repo. Inference about refactoring-tool friction therefore leans on the 5 initiative-executor subagent samples plus surface-test artifacts; widen sample if doing a refactoring-quality retro specifically."
total_roslyn_tool_calls: 656
total_tool_errors: 318
roslyn_specific_tool_errors: 73
roslyn_error_rate_pct: 11.1
---

# Roslyn MCP multi-session retrospective — 2026-05-21 — 14-day window

## 1. Session classification

40 sessions across 5 repos. Aggregate mix: **15 audit/test (37.5%) · 11 release/operational (27.5%) · 6 planning/docs (15%) · 5 refactoring (12.5%) · 3 mixed (7.5%)**.

| session_id (short) | repo | date | phase | notes |
|---|---|---|---|---|
| 1a6ed5dd | Roslyn-Backed-MCP | 2026-05-08 | planning/docs | `/audit-deep` prompt-source check |
| 36084a03 | Roslyn-Backed-MCP | 2026-05-11 | release/operational | surface-test auto-file plumbing |
| e30ac026 | Roslyn-Backed-MCP | 2026-05-12 | mixed | backlog-sweep logic edits + planning |
| 1b503ccc | Roslyn-Backed-MCP | 2026-05-17 | release/operational | `/backlog-sweep:execute mode=parallel count=20` |
| 758e3c35 | Roslyn-Backed-MCP | 2026-05-12 | release/operational | `/backlog-sweep:execute count=5` |
| 11546647 | Roslyn-Backed-MCP | 2026-05-19 | release/operational | `/backlog-sweep:status` |
| 939a786e | Roslyn-Backed-MCP | 2026-05-19 | release/operational | `/backlog-sweep:execute mode=parallel count=20` |
| 00f063bb | Roslyn-Backed-MCP | 2026-05-18 | release/operational | `/backlog-sweep:status` |
| b72b5ba2 | Roslyn-Backed-MCP | 2026-05-10 | planning/docs | `move-to-git-issues.md` planning pass |
| e02dd376 | Roslyn-Backed-MCP | 2026-05-20 | planning/docs | "how many actionable backlog items remain" |
| 75c97ee9 | Roslyn-Backed-MCP | 2026-05-12 | mixed | retrospective-prompt intake, pivoted to `/doc-audit observe` |
| 564d708d | Roslyn-Backed-MCP | 2026-05-12 | release/operational | `/release-cut minor` |
| b6284e57 | Roslyn-Backed-MCP | 2026-05-12 | release/operational | `/backlog-sweep:execute` |
| e2087b47 | Roslyn-Backed-MCP | 2026-05-16 | planning/docs | process-audit-reports.ps1 intake |
| 51ace54a | Roslyn-Backed-MCP | 2026-05-16 | audit/test | post-crash `/mcp-server-surface-test --full` (this repo) |
| 451262be | Roslyn-Backed-MCP | 2026-05-19 | release/operational | `/backlog-sweep:execute mode=parallel count=8` |
| 65688916 | Roslyn-Backed-MCP | 2026-05-07 | planning/docs | `/feature-dev` against backlog + MCP best practices |
| b5e164a8 | Roslyn-Backed-MCP | 2026-05-11 | audit/test | `/roslyn-mcp:mcp-server-surface-test` (this repo) |
| 5ccfd01f | Roslyn-Backed-MCP | 2026-05-12 | release/operational | `/backlog-sweep:execute count=4` |
| 59a91cca | Roslyn-Backed-MCP | 2026-05-12 | release/operational | `/backlog-sweep:execute count=5` |
| 4868dff2 | DotNet-Network-Documentation | 2026-05-10 | audit/test | `/roslyn-mcp:mcp-server-surface-test` (157 Roslyn calls) |
| 09436675 | DotNet-Network-Documentation | 2026-05-16 | audit/test | post-crash `/mcp-server-surface-test` rerun |
| 74e8a63f | IT-Chat-Bot | 2026-05-11 | audit/test | `/roslyn-mcp:mcp-server-surface-test --auto-file` |
| 3b8a91ae | TradeWise | 2026-05-16 | audit/test | post-crash surface-test (uses `plugin_roslyn-mcp_roslyn__*` tool names) |
| 7e3f61eb | DotNet-Firewall-Analyzer | 2026-05-16 | audit/test | post-crash `/mcp-server-surface-test --full` |
| e247fbbc | DotNet-Firewall-Analyzer | 2026-05-10 | audit/test | `/roslyn-mcp:mcp-server-surface-test` (95 Roslyn calls) |
| 2847c970 | DotNet-Firewall-Analyzer | 2026-05-10 | mixed | `move-to-git-issues-end-to-end-verification.md` runner |
| 3dc18049 | TradeWise | 2026-05-16 | audit/test | `/mcp-server-surface-test --full` after clean-main |
| 6a7a9cfc | IT-Chat-Bot | 2026-05-16 | audit/test | post-crash `/mcp-server-surface-test --full` rerun |
| 1ae860dc | DotNet-Firewall-Analyzer | 2026-05-16 | audit/test | post-clean `/mcp-server-surface-test --full` |
| 88af9e04 | IT-Chat-Bot | 2026-05-16 | audit/test | "do you have access to the audit-phase-runner" |
| ddc7fa8d | DotNet-Network-Documentation | 2026-05-16 | audit/test | post-clean `/mcp-server-surface-test --full` |
| a3dbe7cd | TradeWise | 2026-05-12 | planning/docs | `/backlog-sweep:seed-addenda` |
| 3329eaa3 | TradeWise | 2026-05-19 | release/operational | `/backlog-sweep:execute mode=parallel count=10` |
| 3f1fda0b | TradeWise | 2026-05-16 | audit/test | `/roslyn-mcp:mcp-server-surface-test --full` |
| agent-a314ff49 | Roslyn-Backed-MCP (subagent) | 2026-05-16 | refactoring | initiative-executor: `symbol-refactor-preview-auto-applies-without-explicit-apply-call` |
| agent-ae41ab21 | Roslyn-Backed-MCP (subagent) | 2026-05-12 | refactoring | initiative-executor: `parallel-mode-workspace-cap-lru-or-raise` |
| agent-ad522968 | Roslyn-Backed-MCP (subagent) | 2026-05-19 | refactoring | initiative-executor: `find-overrides-payload-overflow-on-corlib-virtual` |
| agent-a9dcb6af | Roslyn-Backed-MCP (subagent) | 2026-05-19 | refactoring | initiative-executor: `find-references-static-extension-host-blind-spot` |
| agent-a28640f6 | Roslyn-Backed-MCP (subagent) | 2026-05-20 | refactoring | initiative-executor: `scaffold-first-test-file-preview-single-target-heuristic` |

**Phase-mix implication for §3 lens:** the dominant `audit/test` slice means many "errors" are surface-test's intentional bad-input probes (Phase 4 of the canonical full prompt); §2a separates these from real bugs. Refactoring lens is supported by only 5 subagent samples, so refactoring-friction claims are flagged as such.

## 2. Task inventory (aggregated, with session ids)

656 total Roslyn MCP tool calls across the 40 sessions. Tool mix (top 15 by call count):

| Tool | Calls | Sessions seen in | Notes |
|---|---:|---:|---|
| `mcp__roslyn__compile_check` | 44 | many | post-Edit gate in subagents + audits |
| `mcp__roslyn__test_run` | 30 | many | **6 of these returned bare-exception (§2a#5)** |
| `mcp__roslyn__workspace_load` | 30 | many | **6 errored; 5 of those rejected worktree paths under "sanctioned root"** |
| `mcp__roslyn__workspace_reload` | 25 | 7 | concentrated in subagents (4–7 reloads each — see §3#2) |
| `mcp__roslyn__analyze_snippet` | 23 | audit | surface-test probes |
| `mcp__roslyn__server_info` | 22 | many | session-start handshake |
| `mcp__roslyn__evaluate_csharp` | 17 | audit | surface-test probes |
| `mcp__roslyn__symbol_search` | 15 | mixed | low usage even in refactoring subagents (§3#3) |
| `mcp__roslyn__get_prompt_text` | 15 | mixed | **10 errored — all surface-test missing-required-arg probes** |
| `mcp__roslyn__apply_text_edit` | 13 | refactoring | competing with Edit for the same writes |
| `mcp__roslyn__workspace_changes` | 12 | refactoring | post-apply state-check |
| `mcp__roslyn__project_diagnostics` | 12 | mixed | |
| `mcp__roslyn__revert_last_apply` | 12 | refactoring | rollback discipline |
| `mcp__roslyn__workspace_health` | 11 | mixed | drift-detection sweep |
| `mcp__roslyn__workspace_list` | 11 | mixed | mostly session-start |

Aggregated user-driven tasks across the 40 sessions:

| Task (verb phrase) | Tool actually used | File type / domain | Right tool? | Sessions (count) |
|---|---|---|---|---|
| Run `/mcp-server-surface-test --full` against C# repo | Roslyn MCP (full surface) | C# solutions | yes | 15× (4868dff2, 09436675, 51ace54a, b5e164a8, 74e8a63f, 3b8a91ae, 7e3f61eb, e247fbbc, 3dc18049, 6a7a9cfc, 1ae860dc, 88af9e04, ddc7fa8d, 3f1fda0b, 3329eaa3) |
| Orchestrate `/backlog-sweep:execute` (spawn N initiative subagents, reconcile) | Bash/gh/git + Skill | mostly markdown/git | yes (orchestrator); subagents are where Roslyn lives | 11× (1b503ccc, 758e3c35, 11546647, 939a786e, 00f063bb, b6284e57, 451262be, 5ccfd01f, 59a91cca, 36084a03, 3329eaa3) |
| Execute one initiative inside subagent (refactor C# in `.worktrees/<id>/`) | **Read+Grep+Edit dominant; Roslyn used only for `workspace_reload`, `compile_check`, `test_run`** | C# source | **partial miss — see §2b#1, §3#3** | 5× (agent-a314ff49, agent-ae41ab21, agent-ad522968, agent-a9dcb6af, agent-a28640f6) |
| Plan / status / reconcile against `ai_docs/backlog.md` and `plan.md` | Edit/Read/Bash | markdown | yes | 6× (b72b5ba2, e02dd376, e2087b47, 65688916, a3dbe7cd, 75c97ee9) |
| Run `/release-cut minor` (bump → ship → tag → reinstall) | Skill + Bash + gh | mixed | yes | 1× (564d708d) |
| Verify `move-to-git-issues` end-to-end | `Bash` + Roslyn (mixed) | C# + scripts | partial (manual run, not a self-checking probe) | 1× (2847c970) |
| Discover capabilities of Roslyn MCP server | `mcp__roslyn__server_info` + `mcp__roslyn__get_prompt_text` with `discover_capabilities` | (introspection) | yes; well-served | many |

## 2a. Roslyn MCP issues encountered

73 of 318 total tool errors were Roslyn MCP related (vs ~245 from `Bash`/`Edit`/`Read` — none Roslyn-correlated; sampled in extraction). Errors bucketed by server-returned `category`:

| Category | Count | Notes |
|---|---:|---|
| InvalidArgument | 26 | Mostly surface-test intentional bad-input probes (line=9999, bogus IDs, missing required keys). |
| InvalidOperation | 17 | Mostly by-design semantic rejections — well-messaged. |
| NotFound | 15 | Symbol / workspace / document / preview-token resolution failures. |
| BareException | 6 | **No detail; tool-side exception masking. See #5 below.** |
| OTHER (`Server "..." not found`) | 6 | Plugin namespace inconsistency. See #1 below. |
| MCP_resource_error (-32002) | 2 | Resource URI shape errors. See #6 below. |
| FileNotFound | 1 | `workspace_load` given a `README.txt` path. |

After dropping surface-test-intentional bad-input probes, the rows below are the **non-intentional** Roslyn MCP friction observed:

### 1. `ReadMcpResourceTool` server-name inconsistency (`plugin:roslyn-mcp:roslyn` vs `plugin_roslyn-mcp_roslyn` vs `roslyn`)

- **Tool:** `ReadMcpResourceTool`
- **Sessions:** [4868dff2] (DotNet-Network-Documentation), [3dc18049] (TradeWise), [3f1fda0b] (TradeWise)
- **Inputs:** resource URI like `roslyn://workspace/<id>/file/...` with server prefix `plugin:roslyn-mcp:roslyn` OR `plugin_roslyn-mcp_roslyn`
- **Symptom:** verbatim from [4868dff2] — `Server "plugin:roslyn-mcp:roslyn" not found. Available servers: plugin:design:slack, plugin:design:figma, ...` (the available-servers list does NOT include any roslyn entry in this listing). From [3dc18049] — `Server "plugin_roslyn-mcp_roslyn" not found. Available servers: plugin:roslyn-mcp:roslyn, ...` (the colon variant IS available — agent used underscore).
- **Impact:** surface-test Phase 9 (resource enumeration round-trip) cannot complete cleanly from the standard skill template; agent retries with multiple prefixes before giving up or falling back to non-resource paths. Maybe 30–60s lost per repo × 5 cross-repos.
- **Workaround:** agent eventually uses tool-namespace `mcp__plugin_roslyn-mcp_roslyn__*` (which does work — see also #2 below) or skips resource-read assertions entirely.
- **Repro confidence:** deterministic (3 cross-repo sessions all hit, all show same server-name lookup failure)

### 2. Dual tool-namespace prefixes co-exist: `mcp__roslyn__*` and `mcp__plugin_roslyn-mcp_roslyn__*`

- **Tool:** `mcp__plugin_roslyn-mcp_roslyn__code_fix_preview`, `mcp__plugin_roslyn-mcp_roslyn__workspace_status`, `mcp__plugin_roslyn-mcp_roslyn__apply_with_verify` (verbatim names)
- **Sessions:** [3b8a91ae] (TradeWise) used the plugin-prefixed tool names exclusively. Most other sessions use `mcp__roslyn__*`.
- **Inputs:** N/A
- **Symptom:** **both prefixes resolve and work** — but they exist in parallel in the registered toolset. Agents that pick one will not see results indexed under the other. No errors are raised; the friction is invisible-but-real (e.g., the agent that errored on `plugin:roslyn-mcp:roslyn` resource URI in [3b8a91ae] simultaneously called `mcp__plugin_roslyn-mcp_roslyn__workspace_status` successfully — different lookup paths).
- **Impact:** non-error friction, but explains why agents mis-pick the resource server name in #1.
- **Workaround:** none needed; both work for tool calls. Resources need the colon-form.
- **Repro confidence:** deterministic (registered both ways)

### 3. `workspace_load` rejects paths outside the "client-sanctioned root"

- **Tool:** `mcp__roslyn__workspace_load`
- **Sessions:** [4868dff2] (DotNet-Network-Documentation), [e247fbbc] (DotNet-Firewall-Analyzer), [74e8a63f] (IT-Chat-Bot)
- **Inputs:** verbatim from [4868dff2] — `Path 'C:\Code-Repo\DotNet-Network-Documentation-surface-test-20260510T053142Z\NetworkDocumentation.sln' is not under any client-sanctioned root. Allowed roots: file://C:\Code-Repo\DotNet-Network-Documentation.`
- **Symptom:** surface-test creates a sibling-directory worktree (`<repo>-surface-test-<ts>`) so apply round-trips happen on a disposable copy. The server pins to the session's CWD as the sole sanctioned root, so the disposable worktree-as-sibling layout is rejected.
- **Impact:** Phase 6/7 of `/mcp-server-surface-test --full` (the apply round-trip phases) had to either run against the live tree (risky) or skip apply-validation. Multiple cross-repo audits affected.
- **Workaround:** sessions [3dc18049] / [3b8a91ae] (TradeWise) switched to using `.worktrees/surface-test-<ts>/` *inside* the repo root — those paths are accepted. The skill template was apparently updated mid-window to prefer the in-repo `.worktrees/` form; the older sibling-directory form lingers in callers.
- **Repro confidence:** deterministic (every session that tried the sibling-dir form failed)

### 4. `ReadMcpResourceTool` URI shape rejected even when workspace ID is current

- **Tool:** `ReadMcpResourceTool`
- **Sessions:** [3b8a91ae] (TradeWise)
- **Inputs:** verbatim — `roslyn://workspace/0b3d320359e14f758b514ce397a6abb8/file/src/TradeWise.Domain/Alerts/AlertRuleType.cs/lines/1-15` and `roslyn://workspace/0b3d320359e14f758b514ce397a6abb8/file/src/TradeWise.Domain/Alerts/AlertRuleType.cs`
- **Symptom:** `MCP error -32002: Unknown resource URI: '...'`. The workspace ID was loaded and active at the time (subsequent `workspace_status` call succeeded against the same ID).
- **Impact:** resource-style file reads do not work as a substitute for `get_source_text`; agents fall back to `mcp__roslyn__get_source_text` or raw `Read`.
- **Workaround:** `mcp__roslyn__get_source_text` works (no errors observed for it on these paths).
- **Repro confidence:** intermittent (only one session reached this code path, but the failure is deterministic for the URI shapes tried)

### 5. `test_run` and `test_coverage` return bare `An error occurred invoking 'X'.` with no detail

- **Tool:** `mcp__roslyn__test_run` (5 instances), `mcp__roslyn__test_coverage` (1 instance)
- **Sessions:** [4868dff2] (DotNet-Network-Documentation, 2× test_run, 1× test_coverage), [agent-a314ff49] (refactoring subagent), [agent-ad522968] (refactoring subagent), [agent-a9dcb6af] (refactoring subagent)
- **Inputs:** standard `projectName` invocations; nothing exotic
- **Symptom:** verbatim — `An error occurred invoking 'test_run'.` That is the entire tool result content. No category, no exceptionType, no stack, no schemaHint — unlike every other Roslyn MCP error which returns the structured JSON envelope with `category`, `message`, `exceptionType`, `_meta`.
- **Impact:** the agent cannot tell whether the tests genuinely ran and failed, whether a precondition was wrong, whether the test discovery failed, or whether the tool itself crashed. **Three of four refactoring subagents that called `test_run` saw this**; all of them then fell back to `Bash dotnet test` which produces verbose output the agent can parse. Cumulative time cost: probably 30–60s per occurrence (fallback + re-parse) ≈ ~5 min in window, but worse than the time cost is the lost confidence in `test_run` as a reliable seam.
- **Workaround:** `Bash dotnet test --logger ...` (every observed fallback).
- **Repro confidence:** deterministic — 4 sessions, 6 instances, identical bare-exception envelope. Reproduces enough that subagents now bypass `test_run` reflexively.

### 6. `find_consumers` / `find_references` / `find_implementations` NotFound on `metadataName`, no fuzzy suggestion

- **Tool:** `mcp__roslyn__find_consumers`, `mcp__roslyn__find_references`, `mcp__roslyn__find_implementations`
- **Sessions:** [agent-a9dcb6af] (refactoring subagent: `find-references-static-extension-host-blind-spot`), [51ace54a] (surface-test), [e247fbbc] (surface-test)
- **Inputs:** metadata names like `SampleLib.AnimalExtensions`, `RoslynMcp.Roslyn.Services.WorkspaceManager.MaxDiagnosticsPerWorkspaceLimit`, `FirewallAnalyzer.Application.Storage.ISnapshotStore`
- **Symptom:** verbatim — `Not found: No symbol could be resolved for metadata name 'X'. The handle may be from a previous workspace version, the symbol may have been removed, or the position may not contain a symbol identifier.`
- **Impact:** When the agent's guessed metadataName is one segment off (e.g., the actual symbol is `MaxDiagnosticsPerWorkspace` and the agent asked for `MaxDiagnosticsPerWorkspaceLimit`), there is no "did you mean…" suggestion. Agent has to fall back to `symbol_search` or `Grep` to disambiguate. Notable in [agent-a9dcb6af] because that subagent's entire initiative was about `find-references` blind spots, so this surfaced reflexively during validation work.
- **Workaround:** `Grep` for the type name, then `symbol_search` with a wider net.
- **Repro confidence:** deterministic when the metadataName is wrong; the message correctly says "may be from previous workspace version OR removed OR position-not-identifier" but does not offer a closest-match suggestion.

### 7. `probe_position` rejects files inside the loaded workspace's `.worktrees/` subdir

- **Tool:** `mcp__roslyn__probe_position`
- **Sessions:** [agent-a314ff49] (refactoring subagent in this repo)
- **Inputs:** verbatim — `File 'C:\Code-Repo\Roslyn-Backed-MCP\.worktrees\symbol-refactor-preview-auto-applies-without-explicit-apply-call\samples\SampleSolution\SampleLib\Dog.cs' is not part of the loaded workspace.`
- **Symptom:** file under `.worktrees/<id>/` is not recognized as part of the workspace, even though the subagent's CWD is that worktree and the workspace was loaded against the worktree's solution.
- **Impact:** the subagent can't probe positions inside its own sandbox using its loaded workspace.
- **Workaround:** load workspace at the worktree's solution path explicitly (the subagent eventually does this) — but the failure mode is silent: the subagent thinks the file is missing rather than the workspace being scoped wrong.
- **Repro confidence:** intermittent (one observed session, but tied directly to the worktree-as-subdir pattern that initiative-executor subagents always use, so likely under-reported because subagents catch it and re-load)

## 2b. Missing tool gaps

### 1. No "load-worktree-as-disposable-sandbox" workflow

- **Task:** Run a refactor or apply round-trip against a disposable copy of the loaded workspace without touching the live tree (the canonical surface-test Phase 6/7 use case).
- **Sessions:** [4868dff2], [09436675], [74e8a63f], [3b8a91ae], [7e3f61eb], [e247fbbc], [3dc18049], [6a7a9cfc], [1ae860dc], [ddc7fa8d], [3f1fda0b] — 11 cross-repo audit sessions. **Recurring.**
- **Why Roslyn-shaped:** the existing `apply_with_verify` is workspace-scoped (same workspace state); there is no "fork this workspace, apply, validate, drop or keep" primitive. The audit prompt has to fake it by creating a sibling/sub directory and loading a second workspace — fragile, hits the sanctioned-root rejection (§2a#3), and leaves real worktree state behind on crash.
- **Proposed tool shape:** `mcp__roslyn__workspace_fork_apply` — input: source `workspaceId` + edits + retention mode (`drop-on-success`, `drop-on-failure`, `keep`). Output: new `workspaceId` for forked sandbox + apply result. Cleans up disk + workspace on the configured retention path. Builds on `apply_with_verify` semantics.
- **Closest existing tool:** `apply_with_verify` (same workspace), or `workspace_load(path)` against a manually-created `.worktrees/<id>/` (current workaround in [3dc18049] et al). The fork primitive would eliminate the sanctioned-root gymnastics and the cross-repo skill-template drift.

### 2. No "find closest symbol" / fuzzy metadataName resolver

- **Task:** Given a guessed metadataName that doesn't match exactly, return the N closest matches by Levenshtein / substring / outline-position.
- **Sessions:** [agent-a9dcb6af], [51ace54a], [e247fbbc] (3 sessions; one was the actual refactor work — moderate recurrence)
- **Why Roslyn-shaped:** the Roslyn workspace already has the full symbol table; the agent does not. When the agent's mental model of a fully-qualified name is one segment off, the round-trip is "ask, NotFound, Grep, symbol_search, retry". A `symbol_search_nearest` primitive would collapse it.
- **Proposed tool shape:** `mcp__roslyn__symbol_search_nearest(query, maxResults=5, kindFilter?)` — returns the 5 closest symbols by name match (no semantic typing required), with their full metadataNames and outline positions.
- **Closest existing tool:** `mcp__roslyn__symbol_search` exists but the data suggests it's underused by subagents (15 calls across 40 sessions vs 8 `find_references` + 7 `go_to_definition`). Possibly because it returns too much or requires a more specific query than a typo would produce.

### 3. No "translate Edit-style change → semantic apply" helper

- **Task:** Subagent has a confidently-localized text change (3 lines, exact context) and wants to apply it atomically with rollback. Currently uses `Edit` (no semantic awareness; can produce build-broken code that compile_check then has to catch).
- **Sessions:** All 5 refactoring subagents — [agent-a314ff49] (7 Edits), [agent-ae41ab21] (n/a sample), [agent-ad522968] (7 Edits), [agent-a9dcb6af] (similar), [agent-a28640f6] (similar). **Recurring.**
- **Why Roslyn-shaped:** the agent already does `compile_check` after Edits. If a semantic `apply_text_edit` could do the Edit + compile_check + auto-rollback as one atomic operation, the subagent wouldn't need the 3-step retry loop. The tool exists (`apply_text_edit`, 13 calls in window) but is used 5× less than `Edit` in refactoring subagents — suggesting it's not ergonomic enough or its semantics aren't well-advertised.
- **Proposed tool shape:** (either) **(a)** improve `apply_text_edit` docs/UX so subagents reach for it first, or **(b)** new `mcp__roslyn__edit_with_compile_gate(path, oldString, newString, replaceAll?, projectName?)` that semantically locates the edit, applies, runs `compile_check`, returns success + diff or rollback.
- **Closest existing tool:** `apply_text_edit` (under-used), `Edit` (over-used, no semantic check).

### 4. No structured `test_run` failure envelope

- **Task:** Run a project's tests and get a parseable summary of which tests failed and why.
- **Sessions:** [4868dff2], [agent-a314ff49], [agent-ad522968], [agent-a9dcb6af] (4 sessions). **Recurring.**
- **Why Roslyn-shaped:** the tool exists (`test_run`, 30 calls) and is supposed to be the canonical replacement for `Bash dotnet test`. But §2a#5 shows it returns a bare exception envelope often enough that subagents reflexively use `Bash dotnet test` instead. The gap isn't "missing tool" — it's "tool exists but is unreliable enough to drive bypass".
- **Proposed tool shape:** fix the bare-exception path: `test_run` should always return the structured `{error: true, category, message, exceptionType, _meta}` envelope, even when the underlying `dotnet test` invocation throws. If the failure is "no project loaded" or "no tests discovered", say so.
- **Closest existing tool:** `test_run` itself (just needs error-path completeness).

### 5. No "what tools exist for this kind of task" lookup

- **Task:** Subagent wants to do a C# refactor and has to choose between `Edit` and 8+ semantic tools. There's no "given task: rename a method across N files, which tools should I use, in what order?" primitive.
- **Sessions:** All 5 refactoring subagents (gap-by-behavior — they reach for `Edit` reflexively).
- **Why Roslyn-shaped:** the server already has a `discover_capabilities` prompt; it could be tagged by task category and surfaced more aggressively in skill descriptions.
- **Proposed tool shape:** a `mcp__roslyn__suggest_tools_for(task: string)` prompt that returns the canonical 2–3 tool chains for common refactor tasks ("rename method", "extract type", "find unused", "move type"). Or — simpler — make `discover_capabilities` show up in skill descriptions and have skills reference it.
- **Closest existing tool:** `get_prompt_text(promptName=discover_capabilities)` exists but the data shows it's not reflexively called by refactoring subagents.

## 3. Recurring friction patterns

Seven patterns, ordered by cumulative session impact. Verbatim quotes are cited to specific session ids.

### Pattern 1 — Plugin namespace drift (`plugin:roslyn-mcp:roslyn` vs `plugin_roslyn-mcp_roslyn` vs `roslyn`)

- **What happened:** the same MCP server is registered/discoverable under at least three names in the surveyed window: tool-call prefix `mcp__roslyn__*`, tool-call prefix `mcp__plugin_roslyn-mcp_roslyn__*`, and resource-URI server name `plugin:roslyn-mcp:roslyn`. From [3dc18049]: `Server "plugin_roslyn-mcp_roslyn" not found. Available servers: plugin:roslyn-mcp:roslyn, ...` From [4868dff2]: `Server "plugin:roslyn-mcp:roslyn" not found. Available servers: plugin:design:slack, plugin:design:figma, ...` — different available-servers lists across sessions.
- **Session spread:** 4 sessions ([4868dff2], [3dc18049], [3b8a91ae], [3f1fda0b]) — all audit/test phase, all cross-repo (DotNet-Network-Documentation + TradeWise).
- **Why it recurs:** the skill templates were generated when one naming convention was active; the Claude Code plugin runtime has shifted naming (or registers under different prefixes depending on whether the plugin is loaded from marketplace vs local). The available-servers list also varies by which MCP servers the user has loaded, so the same skill template fails on some users' machines and not others.
- **What would fix it:** **(a)** register a single canonical resource-URI server name and reject the others at the plugin layer, OR **(b)** add a server alias map so `plugin:roslyn-mcp:roslyn`, `plugin_roslyn-mcp_roslyn`, and `roslyn` all resolve to the same handle, AND **(c)** update the surface-test skill template to probe `server_info` for the live server name before constructing resource URIs.

### Pattern 2 — Workspace-reload churn in refactoring subagents (4–7 per session)

- **What happened:** each initiative-executor subagent in the sample called `workspace_reload` 4–7 times during a single initiative. From the tool-mix data: [agent-a314ff49] = 4× workspace_reload + 9× test_run + 41× Bash + 41× Read + 18× Grep + 7× Edit; [agent-ad522968] = 7× workspace_reload + 18× Bash + 27× Read + 19× Grep + 7× Edit. The interactive parent sessions almost never reload — it's a subagent-specific pattern.
- **Session spread:** 4 of 5 sampled refactoring subagents — refactoring phase only.
- **Why it recurs:** subagent's discipline is "Edit → compile_check → if drift, reload → re-validate". After every Edit, the workspace state is suspect (the workspace was loaded at subagent start, the Edits go through `Edit`/`Write` not `apply_text_edit`, so the workspace is genuinely out of sync). The subagent defensively reloads rather than trusting any incremental-update path.
- **What would fix it:** either (a) make `Edit`/`Write` notify the Roslyn workspace incrementally (probably impractical — `Edit`/`Write` are Claude Code primitives, not Roslyn-aware), OR (b) make subagents reach for `apply_text_edit` first so the workspace stays consistent without reloads (per §2b#3), OR (c) accept the reload pattern but make `workspace_reload` faster on the no-op case and emit a "no-change-detected, skipped" envelope so the subagent learns when reload was unnecessary.

### Pattern 3 — Refactoring subagents bypass Roslyn semantic tools for Read/Grep/Edit

- **What happened:** all 5 sampled refactoring subagents have a tool mix dominated by `Read` (27–41 calls each), `Grep` (18–19), `Bash` (18–41), `Edit` (7 each). Semantic Roslyn tools — `find_references`, `rename_preview`, `symbol_search`, `move_type_to_file_preview`, `extract_method_preview` — are called **0 times** in 4 of 5 sampled subagents (probe_position used once in agent-a314ff49). This is despite the initiatives being C# refactor work (`symbol-refactor-preview-*`, `find-overrides-payload-overflow`, `find-references-static-extension-host-blind-spot`).
- **Session spread:** 5 of 5 refactoring-subagent samples — refactoring phase, but small sample.
- **Why it recurs:** Plausibly three reasons: (1) subagents are working in `.worktrees/<id>/` and probe_position rejects those paths (§2a#7), shaking confidence in the semantic tools' workspace-scoping; (2) `Edit` has a tighter, more familiar contract — exact string-replace — and matches what subagents do when applying tested patches from plan.md; (3) the skill descriptions for `roslyn-mcp:refactor` etc. don't emphasize the seam-correctness story strongly enough.
- **What would fix it:** push the `discover_capabilities` prompt into the initiative-executor brief (per §2b#5), OR add concrete "for this initiative shape, here are the right tools" guidance to the `initiative-executor` agent description.
- **Caveat:** sample is only 5 subagents — widen the sample before concluding the seam is broken vs the brief is.

### Pattern 4 — `test_run` bare-exception drives subagent fallback to `Bash dotnet test`

- **What happened:** 6 instances across 4 sessions of `test_run` returning the literal string `An error occurred invoking 'test_run'.` with no JSON envelope. In each, the subagent immediately fell back to `Bash dotnet test`. From [agent-a314ff49] timing: test_run called 9 times, multiple bare-exception failures, eventual reliance on Bash for validation.
- **Session spread:** 4 sessions across two phases (1 audit/test, 3 refactoring subagent) — cross-cutting.
- **Why it recurs:** the tool's error path is not wrapped in the standard `{error: true, category, message, ...}` envelope. Any exception in the `test_run` MSBuild-launching code path bubbles up as the C# default `Exception.Message` (which is the McpServer's generic invoke-failure message).
- **What would fix it:** wrap `test_run`'s exception path in the same diagnostic envelope as every other tool — categorize as `InvalidOperation`/`InternalError`, surface the actual exception type, name the failing project, suggest `workspace_reload` or `project_diagnostics` next.

### Pattern 5 — `/mcp-server-surface-test --full` post-crash reruns are common and lose state

- **What happened:** 5 of 15 audit-phase sessions start with "the computer crashed during a previous run of /mcp-server-surface-test. do any cleanup of any worktrees or branches, make sure main is clean and then re-run /mcp-server-surface-test --full again" — verbatim repeated across [51ace54a], [09436675], [3b8a91ae], [7e3f61eb], [6a7a9cfc]. All on 2026-05-16, suggesting one crash incident took out multiple in-flight audits.
- **Session spread:** 5 sessions across 4 repos — audit/test phase, one specific day.
- **Why it recurs:** surface-test creates real worktrees (`.worktrees/surface-test-<ts>/` or `<repo>-surface-test-<ts>` sibling) and runs for 90–180 minutes. Without a checkpoint/resume mechanism, a single OS-level crash forces the user to clean up disk state across N repos and start over.
- **What would fix it:** add resumability — a `<repo>/.audit-state.json` checkpoint file that records phase progress, so a rerun can pick up where it left off. Even simpler: the skill template's "Phase 0 cleanup" should be a separate skill that the user can invoke standalone (`/mcp-server-surface-test:cleanup`) so the recovery is one command, not a paragraph of "do any cleanup".

### Pattern 6 — Sanctioned-root rejection blocks cross-directory workspace_load

- **What happened:** `workspace_load` refuses paths not under the original session's CWD. Verbatim from [e247fbbc]: `Path 'C:/Code-Repo/DotNet-Firewall-Analyzer-surface-test-20260510T053000Z/FirewallAnalyzer.slnx' is not under any client-sanctioned root. Allowed roots: file://C:\Code-Repo\DotNet-Firewall-Analyzer.` Sessions in the latter half of the window switched to in-repo `.worktrees/` paths and avoided the error — but the skill templates still produce both forms.
- **Session spread:** 3 sessions ([4868dff2], [e247fbbc], [74e8a63f]) explicitly errored on this; the rest got past it because the template was updated.
- **Why it recurs:** the surface-test template's "create a disposable workspace" step is in tension with the server's path-sanctioning. The skill template carries the intent (do not corrupt the live tree) but the server's safety mechanism doesn't know about it.
- **What would fix it:** ties to §2b#1 — a `workspace_fork_apply` primitive that lives inside the server's path-sanctioning trust boundary. Failing that, document the in-repo `.worktrees/` pattern as canonical and remove the sibling-directory form from older skill versions.

### Pattern 7 — `metadataName`-based symbol lookup has no fuzzy fallback

- **What happened:** when the agent's guessed metadataName is slightly off, `find_references` / `find_consumers` / `find_implementations` / `symbol_info` return `NotFound` with a generic "may be stale OR removed OR position not on identifier" message. No "did you mean..." suggestion is offered. Verbatim from [51ace54a]: `Not found: No symbol could be resolved for metadata name 'RoslynMcp.Roslyn.Services.WorkspaceManager.MaxDiagnosticsPerWorkspaceLimit'.` The actual symbol in this repo is `MaxDiagnosticsPerWorkspace`.
- **Session spread:** 3 sessions ([agent-a9dcb6af], [51ace54a], [e247fbbc]) — one was actual refactoring work, two were audit probes.
- **Why it recurs:** Roslyn's symbol table is queryable; the server just doesn't expose closest-match. Agents that mis-recall a name pay the round-trip cost.
- **What would fix it:** add a closest-match suggestion to the NotFound message (top 3 by name similarity), or expose a new `symbol_search_nearest` tool (per §2b#2).

## 4. Suggested findings (up to 7)

Ranked backlog-candidate findings for the maintainer's review. **This list is informational only — it lives solely in this file. Do not push, append, or sync to `ai_docs/backlog.md` or GitHub Issues from this report.**

### Finding 1: `roslyn-mcp-test-run-bare-exception-envelope`

- **priority hint:** medium-high — 4 sessions, observed across both audit and refactoring phases, drives reflexive subagent bypass of an intended-canonical tool
- **title:** Wrap `test_run` / `test_coverage` exception path in the standard structured error envelope
- **summary:** Six instances across [4868dff2], [agent-a314ff49], [agent-ad522968], [agent-a9dcb6af] of `test_run`/`test_coverage` returning the bare string `"An error occurred invoking 'test_run'."` with no `category`, `message`, `exceptionType`, or `_meta`. Every other Roslyn tool error in the window uses the structured envelope. Subagents who hit this fall back to `Bash dotnet test` and stop trusting `test_run` for the rest of the session — defeating the seam.
- **proposed action:** Audit `RunTestRunHandler` / `RunTestCoverageHandler` for an outer `try { ... } catch (Exception ex)` and convert it to the same envelope used by `compile_check` / `find_references`. Surface exception type, project name, and a `schemaHint` next-step suggestion.
- **evidence:** §2a#5, §2b#4, §3#4 — sessions: [4868dff2], [agent-a314ff49], [agent-ad522968], [agent-a9dcb6af]

### Finding 2: `roslyn-mcp-resource-server-name-aliasing`

- **priority hint:** medium — 4 sessions deterministic, blocks Phase 9 of surface-test on cross-repo, but a real workaround exists
- **title:** Add server-name aliases (`plugin:roslyn-mcp:roslyn`, `plugin_roslyn-mcp_roslyn`, `roslyn`) for MCP resource lookups
- **summary:** From [4868dff2]: `Server "plugin:roslyn-mcp:roslyn" not found...` From [3dc18049]: `Server "plugin_roslyn-mcp_roslyn" not found. Available servers: plugin:roslyn-mcp:roslyn, ...`. The same server is reachable as tool-prefix `mcp__roslyn__*` AND `mcp__plugin_roslyn-mcp_roslyn__*` simultaneously, but resource URIs require a single specific server name that varies by Claude Code session. Drives the surface-test skill template to construct URIs that fail on cross-repo audit machines.
- **proposed action:** At the MCP-server registration layer, register one canonical name and alias the others. Also: update `skills/mcp-server-surface-test/prompts/full.md` Phase 9 to probe `server_info` for the live server name before composing resource URIs.
- **evidence:** §2a#1, §2a#2, §3#1 — sessions: [4868dff2], [3dc18049], [3b8a91ae], [3f1fda0b]

### Finding 3: `roslyn-mcp-workspace-fork-apply-primitive`

- **priority hint:** high — 11 cross-repo sessions are the canonical use case; current workaround is fragile (skill template drift)
- **title:** Add `workspace_fork_apply` (or similar) to support disposable-sandbox apply round-trips
- **summary:** 11 cross-repo audit sessions in the window try to validate apply round-trips against a sibling-directory worktree. The server's path-sanctioning correctly refuses (§2a#3), so the skill template was updated mid-window to use in-repo `.worktrees/<id>/` paths. The newer form works but is still a hand-rolled workaround for a missing primitive. Without a server-blessed fork, callers can't atomically apply-then-validate-then-discard without leaving worktree state on disk after crashes (cf. Pattern 5 — 5 post-crash recovery sessions).
- **proposed action:** Design `mcp__roslyn__workspace_fork_apply(sourceWorkspaceId, edits, retention=drop-on-failure)` — fork the workspace, apply the edits inside the fork, validate (compile + tests if requested), return result + diff, drop or keep the fork per retention. Server owns the disk lifecycle, eliminating the sanctioned-root gymnastics.
- **proposed action (interim):** document the in-repo `.worktrees/` pattern as canonical in the surface-test skill; remove the sibling-directory form from older skill template comments.
- **evidence:** §2a#3, §2b#1, §3#5, §3#6 — sessions: [4868dff2], [09436675], [74e8a63f], [3b8a91ae], [7e3f61eb], [e247fbbc], [3dc18049], [6a7a9cfc], [1ae860dc], [ddc7fa8d], [3f1fda0b]

### Finding 4: `roslyn-mcp-symbol-search-nearest-fuzzy-fallback`

- **priority hint:** medium — only 3 sessions in window but one was actual refactoring work, and the fix is low-risk
- **title:** Add fuzzy "did you mean..." suggestions to `find_references` / `find_consumers` / `find_implementations` / `symbol_info` NotFound responses, or new `symbol_search_nearest` tool
- **summary:** When agents pass a slightly-wrong metadataName, the NotFound message is generic ("may be stale OR removed OR position-not-identifier") and does not suggest closest matches. From [51ace54a] (real surface-test): `'RoslynMcp.Roslyn.Services.WorkspaceManager.MaxDiagnosticsPerWorkspaceLimit'` errored when the actual symbol is `MaxDiagnosticsPerWorkspace`. From [agent-a9dcb6af] (real refactoring): `'SampleLib.AnimalExtensions'` errored similarly. Roslyn's symbol table is queryable; closest-match by name-similarity is cheap.
- **proposed action:** Either (a) augment the NotFound message with top 3 closest matches inline, OR (b) add a `mcp__roslyn__symbol_search_nearest(query, maxResults=5)` tool. (a) is lower-friction for callers; (b) is more general.
- **evidence:** §2a#6, §2b#2, §3#7 — sessions: [agent-a9dcb6af], [51ace54a], [e247fbbc]

### Finding 5: `roslyn-mcp-subagent-tool-discovery-brief`

- **priority hint:** medium-low — under-evidenced (5 subagent samples only), but if real the leverage is large
- **title:** Have refactoring subagents reach for semantic Roslyn tools before `Edit`/`Read`/`Grep`
- **summary:** All 5 sampled refactoring subagents have tool mixes dominated by `Read`/`Grep`/`Edit`/`Bash` and use 0 calls of `find_references`/`rename_preview`/`symbol_search`/`move_type_to_file_preview` despite working on C# refactor initiatives. This is partly because `apply_text_edit` is under-used (13 calls in window vs 84 `Edit` errors, ~5–6× the Edit volume), and partly because the initiative-executor brief doesn't suggest a tool-chain hierarchy. The pattern correlates with the `workspace_reload` churn (Pattern 2) — subagents Edit, then reload, then re-validate, instead of `apply_text_edit` which would keep the workspace in sync.
- **proposed action:** Add a "Roslyn-first toolchain" stanza to the `initiative-executor` agent description: for each refactor shape (rename, move, extract, edit-in-place), name the canonical Roslyn tool. Or: when `roslyn-mcp:refactor` skill detects a C# file in the initiative, surface the `discover_capabilities` prompt automatically.
- **caveat:** sample is 5 subagents — confirm with a wider refactoring-only retro before acting.
- **evidence:** §2b#3, §2b#5, §3#3 — sessions: [agent-a314ff49], [agent-ae41ab21], [agent-ad522968], [agent-a9dcb6af], [agent-a28640f6]

### Finding 6: `roslyn-mcp-surface-test-resumability-and-cleanup-skill`

- **priority hint:** medium — 5 post-crash recovery sessions in one day shows the cost is real; ergonomic fix
- **title:** Add resumability + a standalone `/mcp-server-surface-test:cleanup` to recover from crashed runs without manual cleanup paragraphs
- **summary:** 5 cross-repo audit sessions on 2026-05-16 ([51ace54a], [09436675], [3b8a91ae], [7e3f61eb], [6a7a9cfc]) all start with the identical user prompt: "the computer crashed during a previous run of /mcp-server-surface-test. do any cleanup of any worktrees or branches, make sure main is clean and then re-run /mcp-server-surface-test --full again". The agent in each case spends 5–10 minutes on git/worktree cleanup before re-running. A standalone cleanup skill plus an `.audit-state.json` checkpoint would drop this to "run /mcp-server-surface-test:cleanup, then re-run".
- **proposed action:** (a) Split the cleanup paragraph out of the canonical full.md prompt into a `cleanup` skill or `--cleanup-only` flag. (b) Add `.audit-state.json` checkpoint writes between phases so reruns can resume.
- **evidence:** §3#5 — sessions: [51ace54a], [09436675], [3b8a91ae], [7e3f61eb], [6a7a9cfc]

### Finding 7: `roslyn-mcp-probe-position-worktree-scoping`

- **priority hint:** low — one observed session, but tied to the dominant subagent pattern, so probably under-reported
- **title:** `probe_position` should accept files under `.worktrees/<id>/` when the loaded workspace is rooted at that worktree
- **summary:** From [agent-a314ff49]: `File 'C:\Code-Repo\Roslyn-Backed-MCP\.worktrees\symbol-refactor-preview-auto-applies-without-explicit-apply-call\samples\SampleSolution\SampleLib\Dog.cs' is not part of the loaded workspace.` Subagent had loaded the workspace at the worktree's solution but `probe_position` reported the file as not part of the workspace. Likely a workspace-scope bug rather than a sanctioned-root issue (the file is *inside* the workspace).
- **proposed action:** Investigate the workspace document-set for worktree-rooted loads. Likely a path-normalization bug where the workspace stores forward-slash paths but the request comes in with backslashes or vice versa.
- **evidence:** §2a#7 — sessions: [agent-a314ff49]

## 5. Meta-note

**Phase mix of the window:** 15 audit/test (cross-repo surface-test runs) · 11 release/operational (backlog-sweep:execute orchestration) · 6 planning/docs · 5 refactoring (subagent samples) · 3 mixed. The audit/test slice is heavy enough to bias the visible Roslyn MCP error count upward (those sessions intentionally probe edge cases); the report explicitly separates intentional bad-input probes from real bugs in §2a.

**Where Roslyn MCP friction is currently concentrated:** (1) **server-name ergonomics** — three coexisting naming conventions for the same server (`mcp__roslyn__*`, `mcp__plugin_roslyn-mcp_roslyn__*`, `plugin:roslyn-mcp:roslyn`) confuse resource URIs and skill templates across machines; (2) **`test_run` reliability** — a single bare-exception path in `test_run` is silently driving subagent bypass to `Bash dotnet test`, eroding the seam's value; (3) **disposable-sandbox primitive missing** — the canonical surface-test use case (fork-apply-validate-discard) is hand-rolled against the path-sanctioning safety, with 5 post-crash recovery sessions in one day showing the workflow's fragility; (4) **subagent tool discovery** — refactoring subagents reflexively use `Read`/`Grep`/`Edit` over semantic Roslyn tools, though the sample is too small to confirm whether the seam is broken or the brief is.

**Repo-specific skew:** Roslyn MCP server errors *in this repo* are dominated by intentional surface-test probes ([51ace54a] = 24 errors, [b5e164a8] = 8 errors — both surface-test on this repo). Cross-repo error sessions are also surface-test runs ([4868dff2] = 27, [e247fbbc] = 15, [09436675] = 12). **Net: most error-instances in the window are by-design, not bugs**; the actual bug-shaped findings (Findings 1, 7) come from initiative-executor subagents inside this repo. The refactoring-friction inference (Findings 5, parts of 3) is supported by only 5 subagent samples; a refactoring-only retro should widen that sample.

**One thing to change about default Roslyn MCP usage next time:** push `discover_capabilities` into the initiative-executor agent description (or the `roslyn-mcp:refactor` skill description) so refactoring subagents reach for semantic Roslyn tools before `Edit`/`Read`/`Grep`. The current pattern (4–7 `workspace_reload` calls per subagent because `Edit` desyncs the workspace) is wasteful and the tool to fix it (`apply_text_edit`) already exists — it's a discoverability problem.

**Was the window long enough?** 14 days produced a strong sample for cross-repo audit friction (15 sessions across 5 repos) and a reasonable sample for operational orchestration (11 sessions). The refactoring sample is thin (5 subagents) — most concrete bug-shaped findings (Findings 1, 7) are evidenced by 1–4 sessions each. Recommendation: keep 14 days as the default for the all-phase retro, but run a refactoring-only retro at 30 days when chasing seam-correctness questions. Notable repos in window NOT sampled (SysLog-Server: 42 parent sessions, BioRemote: 38) are mostly C# repos doing non-Roslyn work — sampling them would have diluted signal, not enriched it.
