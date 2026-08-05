---
generated_at: 2026-08-05T21:00:25Z
window: "last 14 days (2026-07-22 → 2026-08-05)"
host_repo: roslyn-backed-mcp
host_repo_path: C:/Code-Repo/Roslyn-Backed-MCP
sources_scanned: ["claude-code", "codex"]
sources_degraded: false
sources_degraded_reason: null
sessions_scanned:
  claude: 75
  codex: 677
  total: 752
sessions_included:
  claude: 10
  codex: 30
  total: 40
codex_subagent_sessions_rolled_up: 131
repos_covered: [roslyn-backed-mcp, biofiletransfer, bioremote, dotnet-firewall-analyzer, dotnet-network-documentation, snipcue, tradewise, claude-global-config, windows-system32]
phase_mix:
  refactoring: 27
  release_operational: 1
  planning_docs: 3
  mixed: 9
phase_mix_by_agent:
  claude: { refactoring: 0, release_operational: 1, planning_docs: 3, mixed: 6 }
  codex: { refactoring: 27, release_operational: 0, planning_docs: 0, mixed: 3 }
issues_by_attribution:
  server_side: 14
  harness_specific: 0
  unknown: 3
truncated: true
---

# Roslyn MCP multi-session retrospective — 2026-08-05 — 14-day window — Claude Code + Codex

## 0. Sources and coverage

**Both harnesses were read.** Method note (read first — it changes how every number below should be interpreted):

| Source | Location(s) scanned | Files found in window | Real Roslyn MCP signal | Notes |
|---|---|---|---|---|
| Claude Code | `~/.claude/projects/*/*.jsonl` (all 21 repo dirs) | 75 `.jsonl` files, mtime-filtered to the 14-day window | 10 sessions (7 with actual `mcp__roslyn__*`/`mcp__plugin_roslyn-mcp_roslyn__*` tool calls, 3 with only skill/text mentions) | See namespace correction below |
| Codex — live | `~/.codex/sessions/YYYY/MM/DD/rollout-*.jsonl` | 308 rollout files | 0 real `mcp__roslyn` function_calls in-window | Naive string-grep for `mcp__roslyn` matched 87 of these; **all 87 were false positives** — tool-search schema dumps or CLAUDE.md preamble text, zero had an actual `namespace=="mcp__roslyn"` function_call |
| Codex — archived | `~/.codex/archived_sessions/rollout-*.jsonl` (flat, no date dirs) | 369 rollout files | 722 real `mcp__roslyn` function_calls across 77 sessions (before subagent roll-up) | This directory carried **100% of the real Codex-side Roslyn MCP evidence** in this window — a date-partitioned-only scan would have found zero |

**Two measurement traps were caught and corrected before analysis, not after:**

1. **Naive `mcp__roslyn` string grep is noise, confirmed empirically.** A plain case-insensitive grep for `mcp__roslyn` matched 87/308 live Codex files and 157/369 archived files, but a strict filter on `.payload.type=="function_call" and .payload.namespace=="mcp__roslyn"` found **zero** real calls in any of the 87 live-dir matches and only 77/157 (49%) of the archived-dir matches actually had a real call. The gap is tool-search schema dumps and instruction-preamble text, exactly as this prompt warned.
2. **Claude Code tool-name namespace correction.** A prior retro in this repo (`20260608T203050Z_...md`) had already hit and corrected this once: Claude Code sessions can register Roslyn MCP tools under **two different namespaces** — `mcp__roslyn__*` (the dev-build registration used only inside this repo) or `mcp__plugin_roslyn-mcp_roslyn__*` (the marketplace-plugin namespace used by every consumer repo). This extraction was verified against the live corpus before writing the matcher (`grep` confirmed 9 in-window `mcp__plugin_roslyn-mcp_roslyn__*` calls existed) and both namespaces are matched and normalized to the flat `mcp__roslyn__<tool>` form used throughout this report. Missing this a second time would have silently zeroed out most of Claude's cross-repo signal (BioRemote, DotNet-Firewall-Analyzer, SnipCue, TradeWise all load the marketplace plugin, not the dev build).

**Codex subagent roll-up.** 131 of the 242 relevant-by-signal Codex rollouts had `thread_source=="subagent"` with `subagent_role=="guardian"` — these are Codex's per-tool-call risk-approval judge invocations (a safety layer that reads a *description* of a planned action and approves/denies it), not independent coding sessions. Only 1 of the 131 guardian rollouts contained an actual `mcp__roslyn` function_call, and it was not pursued further as an edge case. All 131 are excluded from `sessions_included` and reported only as a rolled-up count, per the task's subagent-attribution requirement.

**Budget cap.** Pre-cap relevant sessions: claude=10 (all included, no cap needed), codex=111 (top-level `thread_source=="user"` sessions only, excluding the 131 guardian rollouts). The per-source-floor-then-global-rank cap (floor 15, total cap 40) took all 10 Claude sessions (below the floor) and the top 30 Codex sessions by Roslyn-MCP-invocation count, dropping 81 lower-signal Codex sessions (mostly zero-call, text-mention-only rows). **`truncated: true`** — see §5 for what this means for confidence in codex-only findings.

## 1. Session classification

| agent | session (short) | repo | date | phase | notes |
|---|---|---|---|---|---|
| claude | `a3b2d83d` | roslyn-backed-mcp | 2026-07-08 | planning_docs | `/refactorv2` pass-1 dogfood; `document_symbols` errored on `.sln` path, corrected retry |
| claude | `d64405f0` | bioremote | 2026-07-28 | mixed | refactorv2 pass-2 backlog authoring + PR #1036 merge; no C# touched |
| claude | `5facb12d` | dotnet-firewall-analyzer | 2026-08-04 | mixed | refactorv2 pass-1, 113 backlog rows filed + commit/push; no C# touched |
| claude | `2d4b081e` | snipcue | 2026-08-03 | planning_docs | refactorv2 pass-4 harness run + global script bugfixes |
| claude | `41bc6234` | tradewise | 2026-07-28 | mixed | refactorv2 pass-3, 111 backlog rows + branch/commit/push |
| claude | `2357728f` | snipcue | 2026-07-28 | planning_docs | refactorv2 pass-3 orchestrator; scoring delegated to 16 subagents (not in this transcript) |
| claude | `8c81c12e` | roslyn-backed-mcp | 2026-07-23 | release_operational | `server_info` readiness probe only |
| claude | `55209271` | dotnet-firewall-analyzer | 2026-08-03 | mixed | backlog-sweep orchestration, 4 waves; 2 grep-based C# searches where a Roslyn tool applies |
| claude | `7adb9368` | ~/.claude (global config) | 2026-07-30 | mixed | MCP tooling maintenance, no C# involved (expected — not a gap) |
| claude | `91049964` | Windows/system32 | 2026-07-23 | mixed | OS resource diagnostics, not a code repo |
| codex | `019fa0ca` | roslyn-backed-mcp | 2026-07-26 | refactoring | top-10 remediation; roslyn = read/verify only, all mutation via native `apply_patch` |
| codex | `019fa39c` | roslyn-backed-mcp | 2026-07-27 | mixed | continuation session; `apply_patch` refactor + scratch backlog drafts |
| codex | `019fa18b` | roslyn-backed-mcp | 2026-07-27 | refactoring | 3rd session in sequence; decomposition via `apply_patch`, heavy `validate_recent_git_changes` loop |
| codex | `019faa80` (c605) | dotnet-firewall-analyzer | 2026-07-28 | refactoring | health scan, 2 failed `extract_type_preview` attempts, pivot to hand refactor |
| codex | `019fb31d` | dotnet-network-documentation | 2026-07-30 | refactoring | LoggerMessage conversion + magic-string extraction (Roslyn tool left unused) |
| codex | `019fb39e` | dotnet-firewall-analyzer | 2026-07-30 | refactoring | pure bug-fix/compile_check loop |
| codex | `019fb06d` (9356) | bioremote | 2026-07-30 | refactoring | compile_check-driven loop only, no symbol tools used |
| codex | `019faf23` | bioremote | 2026-07-29 | refactoring | `find_references_bulk` + `apply_text_edit`, RDP-protocol method replacement |
| codex | `019fa0a1` (fb11) | dotnet-firewall-analyzer | 2026-07-26 | refactoring | `analyze_data_flow`, `extract_type_preview` blocked by guardrail |
| codex | `019fa003` | dotnet-firewall-analyzer | 2026-07-26 | refactoring | impact-analysis-heavy session |
| codex | `019fafc8` | bioremote | 2026-07-29 | refactoring | `rename_preview`/`rename_apply` + 13x `compile_check` cascade-chasing loop |
| codex | `019fab52` | biofiletransfer | 2026-07-29 | refactoring | complexity/dead-code cleanup; 2 failed `extract_type_preview` attempts |
| codex | `019fa0a1` (dbf5) | roslyn-backed-mcp | 2026-07-26 | refactoring | read-only re-verification of a branch claiming "remediation complete"; flaky `test_run` |
| codex | `019fb39d` (99fc) | tradewise | 2026-07-30 | refactoring | duplication/complexity remediation pass |
| codex | `019fa0cb` | tradewise | 2026-07-26 | refactoring | purely investigative — 11 `get_source_text` reads, no mutation |
| codex | `019fb06d` (db4e) | dotnet-network-documentation | 2026-07-30 | mixed | 10-row backlog batch; 118 shell calls vs. 15 roslyn calls |
| codex | `019faec2` | biofiletransfer | 2026-07-29 | refactoring | compile_check-driven fix loop, no push/PR (mid-flight) |
| codex | `019fae3e` | bioremote | 2026-07-29 | refactoring | fix/verify loop interrupted by workspace eviction |
| codex | `019faa80` (9db9) | biofiletransfer | 2026-07-28 | refactoring | best-behaved session — every mutation went through Roslyn tools |
| codex | `019fa93f` | dotnet-firewall-analyzer | 2026-07-28 | refactoring | impact analysis blocked by the same `extract_type_preview` refusal shape |
| codex | `019fb43f` | tradewise | 2026-07-30 | mixed | 15 min Roslyn review, then ~2h opaque terminal work |
| codex | `019fae89` | biofiletransfer | 2026-07-29 | refactoring | compile_check-heavy fix loop |
| codex | `019fae92` | dotnet-network-documentation | 2026-07-29 | refactoring | impact-analysis session |
| codex | `019fa004` | roslyn-backed-mcp | 2026-07-26 | refactoring | compile_check/test_run verify loop |
| codex | `019fb39d` (f02d) | dotnet-network-documentation | 2026-07-30 | refactoring | symbol_search + compile_check |
| codex | `019faa77` | snipcue | 2026-07-28 | refactoring | survey-then-fix; CS8115/CS0246 fixes confirmed via compile_check |
| codex | `019fa39c` (3c63) | biofiletransfer | 2026-07-27 | refactoring | module-by-module edit/verify, real compiler errors fixed |
| codex | `019fa002` | dotnet-network-documentation | 2026-07-26 | refactoring | MA0006 `fix_all_preview` blocked (no code-fix provider), manual fix applied |
| codex | `019f9eea` | bioremote | 2026-07-26 | refactoring | credential-adjacent edits + proactive `security_diagnostics` pass |
| codex | `019f9781` | snipcue | 2026-07-25 | refactoring | multi-part fix session; `validate_workspace` mislabeled compile-error on 5/5 calls |

**Aggregate mix:** 40 sessions = **27 refactoring / 1 release-operational / 3 planning-docs / 9 mixed.**

**Per-harness mix:** claude = 0 refactoring / 1 release-operational / 3 planning-docs / 6 mixed (10 sessions). codex = 27 refactoring / 0 release-operational / 0 planning-docs / 3 mixed (30 sessions). **This is a hard split, not a soft skew** — Claude's entire in-window sample was `/refactorv2`-harness dogfooding (backlog authoring, no C# mutation) plus one release probe and two non-repo maintenance sessions; Codex's entire in-window sample was hands-on C# remediation. Neither harness's sample is representative of the other's typical usage this window — see §5.

## 2. Task inventory (aggregated, with agent + session ids)

279 individual task rows were logged across the 40 sessions, collapsing to 169 distinct (tool, domain, outcome) groups. The table below shows every group repeated ≥3 times (24 groups, all well-covered/correct-tool-choice usage) plus, separately, **every** `missed_opportunity` row regardless of repeat count (9 groups) since that is the highest-value signal for tool-selection gaps.

**High-repeat, correctly-tooled groups (≥3 occurrences):**

| Tool/command | Domain | Right tool? | Repeats | Agents | Sample sessions |
|---|---|---|---|---|---|
| `mcp__roslyn__compile_check` | build-verify | yes | 13 | codex | `019fae89`, `019fa004`, `019fb39d` |
| `mcp__roslyn__workspace_load` | workspace | yes | 10 | codex | `019fb39e`, `019fb06d`, `019fa0a1`, +6 |
| `mcp__roslyn__compile_check` | compile-validation | yes | 8 | codex | `019faa77`, `019fa39c`, `019f9eea`, `019f9781` |
| `mcp__roslyn__symbol_search` | symbol-search | yes | 7 | codex | `019fb39e`, `019fb43f`, `019fae92`, `019fb39d` |
| `mcp__roslyn__compile_check` | compile-check (loop) | yes | 7 | codex | `019fb39e`, `019fb06d`, `019faf23`, +2 |
| `mcp__roslyn__workspace_load` | workspace bootstrap | yes | 5 | codex | `019fafc8`, `019fab52`, `019fa0a1`, +2 |
| `mcp__roslyn__compile_check` | build verification | yes | 5 | codex | `019fafc8`, `019fab52`, `019fa0a1`, +2 |
| `mcp__roslyn__compile_check` | verification | yes | 5 | codex | `019fb06d`, `019faec2`, `019fae3e`, `019faa80` |
| `mcp__roslyn__workspace_load` | workspace (na tasks) | na | 5 | codex | `019fb43f`, `019fae89`, `019fae92`, +2 |
| shell `write_stdin` (terminal) | terminal | na | 5+5 | codex | two separate 5-session clusters — opaque long-running terminal blocks |
| `mcp__roslyn__project_diagnostics` | diagnostics | yes | 4 | codex | `019fb39e`, `019fb06d`, `019fae89` |
| shell edit + `compile_check` | bugfix | yes | 4 | codex | `019fb06d`, `019faec2`, `019fae3e` |
| `Write`/`Edit` (scratch tooling) | scratch-tooling | yes | 3 (×2 groups) | claude | refactorv2 harness scripts, all 6 claude planning sessions |
| native `apply_patch` | refactor | **missed_opportunity** | 3 | codex | `019fa18b`, `019faa80`, `019fb31d` |
| `mcp__roslyn__extract_type_preview` | refactor | yes | 3 | codex | `019faa80` (×2), `019fa93f` |
| `mcp__roslyn__find_references` | find-references | yes | 3 | codex | `019fb39e`, `019fa0a1`, `019fa003` |
| `mcp__roslyn__workspace_load`/`reload` | workspace | yes | 3 | codex | `019fb39e`, `019faf23`, `019fa003` |
| `mcp__roslyn__find_references` | analysis | yes | 3 | codex | `019faa80`, `019fa93f` |
| `mcp__roslyn__get_complexity_metrics` | complexity | yes | 3 | codex | `019fb43f`, `019fb39d` |
| `mcp__roslyn__impact_analysis` | impact-analysis | yes | 3 | codex | `019fae92`, `019fb39d` |
| `mcp__roslyn__project_diagnostics` | diagnostics-survey | yes | 3 | codex | `019fa39c`, `019fa002` |

**Every missed-opportunity row (right_tool = "missed_opportunity"):**

| Task | Tool actually used | Domain | Repeats | Sessions |
|---|---|---|---|---|
| Decompose 4 long methods into named helpers to hit complexity/LOC targets; multi-file signature-change refactor across ~30 files; replace repeated magic-string literal with a constant | native `apply_patch` | refactor | 3 | `019fa18b`, `019faa80` (c605), `019fb31d` |
| Search C# source for sync-over-async pattern, constructor call sites, and a 4-method usage census | `rg` via shell | search | 3 | `019faec2`, `019fae3e` |
| Scan C# src/tests for local-storage/registry API usage (security surface check) | `Bash grep -rn` | security scan | 1 | claude `55209271` |
| Locate `ConfigResolved` symbol usage in tests + diff new test methods | `Bash grep -rn` + `git diff` | verification | 1 | claude `55209271` |
| Extract `DocumentSetPersistenceService` (~330 lines) out of `RefactoringService` | native `apply_patch` (44 calls) | refactor | 1 | codex `019fa0ca` |
| Confirm `SweepExpiredForks(string)` overload is dead, then delete it by hand | `find_unused_symbols` → `apply_patch` | dead-code | 1 | codex `019fa39c` |
| Reduce cyclomatic complexity of flagged methods | shell edit (content redacted) | refactor | 1 | codex `019fb06d` |
| Apply whitespace formatting across 15 touched files | `dotnet format whitespace` CLI | formatting | 1 | codex `019fb06d` |
| Reconcile a manual class split by reading two files side by side | `Get-Content -LiteralPath` | refactor | 1 | codex `019fa93f` |

## 2a. Roslyn MCP issues encountered

**17 issue rows, all from Codex sessions except one.** 14/17 attributed `server-side`, 0 `harness-specific`, 3 `unknown`. No genuine client-plumbing (namespace/marshalling/timeout-policy) defect was found in this window on either harness.

| Tool | Agents | Sessions | Symptom | Repro confidence | Attribution |
|---|---|---|---|---|---|
| `mcp__roslyn__extract_type_preview` | codex | 5 (2 repos) | Refuses extraction when selected members reference state left on the source type — no corrected-member suggestion, no auto-expand | deterministic | server-side |
| `mcp__roslyn__extract_type_preview` | codex | 2 | Rejects a typo'd/misremembered `memberNames` entry outright, no fuzzy suggestion | intermittent | server-side |
| `mcp__roslyn__compile_check` | codex | 5 (4 repos) | `files[]` spanning >1 project silently falls back to full-project compile, then filters — correct but 7.3s+ slower than the caller expected | deterministic | server-side |
| `mcp__roslyn__compile_check` | codex | 1 | `workspace not found` after ~37min idle Lru eviction | one-shot | server-side |
| `mcp__roslyn__test_run` | codex | 1 | `WorkspaceEvictedException` mid-run failed 2/110 tests non-deterministically; identical rerun 90s later passed 110/110 | one-shot | server-side |
| `mcp__roslyn__test_run` | codex | 1 | Large compound-OR filter (~10 test classes) ran 120.37s then returned bare `"An error occurred invoking 'test_run'."` with no diagnosable detail | one-shot | unknown |
| `mcp__roslyn__validate_workspace` | codex | 1 | `overallStatus: compile-error` with a phantom 1-count diagnostic despite 0 errors/0 warnings/35-35 tests passing — reproduced on 5/5 calls in the session | deterministic | server-side |
| `mcp__roslyn__project_diagnostics` | codex | 2 | Summary-mode totals (`totalDiagnostics`/`totalInfo`) don't respect the active severity filter while `distinctDiagnosticIds`/`diagnosticGroups` do | intermittent | server-side |
| `mcp__roslyn__validate_recent_git_changes` | codex | 1 | Internal `git status` call timed out (10s) and silently fell back to reporting `clean` with `changedFilePaths: []` despite ~30 real uncommitted files; recurred identically twice in the same session | one-shot | server-side |
| `mcp__roslyn__find_references` | codex | 2 (1 repo) | `metadataName` lookup against an overloaded method returns an `"ambiguous": true` disambiguation payload instead of references, forcing a second call | intermittent | server-side |
| `mcp__roslyn__apply_text_edit` | codex | 1 | Rejected an edit with a Roslyn syntax-check error caused by an incorrect `endLine`/`endColumn`; succeeded on retry with a corrected range | one-shot | server-side |
| `mcp__roslyn__apply_text_edit` | codex | 1 | Identical edit re-applied 30s after prior success; tool reported success again instead of a no-op signal | one-shot | unknown |
| `mcp__roslyn__suggest_refactorings` | codex | 1 | Identical query repeated 3x within ~90s with no intervening state change | one-shot | unknown |
| `mcp__roslyn__split_class_preview` | codex | 1 | Fails outright on a nonexistent `memberNames` entry, no suggestion | one-shot | server-side |
| `mcp__roslyn__fix_all_preview` | codex | 1 | 0 fixed items for `MA0006` — no code-fix provider registered for that analyzer diagnostic | one-shot | server-side |
| `mcp__roslyn__get_code_actions` | codex | 1 | Zero code actions at a caret position with no diagnostic reported there (documented tool behavior, but a dead end for the caller) | one-shot | server-side |
| `mcp__roslyn__document_symbols` | claude | 1 | Errored `FileNotFound` when pointed at a `.sln` path instead of a source document | one-shot | server-side |

Full verbatim quotes for every row above (agent- and session-tagged) are in the underlying workflow evidence at `C:/Users/daryl/AppData/Local/Temp/claude/C--Code-Repo-Roslyn-Backed-MCP/776682eb-03d6-4590-b9b8-52e14dd71955/scratchpad/out/issueRows.json` — retained for the maintainer's convenience; not part of this report's canonical content.

## 2b. Missing tool gaps

| Task | Agents | Sessions | Why Roslyn-shaped | Real gap or discoverability? |
|---|---|---|---|---|
| Cross-file, type-aware usage search (sensitive-API sweep, renamed-symbol lookup, sync-over-async pattern, constructor call-site census, method-usage census) done via `grep`/`rg` instead of semantic tools | **both** | 3 (1 claude, 2 codex) | `find_references`/`find_type_usages`/`symbol_search`/`semantic_grep` exist precisely for this; grep can't resolve indirect construction or distinguish real call sites from comments | **discoverability** — `recommend_workflow` exists to route exactly this and was never invoked in any of the 3 sessions |
| Extract/move a type out of a larger file, carrying member dependencies and using-directives | codex | 2 | `extract_type_preview`/`move_type_to_file_preview` exist for exactly this, but `extract_type_preview` was tried and refused twice with no recovery path | **real gap** — needs an auto-expand-dependencies mode or a structured blocking-dependency list in the refusal payload |
| Decompose a method flagged by Roslyn's own `get_complexity_metrics` into named helpers to hit a target CC | codex | 2 | The refactor target was identified by a Roslyn tool; `extract_method` is the textbook next step but operates on one line-range per call | **real gap** — no guided "decompose by complexity" mode exists for a multi-extraction goal |
| Apply whitespace formatting across 15 files via `dotnet format` CLI instead of Roslyn's formatter | codex | 1 | `format_document_apply`/`format_range_apply` respect the already-loaded in-memory workspace and `.editorconfig` | **real gap** — no batch/multi-file entry point exists, so the CLI wins on convenience for >1 file |
| Replace a magic-string literal with a named constant via `apply_patch` instead of `replace_string_literals_preview` | codex | 1 | Textbook use case for the existing tool, no structural blocker apparent | **user_error** |
| Delete a confirmed-dead method overload via `apply_patch` immediately after identifying it dead via `find_unused_symbols`/`find_references` | codex | 1 | `remove_dead_code_preview/apply` is the exact next step; no evidence it was tried and failed | **user_error** |

## 3. Recurring friction patterns

1. **`extract_type_preview`/`split_class_preview` refusals are dead ends.** 6 codex sessions, 2 repos. The compile-safety guardrail ("the selected members reference state that would remain on the source type") and the member-not-found rejection are both correct/safe, but neither gives a machine-actionable next step — every one of the 6 sessions abandoned the tool for a hand-written split rather than retrying. *Fix:* add a structured `blockingDependencies` field (member names + why each blocks) and/or an auto-expand-dependencies mode.
2. **`compile_check`'s multi-project fallback is silent.** 5 codex sessions, 4 repos, near-identical wording each time. A `files[]` scope spanning >1 `.csproj` silently compiles the full project(s) then filters — correct, but the caller pays full-project latency they never asked for and can only discover the fallback by reading `restoreHint` prose. *Fix:* expose `actualScope` vs `requestedScope` as a structured field.
3. **Workspace Lru eviction causes hard failures and flaky tests.** 2 codex sessions. An idle (~37min) workspace gets evicted mid-session; `compile_check` returns a hard "not found" error, `test_run` throws `WorkspaceEvictedException` mid-run and produces a non-deterministic 2/110 test failure that a clean rerun 90s later disproves. *Fix:* refresh the Lru TTL on every tool call against a workspace, and/or have eviction-adjacent tools auto-reload-and-retry once before surfacing failure.
4. **Aggregate/status rollup fields disagree with their own detail data.** 3 codex sessions. `project_diagnostics` summary totals don't respect the active severity filter while the detail fields do; `validate_workspace` reported `compile-error` with a phantom diagnostic on a fully green build, reproducing on all 5 calls in that session. *Fix:* compute summary/status fields from the same filtered detail set as the detail response (single source of truth).
5. **`find_references` by `metadataName` against an overloaded method returns a disambiguation payload, not references.** 2 codex sessions, 1 repo, same-repo recurrence. This is arguably correct behavior for an inherently ambiguous lookup, but nothing warns the caller in advance. *Fix:* accept an optional parameter-signature hint to resolve common overload cases in one call; have tool descriptions/`recommend_workflow` note the two-call shape.
6. **Both harnesses default to grep over semantic search at the same rate — the one confirmed cross-harness data point.** 1 claude + 2 codex sessions, identical behavior. `recommend_workflow` was never invoked in any of the three. This is an ergonomics/discoverability gap, not a functional defect, and is the only pattern in this dataset with genuine cross-harness confirmation (both harnesses behave the *same*, not differently).
7. **`validate_recent_git_changes` gave a false-clean signal about the exact thing it exists to check.** 1 codex session, but material: the internal `git status` call's fixed 10s timeout was exceeded and the tool silently reported `clean`/`[]` instead of degraded, missing ~30 real uncommitted files. Recurred identically a second time in the same session. *Fix:* on timeout, return a distinct degraded/unknown status, never `clean`; make the timeout configurable for larger repos.
8. **Cross-harness parity — no defect found, but the sample cannot prove that.** *(Reserved slot, as required.)* Of the 17 issue rows, 16 are codex-only and 1 is claude-only (`document_symbols` vs. a `.sln` path), and no tool in this dataset was called by both harnesses in a comparable way except the grep-vs-semantic-search pattern above, where both harnesses behaved *identically*. This is an absence-of-evidence finding driven by the phase-mix split in §1 (claude did 0 of 27 refactoring-phase sessions this window) — **not** a demonstrated absence of parity defects. Treat cross-harness parity as unverified, not clean, until a future window deliberately runs the same refactor-tool-heavy task through both harnesses against the same repo.

## 4. Suggested findings (up to 8)

Informational only — not filed anywhere; the maintainer decides what (if anything) becomes a backlog row.

1. **`extract-type-preview-refusal-retry-path`** — priority **high**. *Add blocking-dependency data to `extract_type_preview` refusal payloads.* Highest recurrence in the dataset (6 sessions, 2 repos, near-identical wording in 5). Every occurrence ended in tool abandonment, never a successful retry. Codex-only observed; claude never attempted a comparably sized extraction this window. **Surface: server.** Evidence: §3 pattern 1, §2a `extract_type_preview`/`split_class_preview` rows, §2b extract/move-type gap row.
2. **`validate-workspace-status-rollup-bug`** — priority **high**. *Fix `validate_workspace`/`project_diagnostics` status rollup bugs.* 3 codex sessions, deterministic (5/5 wrong in one session) against a tool positioned as a trustworthy single gate; forces every caller to duplicate the check via a separate `compile_check`. **Surface: server.** Evidence: §3 pattern 4, §2a `validate_workspace`/`project_diagnostics` rows.
3. **`workspace-lru-eviction-hard-failure`** — priority **high**. *Stop Lru workspace eviction from causing hard failures and flaky tests.* 2 sessions, but produced a non-deterministic test failure against the server's own test suite that could easily be mistaken for a real regression. **Surface: server.** Evidence: §3 pattern 3, §2a `compile_check`/`test_run` rows.
4. **`validate-recent-git-changes-false-clean`** — priority **medium**. *Surface `validate_recent_git_changes` timeouts as degraded, not clean.* Single session, but high materiality — a validation tool gave a false-clean signal about exactly what it exists to check, twice, against ~30 real uncommitted files. **Surface: server.** Evidence: §3 pattern 7, §2a `validate_recent_git_changes` row.
5. **`compile-check-multi-project-fallback`** — priority **medium**. *Expose `compile_check`'s multi-project fallback as a structured signal.* Second-most recurrent finding (5 sessions, 4 repos, deterministic). Lower severity than 1-3 because results stay correct — cost is latency/discoverability only. **Surface: server.** Evidence: §3 pattern 2, §2a `compile_check` fallback row.
6. **`grep-over-semantic-search-discoverability`** — priority **medium**. *Route grep-shaped queries to semantic search tools, not shell grep.* The only pattern confirmed cross-harness with identical behavior in both clients — the highest-confidence discoverability fix to prioritize, ranked medium (not high) because it's ergonomics, not a functional defect. **Surface: server** (tool description / `recommend_workflow` routing). Evidence: §3 pattern 6, §2b grep-over-semantic-search row.
7. **`extract-method-decompose-by-complexity-gap`** — priority **low**. *Add a guided decompose-by-complexity mode to `extract_method`.* Only 2 codex sessions, no cross-harness signal — a feature-gap request, not a defect. **Surface: server.** Evidence: §2b decompose-by-complexity gap row.
8. **`document-symbols-sln-path-guidance`** — priority **low**. *Guide `document_symbols` users off `.sln` paths toward source files.* Single-session, single-harness (claude-only) — the sole claude-tagged issue row in the entire dataset, included despite low confidence because it's a concrete, cheap UX fix and the only signal claude's smaller sample produced. **Surface: server.** Evidence: §2a `document_symbols` row.

## 5. Meta-note

Phase mix is sharply split by harness: codex carried essentially all refactoring-phase work (27 of 30 included sessions, plus 3 mixed, zero release_operational/planning_docs), while claude's 10 sessions ran the opposite mix (planning_docs: 3, mixed: 6, release_operational: 1, zero refactoring) — the two harnesses were doing different jobs, not the same job differently, in this window. The 30/10 codex/claude split (75%/25%) sits just above the 20% single-harness floor nominally, but the pre-cap numbers reveal the real imbalance: codex had 111 relevant sessions trimmed to 30 by the budget cap (81 dropped) while claude's 10 sessions represent everything available, so codex's contribution is both larger and far more heavily filtered, which should temper confidence in any codex-only "pattern" as fully representative of codex's actual session population. Friction concentrates almost entirely in the refactor-tool apply/verify surface — `extract_type_preview`'s dead-end refusals, `compile_check`'s silent multi-project fallback, workspace Lru eviction racing `compile_check`/`test_run`, and `validate_workspace`/`project_diagnostics` rollup bugs — and because that is exactly the work claude's sample didn't do this window, its absence from claude's findings is unconfirmed rather than proven absent; the one friction pattern that did appear identically in both harnesses (grep-over-semantic-search discoverability, `recommend_workflow` going unused) is an ergonomics gap rather than a reliability defect, and is the only genuine cross-harness data point in the set. Of the 17 classified issue rows, 14 are server_side and 0 are harness_specific (3 unknown), and every finding above is filed as a server-surface fix — there is no evidence in this window of a real client/harness parity defect, only an absence of the paired same-tool comparisons that would let one be detected. Repo distribution skews toward DotNet-Firewall-Analyzer, DotNet-Network-Documentation, BioRemote, and BioFileTransfer (5 sessions each), with Roslyn-Backed-MCP's own repo notably included via self-referential sessions (e.g., the `test_run` flakiness finding ran against RoslynMcp's own test suite). Next window, codex usage should default to re-reading `extract_type_preview`/`split_class_preview` refusal payloads and retrying with an expanded member set instead of abandoning the tool for a manual split, and should treat `validate_workspace`/`project_diagnostics` summary fields with suspicion whenever a severity filter is active; claude usage should deliberately include refactor-tool-heavy sessions (`extract_type_preview`, `compile_check`, `test_run`) rather than staying in planning/docs/release work, both to build parity evidence and because claude's current sample cannot confirm or deny whether any of the codex-only findings above are actually codex-specific. The window was not long enough to settle that question: most top findings rest on multiple codex sessions and repos, but 3 of the 8 findings above rest on only 1-2 sessions, essentially every finding is single-harness, and the near-total absence of claude refactor-tool sessions means the next retro should both widen the window and deliberately rebalance which tasks each harness is assigned before treating any "codex-only" finding as harness-specific rather than harness-unconfirmed.

**Two extraction-methodology corrections applied before this report was written** (documented here per Directive #5/#7 — verify, don't inherit, and don't claim done without evidence): (1) the Claude-side tool-namespace gap described in §0 was caught by re-reading a prior retro's own post-publication correction and verifying the same failure mode was live in this window's corpus before writing the matcher — it was; (2) a repo-name path-decoder bug (blanket `-`→`/` replacement mangling hyphenated repo names like `DotNet-Firewall-Analyzer`) was caught by inspecting actual output before trusting it, not after. Both are noted so a future retro re-verifies rather than assumes this report's extraction script is itself bug-free.
