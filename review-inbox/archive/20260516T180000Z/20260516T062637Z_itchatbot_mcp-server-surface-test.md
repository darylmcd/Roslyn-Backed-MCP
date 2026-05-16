# MCP Server Audit Report

## 1. Header
- **Date:** 2026-05-16 (UTC `20260516T062637Z`)
- **Audited solution:** ITChatBot.sln
- **Audited revision:** `90499d2fe13a9ec6522edbb101b8b6e4dba1f4e0` on branch `main`
- **Entrypoint loaded:** `C:\Code-Repo\IT-Chat-Bot\.worktrees\surface-test-20260516T062637Z\ITChatBot.sln`
- **Flags:** (none) — full canonical run
- **Isolation:** disposable worktree at `C:\Code-Repo\IT-Chat-Bot\.worktrees\surface-test-20260516T062637Z` on branch `mcp-server-surface-test/20260516T062637Z`
- **Isolation baseline (primary checkout `git status --porcelain`):** *(empty — clean tree)*
- **Teardown:** `clean` — `workspace_close(drainProcesses=true)` released MSBuild build-server locks; `git worktree remove --force` cleaned the disposable checkout; `git branch -D mcp-server-surface-test/20260516T062637Z` deleted the branch (was at `90499d2`, no upstream); `rmdir .worktrees` removed the empty parent dir; final `git status --porcelain` on the primary checkout is empty (matches Phase 0 baseline). Run-end clean check **PASS**.
- **Client:** Claude Code (CLI; Opus 4.7 1M)
- **Workspace id:** `9d0529442a4c484ab7a6068ff140fa67`
- **Warm-up:** yes — `workspace_warm` ran inline with `workspace_load(prewarm=true)`: 36 projects warmed, 28 cold compilations, 4054 ms
- **Server:** `roslyn-mcp` v1.38.1+7b2c0b99 — .NET 10.0.8 — Windows 10.0.26200
- **Catalog version:** 2026.04
- **Roslyn / .NET:** Roslyn 5.3.0.0 on .NET 10.0.8
- **Live surface:** `tools: 111/58`, `resources: 9/4`, `prompts: 0/20` (parityOk=true, 169 tools registered)
- **Scale:** 36 projects, 837 documents, all `net10.0`, single TFM, sln file
- **Repo shape:** 16 src / 20 test projects (test projects all `OutputType=Exe` → Microsoft.Testing.Platform / TUnit style); `.editorconfig` at root + `tests/.editorconfig` sub-scope; `Directory.Build.props` present; `global.json` pins SDK; DI is registered in `src/api` + `src/worker` host startup; no Central Package Management (no `Directory.Packages.props`); no multi-targeting; analyzers via `Directory.Build.props` (SDK + Roslynator hosting status TBD via `security_analyzer_status`).
- **Prior issue source:** `ai_docs/backlog.md` (referenced from AGENTS.md)
- **Debug log channel:** `no` — Claude Code CLI does not surface `notifications/message` MCP log events. Recorded once; do not re-flag.
- **Report path note:** prose `.md` stays in this repo's `audit-reports/`. Per `--output-mode=findings` default + maintainer detection (`darylmcd`), Phase 19 will auto-file to `darylmcd/Roslyn-Backed-MCP` via `gh issue create` (subject to P0/security refusal contract and dedup pre-check).

### Phase -1 evidence
- `mcp__roslyn__server_info`: callable. `parityOk=true`, registered=`{tools:169, resources:13, prompts:20}` matches surface counts (`111+58=169`, `9+4=13`, `0+20=20`).
- `mcp__roslyn__server_heartbeat`: `connection.state=idle` (pre-load terminal state per spec — `idle` is healthy, the stop-list is `initializing` / `degraded` / absent).
- `roslyn://server/catalog`: 13 resources listed; `summary` totals match `server_info.surface`. Workflow hints catalog (19 named workflows) is non-empty. **PASS.**
- `roslyn://server/resource-templates`: 13 URI templates listed. **PASS.**
- `workspace_health` on `9d0529442a4c484ab7a6068ff140fa67`: `isReady=true`, `analyzersReady=true`, `workspaceDiagnosticCount=0`, `workspaceErrorCount=0`, `workspaceWarningCount=0`, `restoreRequired=false`. **PASS.**
- Live-surface drift detection: deferred to Final surface closure pass (compares ledger ↔ catalog).

### Phase 0 evidence
- Disposable worktree created at `C:\Code-Repo\IT-Chat-Bot\.worktrees\surface-test-20260516T062637Z`; branch `mcp-server-surface-test/20260516T062637Z` (HEAD at `90499d2`).
- Isolation baseline captured (empty `git status --porcelain` against primary checkout `C:/Code-Repo/IT-Chat-Bot`).
- `dotnet restore` precheck on worktree: completed with exit code 0 (background task `b3a1knxt8`).
- `workspace_load` reported `_meta.elapsedMs=16322`, `_meta.queuedMs=7`, `_meta.heldMs=20368`, `_meta.gateMode=rw-lock`, `_meta.cacheHit=false`. Prewarm contributed 4054 ms of the held time (28 cold compilations).
- `workspace_status` and `workspace_health` both report clean load.
- `project_graph` returns 36 projects with consistent assemblyName/filePath, all `net10.0`, no circular references at first glance.
- `roslyn://workspaces` returns one workspace with `count: 1` matching `workspace_list` (count=0 pre-load → count=1 post-load).

### Phase 0.5 dispatch decision
- Default mode (no `--single-agent`). Orchestrator dispatches Groups G1 / G2 / G3 / G5 / G6 / G7 / G8 to `audit-phase-runner` subagents in successive waves.
- Phase 6 (apply-tool exercise) and Phase 6z (worktree teardown) remain orchestrator-owned. Phase 19 finding emission remains orchestrator-owned.

## 2. Coverage summary

| Kind | Category | Stable | Experimental | Exercised | Exercised-apply | Preview-only | Skipped-repo-shape | Skipped-safety | Blocked | Scoped-but-skipped | Notes |
|------|----------|--------|--------------|-----------|------------------|--------------|--------------------|----------------|---------|-------------------|-------|
| tool | diagnostics | ~10 | 0 | 10 | 0 | 0 | 0 | 0 | 0 | 0 | All Phase 1 calls clean |
| tool | metrics | ~7 | 0 | 7 | 0 | 0 | 0 | 0 | 0 | 0 | Phase 2 |
| tool | symbol/refs | ~15 | 0 | 15 | 0 | 0 | 0 | 0 | 0 | 0 | Phase 3 |
| tool | flow | 6 | 0 | 6 | 0 | 0 | 0 | 0 | 0 | 0 | Phase 4 |
| tool | snippet/script | 2 | 0 | 2 | 0 | 0 | 0 | 0 | 0 | 0 | Phase 5 (incl. timeout probe) |
| tool | apply / write | ~16 | ~10 | 14 | 14 | 6 | 0 | 0 | 0 | ~8 | Phase 6 partial (10 sub-phases marked phase-failed-budget); orchestrator+subagent recovery for 6k+6l+6m |
| tool | config | 6 | 0 | 6 | 4 | 0 | 0 | 0 | 0 | 0 | Phase 7 + 8b.5 writers |
| tool | build/test | ~10 | ~3 | 10 | 0 | 0 | 0 | 0 | 0 | 0 | Phase 8 — full test suite 1125/1126 pass |
| tool | concurrency | ~5 | 0 | 5 | 6 | 0 | 0 | 0 | 5 | 0 | Phase 8b sequential baseline collected; parallel probes blocked by client serialization |
| tool | revert | 2 | 0 | 2 | 4 | 0 | 0 | 0 | 0 | 0 | Phase 9: 3 reverts + 2 negative probes |
| tool | file/cross-project | ~6 | ~6 | 4 | 2 | 4 | 1 | 0 | 0 | ~6 | Phase 10 partial; create+delete chain orchestrator-completed; some cross-project previews `scoped-but-skipped` |
| tool | semantic / DI / reflection | 4 | ~2 | 7 | 0 | 0 | 0 | 0 | 0 | 0 | Phase 11 |
| tool | scaffolding | ~4 | ~5 | 6 | 4 | 4 | 0 | 0 | 0 | 0 | Phase 12 end-to-end with cleanup |
| tool | project mutation | ~5 | ~5 | 9 | 2 | 6 | 0 | 0 | 0 | 4 | Phase 13: CPM + multi-target `scoped-but-skipped — repo shape`; LangVersion forward/reverse applied |
| tool | navigation | ~7 | ~1 | 7 | 0 | 0 | 0 | 0 | 0 | 0 | Phase 14 |
| tool | boundary/negative | many | many | many | 0 | 0 | 0 | 0 | 0 | 0 | Phase 17a-d all clean; 17e scoped-but-skipped (orchestrator owns lifecycle) |
| resource | server/workspace | 9 | 4 | 9 | 0 | 0 | 0 | 0 | 4 | 0 | Phase 15 cross-checked via tool channel; resource-channel-only `lines/{range}` marker comment unverifiable from subagent |
| prompt | all | 0 | 20 | 15 | 0 | 0 | 0 | 0 | 0 | 5 | Phase 16: 4 partial (schema-only); 1 overflow |

`exercised-apply` and `preview-only` cells count tool invocations that were apply-mode or preview-mode respectively; rows can show >0 in BOTH because some tools were exercised in both modes across phases.

## 3. Coverage ledger

The detailed ledger is consolidated in **Appendix A** (raw subagent envelopes) per phase. Total tool calls across all phases: **~250** (G1=27 + G2=51 + Phase 5=8 + Phase 6=24+17 = 41 + Phase 6m=1 + Phase 9=11 + G5=30 + Phase 10 inline=2 + G6=24 + G7=23 + G8=40 + Final closure inline=4 + revert calls=2). Live catalog totals (`server_info.surface`): tools 111 stable / 58 experimental, resources 9 stable / 4 experimental, prompts 0 stable / 20 experimental. **`parityOk=true` confirmed at run start.**

Phase 6 sub-phases NOT exercised (marked `phase-failed-budget` per the skill's completion gate — surfaced as P1 audit defects below): **6a, 6c, 6d, 6e, 6f, 6f-ii, 6g, 6h, 6i, 6j**. These were assigned to the first Phase 6 dispatch (general-purpose subagent) which truncated mid-execution after sub-phase 6b. The orchestrator's recovery dispatch covered 6k+6l only to preserve the experimental-promotion scorecard mission.

## 4. Verified tools (working)

The audit verified the following tool categories work end-to-end (cite p50 elapsedMs from Section 6):
- `workspace_load` + `workspace_warm` + `workspace_status` + `workspace_health` + `project_graph` (Phase 0 — 36 projects, 837 docs, 0 errors)
- `project_diagnostics` + `compile_check` + `security_diagnostics` + `nuget_vulnerability_scan` + `list_analyzers` + `diagnostic_details` (Phase 1)
- `get_complexity_metrics` + `get_cohesion_metrics` + `get_coupling_metrics` + `find_unused_symbols` + `find_duplicated_methods` + `find_duplicated_code` + `find_duplicate_helpers` + `find_dead_locals` + `find_dead_fields` + `get_namespace_dependencies` + `get_nuget_dependencies` + `suggest_refactorings` (Phase 2)
- `symbol_search` + `symbol_info` + `document_symbols` + `type_hierarchy` + `find_implementations` + `find_references` + `find_consumers` + `find_type_consumers` + `find_shared_members` + `find_type_mutations` + `find_type_usages` + `callers_callees` + `find_property_writes` + `member_hierarchy` + `symbol_relationships` + `symbol_signature_help` + `impact_analysis` + `probe_position` + `symbol_impact_sweep` (Phase 3)
- `get_source_text` + `analyze_data_flow` + `analyze_control_flow` + `get_operations` + `get_syntax_tree` + `trace_exception_flow` (Phase 4 — expression-bodied member support v1.8+ confirmed)
- `analyze_snippet(expression/program/statements/returnExpression)` + `evaluate_csharp(expr/multi-line/runtime-error/timeout)` (Phase 5 — FLAG-C fix to startColumn confirmed; timeout disclosure excellent)
- `rename_preview` + `rename_apply` (Phase 6b — single apply landed cleanly)
- `restructure_preview` + `replace_string_literals_preview` + `change_signature_preview(add/remove/rename/reorder)` + `change_type_namespace_preview` (Phase 6k — preview-only)
- `organize_usings_preview` + `apply_with_verify` + `extract_method_preview` (Phase 6l)
- `workspace_changes` (Phase 6m / 9)
- `set_editorconfig_option` + `get_editorconfig_options` + `get_msbuild_properties` + `evaluate_msbuild_property` + `evaluate_msbuild_items` (Phase 7)
- `build_workspace` + `build_project` + `test_discover` + `test_related_files` + `test_related` + `test_run` + `test_coverage` + `test_reference_map` + `get_test_coverage_map` + `validate_workspace` + `validate_recent_git_changes` (Phase 8)
- `apply_text_edit` + `apply_multi_file_edit` + `set_diagnostic_severity` + `add_pragma_suppression` (Phase 8b.5 writers)
- `delete_file_preview` + `delete_file_apply` + `create_file_apply` (Phase 10)
- `revert_last_apply` + `revert_apply_by_sequence` (Phase 9 — non-tip rollback PASS; two negative probes clean)
- `semantic_search` + `semantic_grep` + `find_reflection_usages` + `get_di_registrations(summary=true)` + `source_generated_documents` (Phase 11)
- `scaffold_type_preview` + `scaffold_type_apply` + `scaffold_test_preview` + `scaffold_test_apply` + `scaffold_test_batch_preview` + `scaffold_first_test_file_preview` (Phase 12)
- `add_package_reference_preview` + `add_project_reference_preview` + `remove_project_reference_preview` + `set_project_property_preview` + `apply_project_mutation` + `set_conditional_property_preview` (Phase 13)
- `go_to_definition` + `goto_type_definition` + `enclosing_symbol` + `get_symbol_outline` + `get_completions` + `find_references_bulk` + `find_overrides` + `find_base_members` (Phase 14)
- `get_prompt_text` (Phase 16 — 15/20 prompts fully rendered)
- `workspace_close` (Final Closure — drainProcesses=true variant; clean release)

## 5. Phase 6 apply-tool exercise summary
- **Disposable worktree path:** `C:\Code-Repo\IT-Chat-Bot\.worktrees\surface-test-20260516T062637Z` (now removed)
- **Disposable branch:** `mcp-server-surface-test/20260516T062637Z` (deleted at teardown — was at `90499d2`, no upstream)
- **Scope:** 6b (Rename), 6k (Advanced refactor previews — 5 experimental tools), 6l (apply_with_verify — 2 applies), 6m (workspace_changes). Sub-phases 6a, 6c, 6d, 6e, 6f, 6f-ii, 6g, 6h, 6i, 6j marked `phase-failed-budget` (P1 audit defect — first Phase 6 subagent dispatch truncated after sub-phase 6b).
- **Apply-tool calls landed (workspace_changes snapshot @ run end):** 17 sequenced entries — `rename_apply` (1), `symbol_refactor_preview` recorded-as-applied DRIFT (2,3), `apply_with_verify(organize_usings)` (4), `apply_with_verify(extract_method/Equals)` (5), `set_editorconfig_option` (6 — reverted via git checkout), `create_file_apply` (7), `apply_text_edit` (8), `apply_multi_file_edit` (9,10), `set_diagnostic_severity` (11 — reverted via git checkout), `add_pragma_suppression` (12), `delete_file_apply` (13 — orchestrator-inline cleanup of #7), Phase 9 audit-only A1/A2/A3 (14,15,16 — all reverted via revert_last_apply + revert_apply_by_sequence), G8's apply_text_edit (17 — reverted via revert_last_apply at Final Closure).
- **Verification:** `compile_check(severity=Error)` immediately before teardown: **0 errors / 0 warnings / 36 of 36 projects, 1.86 s**. Workspace was healthy at handoff to teardown.
- **Teardown outcome:** `clean` — see Header *Teardown* row for full sequence.

## 6. Performance baseline (`_meta.elapsedMs`)

Consolidated from all phases. Budget per audit principle #3: single-symbol reads ≤5 s, solution scans ≤15 s, writers ≤30 s.

| Tool | Tier | Category | Calls | p50_ms | p90_ms | max_ms | Input scale | Budget | Verdict |
|------|------|----------|-------|--------|--------|--------|-------------|--------|---------|
| workspace_load | stable | workspace | 1 | 16322 | 16322 | 16322 | 36 projects (cold + prewarm) | ≤30 s | PASS (4054 ms of which was prewarm) |
| workspace_status | stable | workspace | 2 | <5 | <5 | <5 | 36 projects | ≤5 s | PASS |
| workspace_health | stable | workspace | 1 | <5 | <5 | <5 | 36 projects | ≤5 s | PASS |
| workspace_warm | stable | workspace | 1 (inline) | 4054 | 4054 | 4054 | 28 cold compilations | ≤30 s | PASS |
| workspace_close | stable | workspace | 1 | 589 | 589 | 589 | drainProcesses=true | ≤5 s | PASS |
| project_graph | stable | workspace | 1 | 2 | 2 | 2 | 36 projects | ≤5 s | PASS |
| project_diagnostics | stable | diagnostics | 5 | 60 | 14000 | 18147 | 36 proj | ≤15 s | **FLAG** (summary=true call 18.1 s exceeds soft budget) |
| compile_check | stable | diagnostics | 7 | 41 | 2100 | 2630 | 36 proj | ≤15 s | PASS (emitValidation in-budget) |
| security_diagnostics | stable | security | 1 | 2625 | 2625 | 2625 | 36 proj | ≤15 s | PASS |
| security_analyzer_status | stable | security | 1 | 13634 | 13634 | 13634 | 36 proj | ≤5 s | **FLAG** (2.7x over single-symbol budget) |
| nuget_vulnerability_scan | stable | security | 1 | 16353 | 16353 | 16353 | 36 proj | ≤15 s | **FLAG** (exceeds 15 s budget) |
| list_analyzers | stable | diagnostics | 2 | 830 | 1658 | 1658 | 36 proj | ≤15 s | PASS |
| diagnostic_details | stable | diagnostics | 2 | 37 | 72 | 72 | 1 file | ≤5 s | PASS |
| get_complexity_metrics | stable | metrics | 1 | 65 | 65 | 65 | 36 proj | ≤15 s | PASS |
| get_cohesion_metrics | stable | metrics | 1 | 123 | 123 | 123 | 36 proj | ≤15 s | PASS |
| get_coupling_metrics | stable | metrics | 1 | 3186 | 3186 | 3186 | 36 proj | ≤15 s | PASS |
| find_unused_symbols | stable | dead-code | 2 | 712 | 981 | 981 | 36 proj | ≤15 s | PASS |
| find_duplicated_methods | stable | duplication | 1 | 112 | 112 | 112 | 36 proj | ≤15 s | PASS |
| find_duplicate_helpers | stable | duplication | 1 | 43 | 43 | 43 | 36 proj | ≤15 s | PASS |
| find_duplicated_code | stable | duplication | 1 | 81 | 81 | 81 | 36 proj | ≤15 s | PASS (alias) |
| find_dead_fields | stable | dead-code | 1 | 1520 | 1520 | 1520 | 36 proj | ≤15 s | PASS |
| find_dead_locals | stable | dead-code | 1 | 55 | 55 | 55 | 1 proj | ≤15 s | PASS |
| get_namespace_dependencies | stable | architecture | 1 | 103 | 103 | 103 | 36 proj | ≤15 s | PASS |
| get_nuget_dependencies | stable | dependencies | 1 | 2561 | 2561 | 2561 | 36 proj | ≤15 s | PASS |
| suggest_refactorings | stable | metrics | 1 | 780 | 780 | 780 | 36 proj | ≤15 s | PASS |
| symbol_search | stable | search | 6+ | 426 | 1337 | 1337 | 36 proj | ≤5 s | PASS (first-call 1.3 s warmup — see improvement note) |
| symbol_info | stable | symbol | 6 | 1 | 4 | 4 | 36 proj | ≤5 s | PASS |
| document_symbols | stable | outline | 3 | 6 | 6 | 6 | per file | ≤5 s | PASS |
| type_hierarchy | stable | hierarchy | 5 | 1 | 5 | 5 | 36 proj | ≤5 s | PASS |
| find_implementations | stable | hierarchy | 3 | 4 | 573 | 573 | 36 proj | ≤5 s | PASS |
| find_references | stable | refs | 4 | 4 | 18 | 18 | 3-15 refs | ≤5 s | PASS |
| find_references_bulk | stable | refs | 1 | 72 | 72 | 72 | 3 symbols | ≤5 s | PASS |
| find_consumers | stable | refs | 3 | 1 | 6 | 6 | 3-7 consumers | ≤5 s | PASS |
| find_type_consumers | stable | refs | 3 | 1 | 8 | 8 | 3-7 files | ≤5 s | PASS |
| find_shared_members | stable | hierarchy | 2 | 13 | 23 | 23 | 0-1 results | ≤5 s | PASS |
| find_type_mutations | stable | mutations | 2 | 16 | 24 | 24 | 0-3 members | ≤5 s | PASS |
| find_type_usages | stable | refs | 3 | 2 | 12 | 12 | 3-15 usages | ≤5 s | PASS |
| callers_callees | stable | refs | 3 | 2 | 10 | 10 | 1-19 calls | ≤5 s | PASS |
| find_property_writes | stable | refs | 2 | 4 | 4 | 4 | 0-4 writes | ≤5 s | PASS |
| member_hierarchy | stable | hierarchy | 2 | 3 | 5 | 5 | 0-1 base | ≤5 s | PASS |
| symbol_relationships | stable | hierarchy | 1 | 51 | 51 | 51 | 6 refs | ≤5 s | PASS |
| symbol_signature_help | stable | symbol | 2 | 62 | 122 | 122 | metadata | ≤5 s | PASS |
| impact_analysis | stable | refs | 1 | 5 | 5 | 5 | 15 refs | ≤5 s | PASS |
| probe_position | experimental | symbol | 2 | 7 | 11 | 11 | per-token | ≤5 s | PASS |
| symbol_impact_sweep | stable | refs | 2 | 38 | 72 | 72 | 15 refs | ≤5 s | PASS |
| get_source_text | stable | read | 8+ | 0 | 1 | 3 | per file | ≤5 s | PASS |
| analyze_data_flow | stable | flow | 6 | 2 | 13 | 13 | 1-85 lines | ≤5 s | PASS |
| analyze_control_flow | stable | flow | 5 | 1 | 6 | 6 | 1-85 lines | ≤5 s | PASS |
| get_operations | stable | flow | 5 | 0 | 3 | 3 | per-token | ≤5 s | PASS |
| get_syntax_tree | stable | flow | 2 | 3 | 4 | 4 | 1-57 lines | ≤5 s | PASS |
| trace_exception_flow | stable | flow | 2 | 11 | 13 | 13 | 9-10 sites | ≤5 s | PASS |
| analyze_snippet | stable | snippet | 5 | 65 | 97 | 155 | small snippet | ≤5 s | PASS |
| evaluate_csharp | stable | script | 5 | 62 | 13003 | 13003 | small snippet | ≤10 s+grace | PASS (timeout fires at 13 s) |
| rename_apply | stable | refactor | 1 | ~few hundred | — | — | 2 files | ≤30 s | PASS |
| symbol_refactor_preview | experimental | refactor-advanced | 1 | 583 | 583 | 583 | 2 ops | ≤30 s | PASS (DRIFT — see 13.1) |
| change_signature_preview | experimental | refactor-advanced | 4 | 40 | 75 | 75 | 1 callsite | ≤30 s | PASS |
| restructure_preview | experimental | refactor-advanced | 2 | 7 | 7 | 7 | 1 file scope | ≤30 s | PASS |
| replace_string_literals_preview | experimental | refactor-advanced | 1 | 22 | 22 | 22 | scope | ≤30 s | PASS |
| change_type_namespace_preview | experimental | refactor-advanced | 1 | 8837 | 8837 | 8837 | partial-class | ≤30 s | PASS (mostly auto-reload) |
| organize_usings_preview | stable | refactor | 1 | 17022 | 17022 | 17022 | 1 file | ≤30 s | PASS (mostly auto-reload) |
| apply_with_verify | stable | apply-verify | 2 | 1758 | 1764 | 1764 | 1 file | ≤30 s | PASS |
| extract_method_preview | stable | refactor | 1 | 106 | 106 | 106 | 1 method | ≤30 s | PASS |
| workspace_changes | stable | session | 2+ | 0 | 10625 | 10625 | session log | ≤5 s | PASS (auto-reload included) |
| revert_last_apply | stable | revert | 3 | 1 | 18449 | 18449 | session log | ≤30 s | PASS (auto-reload included) |
| revert_apply_by_sequence | stable | revert | 3 | 0 | 9470 | 9470 | session log | ≤30 s | PASS |
| get_editorconfig_options | stable | config | 2 | 12 | 12 | 12 | per file | ≤5 s | PASS |
| set_editorconfig_option | stable | config | 2 | 4 | 9199 | 9199 | per file | ≤30 s | PASS (queued behind prior writer) |
| get_msbuild_properties | stable | config | 3 | 142 | 17570 | 17570 | per proj | ≤15 s | PASS (auto-reload spike) |
| evaluate_msbuild_property | stable | config | 1 | 147 | 147 | 147 | per proj | ≤5 s | PASS |
| evaluate_msbuild_items | stable | config | 1 | 121 | 121 | 121 | per proj | ≤5 s | PASS |
| build_workspace | stable | build | 1 | 18257 | 18257 | 18257 | 36 proj | ≤180 s | PASS |
| build_project | stable | build | 1 | 18450 | 18450 | 18450 | 1 proj | ≤180 s | PASS (queued 8 s) |
| test_discover | stable | test | 1 | 245 | 245 | 245 | 18 test proj | ≤30 s | PASS |
| test_related_files | stable | test | 1 | 256 | 256 | 256 | 18 test proj | ≤30 s | PASS |
| test_related | experimental | test | 1 | 58 | 58 | 58 | symbolHandle | ≤30 s | PASS |
| test_run (filter) | stable | test | 1 | 13435 | 13435 | 13435 | 14-test filter | ≤180 s | PASS (with P2 FLAG — see 13.8) |
| test_run (full) | stable | test | 1 | 16949 | 16949 | 16949 | 1126 tests | ≤300 s | PASS (1125 passed, 1 skipped) |
| test_coverage | stable | coverage | 1 | 6 | 6 | 6 | n/a | ≤120 s | PASS (CoverletMissing clean envelope) |
| test_reference_map | experimental | coverage | 1 | 2010 | 2010 | 2010 | 36 proj | ≤30 s | PASS (with P1 FLAG — see 13.3) |
| get_test_coverage_map | experimental | coverage | 1 | 1 | 1 | 1 | n/a | ≤30 s | PASS (alias path) |
| validate_workspace | experimental | validation | 2 | 25530 | 25941 | 25941 | 36 proj | ≤30 s | **FAIL** (timeout — see 13.2) |
| validate_recent_git_changes | experimental | validation | 1 | 31186 | 31186 | 31186 | 36 proj, 10 s git timeout | ≤60 s | PASS (degraded) |
| apply_text_edit | stable | apply-text | 5 | 5 | 10901 | 10901 | per file | ≤30 s | PASS |
| apply_multi_file_edit | stable | apply-text | 1 | 9096 | 9096 | 9096 | 2 files | ≤30 s | PASS (queued 8.4 s) |
| set_diagnostic_severity | stable | config | 1 | 9199 | 9199 | 9199 | per id | ≤30 s | PASS (queued behind writer) |
| add_pragma_suppression | stable | config | 1 | 7 | 7 | 7 | per site | ≤5 s | PASS |
| semantic_search | stable | semantic | 3 | 60 | 101 | 101 | 36 proj | ≤5 s | PASS |
| semantic_grep | experimental | semantic | 2 | 53 | 80 | 80 | 36 proj | ≤5 s | PASS |
| find_reflection_usages | stable | semantic | 1 | 1469 | 1469 | 1469 | 36 proj | ≤15 s | PASS |
| get_di_registrations | stable | di | 2 | 5 | n/a | n/a | 141 regs | ≤15 s | **FAIL on default** (payload overflow — see 13.4); summary mode PASS |
| source_generated_documents | stable | source-gen | 1 | small | small | small | 39 docs | ≤5 s | PASS |
| scaffold_type_preview | stable | scaffold | 3 | 4 | 4 | 4 | per type | ≤30 s | PASS |
| scaffold_type_apply | stable | scaffold | 1 | 652 | 652 | 652 | 1 file | ≤30 s | PASS |
| scaffold_test_preview | stable | scaffold | 3 | 9 | 15 | 333 | per type | ≤30 s | PASS |
| scaffold_test_apply | stable | scaffold | 1 | 528+6324 | 6852 | 6852 | 1 file | ≤30 s | PASS |
| scaffold_test_batch_preview | experimental | scaffold | 1 | 5 | 5 | 5 | 4 targets | ≤30 s | PASS (FLAG 13.10) |
| scaffold_first_test_file_preview | experimental | scaffold | 2 | 14 | 14 | 14 | 1 proj | ≤30 s | PASS |
| delete_file_preview | stable | file-op | 2 | 5 | 5 | 5 | 1 file | ≤30 s | PASS |
| delete_file_apply | stable | file-op | 2 | 548 | 685 | 685 | 1 file | ≤30 s | PASS |
| create_file_preview | stable | file-op | 1 | small | small | small | new file | ≤30 s | PASS |
| create_file_apply | stable | file-op | 1 | small | small | small | new file | ≤30 s | PASS |
| add_package_reference_preview | stable | proj-mut | 1 | 78 | 78 | 78 | csproj | ≤30 s | PASS |
| remove_package_reference_preview | stable | proj-mut | 1 | 13 | 13 | 13 | csproj | ≤30 s | PASS (clean negative) |
| add_project_reference_preview | stable | proj-mut | 1 | 117 | 117 | 117 | csproj | ≤30 s | PASS (clean negative) |
| remove_project_reference_preview | stable | proj-mut | 2 | 2 | 18577 | 18577 | csproj | ≤30 s | PASS (FLAG 13.15 on first) |
| set_project_property_preview | experimental | proj-mut | 3 | 53 | 13384 | 13384 | csproj | ≤30 s | PASS (auto-reload) |
| apply_project_mutation | experimental | proj-mut | 2 | 7460 | 24280 | 24280 | csproj | ≤30 s | PASS (forward + reverse) |
| set_conditional_property_preview | experimental | proj-mut | 1 | 5 | 5 | 5 | csproj | ≤30 s | PASS |
| go_to_definition | stable | nav | 1 | 2 | 2 | 2 | per cursor | ≤5 s | PASS |
| goto_type_definition | stable | nav | 1 | 2 | 2 | 2 | per cursor | ≤5 s | PASS |
| enclosing_symbol | stable | nav | 2 | 2 | 3 | 3 | per cursor | ≤5 s | PASS |
| get_symbol_outline | experimental | outline | 1 | 1 | 1 | 1 | per file | ≤5 s | PASS (ZERO drift) |
| get_completions | stable | nav | 1 | 315 | 315 | 315 | per cursor | ≤5 s | PASS |
| find_overrides | stable | hierarchy | 2 | 30 | 59 | 59 | per symbol | ≤5 s | PASS (FLAG 13.14) |
| find_base_members | stable | hierarchy | 1 | 1 | 1 | 1 | per symbol | ≤5 s | PASS |
| get_prompt_text | experimental | prompt-runtime | 17 | 8 | 16538 | 17631 | per prompt | ≤30 s | **FLAG** (side-effects in 2 prompts — see 13.6) |
| format_document_preview | stable | format | 2 | 17022 | 9019 | 17022 | 1 file | ≤30 s | PASS (auto-reload dominated first) |
| format_document_apply | stable | format | 1 | 7444 | 7444 | 7444 | 1 file | ≤30 s | PASS (stale-token negative probe) |

**Performance verdict:** 4 budget violations (3 stable, 1 experimental) — `project_diagnostics` summary mode (18.1 s on the slowest call), `security_analyzer_status` (13.6 s for a presence check), `nuget_vulnerability_scan` (16.3 s — exceeds 15 s soft budget), `validate_workspace` (P1 hard FAIL at 25 s timeout).

## 7. Schema vs behaviour drift

| Tool | Mismatch kind | Expected | Actual | Severity | Notes |
|------|---------------|----------|--------|----------|-------|
| `symbol_refactor_preview` | Preview-vs-apply contract | Returns preview token, requires explicit `*_apply` redemption | `workspace_changes` records the operation as APPLIED at seq 2+3; previewToken returned but never redeemed by agent | **P1 (HIGH)** | Either auto-applies (regression) OR `workspace_changes` misattributes toolName for an internal apply. Release blocker — see Section 13.1. |
| `validate_workspace` | Timeout gate | 25 s internal timeout sufficient for solution-scope auto-scoped diagnostics | `InternalValidationTimeoutException` at 25 s on 36-project workspace; both auto-scoped and fabricated-path probes fail | **P1** | Section 13.2 |
| `test_reference_map` | Pagination scope | `limit` paginates collections per description | `limit=10` only caps `coveredSymbols`; `mockDriftWarnings` (109 entries) + `uncoveredSymbols` unpaginated → 60 KB payload | **P1** | Section 13.3 |
| `get_di_registrations` | Response-size contract | Default shape paginable | Default shape (showLifetimeOverrides=true) produces 86 KB payload that exceeds MCP token cap → degrades to write-to-disk envelope | **P1** | Section 13.4 |
| (preview token TTL) | Implied lifetime | Token lives until explicit apply | Mid-stream workspace auto-reload (triggered by intervening apply or compile_check) silently invalidates prior preview tokens; error says "not found or expired" without explaining why | **P1** | Section 13.5 |
| `get_prompt_text` | Pure-template-fill expectation | Template substitution returns rendered messages | `debug_test_failure` actually runs `dotnet test` (16.5 s, 1126 test outcomes); `security_review` actually runs `nuget_vulnerability_scan` (17.6 s, 36 projects) | **P1** | Section 13.6 |
| `get_prompt_text` | Payload cap | Rendered text fits MCP inline cap | `analyze_dependencies` (98 KB), `review_test_coverage` (116 KB), `guided_extract_interface` (~30+ KB) overflow | **P1** | Section 13.7 |
| `test_run` | Filter aggregation | OR-pipe filter applies across all matching test exes | Microsoft.Testing.Platform exe runners do NOT honor `FullyQualifiedName~A\|FullyQualifiedName~B` filter syntax; only VSTest does; result aggregator silently drops the mismatch | P2 | Section 13.8 |
| `set_project_property_preview` | Element-remove capability | Round-trippable forward/reverse | Tool only supports set-to-value, NOT element-remove; reverse leaves vestigial XML (`<LangVersion>default</LangVersion>` when original had no element); true round-trip requires `revert_apply_by_sequence` | P2 | Section 13.9 |
| `scaffold_test_batch_preview` | "One preview per target" framing | N targets → N file diffs (or N tokens) | Composite token but `changes[]` contains 1 diff for 4 targets when targets share destination path; 3 "already exists" warnings (informative but length is misleading) | P2 | Section 13.10 |
| `symbol_impact_sweep.mapperCallsites` | Suffix heuristic | Should match Mapper-pattern types | Flags `*Adapter` classes (SlackChannelAdapter, TeamsChannelAdapter) as mapper-callsites — false positive | P2 | Section 13.11 |
| `member_hierarchy` | Caret semantics | Auto-promote return-type-token caret like `symbol_relationships` / `symbol_signature_help` | Resolves return-type token literally to the type; no `preferDeclaringMember` knob | P2 | Section 13.12 |
| `organize_usings_preview` | Removal precision | Only removes provably-unused usings | Removed 3 Microsoft.Extensions namespace imports that appear referenced in ctor signature; `apply_with_verify` post-apply error count was 0 so likely safe but warrants targeted test | P2 | Section 13.13 |
| `set_editorconfig_option` | Idempotent write | Write skipped when value already matches | File touched (CRLF→LF normalization) producing no-op git-dirty status | P3 | cosmetic |
| `find_overrides` | Input validation | Reject or hint when passed an interface TYPE rather than member | Silent empty result | P3 | Section 13.14 |
| `remove_project_reference_preview` | Workspace-staleness handling | Gate's auto-reload should retry internally | Returns `Timeout` category after 18.6 s; retry succeeded in 2 ms | P3 | Section 13.15 |
| `set_project_property_preview` | Allowlist | Audit prompt specified `NoWarn` | `NoWarn` not in allowlist (Nullable / LangVersion / ImplicitUsings / TargetFramework only) | P3 | Tool schema is accurate; audit-prompt's predicted error path is wrong |
| `change_signature_preview` (audit-prompt drift) | Predicted error | `op=reorder` should fail and point at `symbol_refactor_preview` | Succeeded silently for all-positional callsite | Prompt | Adjust audit-prompt expectation; tool behavior is reasonable |
| `get_operations` | Coordinate echo | Echo `requestedLine`/`requestedColumn` | Only resolved position returned | P3 | Inconsistent with `probe_position` / `get_source_text` |
| `apply_text_edit` | Diff rendering | Non-empty `unifiedDiff` for non-empty edit | When `newText` is whitespace at column 1, diff body shows no `+` line | P3 | Cosmetic; behavior is correct |
| `get_symbol_outline` vs `document_symbols` | Drift check | ALIAS should mirror canonical | **ZERO DRIFT — identical tree, same line ranges, same kinds, same modifiers** | ✓ PASS | Excellent alias hygiene |

## 8. Error message quality

| Tool | Probe input | Rating | Notes |
|------|-------------|--------|-------|
| `find_references` | `workspaceId="00000000-bogus"` | EXCELLENT | NotFound, `tool: "find_references"` populated, message names `workspace_list` as recovery path |
| `symbol_info` | fabricated base64 symbolHandle | EXCELLENT | NotFound category, not silent `count:0` (v1.8+ contract); recovery hint included |
| `rename_preview` | fabricated symbolHandle | EXCELLENT | InvalidOperation, suggests `document_symbols` / `enclosing_symbol` refresh |
| `go_to_definition` | line=999999 on 39-line file | EXCELLENT | ArgumentException with exact line count "39 line(s)" |
| `enclosing_symbol` | line=0 col=0 (off-by-one) | EXCELLENT | "Line 0 is out of range" — 1-based contract enforced |
| `analyze_data_flow` | startLine=15 > endLine=5 | EXCELLENT | "startLine (15) must be <= endLine (5)" — names both values |
| `probe_position` | whitespace position | GOOD | Clean response: `tokenKind=EndOfLine`, `syntaxKind=EndOfLineTrivia`, containingSymbol resolved |
| `symbol_search` | empty query | GOOD | Empty result + helpful `note` field; not an error |
| `analyze_snippet` | empty code | GOOD | `isValid: true, errorCount: 0` — accepts empty |
| `evaluate_csharp` | empty code | GOOD | success=true, resultValue="null" — empty as no-op |
| `evaluate_csharp` | infinite loop (timeoutSeconds=3) | EXCELLENT | "Script execution was forcibly abandoned after 13 second(s) (script budget 3s + ROSLYNMCP_SCRIPT_WATCHDOG_GRACE_SECONDS 10s)... 1/8 abandoned worker thread(s) outstanding; restart the MCP host if this happens repeatedly." Best-in-class disclosure. |
| `format_document_apply` | stale token after intervening mutation | EXCELLENT | "Preview token is invalid, expired, or stale because the workspace changed since the preview was generated. Please create a new preview." `staleAction: "auto-reloaded"` field informative. |
| `get_prompt_text` | `promptName="not_a_real_prompt"` | EXCELLENT | NotFound; enumerates all 20 valid prompt names alphabetically |
| `get_prompt_text` | `parametersJson="not valid json"` | EXCELLENT | JsonReaderException-style parser message + actionable hint with `{}` example |
| `remove_package_reference_preview` | non-existent target | EXCELLENT | InvalidOperation, target echoed, exception type surfaced |
| `add_project_reference_preview` | already-existing target | EXCELLENT | "Project reference 'X' already exists" idempotency check |
| `set_project_property_preview` | same-value property | EXCELLENT | "No changes needed — property 'Nullable' is already set to 'enable'" |
| `scaffold_first_test_file_preview` | ambiguous (no testProjectName) | EXCELLENT | Enumerates 10 candidate test projects, names the parameter to set |
| `scaffold_test_apply` | expired preview token (workspace auto-reload) | GOOD with caveat | "not found or expired" — doesn't explain *why* (see Section 13.5) |
| `revert_apply_by_sequence` | already-reverted seq | GOOD | `reason: "unknown-sequence", message: "No revert snapshot exists for that sequence number..."`. Could distinguish "already-reverted" vs "out-of-range" but acceptable |
| `revert_apply_by_sequence` | out-of-range seq 9999 | GOOD | Same `unknown-sequence` response |
| `revert_last_apply` | called twice in succession | EXCELLENT | "No operation to revert. Nothing has been applied in this session..." |
| `remove_project_reference_preview` | stale workspace | GOOD-with-caveat | Timeout category, env-var hint provided; retry succeeded cleanly. See Section 13.15. |
| `workspace_status` | closed workspace id (Phase 17e probe) | EXCELLENT | NotFound, `tool` populated, "Active workspace IDs are listed by workspace_list" |
| `restructure_preview` | pattern with no matches in scope | EXCELLENT | Actionable: "Verify pattern kind, placeholder names, scope filters" |
| `replace_string_literals_preview` | uncommon literal absent from scope | EXCELLENT | Empty preview with descriptive text — better UX than throwing |
| `change_type_namespace_preview` | partial-class type (2 files) | EXCELLENT | Names both files; "requires a unique match" |
| `extract_method_preview` | partial-line / cross-scope | EXCELLENT | "Select complete statements" / "All selected statements must be in the same block scope" |

## 9. Parameter-path coverage

Non-default paths exercised across phases (consolidated):

| Family | Non-default path tested | Status | Notes |
|--------|-------------------------|--------|-------|
| `project_diagnostics` | `projectName` + `severity`, `summary=true`, `diagnosticId`, `offset+limit` pagination | exercised | invariant totals confirmed under severity filter |
| `compile_check` | `severity=Error`, `file=<path>`, `emitValidation=true`, project-scoped | exercised | emit-vs-GetDiagnostics ~60x delta confirmed |
| `list_analyzers` | `projectName` | exercised | 34→31 assemblies, 553→466 rules |
| `get_complexity_metrics` | `minComplexity` | exercised | filter respected |
| `get_cohesion_metrics` | `minMethods=3` | exercised | source-gen partials excluded |
| `find_unused_symbols` | `includePublic=true` | exercised | surfaces low-confidence record props by default |
| `find_dead_locals` | `projectFilter` | exercised | scope-narrowed scan |
| `get_namespace_dependencies` | `circularOnly=true` | exercised | 0 cycles detected |
| `get_nuget_dependencies` | `summary=true` | exercised | 46 packages aggregated |
| `find_references` | `summary=true`, `metadataName` (no file/line) | exercised | preview text suppressed |
| `find_implementations` | `metadataName` | exercised | 2 IChannelAdapter / 42 IDisposable |
| `symbol_signature_help` | `preferDeclaringMember=false` | exercised | literal-token resolution |
| `symbol_impact_sweep` | `summary=true` + `maxItemsPerCategory=10` | exercised | category caps respected |
| `impact_analysis` | `summary=true` | exercised | dropped per-ref arrays |
| `get_syntax_tree` | `maxTotalBytes` | exercised | no truncation at this scale |
| `find_consumers` | `metadataName` only | exercised | resolves without file/line |
| `find_type_consumers` | typeName as fully-qualified metadata name | exercised | file-rollup matches find_consumers cardinality |
| `symbol_search` | `kind=Class/Interface` filter + `projectName` filter | exercised | |
| `trace_exception_flow` | `scopeProjectFilter`, `maxResults=10` | exercised | truncation marker correct |
| `change_signature_preview` | `op=add` + `op=remove` + `op=rename` + `op=reorder` | exercised | all 4 ops returned valid preview tokens |
| `restructure_preview` | structural pattern, project-scoped | exercised | both no-match and matched cases |
| `replace_string_literals_preview` | `replacements[]` with `usingNamespace` | exercised | negative-only path |
| `symbol_refactor_preview` | mixed-kind operations array (rename + edit) | exercised | drift on apply contract — see Section 13.1 |
| `get_msbuild_properties` | `includedNames` (JSON array) + `propertyNameFilter` (substring) | exercised | both respected; `appliedFilter` echoed |
| `apply_with_verify` | `rollbackOnError=true` on clean + induced-conflict | exercised | rollback path NOT triggered on real failure (the induced conflict didn't actually error — see Section 13.16) |
| `find_references_bulk` | `summary=true` + `maxItemsPerSymbol=25` | exercised | parity with single `find_references` confirmed |
| `get_di_registrations` | `showLifetimeOverrides=true` (default) + `summary=true` + `limit=30` | exercised | default oversized; summary works |
| `semantic_grep` | `scope=identifiers` + `scope=all` | exercised | bogus pattern → clean empty |
| `scaffold_type_preview` | `implementInterface=true` AND `=false`, `interfaces:["System.IDisposable"]` | exercised | both branches |
| `validate_workspace` | `summary=true` + `responseFormat=markdown` + `runTests=false` | exercised-FAIL | timeout at 25 s |
| `get_prompt_text` | full parameter coverage for 15/20 prompts | exercised | 4 prompts schema-only (required-param iteration consumed schema-discovery rounds); 1 overflow |

## 10. Prompt verification (Phase 16)

| Prompt | schema_ok | actionable | hallucinated_tools | idempotent | elapsedMs | recommendation_seed |
|--------|-----------|------------|---------------------|------------|-----------|----------------------|
| discover_capabilities | yes | yes | none — all 169 tool names match catalog | not probed | 5 | promote |
| explain_error | yes | yes | none | not probed | 8807 | promote |
| suggest_refactoring | yes | yes | none | not probed | 1 | promote |
| review_file | yes | yes | none | not probed | 21 | promote |
| analyze_dependencies | yes | yes | unverified — output 98 KB exceeded MCP cap | not probed | n/a | hold (payload cap) |
| debug_test_failure | yes | yes | none | not probed | 16538 | hold (side-effect — runs `dotnet test`) |
| refactor_and_validate | yes | yes | none | not probed | 1190 | promote |
| fix_all_diagnostics | yes | yes | none | not probed | 2 | promote |
| guided_package_migration | yes | yes | none | not probed | 2057 | promote |
| guided_extract_interface | yes | yes | none | not probed | 1 | hold (output-size) |
| security_review | yes | yes | none | not probed | 17631 | hold (side-effect — runs nuget vulnerability scan) |
| dead_code_audit | yes | yes | none | not probed | 156 | promote |
| review_complexity | yes | yes | none | not probed | 1 | promote |
| cohesion_analysis | yes | yes | none | not probed | 1 | promote |
| consumer_impact | yes (schema-only) | n/a | unknown | not probed | n/a | partial — needs `line` param |
| guided_extract_method | yes (schema-only) | n/a | unknown | not probed | n/a | partial — needs `methodName` |
| msbuild_inspection | yes | yes | none | not probed | 0 | promote |
| session_undo | yes | yes | none | **yes (byte-identical)** | 0 | promote |
| refactor_loop | yes (schema-only) | n/a | unknown | not probed | n/a | partial — needs `intent` arg |
| review_test_coverage | yes | unknown | unknown | not probed | n/a | hold (output > 116 KB) |

## 11. Experimental promotion scorecard

| Kind | Name | Category | Status | p50_ms | schema_ok | error_ok | round_trip_ok | Failures | Recommendation | Evidence |
|------|------|----------|--------|--------|-----------|----------|----------------|----------|----------------|----------|
| tool | semantic_grep | discovery | exercised | ~50 | yes | yes (clean empty on bogus) | n/a (read-only) | 0 | **promote** | structured matchKind, scope filter, fast, clean negative |
| tool | probe_position | symbol | exercised | 7 | yes | yes (strict resolution on whitespace) | n/a | 0 | **promote** | exact-position semantics; tokenKind/syntaxKind/leadingTriviaBefore populated |
| tool | validate_recent_git_changes | validation | exercised | 31186 | yes | yes (degraded-mode warning) | n/a | 0 | **promote** | succeeded with `warnings=[git status timeout, validated full workspace]`; detected 5 dirty files |
| tool | find_references_bulk (summary mode) | refs | exercised | 72 | yes | n/a | n/a | 0 | **promote** | parity with single find_references confirmed; `maxItemsPerSymbol` caps work |
| tool | apply_with_verify | apply-verify | exercised-apply | 1758 | yes | n/a | partial (rollback path not actually triggered) | 0 | keep-experimental | needs validation against a true CS-error-introducing extraction |
| tool | restructure_preview | refactor-advanced | exercised-preview-only | 7 | yes | yes | n/a (no apply attempted) | 0 | keep-experimental | needs apply round-trip evidence |
| tool | replace_string_literals_preview | refactor-advanced | exercised-preview-only | 22 | yes | yes (graceful empty) | n/a | 0 | needs-more-evidence | negative-only probe; no positive replacement+apply round-trip |
| tool | change_signature_preview | refactor-advanced | exercised-preview-only | 5-75 | yes | n/a (no error fired on negative probe) | n/a (preview only) | 0 | keep-experimental | 4 ops exercised; needs preview_multi_file_edit_apply round-trip for promotion |
| tool | symbol_refactor_preview | refactor-advanced | exercised-preview-only (auto-applied DRIFT) | 583 | DRIFT | n/a | partially clean | 1 (P1) | **needs-more-evidence (blocked by drift)** | preview-vs-apply contract violation; workspace_changes seq #2+#3 |
| tool | change_type_namespace_preview | refactor-advanced | exercised-error-only | 8837 | yes | yes (partial-class detection) | n/a | 0 | needs-more-evidence | positive path not exercised |
| tool | scaffold_test_batch_preview | scaffolding | exercised-preview-only | 5 | DRIFT | n/a | n/a | 1 (P2) | keep-experimental | composite token contract upheld; same-destination dedupe silent |
| tool | scaffold_first_test_file_preview | scaffolding | exercised-preview-only | 14 | yes | yes (excellent ambiguity error) | n/a | 0 | keep-experimental | promotion candidate post auto-pick mode |
| tool | validate_workspace | validation | exercised-FAIL | 25941 | drift (timeout) | n/a | n/a | 1 (P1) | **needs-more-evidence (blocked)** | 25 s timeout regression on 36-project workspaces |
| tool | test_reference_map | test-coverage | exercised-PARTIAL | 2010 | DRIFT (pagination) | n/a | n/a | 1 (P1) | **needs-more-evidence (blocked)** | `limit` paginates only `coveredSymbols`; 60 KB payload at limit=10 |
| tool | get_test_coverage_map | test-coverage | exercised-FAIL-CLEAN | 1 | yes (alias) | yes (CoverletMissing envelope) | n/a | 0 | keep-experimental | alias deprecation field populated; functional parity with test_coverage |
| tool | test_related (symbolHandle mode) | test-discover | exercised | 58 | yes | n/a | n/a | 0 | keep-experimental | promotion candidate post broader signal collection |
| tool | get_prompt_text | prompt-runtime | exercised | 0-17631 | yes (covers 15/20 prompts) | yes (excellent negatives) | n/a | 2 (P1 — side-effects + payload-cap) | **needs-more-evidence (blocked)** | side-effect runs + 100+ KB renders |
| tool | get_symbol_outline | navigation | exercised | 1 | yes (ZERO drift vs document_symbols) | n/a | n/a | 0 | keep-experimental (could be retired; alias is clean) |
| tool | symbol_impact_sweep | refs | exercised | 72 | DRIFT (mapperCallsites FP) | yes | n/a | 1 (P2) | keep-experimental | needs heuristic narrowing |
| tool | apply_project_mutation | project-mutation | exercised-apply | ~7500 forward + ~24000 reverse | yes | n/a | partial (element-remove gap) | 1 (P2) | keep-experimental | round-trip proven on test project; element-remove gap blocks identity-revert |
| tool | set_conditional_property_preview | project-mutation | exercised-preview-only | 5 | yes | n/a | n/a | 0 | **promote** | emits well-formed conditional PropertyGroup |
| prompt | discover_capabilities | prompt | exercised | 5 | yes | n/a | n/a | 0 | **promote** | exhaustive 169-tool enumeration, zero hallucinations |
| prompt | explain_error | prompt | exercised | 8807 | yes | n/a | n/a | 0 | **promote** | embeds source context window correctly |
| prompt | suggest_refactoring | prompt | exercised | 1 | yes | n/a | n/a | 0 | **promote** | sound refactor menu |
| prompt | review_file | prompt | exercised | 21 | yes | n/a | n/a | 0 | **promote** | embeds doc-symbols + diags |
| prompt | refactor_and_validate | prompt | exercised | 1190 | yes | n/a | n/a | 0 | **promote** | sound workflow |
| prompt | fix_all_diagnostics | prompt | exercised | 2 | yes | n/a | n/a | 0 | **promote** | preview-first loop correct |
| prompt | guided_package_migration | prompt | exercised | 2057 | yes | n/a | n/a | 0 | **promote** | composite-preview path |
| prompt | dead_code_audit | prompt | exercised | 156 | yes | n/a | n/a | 0 | **promote** | caveats about reflection/serialization correct |
| prompt | review_complexity | prompt | exercised | 1 | yes | n/a | n/a | 0 | **promote** | hotspot numbers inlined |
| prompt | cohesion_analysis | prompt | exercised | 1 | yes | n/a | n/a | 0 | **promote** | LCOM4 explanation clear |
| prompt | msbuild_inspection | prompt | exercised | 0 | yes | n/a | n/a | 0 | **promote** | concrete tool sequence |
| prompt | session_undo | prompt | exercised + idempotent | 0 | yes | n/a | yes (byte-identical) | 0 | **promote** | canonical undo path |
| prompt | analyze_dependencies | prompt | exercised-overflow | n/a | yes | n/a | n/a | 1 (P1) | keep-experimental | output cap risk on 36-proj solution |
| prompt | debug_test_failure | prompt | exercised-side-effect | 16538 | yes | n/a | n/a | 1 (P1) | **needs-more-evidence (blocked)** | actually runs `dotnet test` |
| prompt | guided_extract_interface | prompt | exercised-overflow | 1 | yes | n/a | n/a | 1 (P1) | keep-experimental | inlines full project graph |
| prompt | security_review | prompt | exercised-side-effect | 17631 | yes | n/a | n/a | 1 (P1) | **needs-more-evidence (blocked)** | actually runs nuget vulnerability scan |
| prompt | review_test_coverage | prompt | exercised-overflow | n/a | yes | n/a | n/a | 1 (P1) | keep-experimental | >116 KB rendered |
| prompt | consumer_impact | prompt | partial (schema-only) | n/a | yes | n/a | n/a | 0 | needs-more-evidence | required param scope |
| prompt | guided_extract_method | prompt | partial (schema-only) | n/a | yes | n/a | n/a | 0 | needs-more-evidence | required param scope |
| prompt | refactor_loop | prompt | partial (schema-only) | n/a | yes | n/a | n/a | 0 | needs-more-evidence | required param scope |
| resource | source_file_lines | resource | partial-blocked | n/a | unknown | n/a | n/a | 0 | needs-more-evidence | resource-channel-only feature (marker comment) not observable via tool channel from subagent |
| resource | server_catalog_full / tools_page / prompts_page | resource | not specifically exercised | n/a | n/a | n/a | n/a | 0 | needs-more-evidence | only `server_catalog` summary read |

**Scorecard summary:** 15 `promote`, 13 `keep-experimental`, 14 `needs-more-evidence` (incl. 6 blocked by FAIL findings), 0 `deprecate`. **The 6 blocked entries (symbol_refactor_preview, validate_workspace, test_reference_map, get_prompt_text, debug_test_failure prompt, security_review prompt) MUST be addressed before their tier promotion is reconsidered.**

## 12. Debug log capture
**N/A — client did not surface MCP log notifications** (Claude Code CLI does not propagate `notifications/message` events to the agent).

## 13. MCP server issues (bugs)

### 13.1 `symbol_refactor_preview` — preview-vs-apply contract violation

| Field | Detail |
|-------|--------|
| Tool | `symbol_refactor_preview` |
| Input | 2-op composite (rename `BuildRejectedResponse_v2` → `BuildRejectedResponse_v3` + edit inserting marker comment in `ChatOrchestrationPipeline.cs`) |
| Expected | `previewToken` returned; workspace unchanged until `preview_multi_file_edit_apply(previewToken)` is called |
| Actual | `previewToken=9c24d391...` returned, BUT `workspace_changes` shows the composite was APPLIED at seq 2+3 with `toolName: "symbol_refactor_preview"` — the apply landed without the agent calling any `*_apply` tool |
| Severity | **P1 (release blocker)** |
| Reproducibility | 1 of 1 in this run (Phase 6k) |
| Likely fix | Either the tool auto-applies (regression — fix the preview-only contract) OR `workspace_changes` misattributes the toolName for an internal apply step (audit-trail correctness fix). Both are valid hypotheses; needs server-side investigation to disambiguate. |

### 13.2 `validate_workspace` — 25 s timeout regression on solution-scale workspaces

| Field | Detail |
|-------|--------|
| Tool | `validate_workspace` |
| Input | `runTests=false, summary=true, responseFormat=markdown` against 36-project / 837-document workspace; also `changedFilePaths=["NONEXISTENT/path.cs"]` (fabricated-path) |
| Expected | Either complete the auto-scoped `project_diagnostics` phase within budget OR return a clean partial result with a clear next step |
| Actual | `InternalValidationTimeoutException` at 25 s; both auto-scoped and fabricated-path probes hit the same gate |
| Severity | **P1** |
| Reproducibility | 2 of 2 in this run (Phase 8) |
| Likely fix | Either raise the internal timeout (env-var-configurable per `ROSLYNMCP_*` patterns), shrink the auto-scope when `IChangeTracker` reports many dirty projects, or partial-result on phase-by-phase basis. `validate_recent_git_changes` succeeded with degraded-mode warnings — could share that fallback pattern. |

### 13.3 `test_reference_map` — pagination cap only scopes `coveredSymbols`

| Field | Detail |
|-------|--------|
| Tool | `test_reference_map` |
| Input | `limit=10` against `ITChatBot.Chat` (153 covered / 1035 uncovered / 109 mockDriftWarnings) |
| Expected | Response paginates all enumerated collections via `limit` (per description: "Responses paginate via offset/limit") |
| Actual | `coveredSymbols` capped to 10, but `mockDriftWarnings` (109 entries) and `uncoveredSymbols` are unpaginated → 60 KB payload at the smallest `limit` value |
| Severity | **P1** (breaks MCP payload contract for any workspace with notable mock drift) |
| Reproducibility | 1 of 1 in this run (Phase 8) |
| Likely fix | Apply `limit`/`offset` to all enumerable collections in the response; OR add per-collection caps (`maxMockDriftWarnings`, `maxUncoveredSymbols`). |

### 13.4 `get_di_registrations` — default response unbounded on real DI graphs

| Field | Detail |
|-------|--------|
| Tool | `get_di_registrations` (default shape, `showLifetimeOverrides=true`) |
| Input | 36-project workspace with 141 DI registrations (90 Singleton / 36 Scoped / 15 Transient) across 111 distinct service types |
| Expected | Default-shape response fits MCP token cap |
| Actual | 86,428-char response exceeds inline cap; tool degrades to write-to-disk envelope |
| Severity | **P1** (default callers fail on real-world DI graphs of this size; `summary=true` is the documented workaround but it's a footgun) |
| Reproducibility | 1 of 1 in this run (Phase 11) |
| Likely fix | Auto-degrade to `summary=true` when projected payload exceeds threshold; emit `hasMore` token; OR introduce `offset`/`limit` defaults on the legacy shape. |

### 13.5 Preview-token TTL — workspace auto-reload silently invalidates tokens

| Field | Detail |
|-------|--------|
| Tool | Multiple — `scaffold_test_apply`, `format_document_apply`, `*_apply` in general |
| Input | Preview token generated by `scaffold_test_preview` at T0; intervening `compile_check` at T1 triggered `staleAction: "auto-reloaded"`; apply attempt at T2 with the original token |
| Expected | Either token remains valid (server cache the diff), OR error message explicitly says the token expired due to workspace reload |
| Actual | `NotFound: Preview token … not found or expired` — generic message that doesn't disambiguate "never existed" from "expired by reload" |
| Severity | P1 (UX trap — costs a round-trip every time a caller interleaves preview with a workspace-mutating call) |
| Reproducibility | 1 of 1 in this run (Phase 12); also observed implicitly in Phase 6k |
| Likely fix | Enrich the error with `reason: "workspace-reloaded"` and an actionable hint OR regenerate the token from cached state when the diff is still valid. |

### 13.6 `get_prompt_text` — side-effects in prompt rendering

| Field | Detail |
|-------|--------|
| Tool | `get_prompt_text` |
| Input | `promptName="debug_test_failure"` with realistic args; separately `promptName="security_review"` |
| Expected | Pure template substitution — tool description claims "returns the rendered message list as JSON" |
| Actual | `debug_test_failure` actually invoked `dotnet test` (16.5 s, captured 1126 test outcomes); `security_review` actually invoked `nuget_vulnerability_scan` against 36 projects (17.6 s) |
| Severity | **P1** (promotion blocker — prompt rendering should be cheap and side-effect-free; this turns every prompt-discovery call into a heavyweight workspace probe) |
| Reproducibility | 1 of 1 each (Phase 16) |
| Likely fix | Separate prompt rendering from prompt execution. Render should return the message-array template only; the agent calls the named tools in sequence at its own pace. |

### 13.7 `get_prompt_text` — payload-cap overflow on large workspaces

| Field | Detail |
|-------|--------|
| Tool | `get_prompt_text` |
| Input | `promptName="analyze_dependencies"` (98 KB rendered), `promptName="review_test_coverage"` (116 KB), `promptName="guided_extract_interface"` (~30+ KB) |
| Expected | Rendered text fits MCP inline payload cap |
| Actual | All 3 exceed the cap; one returned the file-artifact-redirect envelope (analogous to `get_di_registrations` behavior) |
| Severity | **P1** (default callers can't consume these prompts on workspaces > ~30 projects) |
| Reproducibility | 3 of 3 affected prompts (Phase 16) |
| Likely fix | Summarize / paginate the embedded project graph / symbol tables in the prompt templates. Render the prompt envelope at fixed bounded size; defer detail to follow-up tool calls. |

### 13.8 `test_run` — OR-pipe filter aggregation drops MTP exe results

| Field | Detail |
|-------|--------|
| Tool | `test_run` |
| Input | Filter `FullyQualifiedName~A|FullyQualifiedName~B` composed by `test_related_files` against 14 tests across 2 projects |
| Expected | Filter applies across all matching test executables; aggregate result counts every match |
| Actual | Aggregate `total=1 passed=1`; Worker.Tests exe stdout reported "No test matches the given testcase filter" despite test FQNs containing matching substrings. Microsoft.Testing.Platform exe runners do not honor VSTest's OR-pipe filter syntax |
| Severity | P2 |
| Reproducibility | 1 of 1 in this run (Phase 8) |
| Likely fix | Detect MTP exe runners and either rewrite the filter syntax per their grammar, or split into per-exe invocations and merge results. |

### 13.9 `set_project_property_preview` — no element-remove semantic

| Field | Detail |
|-------|--------|
| Tool | `set_project_property_preview` + `apply_project_mutation` |
| Input | Forward: `LangVersion=latest` on test csproj (originally had NO `<LangVersion>` element). Reverse: `LangVersion=default` |
| Expected | Round-trip restores file to byte-identical pre-state |
| Actual | Reverse leaves vestigial `<LangVersion>default</LangVersion>` element; true file-identical revert is impossible via this tool path |
| Severity | P2 (design gap; callers must rely on `revert_apply_by_sequence` for true round-trip) |
| Reproducibility | 1 of 1 in this run (Phase 13) |
| Likely fix | Add `op: "remove"` / `op: "unset"` mode; OR document that `revert_apply_by_sequence` is the canonical revert path for project-property mutations. |

### 13.10 `scaffold_test_batch_preview` — silent same-destination dedupe

| Field | Detail |
|-------|--------|
| Tool | `scaffold_test_batch_preview` |
| Input | 4 targets (same type, different methods) against `ITChatBot.Conversation.Tests` |
| Expected | 4 file diffs in `changes[]` OR a composite token covering 4 distinct files (one per target) |
| Actual | Composite token `57c16fa1...` but `changes[]` contained only 1 file diff + 3 "Skipped … target file already exists" warnings — all 4 targets resolved to the same destination path |
| Severity | P2 (the warnings cover it, but `changes[]` length is misleading for callers reading just the structured output) |
| Reproducibility | 1 of 1 in this run (Phase 12) |
| Likely fix | Vary the output filename by `targetMethodName` (e.g., `InMemoryConversationRepository_AppendMessageAsync_GeneratedTests.cs`) when multiple method-level targets share a type. |

### 13.11 `symbol_impact_sweep.mapperCallsites` — `*Adapter` false-positive

| Field | Detail |
|-------|--------|
| Tool | `symbol_impact_sweep` |
| Input | `IChannelAdapter` interface (2 impls: SlackChannelAdapter, TeamsChannelAdapter) |
| Expected | `mapperCallsites` populated with actual mapper-pattern call sites (`To*` / `From*` methods, etc.) |
| Actual | Both `*Adapter` classes flagged as mapper-callsites — heuristic conflates the Adapter and Mapper design patterns |
| Severity | P2 |
| Reproducibility | 1 of 1 in this run (Phase 3) |
| Likely fix | Narrow the suffix heuristic — require `*Mapper` / `*Converter` / `*Marshaller` rather than the open-ended `*Adapter`. OR weight by member-pattern signature (does the type define `To*` / `From*` methods?). |

### 13.12 `member_hierarchy` — lacks `preferDeclaringMember` knob

| Field | Detail |
|-------|--------|
| Tool | `member_hierarchy` |
| Input | Caret on return-type token (line=30 col=35 inside SlackChannelAdapter.NormalizeInboundAsync's return type) |
| Expected | Auto-promote return-type-token caret to the enclosing method (like `symbol_relationships` and `symbol_signature_help` do) OR provide a `preferDeclaringMember` knob to opt in |
| Actual | Resolves return-type token literally to the type `NormalizedInboundMessage`, not the method; no `preferDeclaringMember` parameter exposed in the schema |
| Severity | P2 (asymmetry across related symbol-resolution tools surprises callers driving the same caret across them) |
| Reproducibility | 1 of 1 in this run (Phase 3) |
| Likely fix | Add `preferDeclaringMember` parameter mirroring `symbol_relationships` / `symbol_signature_help`. |

### 13.13 `organize_usings_preview` — suspected over-removal of Microsoft.Extensions usings

| Field | Detail |
|-------|--------|
| Tool | `organize_usings_preview` |
| Input | `src/worker/Jobs/ContentGapDetectionJob.cs` |
| Expected | Only remove provably-unused usings |
| Actual | Preview removes `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging` — namespaces that appear referenced by ctor signature (logger / config DI types). `apply_with_verify` post-apply error count was 0, so the build still succeeded — either the ctor uses inferred types or the analyzer didn't see refs |
| Severity | P2 (warrants targeted unit test) |
| Reproducibility | 1 of 1 in this run (Phase 6l) |
| Likely fix | Re-test the analyzer's reference scan against ctor parameter types specifically; add fixtures for DI-style files. |

### 13.14 `find_overrides` — silent empty on interface-type metadataName

| Field | Detail |
|-------|--------|
| Tool | `find_overrides` |
| Input | `metadataName="ITChatBot.Conversation.IConversationRepository"` (interface TYPE, not member) |
| Expected | Reject with actionable error ("symbol is a type, did you mean a member?") OR auto-resolve to the interface's first member |
| Actual | Silent `count:0` |
| Severity | P3 |
| Reproducibility | 1 of 1 in this run (Phase 14) |
| Likely fix | Add input validation: if resolved symbol kind is `NamedType` and the symbol is an interface, return `category: InvalidArgument` with a hint suggesting the caller pass a member of the type. |

### 13.15 `remove_project_reference_preview` — Timeout surfaces before gate auto-reload retry

| Field | Detail |
|-------|--------|
| Tool | `remove_project_reference_preview` |
| Input | Standard remove probe during a Phase-6-induced stale-workspace window |
| Expected | Either wait for auto-reload to complete OR return a retry-friendly error category |
| Actual | First attempt returned `Timeout` category at 18.6 s with helpful env-var hint (`ROSLYNMCP_REQUEST_TIMEOUT_SECONDS`); immediate retry succeeded in 2 ms |
| Severity | P3 |
| Reproducibility | 1 of 1 in this run (Phase 13) |
| Likely fix | The gate's `Workspace ... is currently loading` path should retry once internally before surfacing `TimeoutException`. |

### 13.16 `apply_with_verify` — rollback path not validated against real CS-error

| Field | Detail |
|-------|--------|
| Tool | `apply_with_verify(rollbackOnError=true)` |
| Input | `extract_method_preview` token with `methodName="Equals"` (intended to introduce a CS0136 conflict) |
| Expected | Either `status=applied` (v1.15+ regression-resistant) OR `status=rolled_back` with `introducedErrors` populated |
| Actual | `status=applied, preErrorCount=0, postErrorCount=0` — the induced clash didn't actually produce CS-error because `int Equals()` is a legal overload of `object.Equals(object)`. The rollback code path was NOT actually exercised on a real failure |
| Severity | P3 (audit coverage gap, not a defect) |
| Reproducibility | 1 of 1 in this run (Phase 6l) |
| Likely fix | Future re-run should pick a region whose extraction yields a guaranteed CS0136 (e.g., a shadowed LOCAL variable name) to validate the rollback code path end-to-end. |

### 13.17 Phase 6 sub-phases 6a / 6c-6j — `phase-failed-budget` (audit-prompt completion gate)

| Field | Detail |
|-------|--------|
| Tool | (orchestration / dispatch) |
| Input | First Phase 6 dispatch to general-purpose subagent with 12 sub-phases scoped |
| Expected | All sub-phases run with structured envelope returned |
| Actual | Subagent truncated mid-emission after 24 tool calls (sub-phase 6b's verification chain); only 1 apply landed (rename_apply); no envelope returned |
| Severity | P1 (audit defect per the skill's completion gate — "Silent truncation labeled as 'representative probe' is no longer an acceptable outcome — `--full` means full or it means honest failure with a named cause.") |
| Reproducibility | 1 of 1 in this run |
| Likely fix | Future Phase 6 dispatches should be split per-sub-phase (one runner per 6a / 6e / 6f / etc.) to keep each subagent's context budget bounded. The 6k+6l re-dispatch demonstrated this works (17 calls, complete envelope). |

### 13.18 Phase 10 partial completion — `phase-failed-budget`

| Field | Detail |
|-------|--------|
| Tool | (orchestration / dispatch) |
| Input | Phase 10 dispatch to general-purpose subagent |
| Expected | All file/cross-project operations with at least one preview→apply→verify chain |
| Actual | Subagent truncated mid-flow after 12 tool calls. `create_file_apply` for SurfaceTestMarker.cs DID land (seq #7); the matching `delete_file_apply` did NOT. Orchestrator-inline recovery completed the delete chain. |
| Severity | P1 (audit defect; orchestrator recovered the apply-chain mandatory step but the cross-project preview families — `extract_interface_cross_project_preview`, `dependency_inversion_preview`, `move_type_to_project_preview`, `extract_and_wire_interface_preview`, `split_class_preview`, `split_service_with_di_preview`, `migrate_package_preview` — were not exercised) |
| Reproducibility | 1 of 1 in this run |
| Likely fix | Same as 13.17: split per sub-phase or per tool family. |

## 14. Improvement suggestions

UX / output enrichment / workflow gaps with concrete fix sketches:

- `diagnostic_details` should include a one-line "this diagnostic has N occurrences in this scope" preview, similar to `project_diagnostics` summary mode. Currently you must echo back to `project_diagnostics` to count.
- `compile_check` could expose a `compareTo:"getDiagnostics"` mode alongside the emit result, to surface the emit-vs-GetDiagnostics delta as a single response rather than requiring two calls.
- `security_analyzer_status` (13.6 s for a presence check, see Section 6) should cache restore status — the slow path appears to walk every project's package list each time.
- `project_diagnostics` severity filter could surface "filter excluded all rows in scope" hint to distinguish from "scope has no warnings."
- `get_cohesion_metrics` should auto-detect test fixtures (one-test-per-method shape) and tag them with `pattern:"test-fixture"` to let callers filter — currently they flood the LCOM4 top-list.
- `find_unused_symbols(includePublic=true)` should default `excludeRecordProperties=true` to reduce DTO-shaped low-confidence noise (default response includes 23 record-property hits before the first medium-confidence one).
- `suggest_refactorings` could promote cross-file duplication (e.g., `MapHttpFailureType` across adapters) into its own category — currently it only ranks complexity / cohesion / unused.
- `find_dead_fields` could expose `--remediation-hint` describing "remove constructor parameter and assignment" for the constructor-write-only case, since `safelyRemovable=false` discourages action even when the field is genuinely dead.
- `get_namespace_dependencies(circularOnly=true)` with 0 cycles should still return a scanned-projects/namespaces summary so consumers can distinguish "no cycles" from "tool didn't run."
- `find_type_mutations` could expand `MutationScope` classification to detect await-invocation patterns on `HttpClient` / `Process` / `File` / `DbContext` regardless of containing-type field-write status (currently `IO/Network/Process/Database` are not exercisable when mutations route through scoped DI services).
- `get_operations` should echo `requestedLine`/`requestedColumn` for symmetry with `probe_position` and `get_source_text`.
- The `1/8 abandoned-worker` disclosure in `evaluate_csharp` timeout responses is excellent — consider surfacing it as a structured `abandonedWorkerThreadCount` field rather than embedded only in the error string.
- `set_editorconfig_option` should skip the file write entirely when the value already matches on-disk (no CRLF→LF rewrite). Today this produces noise in revert workflows.
- `revert_apply_by_sequence` could distinguish `reason: "already-reverted"` vs `reason: "out-of-range"` for clients that want to surface "this was previously undone" UX differently from "this number was never an apply."
- `get_prompt_text` for `analyze_dependencies` / `review_test_coverage` / `guided_extract_interface` should summarize the embedded project graph / symbol tables before promotion — or render them lazily via follow-up tool calls.
- `scaffold_first_test_file_preview` could prefer the test project whose name most closely mirrors the service's containing project (`ITChatBot.Conversation` → `ITChatBot.Conversation.Tests`) when no `testProjectName` is supplied; today it errors with the candidate list.
- `change_signature_preview` schema should document that `op=reorder` succeeds silently when all callsites are positional-only (the audit prompt assumed it would error — it doesn't, and that's reasonable but undocumented).

## Phase 19 status

**Maintainer detection:** operator is `darylmcd` (matches `gh api user --jq .login` per CLAUDE.md) — auto-file path would route findings to `https://github.com/darylmcd/Roslyn-Backed-MCP/issues`.

**Auto-file deferred:** Due to remaining orchestrator context budget after a long multi-wave dispatch with two recovery re-dispatches (Phase 6 partial → 6k+6l re-dispatch; Phase 10 partial → inline create+delete completion), the `gh issue create` + dedup-check loop for the ~16 actionable findings in Section 13 + 16 in Section 14 was not executed. **The findings above are rendered in the canonical Section 13 envelope shape and are ready for manual filing OR a follow-up audit run focused exclusively on finding emission.** Each Section 13 entry contains: `Tool`, `Input`, `Expected`, `Actual`, `Severity`, `Reproducibility`, `Likely fix` — the exact fields the shared `Render-FindingIssue` PowerShell renderer expects.

**Refusal contract check:** No P0 findings and no `area: security` findings in this audit. All findings are P1 / P2 / P3 in the `tools` / `resources` / `prompts` / `perf` areas. None trigger the security-advisory refusal banner.

**Recommended follow-up:** Manually file the top 5 P1 findings (Sections 13.1, 13.2, 13.3, 13.5, 13.6) via `gh issue create --repo darylmcd/Roslyn-Backed-MCP` with `area:tools` / `severity:P1` labels. Sections 13.4 + 13.7 are bounded to the legacy default shape and may already have related issues (suggest dedup-check first).

## 15. Concurrency matrix (Phase 8b)

### Concurrency probe set
| Slot | Tool | Inputs (concise) | Classification | Notes |
|------|------|------------------|----------------|-------|
| R1 | find_references | metadataName=ChatOrchestrationPipeline, summary=true | reader | 5 hits |
| R2 | project_diagnostics | summary=true | reader | 1445 diags, 25 ids |
| R3 | symbol_search | query=Async, limit=5 | reader | totalCount=1000, hasMore |
| R4 | find_unused_symbols | includePublic=false, limit=10 | reader | 1 hit |
| R5 | get_complexity_metrics | ITChatBot.Chat, minComplexity=10 | reader | 5 hot methods |
| W1 | format_document_preview → format_document_apply | Phase-6-touched file | writer | covered via 8b.5 #1 + #2 |
| W2 | set_editorconfig_option (then `git checkout` revert) | benign key | writer | mutation isolation contract observed |

### Sequential baseline (single-call wall-clock, ms)
| Slot | Wall-clock (ms) | Notes |
|------|------------------|-------|
| R1 | 29 | hot symbol |
| R2 | 36 | summary mode |
| R3 | 175 | 1000-hit cap with summary |
| R4 | 259 | private-only scan |
| R5 | 2 | cached-warm |

### Parallel fan-out and behavioral verification
- **Host logical cores:** unreported by the audit runner
- **Chosen N:** N=min(4, max(2, logical_cores)) — not exercised

| Slot | Parallel wall-clock (ms) | Speedup vs baseline | Expected | Pass / FLAG / FAIL | Notes |
|------|---------------------------|----------------------|----------|---------------------|-------|
| R1 | N/A — client serializes | N/A | N/A | **blocked** | Claude Code CLI does not issue concurrent MCP tool calls |
| R2 | N/A | N/A | N/A | blocked | |
| R3 | N/A | N/A | N/A | blocked | |
| R4 | N/A | N/A | N/A | blocked | |
| R5 | N/A | N/A | N/A | blocked | |

### Read/write exclusion behavioral probe
| Probe | Observed | Expected | Pass / FLAG / FAIL | Notes |
|-------|----------|----------|---------------------|-------|
| R-then-W | N/A | reader → writer waits | blocked | client serializes |
| W-then-R | N/A | writer → reader waits | blocked | client serializes |

### Lifecycle stress
| Probe | Observed | Reader saw | Reader exception | correlationId | Expected | Pass / FLAG / FAIL | Notes |
|-------|----------|-----------|------------------|---------------|----------|---------------------|-------|
| R + workspace_reload | N/A | n/a | n/a | n/a | reader waits | blocked | client serializes |
| R + workspace_close | OBSERVED post-hoc | clean error post-close (Phase 17e probe) | n/a | n/a | reader completes; close waits | PASS (observed via 17e equivalent) | workspace_close completed cleanly (Final Closure); workspace_status on closed id returned clean NotFound |

**Phase 8b verdict:** Sequential baseline established. Parallel + RW-exclusion probes BLOCKED across the board by client serialization (Claude Code CLI does not issue concurrent MCP tool calls). The `WorkspaceExecutionGate` (`rw-lock` mode confirmed in every response's `_meta.gateMode`) WAS observed serializing writer-after-writer calls during Phase 8b.5 (apply_text_edit → apply_multi_file_edit queued 8.4 s; set_diagnostic_severity queued 9.2 s). This is indirect evidence that the gate enforces serialization between writers, even when the client can't drive true concurrency.

## 16. Writer reclassification verification (Phase 8b.5)

| # | Tool | Status | Wall-clock (ms) | Notes |
|---|------|--------|------------------|-------|
| 1 | apply_text_edit (verify=true) | PASS | 54 | ContentGapDetectionJob.cs; verification clean (postErrorCount=0) |
| 2 | apply_multi_file_edit (verify=true) | PASS | 9096 | 2 files (ChatOrchestrationPipeline.cs + Streaming.cs); queued 8.4 s behind prior apply; verification clean |
| 3 | revert_last_apply | skipped | n/a | Phase 9 owns this — exercised there |
| 4 | set_editorconfig_option | PASS | 4 | cross-ref Phase 7 step 2; idempotent CRLF write produced git-dirty status (P3 cosmetic — see Section 7) |
| 5 | set_diagnostic_severity (CA1848=suggestion) | PASS | 9199 | queued 9.2 s behind prior writer; reverted via `git checkout -- .editorconfig` post-call |
| 6 | add_pragma_suppression (CA1848 line 12 ContentGapDetectionJob.cs) | PASS | 7 | `get_source_text` post-apply confirms `#pragma warning disable CA1848` at line 12 |

All 6 writers exercised; one (`revert_last_apply`) deliberately deferred to Phase 9. Gate serialization observed via queueing latency on writers 2 + 5.

## 17. Response contract consistency

| Tools | Concept | Inconsistency | Notes |
|-------|---------|---------------|-------|
| `member_hierarchy` vs `symbol_relationships` / `symbol_signature_help` | Caret auto-promotion to enclosing member | `symbol_relationships` and `symbol_signature_help` accept `preferDeclaringMember` (default true → auto-promote); `member_hierarchy` has no such parameter and always resolves literally | See 13.12 |
| `get_operations` vs `probe_position` / `get_source_text` | Coordinate echo in response | `probe_position` and `get_source_text` echo requested coords; `get_operations` only returns resolved position | See improvement suggestion in Section 14 |
| `revert_apply_by_sequence` reason codes | Distinguishing already-reverted vs unknown sequence | Both surface `reason: "unknown-sequence"` — caller can't distinguish the two cases without re-reading `workspace_changes` | See improvement suggestion in Section 14 |
| `get_di_registrations` vs default-shape contract | Pagination | Tool has `summary=true` to handle scale, but default shape doesn't auto-degrade or expose `offset`/`limit` defaults — diverges from the rest of the catalog's pagination convention | See 13.4 |
| `get_prompt_text` vs catalog `prompt` contract | Pure template fill | 2 prompts (`debug_test_failure`, `security_review`) execute heavy tools during render; other prompts return quickly — inconsistent expectations across the prompt surface | See 13.6 |
| `set_project_property_preview` element-handling | Round-trip identity | Tool can set values but cannot remove elements; reverse-apply leaves vestigial XML. Contrasts with `add_package_reference_preview` / `remove_package_reference_preview` which are symmetric | See 13.9 |

## 18. Known issue regression check (Phase 18)

Prior source: `ai_docs/backlog.md` (3 rows in the Medium queue cite 2026-05-13 Roslyn audit evidence).

| Source id | Summary | Status (re-verified 2026-05-16 via this audit) |
|---|---|---|
| `queue-audit-device-registry-enrichment-complexity` | 2026-05-13 Roslyn complexity scan: `DeviceRegistryEnrichmentPipelineStage.ExecuteAsync` CC=22, 84 LOC, MI=37, max nesting 4 | **Still reproduces.** G1 (this run) re-confirmed: `DeviceRegistryEnrichmentPipelineStage.ExecuteAsync` CC=22 at `src/chat/Pipeline/Stages/DeviceRegistryEnrichmentPipelineStage.cs:14`. Identical numeric profile 3 days later. Backlog row remains open. |
| `queue-audit-admin-management-shared-guards` | 2026-05-13 Roslyn `find_duplicated_methods`: identical `ValidateConfigurationJson` / `ValidateCredentialReference` / raw-secret regex / `WriteAuditAsync` shapes in `SourceManagementService` + `ChannelManagementService` | **Still reproduces (broader pattern).** G1 (this run) `find_duplicated_methods` returned 10 groups; admin-specific cluster confirmed via `AdminAnalyticsEndpoints.HandleGet*Async` family (src/api/Endpoints/AdminAnalyticsEndpoints.cs:26-159) and cross-adapter cluster (`MapHttpFailureType` / `CreateFailure` / `TruncateField` in SysLogServer + NetworkDocumentation). The audit-management-shared-guards backlog row's evidence is a subset of the duplication signal still emitted. |
| `queue-audit-repository-search-async-io` | 2026-05-13 refactor audit: `GitRepositoryAdapter.QueryAsync` / `GetByIdAsync` call `Search`/`GetFileContent` using `File.ReadAllLines`/`File.ReadAllText` on async request paths | **Still reproduces (heuristic confirmation).** Anchor files (`src/adapters/Repository/RepositoryContentSearchEngine.cs`, `src/adapters/Repository/GitRepositoryAdapter.cs`) are unchanged in `git log` since the 2026-05-13 evidence date — the synchronous I/O pattern still exists in code. Spot-checked file paths via Glob; backlog row's evidence remains accurate. |

## 19. Known issue cross-check
- New finding `DeviceRegistryEnrichmentPipelineStage.ExecuteAsync cc=22` matches existing backlog row `queue-audit-device-registry-enrichment-complexity` — no new ticket needed.
- New finding `AdminAnalyticsEndpoints.HandleGet*Async duplication` overlaps with backlog row `queue-audit-admin-management-shared-guards` (broader scope; the backlog row points at SourceManagementService/ChannelManagementService, my audit found the API-endpoint family — both are evidence of the same root cause and should be addressed together).
- New finding `cross-adapter MapHttpFailureType / CreateFailure / TruncateField duplication (SysLogServer + NetworkDocumentation)` is **NOT** captured by the existing backlog — recommend filing a new backlog row `queue-audit-adapter-failure-mapper-extraction` or similar.
- New finding `SourceSyncFailureTrigger._logger constructor-write-only dead field` is **NOT** captured by the existing backlog — a single-line `// dead field` cleanup, not worth a backlog row but worth fixing opportunistically next time that file is touched.

---

## Appendix A — Raw phase evidence (subagent envelopes)

### Wave 1 / G1 — Phase 1 + Phase 2 (audit-phase-runner)

```
<<<RESULT G1>>>

### Phase 1 — Broad diagnostics scan

#### Coverage ledger rows
| Kind | Name | Tier | Category | Status | Phase | lastElapsedMs | Notes |
| tool | project_diagnostics | stable | diagnostics | exercised | 1 | 18147 | summary=true call; sub-50ms on filtered |
| tool | project_diagnostics (filtered) | stable | diagnostics | exercised | 1 | 100 | severity=Warning narrowed to 3 rows; totals invariant |
| tool | compile_check | stable | diagnostics | exercised | 1 | 47 | default pagination, all 36 projects |
| tool | compile_check (severity) | stable | diagnostics | exercised | 1 | 20 | severity=Error filter |
| tool | compile_check (file) | stable | diagnostics | exercised | 1 | 36 | filePath narrowing |
| tool | compile_check (emitValidation) | stable | diagnostics | exercised | 1 | 2630 | ~60x slower vs non-emit |
| tool | security_diagnostics | stable | security | exercised | 1 | 2625 | 0 findings |
| tool | security_analyzer_status | stable | security | exercised | 1 | 13634 | netAnalyzers + SCS present |
| tool | nuget_vulnerability_scan | stable | security | exercised | 1 | 16353 | 0 vulns across 36 projects |
| tool | list_analyzers | stable | diagnostics | exercised | 1 | 1658 | 34 assemblies / 553 rules |
| tool | list_analyzers (projectName) | stable | diagnostics | exercised | 1 | 1 | 31/466 for ITChatBot.Api |
| tool | diagnostic_details (CA1826) | stable | diagnostics | exercised | 1 | 72 | accurate location, supportedFixes=[] expected |
| tool | diagnostic_details (CA1847) | stable | diagnostics | exercised | 1 | 2 | accurate location |
| tool | project_diagnostics (diagId) | stable | diagnostics | exercised | 1 | 21 | diagnosticId filter behaves correctly |

#### Per-call evidence
1. project_diagnostics (summary) — PASS — elapsedMs=18147 — TotalErrors=0 TotalWarnings=3 TotalInfo=1442 — Top IDs: xUnit1051 (311), CA1062 (231), CA1307 (194), CA1515 (180), CA1848 (137).
2. project_diagnostics (projectName=ITChatBot.Api, severity=Warning, limit=50) — PASS invariant — totalInfo=250 totalWarnings=0 totalDiagnostics=0 (correctly zero for project scope; unfiltered call confirms totalWarnings=3 invariant).
3. compile_check (default) — PASS — 0 CS diagnostics across 36 projects.
4. compile_check (emitValidation=true) — PASS — ~56x slower than non-emit baseline; no extra diagnostics.
5. security_diagnostics — PASS — 0 findings.
6. security_analyzer_status — PASS — netAnalyzersPresent=true, securityCodeScanPresent=true, pumaSecurityRulesPresent=false. (FLAG: 13.6s presence check is slow.)
7. nuget_vulnerability_scan — PASS — 0 vulns across 36 projects. (FLAG: 16.3s exceeded 15s budget.)
8. list_analyzers — PASS — 34 assemblies / 553 rules, no LOAD_ERROR.
9. list_analyzers (projectName=ITChatBot.Api) — PASS — narrows to 31/466.
10. diagnostic_details (CA1826 @ line 44 col 30; CA1847) — PASS — exact locations, supportedFixes=[] (documented).

#### Phase 1 findings (P0/P1/P2/P3)
- P3 UX: security_analyzer_status 13.6s for a presence check. Suggest caching restore status.

#### Phase 1 improvement suggestions
- diagnostic_details should include "this diagnostic has N occurrences in this scope" preview.
- compile_check could expose a `compareTo:"getDiagnostics"` mode alongside emit result.
- security_analyzer_status should expose its underlying probe.
- project_diagnostics severity filter could surface "filter excluded all rows in scope" hint.

### Phase 2 — Code quality metrics

#### Coverage ledger rows
| Kind | Name | Tier | Category | Status | Phase | lastElapsedMs | Notes |
| tool | get_complexity_metrics | stable | metrics | exercised | 2 | 65 | minComplexity=10, 15 rows |
| tool | get_cohesion_metrics | stable | metrics | exercised | 2 | 123 | minMethods=3 |
| tool | get_coupling_metrics | stable | metrics | exercised | 2 | 3186 | 15 rows |
| tool | find_unused_symbols (private) | stable | dead-code | exercised | 2 | 981 | count=0 |
| tool | find_unused_symbols (public) | stable | dead-code | exercised | 2 | 443 | 25 low-conf DTO props |
| tool | find_duplicated_methods | stable | duplication | exercised | 2 | 112 | 10 groups |
| tool | find_duplicate_helpers | stable | duplication | exercised | 2 | 43 | 2 hits |
| tool | find_duplicated_code | stable | duplication | exercised | 2 | 81 | alias, identical output |
| tool | find_dead_locals | stable | dead-code | exercised | 2 | 55 | project=Chat, count=0 |
| tool | find_dead_fields | stable | dead-code | exercised | 2 | 1520 | 1 hit, safelyRemovable=false |
| tool | get_namespace_dependencies | stable | architecture | exercised | 2 | 103 | circularOnly=true, 0 cycles |
| tool | get_nuget_dependencies | stable | dependencies | exercised | 2 | 2561 | summary mode, 46 packages |
| tool | suggest_refactorings | stable | metrics | exercised | 2 | 780 | 10 ranked |

#### Per-call evidence
1. get_complexity_metrics — Top 5 hot methods:
   - DeviceRegistryEnrichmentPipelineStage.ExecuteAsync cc=22 (src/chat/Pipeline/Stages/DeviceRegistryEnrichmentPipelineStage.cs:14)
   - AdaptiveCardBuilder.RenderBlock cc=18 (src/channels/Teams/AdaptiveCards/AdaptiveCardBuilder.cs:34)
   - ContentGapDetectionJob.AppendGapSignalsForConversation cc=15 (src/worker/Jobs/ContentGapDetectionJob.cs:152)
   - BlockKitBuilder.RenderBlock cc=15 (src/channels/Slack/BlockKit/BlockKitBuilder.cs:28)
   - ServiceCollectionExtensions.AddRepositoryAdapter cc=14 (src/adapters/Repository/ServiceCollectionExtensions.cs:15)
2. get_cohesion_metrics(minMethods=3) — top LCOM4 outliers are test fixtures (one-test-per-method). Source-gen partials properly excluded.
3. get_coupling_metrics — 15 rows; top efferent test fixtures (Ce=31), AdminMemoryEndpoints (Ce=28), SourceManagementService (Ce=27).
4. find_unused_symbols(false) — count=0 (no private dead code).
5. find_unused_symbols(true) — 23 of 25 are confidence=low record props (NormalizedEvidenceRecord etc.). 2 medium: SysLogServerAdapterOptions.HealthCheckIntervalSeconds:16, SysLogEventRecord.Facility:150.
6. find_duplicated_methods/helpers — verified real duplicates: AdminAnalyticsEndpoints.HandleGet*Async (3 handlers), cross-adapter MapHttpFailureType (SysLogServer/NetworkDocumentation), TruncateField, CreateFailure.
7. find_duplicated_code — identical to find_duplicated_methods (alias documented).
8. find_dead_locals(ITChatBot.Chat) — count=0.
9. find_dead_fields — 1 hit: SourceSyncFailureTrigger._logger (src/channels/Notifications/SourceSyncFailureTrigger.cs:12), constructor-write blocks safelyRemovable.
10. get_namespace_dependencies(circularOnly=true) — 0 cycles, 139 namespaces.
11. get_nuget_dependencies(summary=true) — 46 packages, no version drift, all `10.*` floating versions.
12. suggest_refactorings — top 5 match real complexity/cohesion hotspots; tool sequences correctly reference existing tools.

#### Phase 2 findings (P0/P1/P2/P3)
- P2: AdminAnalyticsEndpoints handler duplication (src/api/Endpoints/AdminAnalyticsEndpoints.cs:26-159).
- P2: cross-adapter duplication (CreateFailure/MapHttpFailureType/TruncateField across SysLogServer + NetworkDocumentation).
- P2: DeviceRegistryEnrichmentPipelineStage.ExecuteAsync — extract method candidate.
- P3: SourceSyncFailureTrigger._logger constructor-write-only dead field.
- P3: SysLogServerAdapterOptions.HealthCheckIntervalSeconds + SysLogEventRecord.Facility public-but-unreferenced.

#### Phase 2 improvement suggestions
- get_cohesion_metrics should auto-detect test fixtures and either suppress or tag with `pattern:"test-fixture"`.
- find_unused_symbols(includePublic=true) should default `excludeRecordProperties=true`.
- suggest_refactorings should promote cross-file duplication into its own category.
- find_dead_fields could expose `--remediation-hint` for constructor-write-only cases.
- get_namespace_dependencies(circularOnly=true) should still return scanned-projects summary when 0 cycles.

### Performance baseline rows
(populated in Section 6 below — consolidated)

### Schema vs behaviour drift
None observed in G1.

### Error message quality
No negative probes triggered errors in G1.

### Parameter-path coverage (G1)
- project_diagnostics: projectName+severity, summary=true, diagnosticId
- compile_check: severity, file, emitValidation
- list_analyzers: projectName
- get_complexity_metrics: minComplexity
- get_cohesion_metrics: minMethods
- find_unused_symbols: includePublic
- find_dead_locals: projectFilter
- get_namespace_dependencies: circularOnly
- get_nuget_dependencies: summary

### Experimental promotion signals (G1)
None — all 27 tools called were stable-tier.

### Run summary
Total calls 27 · 0 hard FAILs · 4 FLAGs (perf) · 0 skipped · wall-clock ~65 s.

<<<END RESULT G1>>>
```

### Wave 1 / G3a — Phase 5 (inline)

```
<<<RESULT PHASE 5>>>

#### Coverage ledger rows
| Kind | Name | Tier | Category | Status | Phase | lastElapsedMs | Notes |
| tool | analyze_snippet(expression) | stable | snippet | exercised | 5 | 97 | 1+2; declared Snippet.Evaluate() |
| tool | analyze_snippet(program) | stable | snippet | exercised | 5 | 62 | Greeter class; declared types as expected |
| tool | analyze_snippet(statements) | stable | snippet | exercised | 5 | 75 | int x = "hello"; CS0029 at startColumn=9 (FLAG-C fix CONFIRMED — was 66 pre-v1.7) |
| tool | analyze_snippet(returnExpression) | stable | snippet | exercised | 5 | 60 | return 42; declared object? Snippet.Run() |
| tool | evaluate_csharp(expr) | stable | script | exercised | 5 | 62 | Enumerable.Range(1,10).Sum() == 55 |
| tool | evaluate_csharp(multi-line) | stable | script | exercised | 5 | 16 | sum of squares 1..5 == 55 (cache-hot) |
| tool | evaluate_csharp(runtime-error) | stable | script | exercised | 5 | 62 | int.Parse("abc") returns success=false with FormatException; graceful |
| tool | evaluate_csharp(timeout) | stable | script | exercised | 5 | 13003 | timeoutSeconds=3 → forcibly abandoned at 13003ms (3s budget + 10s grace); 1/8 abandoned worker disclosed in message |

#### Per-call evidence
1. expression (`1 + 2`) — isValid=true, no diagnostics.
2. program (small Greeter class) — declared NamedType: Greeter, Method: string Greeter.Hello(string name).
3. statements (`int x = "hello";`) — isValid=false, CS0029 at startColumn=9 (user-relative). Confirms FLAG-C fix is in production.
4. returnExpression (`return 42;`) — isValid=true; declared `object? Snippet.Run()`.
5. evaluate_csharp(`Enumerable.Range(1, 10).Sum()`) — resultValue="55", resultType="System.Int32".
6. evaluate_csharp(multi-line sum-of-squares) — resultValue="55", cache-hot 16 ms.
7. evaluate_csharp(`int.Parse("abc")`) — success=false, error="Runtime error: FormatException: The input string 'abc' was not in a correct format."
8. evaluate_csharp(`while (true) { }`, timeoutSeconds=3) — success=false, elapsedMs=13002, error message explicitly states "Script execution was forcibly abandoned after 13 second(s) (script budget 3s + ROSLYNMCP_SCRIPT_WATCHDOG_GRACE_SECONDS 10s). Roslyn does not cancel tight infinite loops; the server no longer waits for cooperative cancellation. 1/8 abandoned worker thread(s) outstanding; restart the MCP host if this happens repeatedly." Disclosure quality is excellent.

#### Phase 5 findings (P0/P1/P2/P3)
- None — all 4 documented behaviors confirmed; FLAG-C fix verified in production.

#### Phase 5 improvement suggestions
- The 1/8 abandoned-worker disclosure in `evaluate_csharp` timeout responses is excellent UX but could surface a follow-up metric: `abandonedWorkerThreadCount` as a structured field (currently embedded only in the error string).

<<<END RESULT PHASE 5>>>
```

### Wave 2-pre / Phase 6 — Apply-tool exercise on disposable worktree (inline + 2 subagents)

**Coverage outcome:** 6b (Rename) and 6k+6l (Advanced refactor previews + apply-with-verify) ran end-to-end via two general-purpose subagent dispatches. Sub-phases 6a / 6e / 6f / 6f-ii / 6g / 6h / 6i / 6j are marked `phase-failed-budget` and surfaced as P1 audit defects per the skill's completion gate — they were assigned to the first Phase 6 subagent dispatch (general-purpose) but that subagent ran out of token budget after sub-phase 6b. The orchestrator re-dispatched a tight 6k+6l-only follow-up to preserve the experimental-promotion-scorecard mission. Sub-phase 6m (`workspace_changes`) ran inline from the orchestrator post-dispatch.

```
<<<RESULT PHASE 6b — Rename (subagent dispatch #1 — partial completion)>>>

Sub-phases completed: 6b. Sub-phases dropped due to budget cutoff: 6a, 6c, 6d, 6e, 6f, 6f-ii, 6g, 6h, 6i, 6j, 6k, 6l, 6m.

Coverage ledger: only `rename_apply` confirmed via workspace_changes seq #1.
- Operation: rename `BuildRejectedResponse` → `BuildRejectedResponse_v2`
- AffectedFiles: src/chat/ChatOrchestrationPipeline.cs + src/chat/ChatOrchestrationPipeline.Streaming.cs
- toolName: rename_apply, appliedAtUtc: 2026-05-16T06:45:32Z
- Verification: cross-tool consistency check (find_references on new name = preview count) was in progress when the subagent ran out of budget. Treat the rename apply as exercised-apply but mark the post-apply verification chain as `phase-failed-budget`.

P1 audit defect (surfaced per the completion gate): Phase 6 sub-phases 6a / 6c / 6d / 6e / 6f / 6f-ii / 6g / 6h / 6i / 6j (10 sub-phases) had no MCP tool-call evidence land before the subagent context cut off. The runner returned mid-emission ("Two callsites + declaration. Let me apply."). The orchestrator's recovery dispatch covered 6k+6l only.

<<<END RESULT PHASE 6b>>>
```

```
<<<RESULT PHASE 6k+6l (subagent dispatch #2)>>>

### Phase 6k — Advanced refactor previews

#### Coverage ledger rows
| Kind | Name | Tier | Category | Status | Phase | lastElapsedMs | Notes |
| tool | restructure_preview | experimental | refactor-advanced | exercised-preview-only | 6k | 7 | First pattern (Convert.ToString) no matches; pivoted to IsNullOrEmpty→IsNullOrWhiteSpace in ITChatBot.Worker, 3 matches across 1 file, valid token, no apply |
| tool | replace_string_literals_preview | experimental | refactor-advanced | exercised-preview-only | 6k | 22 | Negative probe with uncommon literal → previewToken="", description="No matching string literals found in scope", changes=[]; clean empty-result schema |
| tool | change_signature_preview | experimental | refactor-advanced | exercised-preview-only | 6k | 5-75 | All 4 ops (add, remove, rename, reorder) exercised against private static AppendGapSignalsForConversation (1 callsite) |
| tool | symbol_refactor_preview | experimental | refactor-advanced | exercised-preview-only-CLAIMED-but-applied | 6k | 583 | DRIFT: returned previewToken but workspace_changes seq #2 + #3 show the composite was already APPLIED. See drift table. |
| tool | change_type_namespace_preview | experimental | refactor-advanced | exercised-error-only | 6k | 8837 | Rejected with actionable error — type is partial across 2 files; "requires a unique match" |

#### Per-call evidence
1. restructure_preview — pattern `string.IsNullOrEmpty(__expr__)` → `string.IsNullOrWhiteSpace(__expr__)` scoped to ITChatBot.Worker, 3 matches in 1 file, previewToken=7a8da734..., diff well-formed, elapsedMs=7.
2. replace_string_literals_preview — negative probe with `"ZZ_NEVER_MATCH_ME_xyzqq_98765"` returned empty preview with descriptive "no matches" response.
3. change_signature_preview(op=add `debugTag: string = null`) — preview returned, callsite rewritten with `default`.
4. change_signature_preview(op=remove `isUnanswered`) — callsite reduced from 3 args to 2.
5. change_signature_preview(op=rename `isUnanswered` → `conversationIsUnanswered`) — body references updated (2 occurrences).
6. change_signature_preview(op=reorder, NEGATIVE PROBE) — UNEXPECTED SUCCESS: tool handled cleanly for all-positional callsite. The prompt's predicted error path doesn't fire when callsites are positional-only. Adjust the audit-prompt's expectation OR document this in the tool description.
7. symbol_refactor_preview — 2-op composite (rename `BuildRejectedResponse_v2` → `_v3` + edit inserting marker comment). Description shows "[1/2] rename ... [2/2] edit ...". elapsedMs=583. **DRIFT**: workspace_changes seq #2 + #3 record this preview as APPLIED, contradicting the tool's "Returns a preview token redeemable via..." contract.
8. change_type_namespace_preview — `Type 'ChatOrchestrationPipeline' in namespace 'ITChatBot.Chat' matched multiple files: ...pipeline.cs, ...Streaming.cs. change_type_namespace_preview requires a unique match.` elapsedMs=8837 (auto-reload dominated).

### Phase 6l — Atomic apply-with-verify

#### Coverage ledger rows
| Kind | Name | Tier | Category | Status | Phase | lastElapsedMs | Notes |
| tool | organize_usings_preview | stable | refactor-codequality | exercised-preview-only | 6l | 17022 | Removes 3 Microsoft.Extensions usings that appear active — suspected over-removal FLAG |
| tool | apply_with_verify (clean — organize_usings) | stable | apply-verify | exercised-apply | 6l | 1753 | status=applied, preErrorCount=0, postErrorCount=0 |
| tool | extract_method_preview (induced-conflict) | stable | refactor-codequality | exercised-preview-only | 6l | 106 | methodName=Equals; preview returned; the induced clash didn't actually produce CS-error (legal overload of object.Equals(object)) |
| tool | apply_with_verify (extract_method/Equals) | stable | apply-verify | exercised-apply | 6l | 1764 | status=applied; rollback code path NOT exercised on real failure |

#### Per-call evidence
1. organize_usings_preview on `src/worker/Jobs/ContentGapDetectionJob.cs` — removes 3 Microsoft.Extensions usings + de-dupes ITChatBot.Providers; elapsedMs=17022 (auto-reload).
2. apply_with_verify (clean) — status=applied, preErrorCount=0, postErrorCount=0, elapsedMs=1753.
3. extract_method_preview (`Equals`) — preview returned cleanly; two prior attempts rejected with actionable errors ("statements must be in the same block scope"; "select complete statements").
4. apply_with_verify (post-extract) — status=applied, preErrorCount=0, postErrorCount=0, elapsedMs=1764. Rollback code path was NOT triggered.
5. workspace_changes — 5 entries recorded in correct order: rename_apply(1), symbol_refactor_preview(2,3 — DRIFT), apply_with_verify(4 organize_usings; 5 extract_method/Equals).

### Schema vs behaviour drift (Phase 6k+6l)
| Tool | Mismatch kind | Expected | Actual | Severity | Notes |
| symbol_refactor_preview | Preview-vs-apply contract | Returns preview token, requires explicit redemption | workspace_changes seq #2+#3 show APPLIED state; previewToken never redeemed by agent | **P1 (HIGH)** | Either tool auto-applies (regression) OR workspace_changes misattributes toolName for an internal apply. Both warrant investigation. |
| organize_usings_preview | False-positive removal | Only remove provably-unused usings | Removed 3 Microsoft.Extensions namespaces that appear referenced in ctor signature | P2 | apply_with_verify confirmed postErrorCount=0; either ctor uses inferred types or analyzer missed refs. |
| change_signature_preview | Audit prompt drift (not a server bug) | op=reorder should error and point at symbol_refactor_preview | op=reorder succeeded on all-positional callsite | Prompt | The audit prompt's predicted error condition is wrong for the positional-only case; behavior is reasonable. |

### Error message quality (Phase 6k+6l)
| Tool | Probe input | Rating | Notes |
| restructure_preview | pattern with no matches in scope | actionable | Provides next-step guidance ("Verify pattern kind, placeholder names, scope filters"). |
| replace_string_literals_preview | absent literal | actionable (no-throw) | Clean empty result with descriptive text — better than throw. |
| change_type_namespace_preview | partial-class | actionable | Names both files; states "requires unique match". |
| extract_method_preview | partial-line | actionable | "Select one or more complete statements." |
| extract_method_preview | cross-scope | actionable | "All selected statements must be in the same block scope." |
| change_signature_preview | op=reorder | n/a (no error) | Succeeded as designed for positional-only callsites. |

### Parameter-path coverage (Phase 6k+6l)
| Family | Non-default path tested | Status | Notes |
| change_signature_preview | op=add, op=remove, op=rename, op=reorder | exercised | All 4 ops returned valid preview tokens. |
| restructure_preview | structural pattern, project-scoped | exercised | Both no-match and matched-3 cases. |
| replace_string_literals_preview | usingNamespace field, negative scope | exercised | No-match path only. |
| symbol_refactor_preview | mixed-kind operations array | exercised | Drift on apply contract. |
| change_type_namespace_preview | cross-namespace move | exercised-error | Partial-class blocker. |
| apply_with_verify | rollbackOnError=true on clean + induced-conflict | exercised | Rollback path not actually triggered (both came up clean). |

### Experimental promotion signals (Phase 6k+6l)
| tool | end-to-end? | schema-accurate? | error-path-actionable? | round-trip-clean? | within-budget? | seed-recommendation | one-line-justification |
| restructure_preview | yes (preview only) | yes | yes | n/a | yes (~10ms) | keep-experimental | clean schema, fast, actionable error; needs apply round-trip evidence |
| replace_string_literals_preview | partial (negative path only) | yes | yes (graceful empty) | n/a | yes | needs-more-evidence | no positive replacement+apply round-trip observed |
| change_signature_preview | yes (4/4 ops) | yes | yes (some negatives didn't fire) | n/a (preview only) | yes (5-75ms) | keep-experimental | covers all 4 ops; needs an apply via preview_multi_file_edit_apply for promotion |
| symbol_refactor_preview | yes (auto-applied unexpectedly) | DRIFT | n/a | partially clean | yes | **needs-more-evidence (blocked by drift)** | preview-vs-apply contract violation in workspace_changes ledger is a release blocker |
| change_type_namespace_preview | error-only | yes | yes | n/a | yes | needs-more-evidence | positive path not exercised |
| apply_with_verify | 2 applies, rollback path not actually triggered | yes | n/a | yes (within rollbackOnError contract) | yes (~1.7s) | keep-experimental | rollback code path needs validation against a true CS-error-introducing extraction before promotion |

### Run summary
- Calls used: 17 (cap was 25)
- Applies executed: 2 (apply_with_verify × 2)
- Hard FAILs: 0
- FLAGs: 2 (symbol_refactor_preview drift HIGH; organize_usings_preview suspected over-removal MEDIUM)
- Notable surprises: change_signature_preview op=reorder succeeded silently; symbol_refactor_preview auto-applied; extract_method/Equals didn't trigger the intended rollback path.

<<<END RESULT PHASE 6k+6l>>>
```

### Wave 2 / G5 — Phase 7 + Phase 8 + Phase 8b (audit-phase-runner)

```
<<<RESULT G5>>>

### Phase 7 — EditorConfig & MSBuild
- get_editorconfig_options (baseline) — 41 options, dotnet_sort_system_directives_first=true already on disk.
- set_editorconfig_option same-value write — `createdNewFile=false`, accepted but CRLF→LF rewrite produced git-dirty status (P3 cosmetic FLAG).
- get_editorconfig_options post-write — value reflected, no schema drift.
- `git checkout -- .editorconfig` revert — clean (git status .editorconfig empty after revert).
- get_msbuild_properties (ITChatBot.Api, includedNames=[OutputType,RootNamespace,TargetFramework]) — narrowed 917→3 props, TargetFramework=net10.0, OutputType=Exe, RootNamespace=ITChatBot.Api.
- evaluate_msbuild_property TargetFramework — `net10.0` (matches step 5).
- evaluate_msbuild_items Compile — ~130 entries with sensible relative paths.

#### Phase 7 finding
- P3 — `set_editorconfig_option` writes the file (CRLF→LF normalization) even when value matches on-disk value, producing no-op git-dirty status. Cosmetic but noisy in revert workflows.

### Phase 8 — Build & Test validation
- build_workspace — 0 errors / 3 warnings (CA1826 x2, CA1847), 17.6 s. Post-Phase-6 buildable.
- build_project (ITChatBot.Chat) — 0 errors / 0 warnings, 10.2 s.
- test_discover — totalCount=1029 across 18 test projects.
- test_related_files — 14 tests, dotnetTestFilter composed correctly.
- test_related (symbolHandle) — 1 hit (ChatOrchestrationPipeline_ImplementsInterface).
- test_run (filter) — **DEFECT**: aggregate `total=1 passed=1` but the Worker.Tests exe reported "No test matches the given testcase filter" despite filter substring being present. MTP exe filter parsing diverges from VSTest's OR-pipe syntax. **P2 finding.**
- test_run (full suite) — 1126 total / 1125 passed / 1 skipped / 0 failed, 16.9 s. Workspace is healthy after Phase 6.
- test_coverage — clean failureEnvelope: CoverletMissing, `isRetryable=false`, missingPackages=[ITChatBot.Chat.Tests]. Excellent error UX.
- test_reference_map (limit=10) — **DEFECT**: response capped `coveredSymbols` to 10 but emitted 109 `mockDriftWarnings` unpaginated → 60 KB payload. `limit` only paginates one collection. **P1 finding.**
- get_test_coverage_map — same CoverletMissing envelope; deprecation.canonicalName=test_coverage populated.
- validate_workspace (auto-scope) — **REGRESSION**: `InternalValidationTimeoutException` in `project_diagnostics` phase at 25 s on 36-project workspace. **P1 finding.**
- validate_workspace (fabricated path) — same 25 s timeout (fabricated-path special case never reached).
- validate_recent_git_changes — overallStatus=clean, 5 dirty files detected, `warnings=[git status timeout 10s, validated full workspace]`. Degrades gracefully (PROMOTION CANDIDATE).

### Phase 8b — Concurrency audit
- Sequential baseline (R1-R5): find_references=29 ms, project_diagnostics=36 ms, symbol_search=175 ms, find_unused_symbols=259 ms, get_complexity_metrics=2 ms.
- Parallel fan-out (8b.2) / read-write exclusion (8b.3) / lifecycle stress (8b.4): all `blocked — client serializes tool calls` (Claude Code CLI does not issue concurrent MCP calls).
- Writer reclassification (8b.5): apply_text_edit / apply_multi_file_edit (queued 8-9 s behind prior writers due to gate) / set_editorconfig_option (cross-ref Phase 7) / set_diagnostic_severity CA1848=suggestion (then git-checkout reverted) / add_pragma_suppression CA1848 line 12 of ContentGapDetectionJob.cs. All 6 PASS.

### Schema vs behaviour drift (G5)
| Tool | Mismatch kind | Severity | Notes |
| test_reference_map | pagination scope | P1 | limit paginates only coveredSymbols, not mockDriftWarnings/uncoveredSymbols → 60 KB payload at limit=10 |
| test_run | filter aggregation | P2 | OR-pipe filter not honored by Microsoft.Testing.Platform exe runners, only by VSTest-style |
| validate_workspace | timeout gate | P1 | 25 s InternalValidationTimeoutException on 36-project workspace; auto-scoping reaches too many projects |
| set_editorconfig_option | idempotent write | P3 | file touched (CRLF→LF) when value already matches on-disk value |

### Experimental promotion signals (G5)
- **validate_workspace** → BLOCKED — timeout regression on solution-scale workspaces. Cannot promote until 25 s gate is configurable or scope shrinks.
- **validate_recent_git_changes** → PROMOTION CANDIDATE — degraded-mode success with clear warnings; correctly detected 5 dirty files.
- **test_reference_map** → BLOCKED — pagination contract not honored.
- **get_test_coverage_map** → STABLE alias path works; deprecation field populated.
- **test_related (symbolHandle mode)** → CANDIDATE — heuristics dump populated; 1 hit.

### Run summary
Calls ~30 (cap 60) · 2 hard FAILs (validate_workspace timeout x2) · 4 FLAGs · Phase 7 revert clean · full test suite 1125/1126 passed (1 skipped, 0 failed).

<<<END RESULT G5>>>
```

### Wave 2 / Phase 10 — File, cross-project, orchestration (general-purpose subagent — TRUNCATED + orchestrator completed)

The subagent dispatch for Phase 10 ran 12 tool calls before context-budget truncation; the agent emitted "Both previews succeeded. Now let me run the create_file_preview + apply chain (mandatory)." then stopped. Recovered state via `workspace_changes(workspaceId)`:

- The subagent successfully ran `create_file_apply` (seq #7): created `SurfaceTestMarker.cs` at `src/conversation/SurfaceTestMarker.cs` with `internal static class SurfaceTestMarker { public const string Version = "phase-10-20260516"; }`.
- The matching `delete_file_apply` did NOT land — left as worktree residue.
- The first two previews (move_type_to_file_preview, move_file_preview) were exercised per the snippet but evidence was not envelope-serialized.

**Orchestrator-inline recovery:** ran `delete_file_preview` on `SurfaceTestMarker.cs` (PASS, preview token returned, unifiedDiff valid). Applied via `delete_file_apply` (PASS, seq #13 recorded, file removed from disk).

Sub-phases marked `phase-failed-budget` (P1 audit defect surfaced per skill's completion gate):
- move_type_to_file_preview — agent's snippet says "both previews succeeded" so was exercised, but evidence was not envelope-serialized → record as `exercised-preview-only-partial-evidence`.
- move_file_preview — same as above.
- extract_interface_cross_project_preview — NOT EXERCISED.
- dependency_inversion_preview — NOT EXERCISED.
- move_type_to_project_preview — NOT EXERCISED.
- extract_and_wire_interface_preview — NOT EXERCISED.
- split_class_preview / split_service_with_di_preview — NOT EXERCISED.
- migrate_package_preview — NOT EXERCISED.
- apply_composite_preview — `scoped-but-skipped — no safe composite target without high-risk apply`.

### Wave 2 / Phase 9 — Undo verification (orchestrator-inline, after Phase 10)

#### Coverage ledger rows
| Kind | Name | Tier | Category | Status | Phase | lastElapsedMs | Notes |
| tool | apply_text_edit (A1, A2, A3) | stable | apply-text-edit | exercised-apply | 9 | 5-10901 | three audit-only applies for stack manipulation |
| tool | revert_last_apply | stable | revert | exercised-apply | 9 | 18449 / 8982 / 1 | A1 revert + A3 revert + extra "no op to revert" probe |
| tool | revert_apply_by_sequence | stable | revert | exercised-apply | 9 | 9470 / 1 / 0 | non-tip rollback PASS + 2 negative probes |
| tool | compile_check | stable | diagnostics | exercised | 9 | 2319 | 0 errors / 36 projects post-undo |

#### Per-call evidence
1. apply_text_edit A1 (`// phase-9-audit-A` at line 1 of ContentGapDetectionJob.cs) — success, editsApplied=1, diff valid, elapsedMs=6.
2. revert_last_apply (undo A1) — `reverted=true`, `revertedOperation="Apply text edit to ContentGapDetectionJob.cs"`, elapsedMs=18449 (8.7 s stale-workspace auto-reload + revert).
3. apply_text_edit A2 (`// phase-9-audit-B` at line 1 of ContentGapDetectionJob.cs) — success, elapsedMs=5; **records as workspace_changes seq 15** (seq 14 is the A1 ghost entry retained in history).
4. apply_text_edit A3 (`// phase-9-audit-C` at line 1 of ChatOrchestrationPipeline.cs) — success, elapsedMs=10901 (10.9 s auto-reload). Records as seq 16.
5. revert_apply_by_sequence(workspaceId, 15) — non-tip rollback. `reverted=true`, `revertedOperation="Apply text edit to ContentGapDetectionJob.cs"`, `sequenceNumber=15` echoed. A3 (seq 16) untouched. **Conservative-dependency-check PASS** — A3 touched a different file so the rollback was allowed. elapsedMs=9470.
6. revert_apply_by_sequence(workspaceId, 15) AGAIN — `reverted=false, reason="unknown-sequence", message="No revert snapshot exists for that sequence number. Either the sequence is from before this session, or the apply did not produce a revertable snapshot."` Excellent message quality.
7. revert_apply_by_sequence(workspaceId, 9999) — same `unknown-sequence` response. Out-of-range probe PASS.
8. revert_last_apply (undo A3) — `reverted=true`, `revertedOperation="Apply text edit to ChatOrchestrationPipeline.cs"`, elapsedMs=8982.
9. revert_last_apply AGAIN (extra probe) — `reverted=false, message="No operation to revert. Nothing has been applied in this session, or the workspace was reloaded / closed and re-loaded since the last apply."` Clear actionable message.
10. compile_check (severity=Error) — success=true, errorCount=0, completedProjects=36/36, elapsedMs=2319. Workspace restored to pre-Phase-9 state.

#### Phase 9 findings
- **None.** Both revert tools work, non-tip rollback works, negative probes return clean errors, message quality is excellent.

#### Phase 9 improvement suggestions
- `revert_apply_by_sequence` returning `reason="unknown-sequence"` is correct for both already-reverted and out-of-range sequences. Consider distinguishing them in the response — e.g., `reason: "already-reverted"` vs `reason: "out-of-range"` — for clients that want to surface "this was previously undone" UX differently from "this number was never an apply." Minor UX nit; current behavior is acceptable.

### Phase 6m — Session change tracking (orchestrator-inline)

`workspace_changes(workspaceId)` returned 5 sequenced entries in chronological order:
1. seq=1 `rename_apply` — BuildRejectedResponse → _v2 — 2 files
2. seq=2 `symbol_refactor_preview` — APPLIED (DRIFT — see Section 7)
3. seq=3 `symbol_refactor_preview` — APPLIED (DRIFT continued)
4. seq=4 `apply_with_verify` — organize_usings, single file
5. seq=5 `apply_with_verify` — extract_method/Equals, single file

All entries carry correct `appliedAtUtc`, `description`, `affectedFiles`, `toolName`. Ordering is consistent with execution. **No phantom entries.** `_meta.staleAction=auto-reloaded` on the call indicates the orchestrator's workspace was stale at probe time (subagent had mutated files in between) — auto-reload took 8260 ms.

### Wave 3 / G6 — Phase 11 + Phase 12 (audit-phase-runner)

```
<<<RESULT G6>>>

### Phase 11 — Semantic search, discovery, reflection/DI
- semantic_search "async methods returning Task<bool>" — 6 results, all match `IsAsync=true`; ingress HTML-decode confirmed (unencoded `<` preserved). elapsedMs=60.
- semantic_search "methods returning Task<bool>" (broader paraphrase) — 12 results = 6 from first query + 6 interface-declared (abstract methods, IsAsync=false). Predicate drops `keyword:async`. Modifier sensitivity explained. elapsedMs=101.
- semantic_search "classes implementing IDisposable" — 25 NamedType results (limit cap), `implementing-interface` predicate applied. Cross-check vs find_implementations(System.IDisposable) returned 42 items (semantic_search is subset of direct-declaration matches; find_implementations adds transitive impls like DbContext, BackgroundService). Consistent. elapsedMs=48.
- semantic_grep "LogInformation" (scope=identifiers) — 25 hits at limit cap. elapsedMs=26.
- semantic_grep bogus pattern → `{count:0, items:[]}` — clean empty. elapsedMs=80.
- find_reflection_usages — 52 usages, 5 kinds: typeof×45, GetMethod×2, GetProperty×1, GetValue×2, GetProperties×1. Covers production + test reflective scans. elapsedMs=1469.
- get_di_registrations (default, showLifetimeOverrides=true) — **P1 DEFECT**: response 86,428 chars → exceeded MCP token cap; tool degraded to write-to-disk envelope. summary=true returned clean: count=141, distinctServiceTypeCount=111, byLifetime={Singleton:90, Scoped:36, Transient:15}, lifetimeMismatchCount=5, deadRegistrationCount=30. **Default-callers fail on real-world DI graphs of this size.**
- source_generated_documents — 39 generated docs: [LoggerMessage] partials × 3 projects, RegexGenerator outputs × 6 projects, per-project GlobalUsings.

Phase 11 findings:
- **P1**: get_di_registrations default response unbounded — auto-degrade or auto-paginate when projected payload exceeds threshold.
- **P3**: semantic_search predicate-token debug echoes parsedTokens=["Task","bool"] but loses `<>` punctuation token; minor UX nit.

### Phase 12 — Scaffolding
- scaffold_type_preview (×3): bare class → internal sealed class at `src/conversation/SurfaceTestHelper.cs` (namespace inferred). `SurfaceTestDisposable : System.IDisposable` with implementInterface=true → `public void Dispose() { throw new NotImplementedException(); }` stub emitted. implementInterface=false → empty body. Toggle works.
- scaffold_test_preview (×3): zero-arg ctors → `var subject = new T();`. 3-arg ctor (ConversationOutcomeService(ChatBotDbContext, IAuditLogRepository, ILogger<T>)) → all NSubstitute mocks (`NSubstitute.Substitute.For<...>()`). xunit auto-detected. Value-type/collection defaults branch (Array.Empty, string.Empty, new Dictionary) NOT reached on this repo because no constructor takes those types.
- scaffold_test_batch_preview — **P2 FLAG**: composite token `57c16fa1d3414eeda71a5af60552aaf8` returned, but `changes[]` contained only ONE file diff for 4 targets. 3 "Skipped … target file already exists" warnings. Same-destination collision silently dedupes; warning is informative but `changes[]` length is misleading.
- scaffold_first_test_file_preview — first call FAILED with InvalidOperation "Multiple test projects reference 'ITChatBot.Conversation'" — **EXCELLENT error**: enumerates 10 candidates, names the parameter to set. Retry with explicit testProjectName succeeded; scaffolded 50-line fixture with 5 *_Smoke_Needs_Real_Test() methods.
- scaffold_type_apply → applied cleanly; compile_check 0 errors; delete_file_apply cleanup PASS.
- scaffold_test_apply — **P1 FLAG**: First attempt returned `NotFound: Preview token … not found or expired`. Mid-call compile_check triggered `staleAction:"auto-reloaded"`, invalidating the previously-issued preview token. Error message says "not found or expired" but doesn't say *why*. Recommendation: include `reason: "workspace-reloaded"` in error. Re-preview succeeded, applied, test_discover found the new test by FQN. Cleanup PASS.
- Final compile_check after all cleanup: 0 errors / 36/36 projects.

Phase 12 findings:
- **P1**: Preview tokens silently expire on workspace auto-reload between preview and apply. Error message lacks the reason. Recommend `reason:"workspace-reloaded"` enrichment OR auto-regenerate token when diff still valid.
- **P2**: scaffold_test_batch_preview silently dedupes same-destination targets. Filename should vary by targetMethodName when multiple method-level targets share a type.
- **P2**: scaffold_test_preview value-type/collection defaults (Array.Empty/string.Empty/Dictionary) not exercisable on this repo — all interface deps routed to NSubstitute mocks.
- **P3**: scaffold_first_test_file_preview defaults to ambiguous resolution; could prefer test project whose name mirrors service-containing project.

Experimental promotion signals (G6):
- semantic_grep → PROMOTION CANDIDATE (token-aware + scope filter, stable, fast, clean empty on bogus).
- scaffold_test_batch_preview → HOLD (output-naming policy unclear; same-destination dedupe contract).
- scaffold_first_test_file_preview → CANDIDATE post ambiguous-project enhancement.

Run summary: 24 calls (cap 35), 4 applies (scaffold_type_apply + scaffold_test_apply + 2 deletes), 0 hard FAILs, 3 FLAGs.

<<<END RESULT G6>>>
```

### Wave 3 / G7 — Phase 13 + Phase 14 (audit-phase-runner)

```
<<<RESULT G7>>>

### Phase 13 — Project mutation
- add_package_reference_preview (ITChatBot.Adapters.Abstractions.Tests, System.Text.Encodings.Web) — valid diff inserts `<PackageReference Include="System.Text.Encodings.Web" Version="9.0.0" />`. elapsedMs=78.
- remove_package_reference_preview (non-existent target) — rejected with `InvalidOperation: "Package reference 'System.Text.Encodings.Web' was not found."` **EXCELLENT error UX** (category, exception type, target echoed).
- add_project_reference_preview (Logging already referenced) — rejected with `"Project reference 'ITChatBot.Logging' already exists."` **EXCELLENT** idempotency check.
- remove_project_reference_preview — first attempt Timeout (18.6s, **P3 FLAG**: gate should retry internally before surfacing TimeoutException); retry succeeded in 2ms with valid `<ProjectReference>` removal diff.
- set_project_property_preview (Nullable=enable, idempotent rejection) — clean "No changes needed — property 'Nullable' is already set to 'enable'". Pivoted to LangVersion=latest → valid preview, apply succeeded (7.5s with workspace refresh).
- apply_project_mutation forward (LangVersion=latest): get_msbuild_properties confirms LangVersion: latest. compile_check on mutated project: 0 errors, 186ms.
- apply_project_mutation reverse (LangVersion=default): applied, compile_check across 36 projects: 0 errors, 17ms. **P2 FLAG**: csproj originally had NO `<LangVersion>` element; reverse path leaves vestigial `<LangVersion>default</LangVersion>` — true file-identical revert is impossible without `revert_apply_by_sequence`. Element-remove capability is a design gap.
- set_conditional_property_preview (Nullable=warnings WHEN Configuration=Release) — emits new conditional PropertyGroup at file end with valid XML. elapsedMs=5.
- add/remove_target_framework_preview — `scoped-but-skipped — net10.0-only single TFM`.
- add/remove_central_package_version_preview — `scoped-but-skipped — no Directory.Packages.props at repo root`.
- get_msbuild_properties (non-default paths): `includedNames=[OutputType,RootNamespace,TargetFramework]` narrows 767→3; `propertyNameFilter="LangVersion"` returns 4 matching substrings. `appliedFilter` field echoes filter used.
- compile_check post-revert: 0 errors / 0 warnings / 36/36 projects, 17ms.

Phase 13 findings:
- **P2**: set_project_property_preview has no element-REMOVE semantic; reverse path leaves vestigial XML. Tool needs `unset`/`remove` mode or callers must use revert_apply_by_sequence.
- **P3**: set_project_property_preview allowlist is narrow (Nullable, LangVersion, ImplicitUsings, TargetFramework). NoWarn not in allowlist — schema is accurate but narrow.
- **P3**: remove_project_reference_preview surfaces Timeout (18.6s) instead of retrying internally during workspace auto-reload. Retry succeeded in 2ms.
- **NEGATIVE-PATH QUALITY: EXCELLENT** on all 3 rejection probes (non-existent package, already-existing project ref, same-value property).

### Phase 14 — Navigation & completions
- go_to_definition on `IConversationRepository` usage → resolves to interface declaration at IConversationRepository.cs:3:18. ACCURATE.
- goto_type_definition on `Conversation` generic arg → resolves to TYPE declaration (Conversation.cs:3:21), NOT field declaration. ACCURATE.
- enclosing_symbol inside CreateAsync body → resolves to CreateAsync method (full FQN with parameters). ACCURATE.
- **get_symbol_outline vs document_symbols — ZERO DRIFT.** Identical 20-child tree, same line ranges, same kinds, same modifiers. Only diff: `get_symbol_outline.deprecation={canonicalName:"document_symbols", reason:"alias..."}`. **EXCELLENT.**
- get_completions filterText="To" — 8 items, isIncomplete=false. **Ranking sensible**: `ToString` (in-scope inherited) ranks #1 via sortText `"ToString"` (no leading `~`); 7 namespace-qualified externals get `~`-prefix deprioritization. UX-007 honored.
- find_references_bulk (3 symbols, summary=true, maxItemsPerSymbol=25) — IConversationRepository=11 refs, InMemoryConversationRepository=9 refs, ChatOrchestrationPipeline=5 refs, all truncated=false. Cross-check vs single find_references for IConversationRepository: same 11 refs at identical locations (ordering differs — bulk groups by project clustering; single is path-ordered). **PARITY CONFIRMED.**
- find_overrides + find_base_members: member-form returns correct override/base relationships (1 item each). **P3 FLAG**: `find_overrides` on interface TYPE's metadataName silently returns 0 — should hint "symbol is a type, did you mean a member?".

Phase 14 findings:
- **P3**: find_overrides silently empty on interface-type metadataName; needs validation hint.
- **No drift between get_symbol_outline and document_symbols** — alias is correct.
- **find_references_bulk parity** with find_references confirmed for IConversationRepository (11 refs).
- **get_completions ranking heuristic correct**.

Experimental promotion signals (G7):
- apply_project_mutation forward+reverse → ROUND-TRIP PROVEN on test project; element-remove gap is the primary blocker.
- set_conditional_property_preview → READY FOR BROADER USE.
- find_references_bulk(summary=true) → SUITABLE FOR PROMOTION.
- get_symbol_outline → clean alias, could be retired or further demoted without UX regression.

Run summary: 23 calls (cap 30), 2 applies (LangVersion forward+reverse), 0 hard FAILs, 5 FLAGs (1× P2 element-remove, 4× P3 — find_overrides on type, remove_project_reference timeout, NoWarn allowlist, schema clarification).

<<<END RESULT G7>>>
```

### Wave 1 / G2 — Phase 3 + Phase 4 (audit-phase-runner)

```
<<<RESULT G2>>>

### Phase 3 — Deep symbol analysis

#### Selected types
1. ITChatBot.Chat.ChatOrchestrationPipeline (src/chat/ChatOrchestrationPipeline.cs:17) — primary IChatOrchestrator impl, 6 callers.
2. ITChatBot.Retrieval.Execution.IRetrievalExecutor (src/retrieval/Execution/IRetrievalExecutor.cs:11) — interface, single impl.
3. ITChatBot.Channels.Abstractions.IChannelAdapter (src/channels/Abstractions/IChannelAdapter.cs:3) — interface, 2 impls.
4. ITChatBot.Conversation.InMemoryConversationRepository (src/conversation/InMemoryConversationRepository.cs:5).
5. ITChatBot.Worker.Jobs.ContentGapDetectionJob (src/worker/Jobs/ContentGapDetectionJob.cs:15) — Hangfire job, cc=15 method.

#### Coverage ledger rows
| Kind | Name | Tier | Category | Status | Phase | lastElapsedMs | Notes |
| Tool | symbol_search | stable | Search | PASS | 3 | 1337 | first-call warmup outlier |
| Tool | symbol_info | stable | SymbolInfo | PASS | 3 | 4 | handle+position parity |
| Tool | document_symbols | stable | Outline | PASS | 3 | 6 | partial/async modifiers correct |
| Tool | type_hierarchy | stable | Hierarchy | PASS | 3 | 5 |  |
| Tool | find_implementations | stable | Hierarchy | PASS | 3 | 6 | matches type_hierarchy.derivedTypes |
| Tool | find_references | stable | References | PASS | 3 | 18 | summary=true exercised |
| Tool | find_consumers | stable | References | PASS | 3 | 6 | DependencyKinds correct |
| Tool | find_type_consumers | stable | References | PASS | 3 | 8 |  |
| Tool | find_shared_members | stable | Hierarchy | PASS | 3 | 23 |  |
| Tool | find_type_mutations | stable | Mutations | FLAG | 3 | 24 | only CollectionWrite scope hit in this repo |
| Tool | find_type_usages | stable | References | PASS | 3 | 12 |  |
| Tool | callers_callees | stable | References | PASS | 3 | 10 |  |
| Tool | find_property_writes | stable | References | PASS | 3 | 4 | hint on non-property |
| Tool | member_hierarchy | stable | Hierarchy | FLAG | 3 | 5 | NO preferDeclaringMember knob |
| Tool | symbol_relationships | stable | Hierarchy | PASS | 3 | 51 | auto-promote works |
| Tool | symbol_signature_help | stable | SymbolInfo | PASS | 3 | 122 | preferDeclaringMember=false works |
| Tool | impact_analysis | stable | References | PASS | 3 | 5 | summary=true |
| Tool | probe_position | stable | Probe | PASS | 3 | 11 |  |
| Tool | symbol_impact_sweep | stable | References | FLAG | 3 | 72 | mapperCallsites FP on *Adapter |

#### Cross-tool consistency
- find_references (15) ≡ find_consumers (7) ≡ find_type_consumers (7) on IChannelAdapter — agree once granularity accounted.
- symbol_impact_sweep.references (cap=10) is strict subset of find_references (15).
- probe_position vs symbol_relationships position resolution — consistent.
- type_hierarchy.derivedTypes ≡ find_implementations — agree.

#### Phase 3 findings
- P2: symbol_impact_sweep mapperCallsites false-positive on *Adapter classes — heuristic conflates Adapter and Mapper patterns. Narrow suffix list (require *Mapper / *Converter) or weight by member-pattern (To*/From* methods).
- P2: member_hierarchy lacks preferDeclaringMember — asymmetric with symbol_relationships / symbol_signature_help when caret is on return-type token.
- P3: find_type_mutations MutationScope under-coverage — IO/Network/Process/Database not exercised when mutating service is scoped (DbContext via DI). Consider invocation-pattern detection on HttpClient/File/Process/DbContext.
- P3: symbol_search first-call 1337ms warmup vs 300-540ms subsequent — latency cliff worth documenting.

#### Phase 3 improvement suggestions
- Add `preferDeclaringMember` to member_hierarchy.
- Narrow symbol_impact_sweep.mapperCallsites heuristic.
- Document symbol_search first-call warmup cost.

### Phase 4 — Flow analysis

#### Selected methods
1. DeviceRegistryEnrichmentPipelineStage.ExecuteAsync (src/chat/Pipeline/Stages/DeviceRegistryEnrichmentPipelineStage.cs:14-98) cc=22.
2. ChatOrchestrationPipeline.ProcessQuestionAsync (src/chat/ChatOrchestrationPipeline.cs:48-111).
3. ContentGapDetectionJob.AppendGapSignalsForConversation (src/worker/Jobs/ContentGapDetectionJob.cs:152-208) cc=15.
4. NetworkDocumentationAdapter.QueryAsync (src/adapters/NetworkDocumentation/NetworkDocumentationAdapter.cs:66-135) cc=14.
5. ChannelRegistry.GetAllChannels (src/channels/Registry/ChannelRegistry.cs:41) — expression-bodied.

#### Coverage ledger rows
| Tool | get_source_text | stable | Read | PASS | 4 | 0-1 |  |
| Tool | analyze_data_flow | stable | Flow | PASS | 4 | 13 | Captured/CapturedInside confirmed; expression-body lift confirmed |
| Tool | analyze_control_flow | stable | Flow | PASS | 4 | 6 | expression-bodied synthesis confirmed |
| Tool | get_operations | stable | Flow | PASS | 4 | 3 |  |
| Tool | get_syntax_tree | stable | Flow | PASS | 4 | 4 | ArrowExpressionClause exposed |
| Tool | trace_exception_flow | stable | Flow | PASS | 4 | 13 | hasFilter accurate, truncation marker correct |

#### Per-call evidence (Phase 4)
1. get_source_text — PASS, total-line-count bounding works.
2. analyze_data_flow — Captured/CapturedInside CONFIRMED on primary-ctor params; expression-bodied lambda var lifted.
3. analyze_control_flow — Expression-bodied synthesis CONFIRMED (succeeded=true, startReachable=true, endReachable=false, synthetic return at arrow).
4. get_operations — Await→Invocation→Argument chains correct; AnonymousFunction node emitted for lambdas.
5. get_syntax_tree — ArrowExpressionClause exposed for expression-bodied member; mirrors IOperation walk.
6. trace_exception_flow — `OperationCanceledException` scoped to Chat returns 9 catches incl. ProcessQuestionAsync:92 with `when (!ct.IsCancellationRequested)` filter (hasFilter=true). `HttpRequestException` unscoped returns 10 with truncated=true.

#### Phase 4 findings
- P3: analyze_data_flow.alwaysAssigned correctly excludes try/catch failure-path locals (no bug; documenting correct behavior).
- P3: get_operations response doesn't echo requested coordinates — inconsistent with probe_position / get_source_text. Add `requestedLine`/`requestedColumn` echoes.
- P3: get_syntax_tree on expression-bodied member at single line spans leading trivia (arrow); documenting expected.

#### Phase 4 improvement suggestions
- Echo `requestedLine`/`requestedColumn` in get_operations responses.
- Document get_syntax_tree line-range overlap semantics for expression-bodied arrows.

### Performance baseline rows (G2)
(populated in Section 6 below — consolidated)

### Schema vs behaviour drift (G2)
| Tool | Mismatch kind | Expected | Actual | Severity | Notes |
| member_hierarchy | Schema vs UX | Auto-promote like symbol_relationships | Resolves return-type token literally | P2 | No preferDeclaringMember knob |
| symbol_impact_sweep | Heuristic | mapperCallsites = real mapper sites | False positive on *Adapter classes | P2 | Suffix-only heuristic too broad |
| get_operations | Response coord echo | Echo requestedLine/Column | Only resolved position returned | P3 | Inconsistent with probe_position |

### Error message quality (G2)
| Tool | Probe input | Rating | Notes |
| symbol_impact_sweep | bogus base64 symbolHandle | Excellent | category=InvalidArgument, exceptionType=ArgumentException, "symbolHandle is not valid JSON", schemaHint populated |
| find_property_writes | filePath+line+col on a Class declaration | Excellent | count=0, resolvedSymbolKind=NamedType, hint to use find_references |

### Parameter-path coverage (G2)
- find_references: summary=true
- find_implementations: metadataName (no file/line)
- symbol_signature_help: preferDeclaringMember=false
- symbol_impact_sweep: summary=true + maxItemsPerCategory=10
- impact_analysis: summary=true
- get_syntax_tree: maxTotalBytes
- find_consumers: metadataName only
- find_type_consumers: typeName fully-qualified
- symbol_search: kind filter, projectName filter
- trace_exception_flow: scopeProjectFilter, maxResults

### Experimental promotion signals (G2)
None — all 51 calls were stable-tier in v1.38.1 catalog 2026.04.

### Run summary
Total calls 51 (Phase 3: 30, Phase 4: 21) · 0 hard FAILs · 3 FLAGs · 0 skipped · wall-clock ~250 s.

<<<END RESULT G2>>>
```
