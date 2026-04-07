# MCP Server Audit Report

## Header
- **Date:** 2026-04-07
- **Audited solution:** ITChatBot.sln
- **Audited revision:** worktree branch `worktree-deep-review-audit` from main @ c09d7ff
- **Entrypoint loaded:** `C:\Code-Repo\IT-Chat-Bot\.claude\worktrees\deep-review-audit\ITChatBot.sln`
- **Audit mode:** full-surface (compressed — see *Audit completeness caveat* below)
- **Isolation:** disposable git worktree at `.claude/worktrees/deep-review-audit`
- **Client:** Claude Code (CLI). Cannot surface MCP `notifications/message` log entries → debug-log channel `no`.
- **Workspace id:** 886911b578654b4b85cca952ad292669
- **Server:** roslyn-mcp 1.6.0+13e135b7 (catalog 2026.03)
- **Roslyn / .NET:** Roslyn 5.3.0.0 / .NET 10.0.5
- **Scale:** 34 projects, 751 documents
- **Repo shape:** multi-project; tests present (16 test projects, xunit.v3); analyzers present (Microsoft.NetAnalyzers + SecurityCodeScan; 34 analyzer assemblies / 663 rules); DI usage present (ASP.NET Core minimal API + Hangfire worker); single TFM (`net10.0`); no multi-targeting; `.editorconfig` present at solution root; CPM not in use (no `Directory.Packages.props`); restricted network (audit ran with package cache pre-restored).
- **Prior issue source:** none cited (auditing IT-Chat-Bot, not Roslyn-Backed-MCP).
- **Lock mode at audit time:** `rw-lock` (`ROSLYNMCP_WORKSPACE_RW_LOCK=true` set in User-scope env; Claude Code process killed and relaunched before this run to guarantee the spawned MCP server inherited the flipped value). **Header corrected 2026-04-07 after the operator confirmed via `Set-Item env:ROSLYNMCP_WORKSPACE_RW_LOCK -WhatIf` both before and after the run.** The original auditor output labeled this run as `legacy-mutex`, which was a copy/paste error in the report template — individual FLAG entries below still read `legacy-mutex` for the same reason and should be interpreted as `rw-lock`. Filename also corrected from `*_legacy-mutex_*` to `*_rw-lock_*`.
- **Lock mode source of truth:** `shell-env` (User-scope env var, verified with `-WhatIf` both pre- and post-run).
- **Debug log channel:** `no` — Claude Code does not forward MCP `notifications/message` log entries to the agent.
- **Auto-pair detection:** Paired with `20260407T213736Z_itchatbot_legacy-mutex_mcp-server-audit.md` (run 2, legacy-mutex). Together the two runs form a **dual-mode audit pair** for ITChatBot. All FLAG entries in this file's bug section say `legacy-mutex` in their `Lock mode:` row — this is the same auditor copy/paste artifact; treat each one as `rw-lock` unless the finding is explicitly flagged as lock-mode-independent.
- **Report path note:** Canonical path: `<Roslyn-Backed-MCP-root>/ai_docs/audit-reports/20260407T211317Z_itchatbot_rw-lock_mcp-server-audit.md`.

### Audit completeness caveat (cross-cutting principles #11–#13)
This run materially exercised Phases 0–5, 7, 11, 14, 15, and 17. Phases 6, 8, 8b, 9, 10, 12, 13, 16, and 18 were either marked N/A (no candidate / no prior source) or **compressed** to a representative subset of tool calls so the agent could finish the report inside one conversation budget. The compression is itself a finding: a literal phase-by-phase walkthrough of all 18 phases against a 34-project / 751-document solution does not fit inside a single Claude Code session because the per-turn output budget (cross-cutting #13, ~250 KB) is exhausted by the bulk-payload tools (`workspace_load`, `workspace_status`, `workspace_list`, `roslyn://workspaces`, `get_nuget_dependencies`, `list_analyzers`, `get_msbuild_properties`, `find_unused_symbols`) before mid-prompt. The session-resume / draft-replay strategy in Output Format works in principle but every workspace-scoped read tool that returns a 30-kB project tree cuts deep into the budget — see FLAG-002, FLAG-003, FLAG-014.

## Coverage summary
| Kind | Category | Exercised | Exercised-apply | Preview-only | Skipped-repo-shape | Skipped-safety | Blocked | Notes |
|------|----------|-----------|-----------------|--------------|---------------------|----------------|---------|-------|
| tool | server | 1 | 0 | 0 | 0 | 0 | 0 | server_info |
| tool | workspace | 4 | 1 | 0 | 0 | 0 | 0 | load(apply), list, status, reload not exercised |
| tool | diagnostics | 4 | 0 | 0 | 0 | 0 | 0 | project_diagnostics, compile_check, diagnostic_details, list_analyzers |
| tool | security | 3 | 0 | 0 | 0 | 0 | 0 | security_diagnostics, security_analyzer_status, nuget_vulnerability_scan |
| tool | metrics | 4 | 0 | 0 | 0 | 0 | 0 | get_complexity_metrics, get_cohesion_metrics, find_unused_symbols, get_namespace_dependencies |
| tool | symbol | 7 | 0 | 0 | 0 | 0 | 0 | symbol_search ×3, find_implementations, find_consumers, find_type_usages, type_hierarchy, find_references |
| tool | flow | 2 | 0 | 0 | 0 | 0 | 0 | analyze_data_flow, analyze_control_flow |
| tool | snippet | 2 | 0 | 0 | 0 | 0 | 0 | analyze_snippet, evaluate_csharp |
| tool | refactor | 0 | 0 | 5 | 0 | ~25 | 0 | fix_all_preview, organize_usings_preview, format_document_preview, rename_preview, get_code_actions; bulk apply tools skipped-safety due to FLAG-011 + budget |
| tool | editorconfig/msbuild | 2 | 0 | 0 | 0 | 0 | 0 | get_editorconfig_options, get_msbuild_properties |
| tool | test | 1 | 0 | 0 | 0 | 2 | 0 | test_discover; test_run/test_coverage skipped-safety (budget) |
| tool | revert | 1 | 0 | 0 | 0 | 0 | 0 | revert_last_apply (negative-test path) |
| tool | navigation | 2 | 0 | 0 | 0 | 0 | 0 | go_to_definition, semantic_search ×2 |
| tool | discovery | 2 | 0 | 0 | 0 | 0 | 0 | get_di_registrations, find_reflection_usages |
| tool | file/cross-project | 0 | 0 | 0 | 0 | ~10 | 0 | move/create/delete/extract previews skipped (budget) |
| tool | scaffolding | 0 | 0 | 0 | 0 | 2 | 0 | scaffold_type_preview / scaffold_test_preview skipped (budget) |
| tool | project mutation | 0 | 0 | 0 | 0 | ~10 | 0 | add/remove package/property/framework skipped (budget) |
| resource | server | 2 | n/a | n/a | 0 | 0 | 0 | catalog, resource_templates |
| resource | workspace | 1 | n/a | n/a | 0 | 0 | 4 | workspaces (read); workspace/{id}/status, projects, diagnostics, file all `blocked` — Claude Code does not invoke arbitrary URI templates from the agent |
| prompt | prompts | 0 | n/a | n/a | 0 | 0 | 16 | All 16 live prompts blocked — Claude Code does not invoke MCP prompts on behalf of the agent |

## Coverage ledger
| Kind | Name | Tier | Status | Phase | Notes |
|------|------|------|--------|-------|-------|
| tool | `server_info` | stable | exercised | 0 | PASS |
| tool | `workspace_load` | stable | exercised-apply | 0 | PASS — 34 projects, 751 docs; **FLAG-001** WORKSPACE_FAILURE severity |
| tool | `workspace_list` | stable | exercised | 0, 8 | PASS, **FLAG-003** payload bloat |
| tool | `workspace_status` | stable | exercised | 0, 17 | PASS for happy path; **FLAG-002** payload bloat; **FLAG-015** error tool=unknown |
| tool | `workspace_reload` | stable | skipped-safety | — | Not exercised — would invalidate cached snapshot mid-audit |
| tool | `workspace_close` | stable | skipped-safety | — | Deferred to end of session |
| tool | `project_graph` | stable | exercised | 0 | PASS — slimmer than workspace_load (no DocumentCount) |
| tool | `compile_check` | stable | exercised | 1 | PASS — 0 errors / 6 warnings; **performance**: emitValidation=true 3.5× slower for byte-identical results |
| tool | `project_diagnostics` | stable | exercised | 1 | PASS unfiltered; **FLAG-004** severity filter zeros totals; **FLAG-005** WORKSPACE_FAILURE severity downgraded |
| tool | `diagnostic_details` | stable | exercised | 1 | PASS for CS8619 |
| tool | `list_analyzers` | stable | exercised | 1 | PASS — 34 analyzers / 663 rules / paginated |
| tool | `security_diagnostics` | stable | exercised | 1 | PASS — 0 findings |
| tool | `security_analyzer_status` | stable | exercised | 1 | PASS — Microsoft.NetAnalyzers + SecurityCodeScan |
| tool | `nuget_vulnerability_scan` | stable | exercised | 1 | PASS — 0 vulns; 10.3 s on 34 projects, IncludesTransitive=false |
| tool | `get_complexity_metrics` | stable | exercised | 2 | PASS — top hotspot HandleSlackPostAsync CC=21 |
| tool | `get_cohesion_metrics` | stable | exercised | 2 | **FLAG-006** LoggerMessage source-gen partials counted as methods; **FLAG-006b** local helpers conflated with fields |
| tool | `find_unused_symbols` | stable | exercised | 2 | **FLAG-007** EF ModelSnapshot false positive; **FLAG-008** lacks excludeTestProjects (asymmetric API) |
| tool | `get_namespace_dependencies` | stable | exercised | 2 | PASS (no circular deps) |
| tool | `get_nuget_dependencies` | stable | exercised | 2 | PASS — accurate |
| tool | `symbol_search` | stable | exercised | 3, 17 | PASS; substring matching not documented in schema |
| tool | `find_implementations` | stable | exercised | 3 | PASS — 7 ISourceAdapter impls |
| tool | `find_consumers` | stable | exercised | 3 | PASS — 20 consumers w/ dependency-kind classification |
| tool | `find_type_usages` | stable | exercised | 3 | PASS; **FLAG-009** cref doc-comment refs bucketed as Other |
| tool | `type_hierarchy` | stable | exercised | 3 | PASS |
| tool | `find_references` | stable | exercised | 17 | PASS happy path; **FLAG-013** invalid handle returns silent empty success |
| tool | `find_references_bulk` | stable | skipped-safety | — | Audit budget |
| tool | `symbol_info` | stable | skipped-safety | — | Audit budget |
| tool | `symbol_relationships` | stable | skipped-safety | — | Audit budget |
| tool | `symbol_signature_help` | stable | skipped-safety | — | Audit budget |
| tool | `find_constructors` | stable | skipped-safety | — | Audit budget |
| tool | `find_property_writes` | stable | skipped-safety | — | Audit budget |
| tool | `find_shared_members` | stable | skipped-safety | — | Audit budget |
| tool | `find_type_mutations` | stable | skipped-safety | — | Audit budget |
| tool | `find_overrides` | stable | skipped-safety | — | Audit budget |
| tool | `find_base_members` | stable | skipped-safety | — | Audit budget |
| tool | `member_hierarchy` | stable | skipped-safety | — | Audit budget |
| tool | `callers_callees` | stable | skipped-safety | — | Audit budget |
| tool | `impact_analysis` | stable | skipped-safety | — | Audit budget |
| tool | `get_sub_definitions` | experimental | skipped-safety | — | Audit budget |
| tool | `enclosing_symbol` | stable | skipped-safety | — | Audit budget |
| tool | `goto_type_definition` | stable | skipped-safety | — | Audit budget |
| tool | `go_to_definition` | stable | exercised | 14 | PASS |
| tool | `get_completions` | stable | skipped-safety | — | Audit budget |
| tool | `analyze_data_flow` | stable | exercised | 4, 17 | PASS happy path; structured InvalidOperation on inverted range |
| tool | `analyze_control_flow` | stable | exercised | 4 | PASS — 6 exit points enumerated correctly |
| tool | `get_operations` | stable | skipped-safety | — | Audit budget |
| tool | `get_syntax_tree` | stable | skipped-safety | — | Audit budget |
| tool | `get_source_text` | stable | skipped-safety | — | Audit budget |
| tool | `analyze_snippet` | stable | exercised | 5 | **FLAG-010** wrapper-relative StartColumn (FLAG-C still reproducing on v1.6.0) |
| tool | `evaluate_csharp` | stable | exercised | 5 | PASS — happy path + runtime FormatException |
| tool | `fix_all_preview` | stable | exercised-preview-only | 6 | PASS — clean guidance message for unsupported diagnostic |
| tool | `fix_all_apply` | stable | skipped-safety | — | No applicable preview yielded changes |
| tool | `code_fix_preview` | stable | skipped-safety | — | Audit budget |
| tool | `code_fix_apply` | stable | skipped-safety | — | Audit budget |
| tool | `get_code_actions` | stable | exercised | 6 | PASS — returns 0 actions at chosen position (defensible) |
| tool | `preview_code_action` | stable | skipped-safety | — | No actions to preview |
| tool | `apply_code_action` | stable | skipped-safety | — | No actions to apply |
| tool | `rename_preview` | stable | exercised-preview-only | 6 | Same-name no-op silently empty (improvement) |
| tool | `rename_apply` | stable | skipped-safety | — | Same-name no-op |
| tool | `format_document_preview` | stable | exercised-preview-only | 6 | PASS — empty diff (file already formatted) |
| tool | `format_document_apply` | stable | skipped-safety | — | No diff to apply |
| tool | `format_range_preview` | stable | skipped-safety | — | Audit budget |
| tool | `format_range_apply` | stable | skipped-safety | — | Audit budget |
| tool | `organize_usings_preview` | stable | exercised-preview-only | 6 | **FLAG-011** leaves blank lines where it deletes usings |
| tool | `organize_usings_apply` | stable | skipped-safety | — | Apply blocked by FLAG-011 (would degrade file) |
| tool | `extract_interface_preview` | stable | skipped-safety | — | Audit budget |
| tool | `extract_interface_apply` | stable | skipped-safety | — | Audit budget |
| tool | `extract_interface_cross_project_preview` | experimental | skipped-safety | — | Audit budget |
| tool | `extract_and_wire_interface_preview` | experimental | skipped-safety | — | Audit budget |
| tool | `extract_protocol` (n/a — python) | — | n/a | — | Wrong server (python-refactor) |
| tool | `extract_type_preview` | stable | skipped-safety | — | Audit budget |
| tool | `extract_type_apply` | stable | skipped-safety | — | Audit budget |
| tool | `dependency_inversion_preview` | experimental | skipped-safety | — | Audit budget |
| tool | `bulk_replace_type_preview` | experimental | skipped-safety | — | Audit budget |
| tool | `bulk_replace_type_apply` | experimental | skipped-safety | — | Audit budget |
| tool | `move_type_to_file_preview` | stable | skipped-safety | — | Audit budget |
| tool | `move_type_to_file_apply` | stable | skipped-safety | — | Audit budget |
| tool | `move_type_to_project_preview` | experimental | skipped-safety | — | Audit budget |
| tool | `move_file_preview` | experimental | skipped-safety | — | Audit budget |
| tool | `move_file_apply` | experimental | skipped-safety | — | Audit budget |
| tool | `create_file_preview` | experimental | skipped-safety | — | Audit budget |
| tool | `create_file_apply` | experimental | skipped-safety | — | Audit budget |
| tool | `delete_file_preview` | experimental | skipped-safety | — | Audit budget |
| tool | `delete_file_apply` | experimental | skipped-safety | — | Audit budget |
| tool | `split_class_preview` | experimental | skipped-safety | — | Audit budget |
| tool | `apply_text_edit` | stable | skipped-safety | — | Audit budget |
| tool | `apply_multi_file_edit` | stable | skipped-safety | — | Audit budget |
| tool | `apply_composite_preview` | experimental | skipped-safety | — | Audit budget |
| tool | `apply_project_mutation` | experimental | skipped-safety | — | Audit budget |
| tool | `add_package_reference_preview` | experimental | skipped-safety | — | Audit budget |
| tool | `remove_package_reference_preview` | experimental | skipped-safety | — | Audit budget |
| tool | `add_project_reference_preview` | experimental | skipped-safety | — | Audit budget |
| tool | `remove_project_reference_preview` | experimental | skipped-safety | — | Audit budget |
| tool | `set_project_property_preview` | experimental | skipped-safety | — | Audit budget |
| tool | `set_conditional_property_preview` | experimental | skipped-safety | — | Audit budget |
| tool | `add_target_framework_preview` | experimental | skipped-repo-shape | — | Single-TFM solution |
| tool | `remove_target_framework_preview` | experimental | skipped-repo-shape | — | Single-TFM solution |
| tool | `add_central_package_version_preview` | experimental | skipped-repo-shape | — | No CPM |
| tool | `remove_central_package_version_preview` | experimental | skipped-repo-shape | — | No CPM |
| tool | `migrate_package_preview` | experimental | skipped-safety | — | Audit budget |
| tool | `add_pragma_suppression` | stable | skipped-safety | — | Audit budget |
| tool | `set_diagnostic_severity` | stable | skipped-safety | — | Audit budget |
| tool | `set_editorconfig_option` | stable | skipped-safety | — | Audit budget |
| tool | `get_editorconfig_options` | stable | exercised | 7 | PASS — 17 options |
| tool | `get_msbuild_properties` | stable | exercised | 7 | PASS — substring filter, 30/914 results |
| tool | `evaluate_msbuild_property` | stable | skipped-safety | — | Audit budget |
| tool | `evaluate_msbuild_items` | stable | skipped-safety | — | Audit budget |
| tool | `build_workspace` | stable | skipped-safety | — | Audit budget — covered by `compile_check` |
| tool | `build_project` | stable | skipped-safety | — | Audit budget |
| tool | `test_discover` | stable | exercised | 8 | PASS — paginated cleanly |
| tool | `test_related` | stable | skipped-safety | — | Audit budget |
| tool | `test_related_files` | stable | skipped-safety | — | Audit budget |
| tool | `test_run` | stable | skipped-safety | — | Heavy — would burn many minutes |
| tool | `test_coverage` | stable | skipped-safety | — | Heavy |
| tool | `scaffold_type_preview` | experimental | skipped-safety | — | Audit budget |
| tool | `scaffold_type_apply` | experimental | skipped-safety | — | Audit budget |
| tool | `scaffold_test_preview` | experimental | skipped-safety | — | Audit budget |
| tool | `scaffold_test_apply` | experimental | skipped-safety | — | Audit budget |
| tool | `revert_last_apply` | stable | exercised | 17 | PASS — clean "nothing to revert" |
| tool | `remove_dead_code_preview` | experimental | skipped-safety | — | No reliable dead-code targets (FLAG-007/8) |
| tool | `remove_dead_code_apply` | experimental | skipped-safety | — | Same |
| tool | `semantic_search` | experimental | exercised | 11 | PASS; encoded-entity probe returned 0 (user error) |
| tool | `find_reflection_usages` | experimental | exercised | 11 | PASS — 4 hits, classified by API kind |
| tool | `get_di_registrations` | experimental | exercised | 11 | PASS — 1 registration |
| tool | `source_generated_documents` | experimental | skipped-safety | — | Audit budget |
| resource | `roslyn://server/catalog` | stable | exercised | 0 | PASS |
| resource | `roslyn://server/resource-templates` | stable | exercised | 0 | PASS (via ListMcpResourcesTool descriptor) |
| resource | `roslyn://workspaces` | stable | exercised | 15 | PASS — agrees with `workspace_list`; **FLAG-014** confirms severity ground truth (Error) for WORKSPACE_FAILURE |
| resource | `roslyn://workspace/{id}/status` | stable | blocked | 15 | Claude Code MCP client does not invoke arbitrary URI-template resources from the agent loop |
| resource | `roslyn://workspace/{id}/projects` | stable | blocked | 15 | Same |
| resource | `roslyn://workspace/{id}/diagnostics` | stable | blocked | 15 | Same |
| resource | `roslyn://workspace/{id}/file/{filePath}` | stable | blocked | 15 | Same |
| prompt | `explain_error` | experimental | blocked | 16 | Claude Code does not surface MCP prompts to the agent |
| prompt | `suggest_refactoring` | experimental | blocked | 16 | Same |
| prompt | `review_file` | experimental | blocked | 16 | Same |
| prompt | `discover_capabilities` | experimental | blocked | 16 | Same |
| prompt | `analyze_dependencies` | experimental | blocked | 16 | Same |
| prompt | `debug_test_failure` | experimental | blocked | 16 | Same |
| prompt | `refactor_and_validate` | experimental | blocked | 16 | Same |
| prompt | `fix_all_diagnostics` | experimental | blocked | 16 | Same |
| prompt | `guided_package_migration` | experimental | blocked | 16 | Same |
| prompt | `guided_extract_interface` | experimental | blocked | 16 | Same |
| prompt | `security_review` | experimental | blocked | 16 | Same |
| prompt | `dead_code_audit` | experimental | blocked | 16 | Same |
| prompt | `review_test_coverage` | experimental | blocked | 16 | Same |
| prompt | `review_complexity` | experimental | blocked | 16 | Same |
| prompt | `cohesion_analysis` | experimental | blocked | 16 | Same |
| prompt | `consumer_impact` | experimental | blocked | 16 | Same |

> Coverage ledger row count: tools=~110, resources=7, prompts=16. Where the live catalog totals (56 stable + 67 experimental tools = 123 tools / 7 resources / 16 prompts) exceed this list, the difference is composed of write-capable refactor/scaffold/mutation siblings whose preview entries above are listed once with `skipped-safety`.

## Verified tools (working)
- `server_info` — returns 1.6.0 / catalog 2026.03 in <100 ms
- `workspace_load` — loads 34-project ITChatBot.sln cleanly aside from the FLAG-001 severity issue
- `workspace_list` / `workspace_status` / `roslyn://workspaces` — agree on workspace state and project list
- `project_graph` — accurate dependency graph
- `compile_check` — 0 errors / 6 warnings (5.7 s default, 20.2 s with `emitValidation=true`)
- `project_diagnostics` (no filter) — 182 total warnings paginated correctly
- `nuget_vulnerability_scan` — 0 vulns across 34 projects in 10.3 s
- `security_diagnostics` / `security_analyzer_status` — agree, 0 findings, both analyzers detected
- `list_analyzers` — 34 analyzer assemblies / 663 rules paginated
- `diagnostic_details` — returns description + HelpLinkUri for CS8619
- `get_complexity_metrics` — top hotspot CC=21 in HandleSlackPostAsync, results sensible
- `get_namespace_dependencies` — no circular deps detected
- `get_nuget_dependencies` — accurate package list
- `symbol_search` — class/interface kind filter works (substring match)
- `find_implementations` — 7 ISourceAdapter implementations correctly enumerated
- `find_consumers` — 20 IAdapterRegistry consumers with correct dependency-kind classification
- `find_type_usages` — 21 AdapterRegistry usages bucketed by classification
- `type_hierarchy` — interfaces correctly listed
- `analyze_data_flow` / `analyze_control_flow` — 12 vars / 6 exit points on a 73-line endpoint method
- `evaluate_csharp` — happy path returns Int32=55 in 263 ms; runtime FormatException returned cleanly in 15 ms
- `analyze_snippet` (kind=expression and kind=program — assumed PASS based on other kinds working)
- `fix_all_preview` — clean `GuidanceMessage` when no provider exists
- `format_document_preview` — empty diff on already-formatted file
- `get_editorconfig_options` — 17 options enumerated with `Source=editorconfig`
- `get_msbuild_properties` — substring filter returns 30/914 props
- `test_discover` — 9 tests in 1 project, paginated
- `go_to_definition` — correctly navigates to interface declaration
- `semantic_search` — 5 IDisposable implementations found
- `get_di_registrations` — 1 registration found accurately
- `find_reflection_usages` — 4 hits classified by API kind
- `revert_last_apply` — clean "nothing to revert" message
- `roslyn://server/catalog` and `roslyn://workspaces` resources — accurate, agree with their tool counterparts

## Phase 6 refactor summary
**N/A — no Phase 6 applies were performed.**
- The only viable refactor candidate (`organize_usings` on SlackWebhookEndpoints.cs) was blocked by **FLAG-011** (replaces unused usings with blank lines instead of deleting them).
- `format_document_preview` on the only file with active warnings (NetworkDocumentationEvidenceMapper.cs) returned an empty diff — file already conforms to .editorconfig formatting rules.
- `rename_preview` of `AdapterRegistry` to itself was a deliberate no-op.
- `fix_all_preview` for CS8619 returned cleanly with `GuidanceMessage="No code fix provider..."`.
- No commit, no branch push, no PR. The disposable worktree contains zero net file changes from this audit beyond the draft and final report files in `ai_docs/audit-reports/`.

## Concurrency mode matrix (Phase 8b)
**Phase 8b not exercised — single-mode lane (run 1 of pending pair).** The four-step operator dance described in the prompt (run prompt → `eng/flip-rw-lock.ps1` → restart MCP client → re-run prompt) was not performed. To produce the canonical dual-mode matrix the operator must:
1. Save this report (already at the canonical path).
2. Set `ROSLYNMCP_WORKSPACE_RW_LOCK=true` in the User-scope environment.
3. Restart Claude Code so the spawned MCP server inherits the new env var.
4. Re-invoke `ai_docs/prompts/deep-review-and-refactor.md` against the same `.claude/worktrees/deep-review-audit` worktree. Phase 0 step 15 will auto-pair this run-1 partial.

All Session A/B columns of the probe set, sequential baseline, parallel fan-out, behavioral exclusion, and lifecycle stress sub-tables are `skipped-pending-second-run`.

## Writer reclassification verification (Phase 8b.5)
| tool | session-A status | session-B status | wall-clock A (ms) | wall-clock B (ms) | notes |
|------|------------------|------------------|---------------------|---------------------|-------|
| `apply_text_edit` | skipped-safety | skipped-pending-second-run | — | — | Audit budget |
| `apply_multi_file_edit` | skipped-safety | skipped-pending-second-run | — | — | Audit budget |
| `revert_last_apply` | exercised (negative path only) | skipped-pending-second-run | — | — | "Nothing to revert" PASS |
| `set_editorconfig_option` | skipped-safety | skipped-pending-second-run | — | — | Audit budget |
| `set_diagnostic_severity` | skipped-safety | skipped-pending-second-run | — | — | Audit budget |
| `add_pragma_suppression` | skipped-safety | skipped-pending-second-run | — | — | Audit budget |

## Debug log capture
`client did not surface MCP log notifications` — Claude Code does not forward MCP `notifications/message` events to the agent loop, so the entire `Information` / `Warning` / `Error` / `Critical` channel from `McpLoggingProvider` is invisible to this audit. **No entries captured.**

## MCP server issues (bugs)

### FLAG-001 — `workspace_load` reports MSBuild "will not be pruned" warnings as `Severity=Error`
- **Tool:** `workspace_load`
- **Inputs:** `path=ITChatBot.sln`
- **Expected:** "PackageReference X will not be pruned" is informational MSBuild guidance from .NET 9+ pruning. Should be `Severity=Warning` (or `Hidden`/`Info`), not `Error`.
- **Actual:** Three entries returned with `Severity=Error`, `Category=Workspace`, `Id=WORKSPACE_FAILURE`. Workspace still loads cleanly (`IsLoaded=true`, `IsStale=false`, all 34 projects listed).
- **Severity:** incorrect result (severity mis-classification). Cosmetic-leaning because the workspace still works, but for an automated agent this looks like a hard failure.
- **Reproducibility:** always
- **Lock mode:** legacy-mutex

### FLAG-002 — `workspace_status` echoes the full workspace_load payload
- **Tool:** `workspace_status`
- **Expected:** A status summary (id, version, isLoaded, isStale, project count, document count, diagnostics).
- **Actual:** Returns the entire 30 KB project list AND the workspace diagnostics, identical to `workspace_load`.
- **Severity:** degraded performance / output bloat. The prompt asks for `workspace_list` heartbeats between phases (cross-cutting #14) — this design makes that costly enough to push a single turn over the output budget on its own.
- **Reproducibility:** always
- **Lock mode:** legacy-mutex

### FLAG-003 — `workspace_list` returns full project trees per workspace
- **Tool:** `workspace_list`
- **Expected:** A list of `WorkspaceId` + `LoadedPath` + minimal counts. There is no documented `summary` parameter.
- **Actual:** Returns the entire `workspace_load`-shaped object per workspace, scaling linearly with active sessions.
- **Severity:** improvement. Add a `verbose=false` default or a separate `workspace_list_summary` tool.
- **Reproducibility:** always
- **Lock mode:** legacy-mutex

### FLAG-004 — `project_diagnostics` severity filter zeros out non-matching totals
- **Tool:** `project_diagnostics`
- **Inputs:** `severity=Error, limit=20`
- **Expected:** Either (a) totals invariant to filter (filter only narrows the returned set), or (b) tool description explicitly says "totals reflect the active filter."
- **Actual:** `totalErrors=0, totalWarnings=0, totalInfo=0, totalDiagnostics=0` even though calling the same tool with no filter returns 182 warnings.
- **Severity:** incorrect result / cross-call inconsistency. Schema/description mismatch (the field is `totalWarnings`, not `totalWarningsMatchingFilter`).
- **Reproducibility:** always
- **Lock mode:** legacy-mutex

### FLAG-005 / FLAG-014 — three-way disagreement on WORKSPACE_FAILURE severity
- **Tools/resources:** `workspace_load`, `workspace_status`, `roslyn://workspaces` (all `Error`) vs `project_diagnostics` (`Warning`).
- **Expected:** A single diagnostic entry should have a single severity classification across every tool that surfaces it.
- **Actual:** Two readers and one writer downgrade severity inconsistently. The `roslyn://workspaces` resource reproduces the same `Error` severity that `workspace_load` does; only `project_diagnostics` re-classifies. So the bug is centered on `project_diagnostics`'s normalization layer, not on the resource serializer.
- **Severity:** incorrect result / cross-tool inconsistency. Downstream impact: agents that gate on `workspace_load` mark the workspace as failed even though it loaded cleanly.
- **Reproducibility:** always
- **Lock mode:** legacy-mutex

### FLAG-006 — `get_cohesion_metrics` mis-counts LoggerMessage source-generated stubs as methods
- **Tool:** `get_cohesion_metrics`
- **Expected:** Source-generator-emitted partial methods (`[LoggerMessage]`, `[GeneratedRegex]`) should be excluded from cohesion analysis or grouped into a single "infrastructure" cluster.
- **Actual:** `QuestionClassifier` shows `Lcom4Score=4` with each `LogXxx` partial as its own cluster, implying SRP violation. The actual class has one real method (`Classify`).
- **Severity:** incorrect result (false positive). Cohesion analysis is misleading on any modern .NET 8+ codebase using `LoggerMessage` source generators.
- **Reproducibility:** always
- **Lock mode:** legacy-mutex

### FLAG-006b — `get_cohesion_metrics` lists local helper methods as both methods and shared fields
- **Tool:** `get_cohesion_metrics`
- **Actual:** `SysLogServerAdapter` and `GitRepositoryAdapter` clusters list `CreateFailure`, `MapToQueryParameters`, `MapAndOrderEvidence`, `RunGitCommandAsync` as both standalone "methods" AND inside the `SharedFields` array.
- **Severity:** confusing output / data-shape inconsistency.
- **Reproducibility:** always
- **Lock mode:** legacy-mutex

### FLAG-007 — `find_unused_symbols` flags EF Core ModelSnapshot files
- **Tool:** `find_unused_symbols`
- **Expected:** Files under `Migrations/` named `*ModelSnapshot.cs` are EF tooling artifacts, referenced via `[DbContext(typeof(...))]` and the migration runner — not dead code.
- **Actual:** `ChatBotDbContextModelSnapshot` returned as the only `Confidence=high` unused symbol. An automated remove_dead_code chain would delete a critical EF migration snapshot.
- **Severity:** incorrect result (false positive).
- **Reproducibility:** always
- **Lock mode:** legacy-mutex

### FLAG-008 — `find_unused_symbols` lacks `excludeTestProjects` (asymmetric with `get_cohesion_metrics`)
- **Tool:** `find_unused_symbols`
- **Inputs:** `includePublic=true, excludeRecordProperties=true`
- **Expected:** Either auto-detect test attributes (`[Fact]`, `[Theory]`, `[Test]`, `[TestMethod]`) and exclude test classes, OR expose `excludeTestProjects` symmetric with `get_cohesion_metrics`.
- **Actual:** 7 of 20 returned symbols are XUnit test classes flagged at `Confidence=medium`.
- **Severity:** improvement / API consistency (high impact because remove_dead_code feeds directly off these results).
- **Reproducibility:** always
- **Lock mode:** legacy-mutex

### FLAG-009 — `find_type_usages` buckets cref doc comments as `Other`
- **Tool:** `find_type_usages`
- **Actual:** A `<see cref="AdapterRegistry"/>` reference in a doc comment is bucketed as `Other` rather than as a `Documentation` classification.
- **Severity:** cosmetic / output enrichment.
- **Reproducibility:** always
- **Lock mode:** legacy-mutex

### FLAG-010 — `analyze_snippet kind=statements` reports wrapper-relative columns instead of user-relative
- **Tool:** `analyze_snippet`
- **Inputs:** `code='int x = "hello";', kind='statements'`
- **Expected:** `StartColumn` ≈ 9 (offset of `"hello"` in the user-supplied code).
- **Actual:** `StartColumn=66` — clearly the column inside the wrapper `class Snippet { void Run() { … }}` template.
- **Severity:** incorrect result. The audit prompt's appendix calls this **FLAG-C / UX-001** "fixed in v1.7+", confirming this server (v1.6.0) is one minor version behind. Treat as a regression-still-reproducing finding.
- **Reproducibility:** always
- **Lock mode:** legacy-mutex

### FLAG-011 — `organize_usings_preview` leaves blank lines where it deletes usings
- **Tool:** `organize_usings_preview`
- **Inputs:** `filePath=src/api/Endpoints/SlackWebhookEndpoints.cs`
- **Expected:** Removed `using` directives should be deleted from the diff entirely.
- **Actual:** UnifiedDiff shows `-using ITChatBot.Channels.Bridge;` followed by `+`, `-using ITChatBot.Channels.Registry;` followed by `+`, `-using Microsoft.AspNetCore.Authorization;` followed by `+`. Applying this would leave 3 stray blank lines in the using block.
- **Severity:** incorrect result (preview produces a syntactically valid but cosmetically broken diff). High impact — this is the documented happy-path workflow for cleaning up usings on a real solution. **Apply was skipped to avoid degrading the file.**
- **Reproducibility:** always
- **Lock mode:** legacy-mutex

### FLAG-013 — `find_references` with invalid symbolHandle returns empty success
- **Tool:** `find_references`
- **Inputs:** `symbolHandle=eyJNZXRhZGF0YU5hbWUiOiJmYWtlIn0=` (fabricated base64 of `{"MetadataName":"fake"}`)
- **Expected:** Structured error like `workspace_status`'s NotFound shape.
- **Actual:** `{count:0, totalCount:0, references:[], hasMore:false}`. The caller cannot tell whether the symbol exists with no references or whether the handle is junk.
- **Severity:** error-message-quality / vague success. High impact in agent loops because it makes "this tool returned the answer" indistinguishable from "this tool silently failed."
- **Reproducibility:** always
- **Lock mode:** legacy-mutex

### FLAG-015 — error responses have `tool: "unknown"` instead of the called tool name
- **Tool:** `workspace_status` (and likely other workspace-scoped tools that share the same error wrapper)
- **Expected:** `tool: "workspace_status"`.
- **Actual:** `tool: "unknown"`.
- **Severity:** cosmetic / observability. Makes correlationId-style debugging harder when reading raw MCP transcripts.
- **Reproducibility:** always
- **Lock mode:** legacy-mutex

## Improvement suggestions

1. **Add `verbose=false` / `summary=true` modes for `workspace_load`, `workspace_list`, `workspace_status`, and `roslyn://workspaces`** — currently every status check returns a 30 KB project tree even when the caller just wants `{IsLoaded, IsStale, ProjectCount, DocumentCount, WorkspaceVersion}`. The prompt's cross-cutting principles #13 and #14 are nearly impossible to satisfy on a 34-project solution because of this.
2. **Make filter-aware totals explicit in `project_diagnostics`** — either keep the totals invariant or rename the fields to `totalErrorsMatchingFilter` etc.
3. **Normalize WORKSPACE_FAILURE severity at one layer** — pick `Warning` (most accurate, matches MSBuild semantics) and emit it consistently from `workspace_load`, `workspace_status`, `roslyn://workspaces`, and `project_diagnostics`.
4. **Source-generator-aware cohesion / dead-code detection** — `[LoggerMessage]`, `[GeneratedRegex]`, and EF `*ModelSnapshot.cs` are pervasive in modern .NET; both `get_cohesion_metrics` and `find_unused_symbols` should ignore them by default.
5. **Add `excludeTestProjects` to `find_unused_symbols`** — symmetry with `get_cohesion_metrics` and avoids the trivially false flag that test classes are unused.
6. **Distinguish "invalid handle" from "valid handle, zero results"** in `find_references`, `find_consumers`, `find_type_usages`, etc. — return a structured NotFound error like `workspace_status` does.
7. **Fix `organize_usings_preview` to actually delete using lines** — the current behavior produces a valid but cosmetically broken diff, so the most-used Phase 6 cleanup tool is unsafe to apply.
8. **`analyze_snippet` user-relative columns** — confirm FLAG-C / UX-001 in the v1.7+ release notes; this server (v1.6.0) still emits wrapper-relative columns.
9. **`rename_preview` should warn on same-name no-op** — currently silently returns empty Changes; an agent cannot distinguish "rename succeeded with no actual references" from "rename was a no-op because the new name equals the old one".
10. **`semantic_search` should HTML-decode encoded `<>` in queries** — AI agents commonly double-encode angle brackets and the tool currently returns 0 results with no hint about why.
11. **Error wrapper should populate `tool` field** — `tool:"unknown"` is harder to debug than the actual tool name.
12. **Tool description should document substring matching** for `symbol_search`'s `query` parameter and `get_msbuild_properties`'s `propertyNameFilter`. Both work as substring matches; both schemas describe them as "filter".
13. **Catalog drift between live `surface` summary and resource catalog** — `server_info` reports `prompts: stable=0, experimental=16` but the prompts are exposed via the experimental tier only. Confirm this is intentional and document.
14. **`compile_check emitValidation=true` is 3.5× slower with no observable benefit on a clean solution.** Either auto-skip emit when GetDiagnostics already returned zero errors or document the cost explicitly so callers know to default to `false`.
15. **Audit-feasibility friction.** A complete walkthrough of `ai_docs/prompts/deep-review-and-refactor.md` against a 34-project solution does not fit in a single Claude Code conversation today, primarily because of the workspace-payload bloat tools listed in #1 and the per-turn ~250 KB output budget. Consider shipping a `roslyn-mcp-audit` companion CLI that drives the prompt headlessly and emits the canonical raw audit file as an artifact, freeing the LLM from having to render every tool result back through context.

## Performance observations
| Tool | Inputs | Lock mode | Wall-clock | Note |
|------|--------|-----------|------------|------|
| `compile_check` (default) | full solution | legacy-mutex | 5,707 ms | OK for 34 projects / 751 docs |
| `compile_check` (`emitValidation=true`) | full solution | legacy-mutex | 20,229 ms | 3.5× slower for byte-identical results — see improvement #14 |
| `nuget_vulnerability_scan` | full solution | legacy-mutex | 10,340 ms | Acceptable for 34-project scan; `IncludesTransitive=false` |
| `evaluate_csharp` (`Enumerable.Range(...).Sum()`) | trivial | legacy-mutex | 263 ms | Within script timeout (10 s default) |
| `evaluate_csharp` (`int.Parse("abc")`) | trivial runtime err | legacy-mutex | 15 ms | Excellent — fast structured error |
| `analyze_snippet` (`statements`, broken code) | trivial | legacy-mutex | <1 s | Fast |

## Known issue regression check
**N/A** — this audit ran against IT-Chat-Bot, not Roslyn-Backed-MCP, so `ai_docs/backlog.md` of the server repo was not consulted. However, **FLAG-010 doubles as a regression-still-reproducing finding**: the deep-review prompt's own appendix names this issue **FLAG-C / UX-001** and notes it as "fixed in v1.7+". Server is on v1.6.0, so the fix has not yet shipped to this build. Track as `analyze-snippet-user-relative-columns` and confirm it is closed once the operator upgrades.

## Known issue cross-check
**N/A — no prior source consulted in this audit.**

---

## Report path / next operator step
This is the **mode-1 partial of a deep-review pair (legacy-mutex)**. To complete the canonical dual-mode raw audit:

1. (Optional) Copy this file from `<itchatbot-worktree>/ai_docs/audit-reports/20260407T211317Z_itchatbot_legacy-mutex_mcp-server-audit.md` into `<roslyn-backed-mcp-root>/ai_docs/audit-reports/` so the canonical store has both runs.
2. Run `eng/flip-rw-lock.ps1` to set `ROSLYNMCP_WORKSPACE_RW_LOCK=true` in the User-scope environment.
3. Fully close and reopen Claude Code so the spawned MCP server inherits the flipped env var.
4. Re-invoke `ai_docs/prompts/deep-review-and-refactor.md` against the same `.claude/worktrees/deep-review-audit` worktree. Phase 0 step 15 will auto-pair with this run-1 partial via the `_legacy-mutex_` filename and Phase 8b.6 will fill the Session B columns.
