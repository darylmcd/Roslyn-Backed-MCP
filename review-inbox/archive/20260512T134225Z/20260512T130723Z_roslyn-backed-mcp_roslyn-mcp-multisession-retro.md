---
generated_at: 2026-05-12T13:07:23Z
window: "last 14 days (2026-04-28 → 2026-05-12)"
host_repo: roslyn-backed-mcp
host_repo_path: C:\Code-Repo\Roslyn-Backed-MCP
sessions_scanned: 104
sessions_included: 47
repos_covered:
  - Roslyn-Backed-MCP
  - TradeWise
  - SysLog-Server
  - DotNet-Network-Documentation
  - IT-Chat-Bot
  - DotNet-Firewall-Analyzer
phase_mix:
  refactoring: 10
  release_operational: 21
  planning_docs: 7
  audit_surface_test: 9
truncated: false
truncation_notes: |
  No truncation — total in-window JSONL volume (74,503 lines) was under the 200k cap.
  Of 104 sessions in window, 47 were read deeply (3 parallel reader agents + this orchestrator).
  The remaining ~57 sessions had ≤2 Roslyn-MCP tool calls each and were aggregated only via
  cross-cutting grep counts (e.g. workspace_reload patterns, isError counts) rather than per-session deep-reads.
---

# Roslyn MCP multi-session retrospective — 2026-05-12 — 14-day window

Cross-repo retrospective of Claude Code sessions over the last 14 days that touched the Roslyn MCP server. Aggregates concrete tool-level failures, missing-capability gaps, and recurring friction patterns observed during real maintainer dogfood. Local artifact only — not pushed, not synced.

**Cross-cutting numbers (in-window grep across all 104 JSONLs):**
- 23,937 `mcp__roslyn__*` tool-call invocations
- 40 `is_error: true` Roslyn tool_result envelopes (28 real-work, 12 by-design audit-harness probes)
- 38 `workspace_load` calls vs **58 `workspace_reload`** calls — load:reload ratio worse than 1:1
- 818 occurrences of the string `workspace_reload` (combined invocations + skill-doc references)
- 77 cases of `find_references → Grep` immediately after (Roslyn fall-through pattern)
- 5 sessions with audit-harness `/mcp-server-surface-test` or `/mcp-server-stress` skill invocation
- 0 documented `compile_check`/`find_references`/`rename_apply` semantic miscount complaints

---

## 1. Session classification

| Session | Repo | Date | Phase | Notes |
|---|---|---|---|---|
| 4d410565 | Roslyn-Backed-MCP | 04-28 | release/operational | `/backlog-sweep` 5 init parallel — 8 NotFound workspaceId events across subagents |
| eac2bfec | Roslyn-Backed-MCP | 04-28 | release/operational | post-crash sweep recovery; v1.33.2 ship |
| a26fdd10 | Roslyn-Backed-MCP | 05-05 | release/operational | `/backlog-sweep:execute count=5`; v1.34.0 ship |
| 4317ef79 | Roslyn-Backed-MCP | 05-05 | planning/docs | "next steps?"; retro prompt drafted |
| 968e1d37 | Roslyn-Backed-MCP | 05-05 | planning/docs | "complete pass against MCP best practices" |
| 4cb4402c | Roslyn-Backed-MCP | 05-06 | release/operational | 1.34 NuGet workflow misfire investigation |
| 18d0e6b3 | Roslyn-Backed-MCP | 05-06 | release/operational | continued sweep waves |
| 65688916 | Roslyn-Backed-MCP | 05-07 | release/operational | `/feature-dev` against backlog |
| 8405ce90 | Roslyn-Backed-MCP | 05-09 | refactoring | comprehensive C# refactor audit — **0 Roslyn calls** |
| b72b5ba2 | Roslyn-Backed-MCP | 05-09 | planning/docs | move-to-git-issues plan |
| e30ac026 | Roslyn-Backed-MCP | 05-11 | planning/docs | adversarial review of `/backlog-sweep` changes |
| 23c939cd | Roslyn-Backed-MCP | 05-11 | release/operational | `/backlog-sweep:execute parallel count=6` |
| 36084a03 | Roslyn-Backed-MCP | 05-11 | planning/docs | `/mcp-server-stress --target=TradeWise` cross-repo Q |
| 3bb3ca9c | Roslyn-Backed-MCP | 05-11 | planning/docs | sweep model recommendation Q |
| 758e3c35 | Roslyn-Backed-MCP | 05-12 | release/operational | `/backlog-sweep:execute count=5` — **3× 8-workspace cap hit** |
| 564d708d | Roslyn-Backed-MCP | 05-12 | release/operational | `/release-cut minor` → v1.36.0 |
| c2a4c5b7 | TradeWise (worktree) | 05-05 | refactoring | full C# refactor audit on 627-doc solution — **1 Roslyn call** total |
| 6f973993 | TradeWise | 05-11 | planning/docs | `/backlog-sweep:prepare count=24` |
| 8eaad4ff | TradeWise | 05-12 | refactoring | sweep parallel count=5 — Windows file-lock teardown errors |
| 56bb8a22 | TradeWise | 05-11 | refactoring | sweep parallel count=6 |
| adcc0ca2 | TradeWise | 05-12 | refactoring | sweep parallel count=5 |
| a0a7ecf1 | TradeWise | 05-12 | refactoring | sweep parallel count=5 — clean run, 0 Roslyn |
| d9bd2e13 | TradeWise | 05-11 | refactoring | ADR-022 impact analysis — 0 Roslyn |
| b65dddea | TradeWise | 05-07 | refactoring | sweep with 5× `dotnet build-server shutdown` lock workarounds |
| b52221ca | TradeWise | 05-06 | planning/docs | "top 10 backlog quick wins" — 0 Roslyn |
| 5878f8db | TradeWise | 05-07 | refactoring | sweep — branch-used-by-worktree errors |
| 9e16bda2 | TradeWise | 05-11 | refactoring | sweep |
| 3b62ea8a | TradeWise | 05-12 | refactoring | sweep |
| d2f114f5 | TradeWise | 05-12 | refactoring | sweep |
| fcde52df | SysLog-Server | 05-07 | release/operational | `/roslyn-mcp:workspace-health` — Rust repo misclassified |
| 1af7a270 | SysLog-Server | 05-07 | release/operational | `/backlog-sweep:execute` (correctly skipped Roslyn for Rust) |
| 8a0ae909 | SysLog-Server | 04-28 | planning/docs | end-to-end feature assessment |
| 66282528 | SysLog-Server | 05-07 | release/operational | sweep |
| e394d419 | SysLog-Server | 05-08 | release/operational | reconcile-branches + sweep |
| 2da654c1 | DotNet-Network-Documentation | 04-28 | planning/docs | `/backlog-sweep:plan` |
| d2cbf0ee | DotNet-Network-Documentation | 05-04 | release/operational | sweep parallel — crash + remediate |
| fbc81982 | DotNet-Network-Documentation | 05-05 | release/operational | sweep serial |
| c322ec61 | DotNet-Network-Documentation | 05-05 | planning/docs | `/backlog-sweep:review` |
| 4e707c75 | DotNet-Network-Documentation | 04-28 | refactoring | SSH feature parity — 4 slices, 0 Roslyn |
| 9f7a3457 | DotNet-Network-Documentation | 05-05 | planning/docs | `/feature-dev` — 0 Roslyn calls |
| 199aee46 | DotNet-Network-Documentation | 05-02 | refactoring | P3 web-ui — workspace_load REJECTED on sibling worktree |
| 396e0724 | DotNet-Network-Documentation | 05-02 | audit/special | **custom Python harness driving 51 experimental tools — documented 13+8 param-name bugs** |
| 121407c8 | IT-Chat-Bot | ~04-15 | release/operational | `/ship` cleanup — clean |
| 102bd77d | IT-Chat-Bot | ~04-24 | audit/special | `/audit-toolset` full 8-tool run — **textbook Roslyn flow** |
| 01c5f16b | IT-Chat-Bot | ~04-22 | planning/docs | `/doc-audit` initial — 0 Roslyn (expected) |
| 173f2cb3 | IT-Chat-Bot | ~04-24 | planning/docs | aborted very early |
| 2847c970 | DotNet-Firewall-Analyzer | 05-10 | release/operational | move-to-git-issues E2E verify — 18 Roslyn tools, no errors |
| 4868dff2 | DotNet-Network-Documentation | 05-10 | audit/surface-test | full `/mcp-server-surface-test` — 2309 calls, 17 by-design errors |
| e247fbbc | DotNet-Firewall-Analyzer | 05-10 | audit/surface-test | full surface-test — sandbox blocked half the phases |
| 74e8a63f | IT-Chat-Bot | 05-11 | audit/surface-test | surface-test `--auto-file` — **15 GitHub issues filed (#614-#628)** |
| b5e164a8 | Roslyn-Backed-MCP | 05-11 | audit/surface-test | self-test (partial) for move-to-git-issues design |
| 17756d59 | Roslyn-Backed-MCP | 05-11 | audit/surface-test | `/mcp-server-stress --target=TradeWise` — **blocked at workspace_load** |
| 9b7448de | Roslyn-Backed-MCP | 05-10 | audit/surface-test | partial run |
| fb90f788 | IT-Chat-Bot | 05-10 | audit/surface-test | full surface-test `--auto-file` |
| ed2acc18 | DotNet-Network-Documentation | 05-10 | audit/surface-test | partial run |

**Aggregate phase mix (47 deeply-analyzed sessions):** 10 refactoring / 21 release-operational / 7 planning-docs / 9 audit (surface-test + experimental-promo). Roslyn-Backed-MCP is overwhelmingly release+operational dogfood; TradeWise is the only repo where sustained C# refactoring drives the work; SysLog-Server is Rust (correctly skipped); DotNet-Network-Documentation is mixed planning + medium feature execution; IT-Chat-Bot is mostly housekeeping.

The retro lens therefore weights **release/operational reliability** highest, **refactoring tool-coverage** second, and treats audit sessions as a separate dataset for surface-defect signal only.

---

## 2. Task inventory (aggregated, with session ids)

Each row collapses identical task shapes across sessions.

| Task (verb phrase) | Tool actually used | Domain | Right tool for the job? | Sessions (count × ids) |
|---|---|---|---|---|
| Plan a backlog sweep (read .md, write plan.md + state.json) | Read + Edit + Glob + Grep | markdown planning | yes — non-semantic | 2da654c1, 6f973993, b72b5ba2, 4317ef79 (4) |
| Execute backlog initiative in parallel waves | Agent (subagent fan-out) + Bash (gh/git/dotnet) | C# code + git ops | partial — subagents make Roslyn calls but routine flow does not pre-emptively load workspace | 4d410565, a26fdd10, 23c939cd, 18d0e6b3, 758e3c35, d2cbf0ee, fbc81982, c322ec61, eac2bfec, b65dddea, 8eaad4ff, 56bb8a22, adcc0ca2, a0a7ecf1, 5878f8db, 1af7a270 (16) |
| Cut a release (bump → verify → ship → tag → reinstall) | Bash (dotnet, git, gh) + Edit (version files) | release ops | yes | 564d708d, 4cb4402c (2) |
| Comprehensive C# refactor audit on a multi-project solution | Read + Grep + Edit + Bash + Agent (subagents) | C# semantic | **NO — missed opportunity, 0 Roslyn semantic calls** | 8405ce90, c2a4c5b7, d9bd2e13, b52221ca, 4e707c75 (5) |
| Implement specific C# feature (e.g. SSH endpoint parity, P3 web-ui) | Read + Edit + Bash + occasional Roslyn | C# semantic | partial — `find_implementations` / `compile_check` underused | 4e707c75, 199aee46, b65dddea (3) |
| Adversarial plan review (read plan.md, write findings) | Read + Edit | markdown review | yes | c322ec61, e30ac026 (2) |
| Run `/audit-toolset` 8-pillar audit | Roslyn (16 calls) + Agent + Bash | full-stack audit | yes — Roslyn used correctly | 102bd77d (1) |
| Run `/mcp-server-surface-test` (full surface audit) | Roslyn (157 / 95 calls per session) | server surface validation | yes — purpose-built | 4868dff2, 74e8a63f, e247fbbc, b5e164a8, fb90f788, ed2acc18, 9b7448de (7) |
| Cross-repo workspace_load via `/mcp-server-stress --target=` | workspace_load → **rejected** | cross-root semantic | **gap — blocked by client-sanctioned-roots** | 17756d59, 36084a03, e247fbbc, 4868dff2 (4) |
| ADR / contract impact analysis ("is X still a restriction?") | Read + Bash + Edit | C# semantic | **NO — `find_consumers`/`impact_assessment`/`find_references` would fit** | d9bd2e13 (1) |
| Quick-wins identification ("top 10 backlog tasks") | Read + Grep | C# semantic + project mgmt | **NO — `complexity_metrics`+`dead_code`+`find_duplicate_helpers` would fit** | b52221ca (1) |
| Workspace health probe on non-C# repo | workspace_list + server_info | classification | **partial — server returned correct empty result, skill misinterpreted it** | fcde52df, c2a4c5b7 (2) |
| Drive every experimental Roslyn tool from a custom harness | mcp__roslyn__* (~51 distinct tools) | server promotion-readiness scoring | yes — purpose-built audit | 396e0724 (1) |
| Doc audit / standardization | Read + Edit + Glob | markdown | yes | 01c5f16b (1) |
| `/ship` pipeline (commit → push → PR → merge → cleanup) | Bash (gh, git) + Edit | release ops | yes | 121407c8, multiple via sweeps (5+) |

**Missed-opportunity rate** (Roslyn-shaped tasks where Claude reached for non-semantic tooling): **9 of 16 inspected task types**. The majority of refactor/audit/impact-analysis work was driven syntactically.

---

## 2a. Roslyn MCP issues encountered (real-work, audit-harness probes excluded)

Audit-harness errors are surfaced separately in §2a-AUDIT — they're *by-design* input-validation probes. Real-work errors below are unambiguously friction.

| # | Tool | Sessions | Inputs (summarized) | Symptom (verbatim ≤200 chars) | Impact | Workaround | Repro confidence |
|---|---|---|---|---|---|---|---|
| R1 | `workspace_load` | 758e3c35 (3×), implied in a26fdd10 | parallel subagents each calling `workspace_load` on their own worktree | *"Invalid operation: The server is already tracking 8 workspaces. Close an existing workspace before loading another."* [758e3c35 subagent a2065e36 L64, a3431796 L30, a8db57a5 L57] | Parallel-mode subagent failed-out on first attempt; orchestrator forced to manual workspace_close. Cost: minutes per wave. | Manual `workspace_close` of an LRU-ish workspace, then retry. | **Deterministic** (≥4 parallel waves → always hits cap) |
| R2 | `test_run`, `workspace_close`, `workspace_reload`, `compile_check` | 4d410565 (8 events), 23c939cd, 18d0e6b3 | calls reusing a `workspaceId` after host process recycled | *"Not found: Workspace '862fb0ed62ad4509a4bbf6435f87b836' not found or has been closed. Active workspace IDs are listed by workspace_list."* [4d410565 subagent a7c6900d L230] | Subagents holding cached `workspaceId` had to detect failure, `workspace_list`, reload, retry. Tax ~ 1 call per cycle. | Detect NotFound → workspace_reload → retry. Subagent comment: *"Looks like another transparent host recycle happened during this session — interestingly, the existing `KeyNotFoundException` with `category=\"NotFound\"` is exactly the bug my PR fixes"* | **Intermittent** (4+ sessions, identical pattern) |
| R3 | `compile_check` | 4d410565 (1) | first call after host recycle | *"Not connected"* (raw transport string, no error envelope) [4d410565 subagent a843af1d L131] | Subagent unsure how to recover — fell back to reload + retry. No `category`/`schemaHint` to consume. | Same as R2. | One-shot (1 session) — but the transport-level "Not connected" with no envelope is a discrepancy from the rest of the surface. |
| R4 | `workspace_load` | 17756d59, 36084a03 (user-relayed), 199aee46, e247fbbc, 4868dff2 | path outside the launching session's repo root (e.g. sibling worktree at `../*-surface-test-20260510T...`, or cross-repo `C:/Code-Repo/TradeWise/...`) | *"Path '...' is not under any client-sanctioned root. Allowed roots: file://C:\\Code-Repo\\Roslyn-Backed-MCP. Check that all required parameters are provided and values match the expected types."* [199aee46 L629; same shape across 4 other sessions] | Hard block — entire downstream Roslyn analysis pathway disabled. Workaround: re-launch Claude inside target repo, or fall back to `dotnet build`/`dotnet test` only. | Re-launch session in target repo, or use non-Roslyn validation. | **Deterministic** (≥5 sessions, identical message) |
| R5 | `document_symbols`, `get_symbol_outline` | a26fdd10 | passing `symbolHandle` (returned by prior `symbol_info`) instead of `filePath` | *"Parameter 'arguments' is invalid: The arguments dictionary is missing a value for the required parameter 'filePath'. (Parameter 'arguments'). Check that all required parameters are provided and values match the expected types."* [a26fdd10 subagent ab3e6403 L90, L92] | Subagent had to abandon handle-based traversal and re-resolve back to `filePath`. | Track filePath alongside any symbolHandle obtained earlier. | One-shot evidenced, but the API asymmetry is structural — any subagent reaching for handle-driven navigation will hit it. |
| R6 | `test_related_files` | a26fdd10, 23c939cd | `filePaths` passed as a JSON-encoded string `"[\"...\", \"...\"]"` instead of a native array | *"Parameter binding failed (JSON deserialization): The JSON value could not be converted to System.String[]..."* [a26fdd10 subagent acc4cacc L178; 23c939cd subagent a447e327 L86] | Subagent fired `ToolSearch` to re-fetch schema, then retried with native array. ~1 round-trip lost. | ToolSearch re-fetch → retry with array. | Intermittent (2 sessions, same root cause) |
| R7 | `workspace_load` | 23c939cd | older skill prompt passed `solutionOrProjectPath` (deprecated name) instead of `path` | *"Parameter 'arguments' is invalid: The arguments dictionary is missing a value for the required parameter 'path'... schemaHint: 'workspace_load(path: string, verbose: bool?, autoRestore: bool?, prewarm: bool??)'"* [23c939cd subagent a3d010cc L85] | `schemaHint` field let subagent self-correct same-turn. ~ 1 retry. | Consume `schemaHint` and retry. | One-shot, but the docs-drift root cause (legacy skill prompts referencing renamed params) is structural. |
| R8 | `Read` (on a Roslyn-MCP skill prompt file, not Roslyn tool per se) | b72b5ba2 | reading `.claude/skills/mcp-server-stress/prompts/prompt.md` | *"File content (31976 tokens) exceeds maximum allowed tokens (25000)"* [b72b5ba2 L85] | Forced offset+limit chunked reads of the project's own skill prompts, fragmenting comprehension. | offset/limit chunked Read. | Deterministic for any file > 25k tokens; cross-cutting Read limitation, not Roslyn-specific, but bites the maintainer's own skill prompts. |
| R9 | `Skill` invocation | 36084a03 | invoking `roslyn-mcp:release-cut` with the plugin prefix | *"&lt;tool_use_error&gt;Unknown skill: roslyn-mcp:release-cut&lt;/tool_use_error&gt;"* [36084a03 L767] | Plugin/global namespace confusion — `release-cut` is registered globally without prefix; `roslyn-mcp:` prefix only applies to a subset of skills. | Drop the prefix. | One-shot but mirrors the broader naming-discoverability pattern. |
| R10 | `Bash` chain calling `verify-release.ps1` (Roslyn-Backed-MCP release flow) | 564d708d | `/release-cut minor` running `bump.ps1` Edit on `.claude-plugin/server.json` | *"Found 2 matches of the string to replace, but replace_all is false"* [564d708d L228] | `/bump` doesn't pre-emptively detect that a version string appears twice in `.claude-plugin/server.json`. Forced manual remediation. | Manual edit. | One-shot, but the shape-change risk is detectable from the release flow's own files. |

**Real-work Roslyn-tool errors total:** 11 distinct failure modes (counts collapsed across sessions). 5 of 11 are workspace-lifecycle (R1–R4), 4 of 11 are schema/parameter friction (R5–R7, R9), 1 is non-Roslyn but Roslyn-skill-prompt-induced (R8), 1 is release-flow safety net (R10).

### 2a-AUDIT — Surface-test harness findings (by-design probe noise, plus 4 real bugs)

The 5 surface-test sessions fired thousands of intentional invalid inputs to validate the server's error envelope. The vast bulk of audit errors (`Parameter '...' is invalid`, `Not found: No symbol...`, `Invalid operation: type has no public instance members`) are exactly what they're supposed to surface — confirmation, not friction. Three exceptions worth promoting:

| # | Audit-only bug | Sessions | Symptom (verbatim) |
|---|---|---|---|
| A1 | `fix_all_preview` `FixAllProviderCrash` for IDE0305 | 4868dff2 (L418) | server reported gracefully with `perOccurrenceFallbackAvailable: true` — but second crash in the IDE0300/IDE0305 family confirms a class of analyzer-fixer flakiness |
| A2 | `get_syntax_tree` byte-cap not enforced | 4868dff2 (BUG-1 in audit report) | 109KB returned vs 40KB documented budget — server-side limit bypass |
| A3 | `test_run` / `test_coverage` timeout returns bare error string without Failure[s] payload | 4868dff2 (BUG-3 in audit report); also 4868dff2 L531/537/550 — *"An error occurred invoking 'test_run'."* with no envelope detail | observability gap; no error category, no schemaHint, no perOccurrence detail |
| A4 | `move_type_to_project_preview` leaks raw `ProjectId` tokens in error string | 4868dff2 L599 | *"Adding project reference from '(ProjectId, #e87edc0a-72a3-4cfb-bc50-311124fd7a03 - C:\\...)' to '(ProjectId, #1bdedfd8-...)' would create a circular dependency"* — leaky abstraction |
| A5 | `goto_type_definition` cannot navigate to runtime/external types (e.g. `bool`) | e247fbbc L271 | *"Invalid operation: Cannot navigate to type definition for 'bool' — neither the type nor any of its type arguments are defined in source. This typically means the type is defined in the .NET runtime or an external assembly."* — surfaces a UX gap (no fallback to metadata browser) |
| A6 | Phase-10 false claim — assistant scoped `format_range_preview` but ran out of context before invoking it; report initially listed it as exercised | 4868dff2 L1001 retrospective | user caught it; coverage adjusted 94→93 — observability/auditability issue in audit-skill itself |

The `--auto-file` flow worked: **74e8a63f filed 15 GitHub issues #614–#628 (6 P2, 9 P3)** based on its run. The audit pipeline is functionally proven.

---

## 2b. Missing Roslyn MCP tool gaps

Each row collects a workflow where no Roslyn tool fit but one semantically should have — forcing fallback to Grep/Edit/raw `dotnet`/Bash.

| # | Task | Sessions (count × ids) | Why Roslyn-shaped | Proposed tool shape | Closest existing tool / why it fell short |
|---|---|---|---|---|---|
| G1 | Make routine flows (`/backlog-sweep:execute`, `/feature-dev`, `/ship`) lazy-load workspace + offer semantic primitives **before** the first Read/Grep | **12 sessions** — c2a4c5b7, 6f973993, 8eaad4ff, 56bb8a22, adcc0ca2, a0a7ecf1, d9bd2e13, b65dddea, b52221ca, 5878f8db, 8405ce90, 4e707c75 | These are all C# code-touching tasks where workspace_load was NEVER called and Roslyn was bypassed end-to-end | **Skill-level "workspace-aware" prelude** — a small probe (`*.sln`/`*.csproj` glob) that, on positive hit, auto-calls `workspace_load` and surfaces the top 5 semantic primitives Claude should reach for in the flow | `workspace-health` skill — exists, but doesn't auto-load. **Recurring (≥3 sessions)** |
| G2 | Cross-root / cross-repo `workspace_load` for stress-test / audit flows | 4 sessions — 17756d59, 36084a03, e247fbbc, 4868dff2 | Audit/stress workflows MUST analyze a target repo other than the launching session's repo | **Opt-in `--target` mode** that widens `client-sanctioned-roots` for the audit duration, or a *separate* `audit_workspace_load(targetRoot, ttl)` primitive with isolation | None exists. `workspace_load` always honors the launching session's pinned root. **Recurring (≥3 sessions)** |
| G3 | LRU eviction policy at the 8-workspace tracking cap | 2 sessions — 758e3c35, implied across all parallel-mode sweeps | Parallel-mode subagents independently call `workspace_load`; orchestrator has no cross-subagent registry to coordinate cleanup | `workspace_load` should accept `evictPolicy="lru"` and silently close the least-recently-used workspace if the cap is hit, or expose a `workspace_close_lru()` helper Claude can call before retry | `workspace_close` exists but is rarely called from skills; no eviction hint. **Recurring (≥2 sessions, multiple subagents within)** |
| G4 | Symbol-handle-driven traversal | 1 session — a26fdd10 | `symbol_info` returns a fat `symbolHandle`; Claude reasonably wants to pivot from it into `document_symbols`/`get_symbol_outline`/`find_implementations` without re-resolving back to filePath | Make `document_symbols`/`get_symbol_outline` accept either `filePath` OR `symbolHandle` (handle resolves to its file); or add a `resolve_handle` primitive | Today the asymmetry forces a roundtrip via `symbol_info → filePath → outline`. **One-shot but structural** |
| G5 | `compile_check`/`format_check`/`validate_workspace` after parallel-worktree merges | many sessions (TradeWise sweep family) — 8eaad4ff, 56bb8a22, adcc0ca2, a0a7ecf1, b65dddea, 5878f8db | Post-merge validation today is `dotnet build` exit code; misses symbol-level regressions (deleted references, dropped overloads) | Promote `validate_workspace` as a one-call post-merge gate that runs compile_check + format_check + a quick diagnostic sweep | `validate_workspace` exists in catalog but skills don't call it. **Recurring (≥5 sessions, structural)** |
| G6 | Repo-language classifier in `workspace-health` | 1 session — fcde52df, also c2a4c5b7 | `/roslyn-mcp:workspace-health` ran on a Rust repo and reported `workspaceCount:0`; Claude then guessed "python repo" from globally-loaded sibling MCP servers | `workspace-health` should glob for `*.csproj`/`*.sln` (and tagging files for other ecosystems) and emit a definitive `applicable: true|false, detectedStack: "rust"` | None. Skill currently reports server status only. User-explicit gap |
| G7 | Release-flow shape-change detector (catch repeated version strings in plugin metadata) | 1 session — 564d708d | `/bump` Edit on `.claude-plugin/server.json` failed when the version string appears twice in the file (plugin + manifest copies) | `/bump` or `mcp__roslyn__version_bump` should scan for all `\"version\":\"X\"` occurrences first and either replace all or warn | `version_bump` exists for csproj XML, not plugin JSON shape. One-shot but reproducible |
| G8 | "Did you actually use Roslyn?" diagnostic at end of session | 1 session — 8405ce90 (the canonical case), plus implicit across G1 | Comprehensive C# refactor audit ran with 0 Roslyn calls despite addenda explicitly steering to Roslyn for C# work | Skill self-check: at end of a flow tagged "C# refactoring" the skill emits a meta-line like `"semantic_calls": 0 — consider rerunning with Roslyn assist"` | None. Pure prompt/skill concern, but server-side telemetry would be the support layer. |
| G9 | "Release this workspace handle" callable from skills before worktree teardown | 4 sessions — b65dddea (5× `dotnet build-server shutdown`), 8eaad4ff (L639 perm-denied), 56bb8a22 (L173), 5878f8db | Windows MSBuild file-locks pile up after parallel Roslyn workspace use; teardown of `.worktrees/*` fails until `dotnet build-server shutdown` flushes them | `workspace_close(handle, force=true, drainProcesses=true)` callable from `ship`/`reconcile-branches` skills before `git worktree remove` | `workspace_close` exists but is not invoked from any teardown-adjacent skill. **Recurring (≥4 sessions)** |
| G10 | Skill-name discoverability ("does Roslyn MCP have a workspace stress test?") | 1 session — a26fdd10 (user had to ask twice) | User asked plain-English question; Claude routed to wrong skill | Reverse index: `mcp__roslyn__semantic_search` over the **skill catalog**, surfacing "`/mcp-server-surface-test` matches your request" | `roslyn-mcp:semantic-find` exists for code, not for skills. One-shot but illustrative of broader discoverability friction |

**Recurring gaps (≥3 sessions):** G1, G2, G3 (counts inflated when subagent waves are counted), G5, G9. Combined they describe a workspace-lifecycle and skill-prompt-hygiene problem rather than a missing-tool problem — the tools largely exist; the routine flows don't reach for them.

---

## 3. Recurring friction patterns (cross-session, top 7)

### P1. Workspace is rarely loaded during real-work C# sessions
**What happened:** 12 of 13 TradeWise sessions (the maintainer's main C# dogfood target) called `workspace_load` zero times. The lone exception was c2a4c5b7 (which succeeded on a 627-doc solution from a worktree path). The "Comprehensive Codebase Refactor Audit" in c2a4c5b7 then performed **51 Reads, 42 Edits, 35 Bashes, 11 Greps, and zero Roslyn semantic calls** after that load. Same shape repeats in d9bd2e13 (ADR-022 impact analysis — exactly the workflow `find_consumers` was built for), b52221ca (top-10 quick wins — exactly the workflow `complexity_metrics`+`dead_code` was built for), 8405ce90 inside Roslyn-Backed-MCP itself, 4e707c75 (SSH parity implementation in DotNet-Network-Documentation), and the entire backlog-sweep family.
**Session spread:** ≥12 sessions across 3 repos; phase mix dominated by refactoring/release-operational.
**Why it recurs:** Routine skills (`/backlog-sweep:execute`, `/feature-dev`, `/ship`) don't include a workspace-aware prelude. There is no automatic "this CWD looks like C#, lazy-load" probe. The maintainer-only `/audit-toolset` (102bd77d) and `/mcp-server-surface-test` (audit sessions) are the only flows that consistently load and use Roslyn.
**What would fix it:** Gap G1. Either bake a "workspace prelude" into the common skills, or have `workspace-health` (or a new lightweight skill) auto-load when invoked from a C# repo.

### P2. Workspace-lifecycle staleness creates a constant retry tax
**What happened:** Counted across the 16 deeply-analyzed Roslyn-Backed-MCP sessions: **38 `workspace_load` calls produced 58 `workspace_reload` calls and 21 `load → reload` sequences**. Concretely in 4d410565 a single sweep produced 8 `NotFound` events on cached `workspaceId`s ("Not found: Workspace '862fb0ed62ad4509a4bbf6435f87b836' not found or has been closed"). Two `compile_check`s emitted the raw transport string *"Not connected"* with no error envelope.
**Session spread:** ≥4 sessions, all release/operational phase.
**Why it recurs:** Host-process recycles and workspace-cache evictions invalidate the `workspaceId` token that subagents are holding. Subagents only learn this by issuing the next call and getting NotFound. The recovery is well-documented in the error message but is not atomic — three round-trips per recovery.
**What would fix it:** Atomic "rebind handle if needed" semantics on long-lived workspaceIds, OR a heartbeat / `server_heartbeat` channel subagents are encouraged to poll, OR (cheapest) an `autoReload=true` flag on `workspace_load` that auto-recovers on NotFound. The existing `schemaHint` pattern (23c939cd) shows the error envelope can carry actionable recovery hints — apply same approach to NotFound.

### P3. 8-workspace tracking cap is too tight for parallel-mode sweeps
**What happened:** 758e3c35 (today's parent session, plan 20260511T184004Z) hit *"Invalid operation: The server is already tracking 8 workspaces. Close an existing workspace before loading another."* three times in 13 subagents. Different worktree paths each time. No LRU eviction. No proactive `workspace_close` from the orchestrator or subagents.
**Session spread:** Deterministic on any parallel sweep with count≥4; observed once explicitly.
**Why it recurs:** The cap is unconditional, but the parallel-execution skill doesn't budget for it. Each subagent independently calls `workspace_load` on its own worktree; nothing closes idle workspaces from prior waves.
**What would fix it:** Gap G3 — either `evictPolicy: "lru"` on `workspace_load`, a `workspace_close_lru()` helper, or a higher cap (e.g. 16) with same defaults. Even a clearer error envelope (`activeWorkspaces: [...]` + `lruCandidate: "X"`) would let subagents recover without orchestrator intervention.

### P4. Client-sanctioned-roots sandbox blocks worktree-driven flows
**What happened:** *"Path '...' is not under any client-sanctioned root. Allowed roots: file://C:\\Code-Repo\\Roslyn-Backed-MCP."* surfaced in 5 sessions — 17756d59 (cross-repo stress-test attempt), 36084a03 (cross-repo discussion, user explicitly closed loop as a gap), 199aee46 (production work on a sibling worktree), and audit sessions e247fbbc + 4868dff2 (surface-tests that wanted to run on a temp worktree copy). User-relayed quote: *"now that we know we cant cross repo using the /mcp-server-stress --target= variable can we (should we) look at the differences..."* [36084a03 L1164].
**Session spread:** ≥5 sessions, mix of refactoring + audit + planning phases.
**Why it recurs:** The Roslyn MCP server hard-pins its file-access sandbox to the launching session's repo root for safety. Anything outside that root (sibling worktrees the user creates for audit isolation, or a target repo for `/mcp-server-stress --target=`) is rejected at `workspace_load`. The shape repeats deterministically.
**What would fix it:** Gap G2. Either widen sanctioned-roots dynamically via an opt-in audit/stress mode (`--allowExternalRoots=<paths>`), or document an alternate path (e.g. "open audit in target repo's own session"). The audit-skill should pre-flight check and fail-fast with the alternate path, not get blocked at Step 1.

### P5. Audit + refactor subagents bypass Roslyn even when the skill addenda explicitly promote it
**What happened:** 8405ce90 — "Comprehensive Codebase Refactor Audit — No Holds Barred" against Roslyn-Backed-MCP itself, the maintainer's own server. 5 dispatched subagents used Bash/Grep/Glob/Read/Monitor; **zero Roslyn semantic calls**. The repo's own backlog-sweep addenda explicitly says "Find callers/consumers → `mcp__roslyn__find_references`, not Grep." The subagents did the opposite. Mirror shape in c2a4c5b7 (TradeWise refactor audit, 1 workspace_load, 0 subsequent Roslyn calls).
**Session spread:** ≥2 sessions, but structural — the audit/refactor skill prompts that dispatch subagents don't seed Roslyn-tool affordances into the subagent's context.
**Why it recurs:** Subagents start in fresh context. The skill prompts that launch them list dozens of capabilities but Roslyn primitives are not at the top of the list. LLM defaults to syntactic exploration.
**What would fix it:** Gap G8. Update the audit/refactor skill prompts to inject a hard-coded "for C# repos, first call `workspace_load`, then prefer these primitives: ..." preamble into each dispatched subagent. Also: a session-end self-check that flags audit sessions with 0 semantic calls.

### P6. Schema friction: array-vs-string-encoding, renamed parameters, handle-vs-filePath asymmetry
**What happened:** Three sub-shapes documented:
- `filePaths` (`string[]`) repeatedly passed as a JSON-encoded string literal — 2 sessions (a26fdd10 acc4cacc L178; 23c939cd a447e327 L86). Error envelope's `category=InvalidArgument` + plain English message let subagents recover same-turn via ToolSearch re-fetch.
- `workspace_load`: older skill prompts use deprecated `solutionOrProjectPath` instead of current `path` — 1 session (23c939cd a3d010cc L85). `schemaHint` field made recovery seamless.
- `document_symbols` / `get_symbol_outline` reject `symbolHandle` (only `filePath`) — 1 session (a26fdd10 ab3e6403 L90, L92). Forced abandonment of handle-driven traversal.
- `find_shared_members` "this server uses parameter name 'column' (1-based), not the LSP-style 'character'" — 1 audit session (396e0724 L184), but the audit retrospective documented **13 tools + 8 prompts** affected by the broader `packageName` vs `packageId`, `project` vs `projectName` family of inconsistencies — *"the most impactful finding — rated high severity"* per the audit's own report.
**Session spread:** ≥4 sessions; the 396e0724 audit makes the parameter-naming inconsistency reproducible across the entire experimental surface.
**Why it recurs:** Tool schemas are not strictly normalized across the surface; some accept LSP-style names, some don't; some array params are confused by the LLM as needing stringification; legacy parameter names linger in skill prompts.
**What would fix it:** (a) Tighten error messages to always identify *which* parameter is wrong (already 18 of 19 errors do; 1 emits bare *"An error occurred"*). (b) Pick a single canonical convention (camelCase, lower-first, no LSP aliases) and migrate. (c) Document the array vs string-encoded-array gotcha somewhere subagents will see. Gap G4 also fits here.

### P7. Skill discoverability and naming-prefix confusion
**What happened:** Two distinct events:
- a26fdd10 — user asked *"do we have a skill that run the roslyn workspace stress test?"*, Claude routed to a non-Roslyn skill; user re-asked *"I was asking specifically about the roslyn mcp server (this project) and a skill that will stress test all of the functionality exposed by the mcp server..."* before Claude found `/mcp-server-surface-test`.
- 36084a03 L767 — Claude invoked `roslyn-mcp:release-cut`, got *"&lt;tool_use_error&gt;Unknown skill: roslyn-mcp:release-cut&lt;/tool_use_error&gt;"* — installed name is bare `release-cut` (global), not plugin-prefixed.
- fcde52df L25 — *"why do you think this is a python repo?"* — user frustrated that `/roslyn-mcp:workspace-health` skill (correctly reporting `workspaceCount:0`) was followed by Claude guessing language from sibling MCP servers' registrations, rather than from the CWD.
**Session spread:** 3 sessions; planning/release phases.
**Why it recurs:** Skill-name namespace is inconsistent (some skills are plugin-prefixed `roslyn-mcp:foo`, some are bare `foo`); user-facing skill search is keyword-only with no semantic synonym table; the `workspace-health` skill's contract reports server state but doesn't surface CWD-level repo classification.
**What would fix it:** Gap G6 (workspace-health language classifier), Gap G10 (skill semantic search), and a one-line `INSTALLED_AS:` row in each skill's frontmatter so Claude can disambiguate prefix vs bare names.

---

## 4. Suggested findings (7)

Ranked. Each is a backlog candidate for the Roslyn MCP maintainer's review — **informational only, not synced to any external backlog**.

### F1 — `routine-flows-never-load-workspace`
- **priority hint:** **high** — 12 sessions across 3 repos; cleanest single lever to lift Roslyn's actual usage rate
- **title:** Bake a workspace-load prelude into routine skills so C# work uses Roslyn by default
- **summary:** In 12 deeply-analyzed sessions (every TradeWise sweep, the comprehensive refactor audit in 8405ce90, the SSH-parity exercise in 4e707c75, the ADR impact analysis in d9bd2e13, the "top 10 quick wins" exercise in b52221ca), `workspace_load` was never called. Tasks that map naturally to `find_references`/`find_consumers`/`complexity_metrics`/`dead_code` instead ran on Read+Grep+Edit. The server isn't broken — it's silent because nobody invites it.
- **proposed action:** Add a workspace-aware prelude to `/backlog-sweep:execute`, `/feature-dev:feature-dev`, `/ship`, and the comprehensive-refactor flow. Probe for `*.sln`/`*.csproj`; on hit, auto-`workspace_load`; surface the top 5 semantic primitives as a hint to dispatched subagents.
- **evidence:** 2b#G1; 3#P1; 3#P5; sessions c2a4c5b7, 6f973993, 8eaad4ff, 56bb8a22, adcc0ca2, a0a7ecf1, d9bd2e13, b65dddea, b52221ca, 5878f8db, 8405ce90, 4e707c75

### F2 — `client-sanctioned-roots-blocks-audit-and-worktree-flows`
- **priority hint:** **high** — 5 sessions; user explicitly closed loop as a capability gap; blocks audit-skill scalability
- **title:** Add opt-in cross-root mode for `/mcp-server-stress --target=` and worktree-isolated audit flows
- **summary:** *"Path '...' is not under any client-sanctioned root. Allowed roots: file://C:\\Code-Repo\\Roslyn-Backed-MCP"* — surfaced in 17756d59 (cross-repo stress blocked at workspace_load), e247fbbc + 4868dff2 (surface-test couldn't analyze its disposable worktree copy), 199aee46 (production work on a sibling P3 worktree), and was relayed verbatim into 36084a03 with the user explicitly framing it as a workflow blocker: *"now that we know we cant cross repo using the /mcp-server-stress --target= variable..."*
- **proposed action:** Either widen `client-sanctioned-roots` dynamically for the duration of audit/stress flows (`--allowExternalRoots=<path>` flag, time-limited), or add an audit-only `audit_workspace_load(targetRoot, ttl=30min)` primitive isolated from the main workspace pool. Document the alternate "open audit in the target repo's own session" pattern in the surface-test skill until the server fix lands.
- **evidence:** 2a#R4; 2b#G2; 3#P4; sessions 17756d59, 36084a03, e247fbbc, 4868dff2, 199aee46

### F3 — `parallel-mode-saturates-8-workspace-cap`
- **priority hint:** **high** — deterministic on any parallel sweep ≥4 waves; blocks the very workflow the maintainer is most invested in
- **title:** Add LRU eviction (or raise the cap) for `workspace_load` saturation during parallel sweeps
- **summary:** 758e3c35 (today's session) hit *"Invalid operation: The server is already tracking 8 workspaces. Close an existing workspace before loading another."* three times in one parallel-mode wave. No LRU eviction. No proactive `workspace_close` from the orchestrator. The cap is unconditional and `workspace_close` is never called in 14 of 16 Roslyn-Backed-MCP sessions despite `workspace_load` being called 38 times.
- **proposed action:** Add `evictPolicy: "lru"` to `workspace_load` (default off, opt-in for parallel skills), or expose a `workspace_close_lru()` helper Claude can call before retry. Alternatively raise the cap to 16. Update the error envelope to include `activeWorkspaces` + `lruCandidate` fields so subagents can recover without orchestrator intervention.
- **evidence:** 2a#R1; 2b#G3; 3#P3; session 758e3c35

### F4 — `parameter-schema-inconsistency-and-array-stringification`
- **priority hint:** **medium-high** — 4 sessions; the 396e0724 audit found 13 tools + 8 prompts with the same root cause, rated *"high severity"* by the audit itself
- **title:** Normalize parameter naming across the surface and document the array-vs-stringified-array gotcha
- **summary:** Three distinct shapes: (a) `filePaths` (string array) was repeatedly passed as a JSON-encoded string literal in a26fdd10 + 23c939cd; (b) deprecated `solutionOrProjectPath` was passed in 23c939cd because skill prompts hadn't caught up to the rename to `path`; (c) the experimental-promotion exercise in 396e0724 surfaced `packageName` vs `packageId`, `project` vs `projectName`, and `column` vs LSP-style `character` across 13 tools and 8 prompts — and one of those errors emitted only a generic *"An error occurred"* without parameter detail. The `schemaHint` field is doing real work when present (it enabled same-turn recovery in 23c939cd a3d010cc L85) — apply consistently.
- **proposed action:** Pick a canonical naming convention. Migrate. Make every InvalidArgument error envelope carry `schemaHint`. Add a "common gotchas" snippet to each tool's docs (array vs string-encoded array; column vs character). The 396e0724 audit report has the full inventory.
- **evidence:** 2a#R5, R6, R7, R9, A3; 2b#G4; 3#P6; sessions a26fdd10, 23c939cd, 396e0724

### F5 — `windows-msbuild-file-locks-block-worktree-teardown`
- **priority hint:** **medium** — 4+ TradeWise sessions; recurring orchestration tax; Roslyn-server-adjacent rather than core
- **title:** Surface a `workspace_close(drainProcesses=true)` callable from ship/reconcile-branches skills before worktree removal
- **summary:** Across b65dddea (5 separate `dotnet build-server shutdown` Bash invocations across L199/283/406/612/817), 8eaad4ff (L639 "failed to delete '.worktrees/...': Permission denied"), 56bb8a22 (L173), and 5878f8db (L88), the orchestrator can't `git worktree remove` directly because MSBuild file-locks held by the Roslyn workspace's underlying process pin the directory. The workaround is `dotnet build-server shutdown` before each teardown — repeated 5× in a single session is direct evidence of the friction.
- **proposed action:** Wire `workspace_close(force=true, drainProcesses=true)` into the ship-skill and reconcile-branches-skill teardown sequences. Document the pattern in the worktree-cleanup section of CLAUDE.md/AGENTS.md.
- **evidence:** 2b#G9; 3#P5 (indirect); sessions b65dddea, 8eaad4ff, 56bb8a22, 5878f8db

### F6 — `workspace-health-misclassifies-non-csharp-repo`
- **priority hint:** **medium** — single session evidence but high-confidence pattern; one-line fix
- **title:** Make `workspace-health` probe the CWD for `*.csproj`/`*.sln` and emit `applicable: true|false, detectedStack`
- **summary:** fcde52df ran `/roslyn-mcp:workspace-health` on SysLog-Server (Rust). Server returned correctly empty (`workspaceCount: 0`). Claude then inferred *"this is a python repo"* from the globally-loaded `python-refactor` MCP registration. User: *"why do you think this is a python repo?"* [L25]. The skill emits a definitive server-status report but not a CWD-level "applicable: yes|no for C#" verdict. A minimal Glob would close the loop.
- **proposed action:** Update `workspace-health` skill to glob `*.csproj`/`*.sln`/`Directory.Build.props` in CWD, and emit a top-level `applicable: <bool>, detectedStack: <"csharp"|"rust"|"python"|...>`. Suppress the "consider loading the workspace" hint when applicable is false.
- **evidence:** 2b#G6; 3#P7; session fcde52df

### F7 — `audit-and-refactor-skills-bypass-roslyn-end-to-end`
- **priority hint:** **medium** — structural; 2 strong examples; lifts the floor on every refactor flow if fixed
- **title:** Add a session-end self-check that flags C#-tagged audit/refactor flows with 0 semantic Roslyn calls
- **summary:** 8405ce90 ("Comprehensive Codebase Refactor Audit — No Holds Barred" against Roslyn-Backed-MCP) ran with 5 subagents and zero Roslyn calls. c2a4c5b7 (TradeWise refactor audit) loaded the workspace once then did 51 Reads + 42 Edits + 35 Bashes + 11 Greps + zero semantic Roslyn calls afterward. The addenda in this very repo explicitly say "for C# semantic work, use Roslyn first" — the dispatched subagents don't see that guidance and default syntactic.
- **proposed action:** (i) Update audit/refactor skill prompts to inject Roslyn-first preamble into each dispatched subagent. (ii) Add a session-end skill self-check: tag the session class, count Roslyn calls, emit a meta-line if the class is C#-refactor-audit and count is 0. Telemetry could later promote this to a hook.
- **evidence:** 2b#G1, G8; 3#P5; sessions 8405ce90, c2a4c5b7

---

## 5. Meta-note

**Phase mix.** 47 deeply-analyzed sessions split 10 refactoring / 21 release-operational / 7 planning-docs / 9 audit. Roslyn-Backed-MCP itself is overwhelmingly release+operational dogfood (16/16 sessions); TradeWise is the only sustained refactoring corpus (12/13 sessions); DotNet-Network-Documentation is mixed; SysLog-Server is Rust (correctly skipped). The release-operational majority biases findings toward sweep-orchestration and workspace-lifecycle issues over deep semantic-tool failure modes — and indeed, **no `find_references`/`compile_check`/`rename_apply`/`extract_method` semantic miscount complaints** appeared across 300 real-work Roslyn calls. Core semantic operations are sound; pain is at the workspace lifecycle, schema, and skill-affordance layer.

**Where friction concentrates.** Two surfaces dominate: (a) **workspace lifecycle** — 8-cap saturation, host-recycle NotFound, sanctioned-roots blocking, file-locks at teardown. (b) **discoverability / ergonomics** — routine flows never invite Roslyn in; skill names disagree on prefix; `workspace-health` doesn't classify the repo. The "reliability" and "coverage" surfaces are healthier than usage rate suggests, because the tools simply aren't being called.

**Repo-specific skew.** Three of the five sandbox-rejection events were in audit-context sessions (17756d59 stress-test, e247fbbc + 4868dff2 surface-test) — meaning the audit-skill design is what made the gap visible. The same shape recurred in real production work (199aee46), so it's not purely an audit-skill artifact. TradeWise's near-zero Roslyn usage despite 12 multi-hour C# sessions is the single most surprising finding — it's not a server problem, it's a skill-prompt and adoption pattern problem, and F1 + F7 together address it.

**What I'd change about default Roslyn-MCP usage next time.** Adopt the lazy-load prelude (F1) before anything else. Today's mental model is "the user/Claude must explicitly choose Roslyn"; the reverse posture — "Roslyn is the default for any C# work and you opt out for non-semantic tasks" — would lift utilization by an order of magnitude based on the missed-opportunity rate (9 of 16 task types).

**Window adequacy.** 14 days was long enough — findings cluster across at least 5 different repos and a healthy mix of phases. The signal-to-noise was good: 23,937 Roslyn calls produced only 40 errors (28 real-work + 12 audit-probe-by-design), and the 11 distinct real-work failure modes recur across multiple sessions with consistent shapes. No need to widen on the next retro; if anything a 7-day window would have caught the highest-value patterns. Suggest 14 days remain the default cadence.

---

*End of report. No external sync. Local artifact only.*
