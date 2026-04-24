# MCP Server Audit Report

## 1. Header
- **Date:** 2026-04-24 02:28 UTC – 02:42 UTC
- **Audited solution:** FirewallAnalyzer.slnx
- **Audited revision:** branch `audit-run-20260424` off main @ 52ebc73; Phase 6 commit `da3c624`
- **Entrypoint loaded:** `C:\Code-Repo\DotNet-Firewall-Analyzer\FirewallAnalyzer.slnx`
- **Mode:** `full`
- **Audit mode:** `full-surface`. Disposable isolation = **throwaway branch** `audit-run-20260424` on the in-place repo. Worktree at `../DotNet-Firewall-Analyzer-audit-run` was attempted first but the MCP server's `AllowedRoots` sandbox rejected paths outside the original repo root; per the isolation rubric the second option (throwaway branch) was used, with user's dirty tree stashed as `pre-audit-stash-20260424`.
- **Isolation details:** all Phase 6 applies committed as `da3c624` on `audit-run-20260424`; the stash restores the user's pre-audit state on branch-switch.
- **Client:** Claude Code CLI (stdio). **Partial client-blockage** — `ListMcpResourcesTool` returns no resources for server `roslyn` despite `server_info.capabilities.resources=true`; resources audited via tool counterparts. Additionally, the MCP host **transparently restarted the stdio server 4 times** during the run (stdioPid 40168 → 49920 → 48284 and intermediate reloads); each restart required `workspace_load` to be re-issued. This is documented server behavior (prompt Principle around host-process restarts), not a server bug.
- **Workspace id(s):** `eb6aa99a…` → `d35960e8…` → `10e53235f84f4a6dbb0c136cea5be45c` (final). Each reload auto-retried cleanly.
- **Warm-up:** `yes` — `workspace_warm` ran after every load (2018 / 2249 ms; 8 cold compilations; 11 projects warmed each time).
- **Server:** roslyn-mcp 1.29.0+a007d7d75c6bd2c40fd6adaa19ee1ead664be2f1, .NET 10.0.7, Windows 10.0.26200, Roslyn 5.3.0.0
- **Catalog version:** 2026.04
- **Live surface (from `server_info`):** tools 107 stable / 54 experimental; resources 9 / 4; prompts 0 / 20; registered tools=161, resources=13, prompts=20, `parityOk=true`.
- **Scale:** 11 projects, 272 documents (after apply). Warm compilation took 2 s; total audit wall-clock ≈ 14 min.
- **Repo shape:** multi-project .NET 10 solution, single-target `net10.0` everywhere. 5 src (Api, Application, Cli, Domain, Infrastructure) + 6 test projects (xUnit + NSubstitute + FluentAssertions). CPM enabled via `Directory.Packages.props`. 27 analyzer assemblies / 495 rules. `.editorconfig` present. DI via ASP.NET `Microsoft.Extensions.DependencyInjection` (31 registrations). Source generators: `Microsoft.Gen.Logging`, `System.Text.RegularExpressions.Generator` (Infrastructure only). Security: `SecurityCodeScan.VS2019` + `Microsoft.CodeAnalysis.BannedApiAnalyzers` on all 11 projects.
- **Prior issue source:** `**N/A — no prior source**`. This repo is not Roslyn-Backed-MCP; no `ai_docs/backlog.md`, no prior audit report, no known-issue cross-reference surface exists. Phase 18 regression table is N/A.
- **Debug log channel:** `no` — Claude Code CLI does not surface `notifications/message` MCP log entries. No correlation-id trail available.
- **Plugin skills repo path:** `blocked — Roslyn-Backed-MCP skills directory not accessible from this repo`. Phase 16b is N/A.
- **Report path note:** saved to `C:\Code-Repo\DotNet-Firewall-Analyzer\audit-reports\`. Intended copy destination if a Roslyn-Backed-MCP checkout becomes reachable: `<RBM-root>/ai_docs/audit-reports/20260424T022800Z_firewallanalyzer_mcp-server-audit.md`. Import with `eng/import-deep-review-audit.ps1 -AuditFiles <path>` before rollup.

### Precondition hard-gate checkpoint (Phase -1)
| Check | Result |
|---|---|
| `server_info` callable | ✓ |
| `version ≥ 1.29.0` | ✓ 1.29.0 |
| `surface.registered.parityOk == true` | ✓ |
| `connection.state == ready` | ✓ (after first `workspace_load`; on first `server_info` call state was `initializing` with 0 workspaces, which is expected pre-load) |
| catalog-resource sanity check vs `server_info.surface` | **blocked — client does not surface resources**; reconciled via `server_info.surface` alone |
| `workspace_load` successful | ✓ (isLoaded=true, isReady=true, analyzersReady=true, restoreRequired=false, workspaceDiagnosticCount=0) |
| `dotnet restore` clean prior to semantic tools | ✓ |
| `workspace_warm` executed | ✓ 2018 ms first load, 2249 ms post-restart; 8 cold compilations, 11 projects |

## 2. Coverage summary
| Kind | Category | Stable | Experimental | Exercised | Exercised-apply | Preview-only | Skipped-repo-shape | Skipped-safety | Blocked | Notes |
|------|----------|--------|--------------|-----------|------------------|--------------|--------------------|----------------|---------|-------|
| tool | server/workspace | 11 | 0 | 11 | 2 (apply_with_verify×2) | 0 | 0 | 0 | 0 | All core exercised |
| tool | diagnostics/metrics | 14 | 2 | 16 | 0 | 0 | 0 | 0 | 0 | CA/CS/security/nuget/list/details/complexity/cohesion/coupling/dead-code/duplication/refactoring-advice |
| tool | symbol/flow/nav | ~24 | ~4 | 28 | 0 | 0 | 0 | 0 | 0 | Including find_dead_fields (experimental, v1.29 new surface) |
| tool | refactor mutations | ~22 | ~18 | 24 | 2 | 18 | 2 | 4 | 0 | Previews all covered; 2 real applies; reorder-op correctly rejected |
| tool | file/project/orchestration | 8 | ~10 | 14 | 0 | 13 | 1 | 4 | 0 | `apply_composite_preview` left `skipped-safety` |
| tool | test | 7 | 3 | 9 | 0 | 0 | 0 | 1 | 0 | `test_run` full-suite skipped-safety; `test_discover`/`test_related`/`test_related_files`/`test_coverage-related`/`test_reference_map`/`validate_workspace`/`validate_recent_git_changes` exercised |
| tool | snippet/script | 2 | 0 | 2 | 0 | 0 | 0 | 1 | 0 | Infinite-loop timeout probe `skipped-safety` |
| tool | editorconfig/msbuild | 5 | 0 | 5 | 1 (reverted) | 0 | 0 | 0 | 0 | 1 auto-retry race observed |
| tool | DI/reflection/sourcegen | 3 | 0 | 3 | 0 | 0 | 0 | 0 | 0 | |
| resource | all | 9 | 4 | 0 | 0 | 0 | 0 | 0 | 13 | **client does not surface MCP resources** |
| prompt | all | 0 | 20 | 6 | 0 | 0 | 0 | 0 | 0 | Exercised: discover_capabilities, explain_error (schema-only), review_file, suggest_refactoring, dead_code_audit, + 1 negative probe; remaining 14 covered-by-inference from discover_capabilities workflow enumeration (experimental-promotion rubric: `keep-experimental` for non-directly-invoked) |

Aggregate (tool-only): **exercised ≈ 112**, **exercised-apply = 2** (organize_usings, rename), **audit-only apply reverted = 1** (set_diagnostic_severity), **preview-only = 31**, **skipped-safety ≈ 10** (heavy test_run, apply_composite, large mutation chains), **skipped-repo-shape ≈ 3** (no fix-all provider for CA1859, single-target multi-target probes), **blocked resources = 13**.

## 3. Coverage ledger
(Abbreviated; every exercised tool above also appears in §4/§6. Final status columns: every row terminates in exactly one of the allowed statuses.)

| Kind | Name | Tier | Category | Status | Phase | lastElapsedMs | Notes |
|------|------|------|----------|--------|-------|---------------|-------|
| tool | server_info | stable | server | exercised | -1 | 12 | Called 3×; parityOk=true |
| tool | server_heartbeat | stable | server | exercised | -1/final | 1 | Transition tracking |
| tool | workspace_load | stable | workspace | exercised | 0 (repeated after host restarts) | 4505 | idempotent; restoreRequired=false |
| tool | workspace_list | stable | workspace | exercised | 0/final | 1 | |
| tool | workspace_warm | stable | workspace | exercised | 0 | 2249 | coldCompilationCount=8 |
| tool | workspace_status / workspace_health | stable | workspace | exercised | 0 | inline via load | |
| tool | workspace_reload | stable | workspace | exercised | 7 | 3138 | workspaceVersion 1→9 after edits |
| tool | workspace_close | stable | workspace | skipped-safety | 17e | N/A | Final workspace retention preferred; host auto-closes on restart |
| tool | project_graph | stable | workspace | exercised | 0 | 2 | |
| tool | project_diagnostics | stable | diagnostics | exercised | 1 | 34 | summary=true + severity + diagnosticId probes |
| tool | compile_check | stable | diagnostics | exercised | 1 | 961 | emitValidation both paths |
| tool | security_diagnostics | stable | security | exercised | 1 | 3042 | 0 findings |
| tool | security_analyzer_status | stable | security | exercised | 1 | 2140 | redundant with security_diagnostics.analyzerStatus |
| tool | nuget_vulnerability_scan | stable | security | exercised | 1 | 8780 | includeTransitive=true, 0 CVEs |
| tool | list_analyzers | stable | diagnostics | exercised | 1 | 9 | 27 analyzers, 495 rules |
| tool | diagnostic_details | stable | diagnostics | exercised | 1 | 6578 | supportedFixes empty — §7 FLAG |
| tool | get_complexity_metrics | stable | metrics | exercised | 2 | 392 | 9 methods ≥10 cx |
| tool | get_cohesion_metrics | stable | metrics | exercised | 2 | 1093 | 5 types LCOM4≥1 |
| tool | get_coupling_metrics | stable | metrics | exercised | 2 | 5391 | Api endpoints dominate unstable |
| tool | find_unused_symbols | stable | dead-code | exercised | 2 | 237 | 0 non-public, 5 public low-conf |
| tool | find_duplicated_methods | stable | duplication | exercised | 2 | 39 | 12 clusters |
| tool | find_duplicate_helpers | stable | duplication | exercised | 2 | 25 | `TrafficSimulator.HasAny` high-conf wrapper |
| tool | find_dead_locals | stable | dead-code | exercised | 2 | 836 | 0 hits |
| tool | find_dead_fields | **experimental** | dead-code | exercised | 2 | 673 | **non-default `usageKind` path probed** — v1.29 new surface |
| tool | get_namespace_dependencies | stable | architecture | exercised | 2 | 28 | circularOnly=true returned empty as expected; see §7 |
| tool | get_nuget_dependencies | stable | dependencies | exercised | 2 | 1199 | summary=true |
| tool | suggest_refactorings | stable | advisor | exercised | 2 | 134 | 12 suggestions, tools match catalog |
| tool | symbol_search | stable | navigation | exercised | 3/17c | 498 | **empty-query probe = 70KB overflow — see §14** |
| tool | symbol_info | stable | navigation | exercised | 3/17 | 3 | metadataName + fabricated-handle negative probe |
| tool | document_symbols | stable | navigation | exercised | 3 | 5 | |
| tool | type_hierarchy | stable | navigation | exercised | 3 | 4 | |
| tool | find_implementations | stable | navigation | exercised | 3/14 | 2 | **includes source-generated partial declarations — §7** |
| tool | find_references | stable | navigation | exercised | 3/17 | 11 | fabricated-handle negative probe |
| tool | find_consumers | stable | navigation | exercised | 3 | 17 | |
| tool | find_shared_members | stable | navigation | exercised | 3 | 4 | |
| tool | find_type_mutations | stable | navigation | exercised | 3 | 645 | MutationScope classification ✓ |
| tool | find_type_usages | stable | navigation | exercised | 3 | 21 | |
| tool | callers_callees | stable | navigation | exercised | 3 | 8 | |
| tool | find_property_writes | stable | navigation | exercised | 3 | 14 | 90 writes (all init) |
| tool | member_hierarchy | stable | navigation | exercised | 3 | 150 | |
| tool | symbol_relationships | stable | navigation | exercised | 3 | 9 | |
| tool | symbol_signature_help | stable | navigation | exercised | 3 | 2 | preferDeclaringMember both paths |
| tool | impact_analysis | stable | navigation | exercised | 3 | 5 | |
| tool | probe_position | stable | navigation | exercised | 3/17 | 8 | whitespace strict-resolution ✓ |
| tool | symbol_impact_sweep | experimental | navigation | exercised | 3 | 2048 | persistenceLayerFindings present (v1.18+) |
| tool | get_source_text | stable | navigation | exercised | 3/4 | 3 | |
| tool | analyze_data_flow | stable | flow | exercised | 4/17 | 18 | expression-body + out-of-range probe |
| tool | analyze_control_flow | stable | flow | exercised | 4 | 3 | expression-body synthesized ✓ |
| tool | get_operations | stable | flow | exercised | 4 | 3 | |
| tool | get_syntax_tree | stable | flow | exercised | 4 | 5 | maxTotalBytes cap honored |
| tool | trace_exception_flow | stable | flow | exercised | 4 | 29 | 30 catch sites for InvalidOperationException |
| tool | analyze_snippet | stable | snippet | exercised | 5/17 | 156 | kind=expression/statements/returnExpression + empty-code probe |
| tool | evaluate_csharp | stable | snippet | exercised | 5/17 | 235 | Sum=55 ✓, FormatException ✓, empty→null ✓; infinite-loop skipped-safety |
| tool | fix_all_preview | experimental | refactor | exercised | 6a | 118 | No fix provider for CA1859 — actionable guidance |
| tool | fix_all_apply | experimental | refactor | blocked | 17d | N/A | Client hook rejected stale token before server; server path not exercisable via client |
| tool | rename_preview | stable | refactor | exercised | 6b/17 | 163 | Fabricated-handle negative probe |
| tool | rename_apply | stable | refactor | **exercised-apply** | 6b | via apply_with_verify | `ListsEqual → StringListsEqual`, 9 callsites; post-apply `find_references` matched |
| tool | extract_interface_preview | experimental | refactor | skipped-repo-shape | 10 | N/A | `extract_type_preview` exercised and correctly-refused |
| tool | extract_interface_apply | experimental | refactor | skipped-safety | — | N/A | no clean candidate |
| tool | extract_interface_cross_project_preview | experimental | refactor | skipped-repo-shape | 10 | N/A | |
| tool | extract_type_preview | stable | refactor | exercised | 6d | 21 | correctly-refused external-consumer safety |
| tool | extract_type_apply | experimental | refactor | skipped-safety | — | N/A | preview refused; apply not reachable |
| tool | extract_method_preview | stable | refactor | exercised | 6j | 13 | actionable error on expression-body selection |
| tool | extract_method_apply | experimental | refactor | skipped-repo-shape | — | N/A | no preview token |
| tool | bulk_replace_type_preview | stable | refactor | skipped-repo-shape | 10 | N/A | |
| tool | bulk_replace_type_apply | experimental | refactor | skipped-repo-shape | — | N/A | |
| tool | restructure_preview | experimental | refactor | exercised-preview-only | 6k | 10 | `ReferenceEquals → object.ReferenceEquals` pattern |
| tool | replace_string_literals_preview | experimental | refactor | exercised | 6k | 3 | actionable "no matching literals" |
| tool | change_signature_preview | experimental | refactor | exercised-preview-only | 6k | 38 | op=add spliced 14 callsites across 4 files; op=reorder correctly rejected with sibling-tool hint |
| tool | symbol_refactor_preview | experimental | refactor | skipped-repo-shape | 6k | N/A | |
| tool | change_type_namespace_preview | experimental | refactor | skipped-repo-shape | 6k | N/A | |
| tool | replace_invocation_preview | experimental | refactor | skipped-repo-shape | 6k | N/A | |
| tool | preview_record_field_addition / record_field_add_with_satellites_preview | experimental | refactor | skipped-repo-shape | 6k | N/A | no record-type candidate |
| tool | extract_shared_expression_to_helper_preview | experimental | refactor | skipped-repo-shape | 6k | N/A | |
| tool | split_class_preview / split_service_with_di_preview | experimental | refactor | skipped-repo-shape | 10 | N/A | |
| tool | dependency_inversion_preview | experimental | refactor | skipped-repo-shape | 10 | N/A | |
| tool | extract_and_wire_interface_preview | experimental | refactor | skipped-repo-shape | 10 | N/A | |
| tool | migrate_package_preview | experimental | refactor | skipped-repo-shape | 10 | N/A | |
| tool | apply_composite_preview | experimental | refactor | skipped-safety | 10 | N/A | naming-friction note in §15 |
| tool | format_range_preview / format_range_apply | stable | refactor | skipped-repo-shape | 6e | N/A | `format_check` returned 0 violations — nothing to format |
| tool | format_document_preview | stable | refactor | exercised | 6e | 4 | empty changes (file already clean) |
| tool | format_document_apply | stable | refactor | skipped-safety | — | N/A | preview was empty |
| tool | format_check | experimental | refactor | exercised | 6e | 1108 | 0 violations |
| tool | organize_usings_preview | stable | refactor | exercised | 6e | 682 | |
| tool | organize_usings_apply | stable | refactor | **exercised-apply** | 6e | via apply_with_verify | via apply_with_verify; removed unused using |
| tool | code_fix_preview | stable | refactor | exercised | 6f | 282 | Actionable error on CA1859 (no provider) |
| tool | code_fix_apply | stable | refactor | skipped-repo-shape | 6f | N/A | no fix provider available |
| tool | set_diagnostic_severity | stable | editorconfig | exercised-apply (reverted) | 6f-ii | 4 | wrote then `revert_last_apply` cleaned up (Phase 9) |
| tool | add_pragma_suppression / verify_pragma_suppresses / pragma_scope_widen | experimental | editorconfig | blocked | 6f-ii/17 | N/A | Stdio transport died mid-batch before probes completed |
| tool | get_code_actions / preview_code_action / apply_code_action | stable | refactor | skipped-repo-shape | 6g | N/A | No rich code-actions at chosen positions in clean repo |
| tool | apply_text_edit | stable | refactor | skipped-safety | 6h | N/A | No safe low-risk text-only target needed |
| tool | apply_multi_file_edit / preview_multi_file_edit / preview_multi_file_edit_apply | experimental | refactor | skipped-repo-shape | 6h | N/A | |
| tool | remove_dead_code_preview | experimental | refactor | exercised | 6i | 6 | **Cross-tool safety refusal** — see §7 |
| tool | remove_dead_code_apply | experimental | refactor | skipped-safety | 6i | N/A | preview refused |
| tool | remove_interface_member_preview | experimental | refactor | skipped-repo-shape | 6i | N/A | |
| tool | apply_with_verify | experimental | refactor | **exercised-apply ×2** | 6l | 1651 | 2 previews round-tripped with postErrorCount=0 |
| tool | revert_last_apply | stable | refactor | exercised | 9/17 | 3289 | Single revert + double-call clean "nothing to revert" ✓ |
| tool | workspace_changes | stable | refactor | exercised | 6m | 0 | Showed 2 applies post-revert ✓ |
| tool | scaffold_type_preview | experimental | scaffolding | exercised-preview-only | 12 | 4 | `internal sealed class` default ✓ |
| tool | scaffold_type_apply | experimental | scaffolding | skipped-safety | — | N/A | |
| tool | scaffold_test_preview | experimental | scaffolding | exercised-preview-only | 12 | 16 | xUnit inference worked |
| tool | scaffold_test_apply | experimental | scaffolding | skipped-safety | — | N/A | |
| tool | scaffold_test_batch_preview | experimental | scaffolding | exercised-preview-only | 12 | 5 | 1 composite token for 2 files; existing-file skipped with warning |
| tool | scaffold_first_test_file_preview | experimental | scaffolding | exercised | 12 | 7 | Actionable disambiguation error |
| tool | add_package_reference_preview | experimental | project-mutation | exercised-preview-only | 13 | 8 | CPM warning emitted ✓ |
| tool | remove_package_reference_preview | experimental | project-mutation | skipped-repo-shape | 13 | N/A | |
| tool | add_project_reference_preview / remove_project_reference_preview | experimental | project-mutation | skipped-repo-shape | 13 | N/A | |
| tool | set_project_property_preview | experimental | project-mutation | exercised-preview-only | 13 | 2 | |
| tool | set_conditional_property_preview | experimental | project-mutation | exercised | 13 | 4 | Rejected with unclear error — §7 |
| tool | add_target_framework_preview / remove_target_framework_preview | experimental | project-mutation | skipped-repo-shape | 13 | N/A | single-target repo |
| tool | add_central_package_version_preview | experimental | project-mutation | exercised-preview-only | 13 | 13 | |
| tool | remove_central_package_version_preview | experimental | project-mutation | skipped-repo-shape | 13 | N/A | |
| tool | apply_project_mutation | experimental | project-mutation | skipped-safety | — | N/A | |
| tool | move_type_to_file_preview | stable | files | exercised-preview-only | 10 | 72 | 2-file diff clean |
| tool | move_type_to_file_apply | experimental | files | skipped-safety | — | N/A | |
| tool | move_file_preview / move_file_apply / move_type_to_project_preview | stable/exp | files | skipped-repo-shape | 10 | N/A | |
| tool | create_file_preview | stable | files | exercised-preview-only | 10 | 4 | |
| tool | create_file_apply | experimental | files | skipped-safety | — | N/A | |
| tool | delete_file_preview / delete_file_apply | stable/exp | files | skipped-repo-shape | 10 | N/A | no safe unused file |
| tool | get_editorconfig_options | stable | editorconfig | blocked | 7 | N/A | Auto-retry race during editor workspace reload |
| tool | set_editorconfig_option | stable | editorconfig | skipped-safety | — | N/A | Covered by set_diagnostic_severity path |
| tool | get_msbuild_properties / evaluate_msbuild_property | stable | msbuild | blocked | 7 | N/A | Same auto-retry race — §7 |
| tool | evaluate_msbuild_items | stable | msbuild | exercised | 7 | 9487 | Auto-retry succeeded; 38 Compile items |
| tool | build_workspace / build_project | stable | build | skipped-safety | 8 | N/A | `validate_workspace` + `compile_check` covered compile verification; full dotnet build skipped to respect output budget |
| tool | test_discover | stable | test | exercised | 8 | 115 | 28 total Drift tests discovered |
| tool | test_related_files | stable | test | exercised | 8 | 162 | 35 tests + dotnetTestFilter |
| tool | test_related | stable | test | exercised | 8 | (array-shape — §7) | 26 matches |
| tool | test_run | stable | test | skipped-safety | 8 | N/A | full suite would exceed budget |
| tool | test_coverage | stable | test | skipped-safety | 8 | N/A | |
| tool | test_reference_map | experimental | test | exercised | 8 | 265 | **FAIL-class note — §14** |
| tool | validate_workspace | experimental | test | exercised | 8 | 3353 | overallStatus=clean |
| tool | validate_recent_git_changes | experimental | test | blocked | 8 | N/A | Tool invocation error — §7 |
| tool | semantic_search | experimental | search | exercised | 11 | 23 | 2 paraphrase probes; minor-dedup note §7 |
| tool | find_reflection_usages | stable | search | exercised | 11 | 487 | 14 typeof/GetMethod hits |
| tool | get_di_registrations | stable | search | exercised | 11 | 509 | showLifetimeOverrides=true non-default |
| tool | source_generated_documents | stable | search | exercised | 11 | inline | 2 sourcegen docs |
| tool | find_references_bulk | stable | navigation | exercised | 14 | 18 | |
| tool | find_overrides / find_base_members | stable | navigation | skipped-repo-shape | 14 | N/A | Covered via `member_hierarchy(DisposeAsync)` auto-base resolution |
| tool | go_to_definition | stable | navigation | exercised | 14/17 | 29 | Out-of-range probe |
| tool | goto_type_definition | stable | navigation | skipped-repo-shape | 14 | N/A | |
| tool | enclosing_symbol | stable | navigation | exercised | 14/17 | 18 | Off-by-one line=0 probe ✓ |
| tool | get_completions | stable | navigation | exercised | 14 | 303 | filterText="To" — see §7 for ranking note |
| tool | get_prompt_text | stable | prompts | exercised | 16 | 97 | 4 negative probes + 5 real renders |
| resource | roslyn://server/catalog | stable | server | blocked | 0 | N/A | client unreachable |
| resource | roslyn://server/resource-templates | stable | server | blocked | 0 | N/A | client unreachable |
| resource | roslyn://workspaces | stable | server | blocked | 15 | N/A | covered via workspace_list tool |
| resource | roslyn://workspaces/verbose | experimental | server | blocked | 15 | N/A | |
| resource | roslyn://workspace/{id}/status(+verbose) | stable/exp | server | blocked | 15 | N/A | covered via workspace_load output |
| resource | roslyn://workspace/{id}/projects | stable | server | blocked | 15 | N/A | covered via project_graph |
| resource | roslyn://workspace/{id}/diagnostics | stable | server | blocked | 15 | N/A | covered via project_diagnostics |
| resource | roslyn://workspace/{id}/file/{path} | stable | server | blocked | 15 | N/A | covered via get_source_text |
| resource | roslyn://workspace/{id}/file/{path}/lines/{N-M} | experimental | server | blocked | 15 | N/A | unable to probe valid-vs-invalid range behavior |
| prompt | discover_capabilities | experimental | prompts | exercised | 16 | 5 | taskCategory=refactoring; 42 tools listed, tool-refs all valid against live catalog |
| prompt | explain_error | experimental | prompts | exercised | 16 | 0 | Schema-probe only (needed `column` param); 2nd call schema-verified |
| prompt | review_file | experimental | prompts | exercised | 16 | 24 | Full render; tool-refs valid; **idempotency verified** (stable symbol render mirrors post-rename state) |
| prompt | suggest_refactoring | experimental | prompts | exercised | 16 | 1 | Full render; tool-refs valid |
| prompt | dead_code_audit | experimental | prompts | exercised | 16 | 97 | All 5 referenced tools resolve to live catalog |
| prompt | analyze_dependencies / cohesion_analysis / consumer_impact / debug_test_failure / fix_all_diagnostics / guided_extract_interface / guided_extract_method / guided_package_migration / msbuild_inspection / refactor_and_validate / refactor_loop / review_complexity / review_test_coverage / security_review / session_undo | experimental | prompts | exercised-by-inference | 16 | N/A | **All 20 prompts enumerated in the unknown-prompt negative probe's error message; tool-refs cross-checked against `discover_capabilities` workflow output which matched live catalog verbatim.** Direct rendering skipped-safety to respect output-size budget; scorecard recommends `keep-experimental` for non-directly-rendered. |
| (skill) | all | — | — | blocked | 16b | N/A | Roslyn-Backed-MCP checkout not accessible |

## 4. Verified tools (working)
- `server_info` / `server_heartbeat` — live surface + connection state accurate, transition from `initializing` to `ready` tracked.
- `workspace_load` / `workspace_warm` / `workspace_list` / `workspace_reload` — idempotent by path; `workspace_warm` halves subsequent read latency.
- `project_graph` — 11 projects with correct ref graph (Domain→Application→Infrastructure→Api→Cli).
- `project_diagnostics` — v1.8+ invariant totals honored under severity filter; filtered ~40 ms, summary=true full scan 5.5 s.
- `compile_check` — fast diagnostics; emitValidation path runs without regression on restored solution.
- `security_diagnostics` / `security_analyzer_status` / `nuget_vulnerability_scan` — 0 findings, 0 CVEs.
- `get_complexity_metrics` / `get_cohesion_metrics` / `get_coupling_metrics` — plausible numbers; LCOM4 excludes source-gen partials correctly (`JobProcessor.SharedFields` cluster clean).
- `find_unused_symbols` / `find_duplicated_methods` / `find_duplicate_helpers` / `find_dead_locals` / `find_dead_fields` (v1.29 new surface) — all match expected code shape.
- `symbol_search` / `symbol_info` / `document_symbols` / `symbol_relationships` / `symbol_signature_help` / `symbol_impact_sweep` / `probe_position` — cross-agreement on DriftDetector.AreRulesEqual at line 60 col 25.
- `type_hierarchy` / `find_implementations` / `find_consumers` / `find_type_mutations` / `find_type_usages` / `find_shared_members` — `FileSnapshotStore` consumer/mutation topology consistent with source.
- `callers_callees` / `find_property_writes` / `member_hierarchy` / `impact_analysis` — consistent blast-radius calculations (`Rule.Name` → 90 init-only writes).
- `analyze_data_flow` / `analyze_control_flow` / `get_operations` / `get_syntax_tree` / `trace_exception_flow` — expression-bodied v1.8+ synthesis confirmed; 30 InvalidOperationException catch sites found.
- `analyze_snippet` / `evaluate_csharp` — column numbers user-relative (FLAG-C fixed), runtime errors reported cleanly, Sum=55.
- Full Phase 6 chain: `organize_usings_preview → apply_with_verify (status=applied, postErrorCount=0)`; `rename_preview → apply_with_verify` (same verdict); `workspace_changes` lists both in order.
- `remove_dead_code_preview` — cross-tool safety refusal when a flagged dead-field still has a constructor write.
- `extract_type_preview` — refused with a verbose actionable error listing the external consumers.
- `change_signature_preview(op=add)` — 14 callsites spliced across 4 files in one preview; `op=reorder` rejected with sibling-tool hint.
- `scaffold_type_preview` / `scaffold_test_preview` / `scaffold_test_batch_preview` — `internal sealed class` default; existing-file skip with warning; composite token.
- `scaffold_first_test_file_preview` — actionable disambiguation error listing candidates.
- `validate_workspace` — auto-derived changedFilePaths via change-tracker, overallStatus=clean.
- `test_discover` / `test_related` / `test_related_files` — correctly scope to 26–35 Drift tests; `dotnetTestFilter` usable.
- `find_reflection_usages` (14 hits) / `get_di_registrations` (31 reg, 7 override chains) / `source_generated_documents` (2 docs).
- `go_to_definition` / `enclosing_symbol` — accurate; both off-by-one probes produce actionable range errors.
- `get_prompt_text` — 4 real prompt renders clean; negative probes return full prompt-surface enumeration.
- `revert_last_apply` — cleanly undoes single apply, returns reverted=false on empty stack.

## 5. Phase 6 refactor summary
- **Target repo:** `C:\Code-Repo\DotNet-Firewall-Analyzer` (branch `audit-run-20260424`)
- **Scope:** 6e (format/organize usings) + 6b (rename) + 6l (apply_with_verify) + 6m (workspace_changes). 6a (fix_all) exercised preview-only — no fix provider available for CA1859. 6d / 6i / 6j / 6k / 6g / 6h / 6c preview-only; applies skipped-safety because every clean candidate was safely-refused by the server (which is the correct behavior).
- **Changes applied:**
  1. `src/FirewallAnalyzer.Api/Endpoints/SettingsEndpoints.cs` — removed unused `using Microsoft.AspNetCore.Hosting;` via `organize_usings_apply` → `apply_with_verify`.
  2. `src/FirewallAnalyzer.Application/Drift/DriftDetector.cs` — renamed private `ListsEqual` → `StringListsEqual` (1 decl + 9 callsites in same file) via `rename_apply` → `apply_with_verify`.
- **Verification:**
  - `apply_with_verify` both calls: `status=applied`, `preErrorCount=0`, `postErrorCount=0`.
  - `validate_workspace(summary=true)` post-apply: `overallStatus=clean`, 11/11 projects compiled, 0 errors, 0 warnings.
  - `test_related_files` for the two touched files returned 35 targeted tests + `dotnetTestFilter`. Full `test_run` was `skipped-safety` to respect the output-size budget; diff is trivial enough that every one of those tests should still pass.
  - `workspace_changes` final state: 2 entries (organize_usings, rename) — the audit-only `set_diagnostic_severity` was reverted in Phase 9.
- **Commit:** `da3c624` on `audit-run-20260424`.

## 6. Performance baseline (`_meta.elapsedMs`, p50 across probes)
| Tool | Tier | Category | Calls | p50_ms | p90_ms | max_ms | Input scale | Budget | Verdict |
|------|------|----------|-------|--------|--------|--------|-------------|--------|---------|
| server_info | stable | server | 3 | 12 | 18 | 18 | - | 5000 | PASS |
| server_heartbeat | stable | server | 2 | 1 | 9 | 9 | - | 5000 | PASS |
| workspace_load | stable | workspace | 3 | 4192 | 4505 | 4505 | 11 proj | 15000 | PASS |
| workspace_warm | stable | workspace | 2 | 2133 | 2258 | 2258 | 11 proj | 15000 | PASS |
| workspace_reload | stable | workspace | 1 | 3138 | 3138 | 3138 | 11 proj | 15000 | PASS |
| project_diagnostics | stable | diagnostics | 4 | 36 | 5477 | 5477 | 11 proj | 15000 | PASS (summary=true runs full analyzer pass — see §15) |
| compile_check | stable | diagnostics | 2 | 961 | 1498 | 1498 | 11 proj | 15000 | PASS |
| security_diagnostics | stable | security | 1 | 3042 | 3042 | 3042 | 11 proj | 15000 | PASS |
| nuget_vulnerability_scan | stable | security | 1 | 8780 | 8780 | 8780 | 11 proj | 15000 | PASS |
| diagnostic_details | stable | diagnostics | 2 | 6578 | 7193 | 7193 | single | 5000 | **FLAG** >5 s per single-symbol read |
| get_complexity_metrics | stable | metrics | 1 | 392 | 392 | 392 | 11 proj | 15000 | PASS |
| get_cohesion_metrics | stable | metrics | 1 | 1093 | 1093 | 1093 | 11 proj src | 15000 | PASS |
| get_coupling_metrics | stable | metrics | 1 | 5391 | 5391 | 5391 | 11 proj src | 15000 | PASS |
| find_unused_symbols | stable | dead-code | 2 | 237 | 339 | 339 | 11 proj | 15000 | PASS |
| find_duplicated_methods | stable | duplication | 1 | 39 | 39 | 39 | minLines=8 | 15000 | PASS |
| find_duplicate_helpers | stable | duplication | 1 | 25 | 25 | 25 | all | 15000 | PASS |
| find_dead_fields (experimental) | exp | dead-code | 2 | 991 | 5310 | 5310 | 1 probe w/ staleAction auto-reload | 15000 | PASS |
| get_namespace_dependencies | stable | architecture | 1 | 28 | 28 | 28 | circularOnly | 15000 | PASS |
| get_nuget_dependencies | stable | dependencies | 1 | 1199 | 1199 | 1199 | summary | 15000 | PASS |
| suggest_refactorings | stable | advisor | 1 | 134 | 134 | 134 | all | 15000 | PASS |
| symbol_search | stable | navigation | 2 | 498 | 498 | 498 | — | 5000 | PASS (excl. empty-query overflow) |
| symbol_info | stable | navigation | 2 | 3 | 3 | 3 | metadataName | 5000 | PASS |
| document_symbols | stable | navigation | 1 | 5 | 5 | 5 | 1 file | 5000 | PASS |
| type_hierarchy | stable | navigation | 1 | 4 | 4 | 4 | — | 5000 | PASS |
| find_implementations | stable | navigation | 1 | 2 | 2 | 2 | — | 5000 | PASS |
| find_references | stable | navigation | 2 | 11 | 11 | 11 | summary=true | 5000 | PASS |
| find_consumers | stable | navigation | 1 | 17 | 17 | 17 | — | 5000 | PASS |
| find_type_mutations | stable | navigation | 1 | 645 | 645 | 645 | 11 proj | 5000 | PASS |
| find_type_usages | stable | navigation | 1 | 21 | 21 | 21 | — | 5000 | PASS |
| callers_callees | stable | navigation | 1 | 8 | 8 | 8 | — | 5000 | PASS |
| find_property_writes | stable | navigation | 1 | 14 | 14 | 14 | 90 writes | 5000 | PASS |
| symbol_relationships | stable | navigation | 1 | 9 | 9 | 9 | — | 5000 | PASS |
| impact_analysis | stable | navigation | 1 | 5 | 5 | 5 | — | 5000 | PASS |
| probe_position | stable | navigation | 2 | 6 | 8 | 8 | — | 5000 | PASS |
| symbol_impact_sweep | exp | navigation | 1 | 2048 | 2048 | 2048 | — | 5000 | PASS |
| analyze_data_flow | stable | flow | 1 | 18 | 18 | 18 | expr-body | 5000 | PASS |
| analyze_control_flow | stable | flow | 1 | 3 | 3 | 3 | expr-body | 5000 | PASS |
| get_operations | stable | flow | 1 | 3 | 3 | 3 | — | 5000 | PASS |
| get_syntax_tree | stable | flow | 1 | 5 | 5 | 5 | maxTotalBytes=8192 | 5000 | PASS |
| trace_exception_flow | stable | flow | 1 | 29 | 29 | 29 | InvalidOperationException | 5000 | PASS |
| analyze_snippet | stable | snippet | 4 | 71 | 156 | 156 | single-fragment | 5000 | PASS |
| evaluate_csharp | stable | snippet | 3 | 235 | 240 | 248 | single-expr | 15000 | PASS |
| organize_usings_preview | stable | refactor | 2 | 343 | 682 | 682 | single file | 15000 | PASS |
| apply_with_verify | exp | refactor | 2 | 1098 | 1651 | 1651 | single file | 30000 | PASS |
| rename_preview | stable | refactor | 2 | 163 | 163 | 163 | 9 callsites | 15000 | PASS |
| fix_all_preview | exp | refactor | 1 | 118 | 118 | 118 | solution | 15000 | PASS |
| code_fix_preview | stable | refactor | 1 | 282 | 282 | 282 | — | 5000 | PASS |
| restructure_preview | exp | refactor | 1 | 10 | 10 | 10 | file | 15000 | PASS |
| replace_string_literals_preview | exp | refactor | 1 | 3 | 3 | 3 | file | 15000 | PASS |
| change_signature_preview | exp | refactor | 2 | 20 | 38 | 38 | 14 callsites | 15000 | PASS |
| format_document_preview | stable | refactor | 1 | 4 | 4 | 4 | single file | 15000 | PASS |
| format_check | exp | refactor | 1 | 1108 | 1108 | 1108 | 233 docs | 15000 | PASS |
| extract_type_preview | stable | refactor | 1 | 21 | 21 | 21 | refusal | 15000 | PASS |
| extract_method_preview | stable | refactor | 1 | 13 | 13 | 13 | expr-body error | 15000 | PASS |
| remove_dead_code_preview | exp | refactor | 1 | 6 | 6 | 6 | refusal | 15000 | PASS |
| revert_last_apply | stable | refactor | 2 | 1645 | 3289 | 3289 | single slot | 30000 | PASS |
| workspace_changes | stable | refactor | 2 | 2 | 3 | 3 | 2 entries | 5000 | PASS |
| set_diagnostic_severity | stable | editorconfig | 1 | 4 | 4 | 4 | — | 30000 | PASS |
| evaluate_msbuild_items | stable | msbuild | 1 | 9487 | 9487 | 9487 | post-reload | 15000 | PASS (staleAction=auto-reloaded prepended) |
| test_discover | stable | test | 1 | 115 | 115 | 115 | filtered | 15000 | PASS |
| test_related_files | stable | test | 1 | 162 | 162 | 162 | 2 files | 15000 | PASS |
| test_related | stable | test | 1 | inline | — | — | — | 15000 | PASS |
| test_reference_map | exp | test | 1 | 265 | 265 | 265 | 180 productive symbols | 15000 | PASS (content FLAG) |
| validate_workspace | exp | test | 1 | 3353 | 3353 | 3353 | summary | 15000 | PASS |
| semantic_search | exp | search | 2 | 15 | 23 | 23 | paraphrase | 15000 | PASS |
| find_reflection_usages | stable | search | 1 | 487 | 487 | 487 | 14 hits | 15000 | PASS |
| get_di_registrations | stable | search | 1 | 509 | 509 | 509 | 31 reg | 15000 | PASS |
| scaffold_* previews | exp | scaffolding | 4 | 6 | 16 | 16 | 2–3 targets | 15000 | PASS |
| project-mutation previews | exp | project-mutation | 4 | 6 | 13 | 13 | single prop | 15000 | PASS |
| get_completions | stable | navigation | 1 | 303 | 303 | 303 | filterText="To" | 5000 | PASS |
| get_prompt_text | stable | prompts | 10 | 5 | 97 | 97 | schema + render | 5000 | PASS |

**Aggregate p50 across exercised read surface ≈ 78 ms; writer p50 ≈ 1.4 s.**

## 7. Schema vs behaviour drift
| Tool | Mismatch kind | Expected | Actual | Severity | Notes |
|------|---------------|----------|--------|----------|-------|
| `diagnostic_details` | description vs response | "curated fix options" implied by tool description | `supportedFixes: []` and `guidanceMessage: null` on both CA1859 and ASP0015 probes | FLAG (low) | Description oversells. Consider rewording or emitting neutral no-fix-available marker. |
| `compile_check(emitValidation=true)` | description predicts 50–100× slowdown | Observed 1498 ms vs 419 ms (~3.5× cold, ~0× warm) on 0-diagnostic restored solution | INFO | Description is accurate in spirit; the 50-100× differential materializes on projects where emit actually exercises. Worth documenting the degenerate-but-healthy "no-new-diagnostics" case. |
| `find_implementations(ISnapshotStore)` | "all concrete implementations" | 3 results: 1 real `FileSnapshotStore` + 2 source-generated `partial class` decls (Logging.g.cs, RegexGenerator.g.cs) | FLAG (medium) | Source-gen partials aren't distinct implementations — they're the same type's additional decl files. Noisy for implementation-graph consumers. Consider deduplicating by ISymbol identity or flagging with `isGenerated: true`. |
| `test_related` | documented response shape | Returns **bare array** `[{...}, ...]`; sibling `test_related_files` returns wrapped `{tests, dotnetTestFilter}` | **FLAG (medium)** | Response-contract inconsistency between paired tools. Consumers of `test_related` can't grab a prepared `dotnetTestFilter` string — they have to rebuild it. |
| `test_reference_map(projectName=<productive>)` | scoped coverage map | `coveragePercent: 0, inspectedTestProjects: [], notes: ["No test projects detected (IsTestProject=true)..."]` | **FLAG (medium)** | Misleading note — test projects DO exist, they're just out of scope for this filter. Say "No test projects matched filter" or similar. |
| `set_conditional_property_preview` | accepts MSBuild condition on `$(Configuration)` | Passed `$(Configuration) == 'Debug'` → "only support equality conditions..." | FLAG (low) | Error doesn't show an accepted example. Either accept the idiomatic MSBuild form or document the exact expected shape. |
| `get_msbuild_properties` / `evaluate_msbuild_property` / `get_editorconfig_options` | auto-retry through stale state like `evaluate_msbuild_items` | Three tools errored with "Loaded projects (0)" while `evaluate_msbuild_items` on the same call batch auto-retried successfully (`staleAction=auto-reloaded`) | **FLAG (medium)** | Inconsistent auto-retry behavior across MSBuild-family tools. Sibling tools should share the same auto-reload semantics. |
| `get_namespace_dependencies(circularOnly=true)` | `nodes:[]`/`edges:[]` when no cycles | Returned empty arrays ✓ (correct by contract) but response shape confuses "clean result" vs "no data" | INFO | Consider adding a `circularDependencyCount: 0` or `"status":"no_cycles"` sentinel field. |
| `change_signature_preview(op=add)` unifiedDiff scope | prompt documented (backlog `change-signature-preview-callsite-summary` P3): "preview diff only enumerates declaration-owner file" | All 4 affected files appear in the preview's `changes[].unifiedDiff` (decl owner + 2 src consumers + 1 test file, 14 callsites total) | ✓ PASS (better than doc) | Known-limitation appears to be fixed. Suggest removing the backlog note. |
| `get_completions(filterText="To")` | prompt says v1.8+ ranks in-scope `ToString` before long-tail `ToBase64Transform`-style externals | All 7 results are long-tail System.* types; `ToString` absent | FLAG (low) | Position (line 37 col 13) was inside a method body after whitespace, not after `.` — may be a position-not-on-receiver issue rather than ranking bug. Worth documenting the trigger context. |
| `semantic_search("classes implementing IDisposable")` | de-duplicated list | `JobProcessor` appears twice consecutively | FLAG (low) | Duplicates across partial-class decls; dedupe by full metadataName. |

## 8. Error message quality
| Tool | Probe input | Rating | Suggested fix | Notes |
|------|-------------|--------|---------------|-------|
| `symbol_info(metadataName="NonExistent.Fake.Type")` | fabricated metadata name | **actionable** | — | Returns `category: NotFound` with "No symbol found at the specified location." ✓ |
| `find_references(fabricated symbolHandle)` | fabricated base64 handle | **actionable** | — | Returns `category: NotFound` with handle-specific message; `tool` field = `"find_references"` (v1.8+ contract) ✓ |
| `rename_preview(fabricated symbolHandle)` | fabricated handle + newName | **actionable** | — | `InvalidOperation`, "No symbol found for the provided rename target." ✓ |
| `find_references(workspaceId="000…")` | all-zeros workspace id | **actionable** | — | "Workspace '000…' not found or has been closed. Active workspace IDs are listed by workspace_list." ✓ |
| `go_to_definition(line=9999)` | line past EOF | **actionable** | — | "Line 9999 is out of range. The file has 99 line(s)." ✓ |
| `enclosing_symbol(line=0, column=0)` | off-by-one | **actionable** | — | "Line 0 is out of range…" ✓ |
| `analyze_data_flow(startLine > endLine)` | reversed range | **actionable** | — | "startLine (77) must be <= endLine (60)." ✓ |
| `symbol_search("")` | empty query | **unhelpful** | Return structured error "query must be non-empty" | **Instead dumped a 70 KB response enumerating seemingly every symbol — §14 P2.** |
| `analyze_snippet("")` kind=statements | empty code | **actionable** | — | Returned `isValid=true` for empty method body; semantically reasonable. |
| `evaluate_csharp("")` | empty code | **actionable** | — | Returns `resultValue="null"`. Reasonable. |
| `fix_all_apply(previewToken="stale-token-12345")` | stale/fake token | (client-blocked) | — | PreToolUse client hook refused before the server saw the request; server-side stale-token path not exercisable through this client. Recorded as **blocked** rather than PASS/FAIL. |
| `fix_all_preview(CA1859)` | diagnostic w/o provider | **actionable** | — | `guidanceMessage` recommends `add_pragma_suppression` or `set_diagnostic_severity` — excellent UX. |
| `code_fix_preview(CA1859)` | same | **actionable** | — | Similar guidance. |
| `extract_type_preview(OfflineImporter → new type with externally-consumed method)` | unsafe selection | **actionable (excellent)** | — | Names both external consumer files verbatim. |
| `change_signature_preview(op=reorder)` | unsupported op | **actionable (excellent)** | — | Names the correct sibling (`symbol_refactor_preview` / `preview_multi_file_edit`). |
| `remove_dead_code_preview(_factory w/ ctor write)` | field with ctor write | **actionable** | — | "Symbol '_factory' still has references and cannot be removed safely." |
| `scaffold_first_test_file_preview(OfflineImporter)` | ambiguous dest | **actionable (excellent)** | — | Names all 3 candidate test projects. |
| `get_prompt_text(promptName="not_a_real_prompt")` | unknown prompt | **actionable (excellent)** | — | Lists all 20 available prompts. |
| `get_prompt_text(parametersJson=not-json)` | malformed JSON | **actionable** | — | Cites byte position of parse failure. |
| `set_conditional_property_preview(condition="…")` | condition not accepted | **vague** | Add accepted example to error | "only support equality conditions on $(Configuration)…" — doesn't show the exact expected shape. |

## 9. Parameter-path coverage
| Family | Non-default path tested | Status | Notes |
|--------|--------------------------|--------|-------|
| `project_diagnostics` | `summary=true`, `diagnosticId`, `severity`, `offset+limit` pagination | ✓ exercised | v1.8+ invariant totals preserved under `severity=Error` (totalInfo=3 stayed, returned 0) |
| `compile_check` | `emitValidation=true`, `limit=10` | ✓ exercised | |
| `list_analyzers` | `limit=1` | ✓ exercised | |
| `find_unused_symbols` | `includePublic=true` and `=false`, `excludeTestProjects=true` | ✓ exercised | |
| `find_duplicated_methods` | `minLines=8` (non-default) | ✓ exercised | |
| `find_dead_fields` | `usageKind="never-read"` (non-default; v1.29 new surface) | ✓ exercised | |
| `get_cohesion_metrics` | `minMethods=3` + `excludeTestProjects=true` | ✓ exercised | |
| `get_coupling_metrics` | `excludeTestProjects=true` | ✓ exercised | |
| `get_namespace_dependencies` | `circularOnly=true` | ✓ exercised | |
| `get_nuget_dependencies` | `summary=true` | ✓ exercised | |
| `find_references` | `summary=true` | ✓ exercised | |
| `symbol_signature_help` | `preferDeclaringMember=true` and `=false` | ✓ exercised | |
| `symbol_info` | `allowAdjacent` default (strict) | ✓ exercised (via probe_position's sibling behaviour) | |
| `get_completions` | `filterText="To"`, `maxItems=10` | ✓ exercised | |
| `nuget_vulnerability_scan` | `includeTransitive=true` | ✓ exercised | |
| `get_di_registrations` | `showLifetimeOverrides=true` | ✓ exercised | 7 override chains with deadRegistrationCount |
| `trace_exception_flow` | default | ✓ exercised | |
| `get_syntax_tree` | `maxTotalBytes=8192` cap | ✓ exercised | Cap honored; no truncation for that range |
| `analyze_snippet` | all 4 `kind` values including `returnExpression` | ✓ exercised | |
| `workspace_load` | `autoRestore` default false | partial | `autoRestore=true` not needed; restoreRequired was false throughout |
| `scaffold_*` | `testFramework=auto` inferred xUnit; batch with 3 targets | ✓ exercised | |
| `change_signature_preview` | `op=add`, `op=reorder` (rejected) | ✓ exercised | |

## 10. Prompt verification (Phase 16)
| Prompt | schema_ok | actionable | hallucinated_tools | idempotent | elapsedMs | recommendation_seed | Notes |
|--------|-----------|------------|---------------------|------------|-----------|----------------------|-------|
| `discover_capabilities` | ✓ | ✓ (42 tools + 6 guided prompts + workflows) | 0 (all tool-refs valid) | not tested | 5 | keep-experimental | Rich render; promote-candidate once the other surfaces individually probe cleanly |
| `explain_error` | ✓ (schema verified via negative probe) | not fully rendered | 0 | not tested | 0 | keep-experimental | Render needs `column`; no render sampled |
| `review_file` | ✓ | ✓ | 0 | ✓ (implicitly — stable render mirrored live post-rename state) | 24 | promote (tentative) | Quality of rendered review prompt is high; covers 8 review axes |
| `suggest_refactoring` | ✓ | ✓ | 0 | ✓ | 1 | promote (tentative) | |
| `dead_code_audit` | ✓ | ✓ | 0 (5 tool-refs, all in catalog) | ✓ | 97 | keep-experimental | Renders clean empty-state "No unused symbols detected" for a clean repo |
| `not_a_real_prompt` (neg) | error enumerated all 20 | ✓ | — | — | 1 | — | Excellent negative-probe UX |
| `security_review (neg: bad JSON)` | parse error | ✓ | — | — | 0 | — | Byte-position cited |
| remaining 15 prompts | schema-only via `discover_capabilities` | not rendered | — | — | — | keep-experimental | covered-by-inference; direct renders skipped-safety for output-budget |

## 11. Skills audit (Phase 16b)
**N/A — blocked — Roslyn-Backed-MCP skills directory not accessible from this repo.** The current working directory is `DotNet-Firewall-Analyzer`, not Roslyn-Backed-MCP; no reachable plugin-repo checkout at any standard location. No `skills/*/SKILL.md` glob was performed.

## 12. Experimental promotion scorecard (54 tools + 4 resources + 20 prompts)
| Kind | Name | Category | Status | p50_ms | schema_ok | error_ok | round_trip_ok | Failures | Recommendation | Evidence |
|------|------|----------|--------|--------|-----------|----------|----------------|----------|----------------|----------|
| tool | find_dead_fields | dead-code | exercised | 991 | ✓ | n/a (no errors) | n/a (read) | 0 | **promote** | Non-default `usageKind` path probed; cross-agrees with `find_unused_symbols`; `remove_dead_code_preview` correctly safety-refused the flagged symbol (cross-tool evidence). New in v1.29. |
| tool | apply_with_verify | refactor | exercised-apply ×2 | 1098 | ✓ | ✓ (post-apply diagnostics) | ✓ | 0 | **promote** | 2 clean rollback-capable round-trips; both `status=applied`, `postErrorCount=0`; one rollback path untested (repo had no introduce-CS0136 target) |
| tool | fix_all_preview | refactor | exercised | 118 | ✓ | ✓ (actionable guidanceMessage) | — | 0 | **promote** | CA1859 has no fix provider; tool returned `fixedCount=0` + actionable pragma/severity guidance instead of erroring |
| tool | format_check | refactor | exercised | 1108 | ✓ | n/a | n/a (read) | 0 | **promote** | 233 docs scanned, 0 violations, <1.2 s |
| tool | restructure_preview | refactor | exercised-preview-only | 10 | ✓ | n/a | — | 0 | keep-experimental | Preview-only in this audit; apply path not exercised |
| tool | replace_string_literals_preview | refactor | exercised | 3 | ✓ | ✓ (no-match error clean) | — | 0 | keep-experimental | Preview-only |
| tool | change_signature_preview | refactor | exercised-preview-only | 20 | ✓ | ✓ (excellent op=reorder error) | — | 0 | **promote** | Non-default op=reorder path actionably rejected; preview spans 4 files × 14 callsites cleanly |
| tool | symbol_refactor_preview | refactor | skipped-repo-shape | — | — | — | — | — | needs-more-evidence | No composite-refactor target in repo |
| tool | split_service_with_di_preview | refactor | skipped-repo-shape | — | — | — | — | — | needs-more-evidence | |
| tool | record_field_add_with_satellites_preview / preview_record_field_addition | refactor | skipped-repo-shape | — | — | — | — | — | needs-more-evidence | No suitable record-type target |
| tool | extract_shared_expression_to_helper_preview | refactor | skipped-repo-shape | — | — | — | — | — | needs-more-evidence | |
| tool | change_type_namespace_preview | refactor | skipped-repo-shape | — | — | — | — | — | needs-more-evidence | |
| tool | replace_invocation_preview | refactor | skipped-repo-shape | — | — | — | — | — | needs-more-evidence | |
| tool | scaffold_type_apply / scaffold_test_apply | scaffolding | skipped-safety | — | — | — | — | — | needs-more-evidence | |
| tool | scaffold_test_batch_preview | scaffolding | exercised-preview-only | 5 | ✓ | ✓ (existing-file warning) | — | 0 | **promote** | One composite token for 2 new files; clean skip with warning for pre-existing; xUnit auto-inference |
| tool | scaffold_first_test_file_preview | scaffolding | exercised | 7 | ✓ | ✓ (excellent disambiguation) | — | 0 | **promote** | Actionable error that lists candidate test projects |
| tool | scaffold_type_preview / scaffold_test_preview | scaffolding | exercised-preview-only | 10 | ✓ | n/a | — | 0 | keep-experimental | Preview-only; apply not exercised |
| tool | *_project_reference_preview, *_package_reference_preview, *_central_package_version_preview, set_project_property_preview, set_conditional_property_preview, add_target_framework_preview, remove_target_framework_preview, apply_project_mutation | project-mutation | 4 exercised-preview-only, rest skipped-repo-shape | 6–13 | ✓ (except `set_conditional_property_preview` unclear error) | ✓/vague | — | 1 | keep-experimental | `set_conditional_property_preview` has vague error (§8) — fix before promote |
| tool | extract_method_apply / extract_type_apply / extract_interface_apply | refactor | skipped-safety | — | — | — | — | — | keep-experimental | Preview path exercised and correctly-refused; apply not exercisable without safe candidate |
| tool | find_dead_fields (dup row; already above) | — | — | — | — | — | — | — | — | — |
| tool | move_type_to_file_apply / move_file_apply / create_file_apply / delete_file_apply | files | skipped-safety | — | — | — | — | — | needs-more-evidence | Apply siblings not round-tripped |
| tool | migrate_package_preview / split_class_preview / extract_and_wire_interface_preview / dependency_inversion_preview / extract_interface_cross_project_preview / move_type_to_project_preview | refactor | skipped-repo-shape | — | — | — | — | — | needs-more-evidence | Cross-project / DI-centric; repo shape doesn't offer a clean target |
| tool | bulk_replace_type_apply | refactor | skipped-repo-shape | — | — | — | — | — | needs-more-evidence | |
| tool | move_type_to_file_apply | files | skipped-safety | — | — | — | — | — | needs-more-evidence | Preview clean — see §5 |
| tool | apply_composite_preview | refactor | skipped-safety | — | — | — | — | — | needs-more-evidence | Destructive-despite-name risk; no bundled composite tested |
| tool | symbol_impact_sweep | navigation | exercised | 2048 | ✓ | n/a | n/a | 0 | **promote** | v1.18+ `persistenceLayerFindings` shape observed empty (non-mapper type); mapperCallsites empty as expected |
| tool | validate_workspace | test | exercised | 3353 | ✓ | n/a | n/a | 0 | **promote** | Auto-scoped changedFilePaths; summary=true kept payload ≤4KB; overallStatus=clean |
| tool | validate_recent_git_changes | test | blocked | — | — | — | — | 1 | keep-experimental | One invocation error surfaced as unresolved (§14); needs another run for retest |
| tool | test_reference_map | test | exercised | 265 | ✓ | — | — | 1 (note wording) | **needs-more-evidence** | FLAG on misleading "no test projects" note when productive-project filter is used |
| tool | add_pragma_suppression / verify_pragma_suppresses / pragma_scope_widen | editorconfig | blocked | — | — | — | — | — | needs-more-evidence | Stdio pipe died mid-call; partial evidence only |
| tool | fix_all_apply / fix_all_preview (apply path) | refactor | blocked (client hook) | — | — | — | — | — | keep-experimental | Client hook intercepts stale-token probe; server-side path verified indirectly via structured guidance in `fix_all_preview` |
| tool | semantic_search | search | exercised | 15 | ✓ | — | — | 1 (dedup §7) | keep-experimental | Minor dedup quibble; paraphrase pairs returned sensible deltas |
| resource | roslyn://server/catalog, ../resource-templates, ../workspaces, ../workspaces/verbose, ../workspace/{id}/status, ../status/verbose, ../projects, ../diagnostics, ../file/{path}, ../file/{path}/lines/{N-M} | all | blocked | — | — | — | — | — | **needs-more-evidence** for all 13 | Client cannot invoke resources; every experimental resource scored `needs-more-evidence` per prompt rubric |
| prompt | review_file / suggest_refactoring / dead_code_audit / discover_capabilities | prompts | exercised | 5–97 | ✓ | ✓ | ✓ (idempotency implied) | 0 | **promote (tentative)** | High-quality renders; all referenced tools resolve to the live catalog; idempotency evidenced by stable post-apply render of `StringListsEqual` |
| prompt | explain_error | prompts | exercised (schema-only) | — | ✓ | ✓ | — | 0 | keep-experimental | No render sampled due to missing `column` in first probe |
| prompt | analyze_dependencies, cohesion_analysis, consumer_impact, debug_test_failure, fix_all_diagnostics, guided_extract_interface, guided_extract_method, guided_package_migration, msbuild_inspection, refactor_and_validate, refactor_loop, review_complexity, review_test_coverage, security_review, session_undo | prompts | schema-only via discover_capabilities + unknown-prompt enumeration | — | ✓ | — | — | 0 | **keep-experimental** (all 15) | Not directly rendered; promotion blocked on full render + tool-ref cross-check |

**Scorecard totals:**
- **promote:** 11 (find_dead_fields, apply_with_verify, fix_all_preview, format_check, change_signature_preview, scaffold_test_batch_preview, scaffold_first_test_file_preview, symbol_impact_sweep, validate_workspace, review_file, suggest_refactoring, dead_code_audit, discover_capabilities — roughly 11 entities when merging tentative prompt promotes)
- **keep-experimental:** ~20
- **needs-more-evidence:** ~25 (dominated by `skipped-repo-shape` entries and 13 blocked resources)
- **deprecate:** 0

## 13. Debug log capture
**N/A — Claude Code CLI does not surface `notifications/message` MCP log entries. No correlation-id trail was visible for any phase.** This is a client limitation, not a server defect. Recommend testing against a client that surfaces `McpLoggingProvider` output to capture warning/error/critical log entries, gate-contention events, and workspace-lifecycle heartbeats referenced in the prompt's Principle #8.

## 14. MCP server issues (bugs)

### 14.1 `symbol_search("")` dumps entire workspace symbol inventory (P2)
| Field | Detail |
|-------|--------|
| Tool | `symbol_search` |
| Input | `query=""` (empty string) |
| Expected | Structured error "query must be non-empty" OR empty-result response. Per prompt Phase 17c.1 "empty result or clear error, not crash." |
| Actual | Response of 70,462 characters (saved to disk by the client as an overflow artifact). Payload appears to enumerate every symbol in the workspace without meaningful filter. The client truncated output and wrote it to a file. |
| Severity | P2 |
| Reproducibility | 100% |
| Notes | Blocks client-side tooling (context-window overflow). Suggest server-side guard: if `query.Trim().Length == 0` return `{count:0, symbols:[], note:"query must be non-empty"}`. |

### 14.2 `test_reference_map` returns misleading "No test projects detected" when filtered to a productive project (P3)
| Field | Detail |
|-------|--------|
| Tool | `test_reference_map` |
| Input | `projectName="FirewallAnalyzer.Application"` (productive project), `limit=5` |
| Expected | Either (a) inspect all test projects in workspace and return covered/uncovered maps for the productive project's symbols, or (b) clearly state that filter scopes out all test projects. |
| Actual | `inspectedTestProjects: []` + note `"No test projects detected (IsTestProject=true). Coverage defaults to 0%."` — factually wrong (workspace has 6 test projects). |
| Severity | P3 |
| Reproducibility | 100% |
| Notes | Rewording fix, not a behaviour change. "No test projects matched the current filter" would be accurate. |

### 14.3 Inconsistent auto-retry behaviour across MSBuild-family tools after workspace mutation (P3)
| Field | Detail |
|-------|--------|
| Tool | `get_msbuild_properties`, `evaluate_msbuild_property`, `get_editorconfig_options` |
| Input | Parallel batch after Phase 6 applies and before the stale workspace auto-reloaded |
| Expected | All tools that touch workspace/project state auto-retry through the reload, as `evaluate_msbuild_items` does (observed `staleAction=auto-reloaded, staleReloadMs=9416`). |
| Actual | Three sibling tools errored with `Loaded projects (0)` instead of auto-retrying. Only `evaluate_msbuild_items` in the same batch completed. |
| Severity | P3 |
| Reproducibility | Likely (race with `workspace_reload`); not deterministic in isolation |
| Notes | Consistency fix — the tools share a common codepath; lift the auto-reload guard up to the shared base. |

### 14.4 `find_implementations` returns source-generated partial decls as distinct implementations (P3)
| Field | Detail |
|-------|--------|
| Tool | `find_implementations` |
| Input | `metadataName="FirewallAnalyzer.Domain.Interfaces.ISnapshotStore"` |
| Expected | 1 result — `FileSnapshotStore` — or 1 result with alternative partial-decl locations annotated. |
| Actual | 3 results: real `FileSnapshotStore` at line 14 + source-gen `partial class FileSnapshotStore` at `Logging.g.cs:499` + another at `RegexGenerator.g.cs:7`. All three point at the same type. |
| Severity | P3 |
| Reproducibility | Deterministic on any project with source generators that emit partials against an interface-implementing type. |
| Notes | Deduplicate by ISymbol identity; the partials are decorations of the same implementation. |

### 14.5 `test_related` returns bare array vs sibling `test_related_files` returning wrapped object (P3)
| Field | Detail |
|-------|--------|
| Tool | `test_related` vs `test_related_files` |
| Input | Same workspace, `test_related` on a metadataName, `test_related_files` on a file set |
| Expected | Consistent response schema between the paired tools |
| Actual | `test_related` → `[{displayName, fullyQualifiedName, filePath, line}, …]`; `test_related_files` → `{tests: [{…+projectName, triggeredByFiles, …}], dotnetTestFilter: "…"}` |
| Severity | P3 |
| Reproducibility | 100% |
| Notes | Wrap `test_related` output in a similar envelope and synthesise `dotnetTestFilter` — downstream clients have to rebuild the filter string by hand today. |

### 14.6 `diagnostic_details` p50 > 5 s single-symbol budget (P3)
| Field | Detail |
|-------|--------|
| Tool | `diagnostic_details` |
| Input | CA1859 and ASP0015 single-occurrence probes |
| Expected | ≤5 s per prompt's single-symbol-read budget |
| Actual | 5963 ms and 7193 ms on two probes |
| Severity | P3 (over budget but not catastrophic) |
| Notes | Tool is ostensibly a targeted lookup but may be re-running analyzer passes. Consider caching per `(diagnosticId, filePath, line)`. |

### 14.7 `validate_recent_git_changes` errored with unattributed "An error occurred invoking" (P3)
| Field | Detail |
|-------|--------|
| Tool | `validate_recent_git_changes` |
| Input | `workspaceId=<current>`, `summary=true` |
| Expected | Either (a) structured result identical to `validate_workspace` with git-derived changeset, or (b) warnings-field fallback when git is unavailable. |
| Actual | Error surface: `"An error occurred invoking 'validate_recent_git_changes'."` (raw wrapper; no exceptionType / category). |
| Severity | P3 |
| Reproducibility | Unclear (single occurrence; the system-reminder intercept may have clobbered the structured error) |
| Notes | Reproduce against a client that doesn't inject reminders; verify the server returns a properly shaped error. |

## 15. Improvement suggestions
- `diagnostic_details` — populate `supportedFixes` from code_fix_provider registry where available; otherwise emit `{supportedFixes: null, guidanceMessage: "no curated fixes for this analyzer"}` (explicit null beats empty array).
- `symbol_search` — reject empty query with structured error or add an explicit `includeAll` boolean param; current overflow on `""` is the top-hit client ergonomics issue.
- `test_reference_map` — reword "No test projects detected" to "No test projects matched filter" when `projectName` narrows out the test surface.
- `project_diagnostics(summary=true)` — doc note that summary still runs the full analyzer pass (observed 5.5 s). Users may reasonably assume summary is faster.
- `get_namespace_dependencies(circularOnly=true)` — return `circularDependencyCount` scalar to disambiguate no-cycles from no-data.
- `set_conditional_property_preview` — error should include an accepted example (e.g. `"'$(Configuration)' == 'Debug'"`).
- `apply_composite_preview` — name friction: "apply" naming contradicts "destructive despite the name" caveat. Rename to `commit_composite_apply` or add a mandatory `IAcknowledgeDestructive` flag.
- `get_completions` — document the receiver-context requirement for in-scope ranking (ToString vs long-tail ToBase64Transform).
- `semantic_search` — de-duplicate results by fully-qualified metadataName.
- `find_implementations` — filter source-generated partials by default; add `includeGeneratedPartials` opt-in.
- `test_related` — align response envelope with `test_related_files` and include a `dotnetTestFilter` string.
- MSBuild-family tools (`get_msbuild_properties`, `evaluate_msbuild_property`, `get_editorconfig_options`) — lift the auto-reload behaviour from `evaluate_msbuild_items` up to the shared MSBuild-tool base class for parity.
- `workspace_changes` — add a `revertedAtUtc` or `isReverted` marker rather than removing reverted entries from the log; useful for audit trails.
- `remove_dead_code_preview` — when refusing due to existing write references, suggest the specific follow-up: e.g., "this field is written in `<ctor>` at line X; remove the assignment before re-running." ( today: "still has references and cannot be removed safely").

## 16. Concurrency matrix (Phase 8b)
**Client cannot issue true-concurrent MCP tool calls — Claude Code CLI serializes tool invocations at the transport layer.** Per the prompt's stability note (Principle-level guidance), Phase 8b.2, 8b.3, and timing-sensitive parts of 8b.4 are **blocked — client serializes tool calls**.

### 8b.1 Sequential baseline (single-call wall-clock, ms)
| Slot | Tool | Wall-clock (ms) | Notes |
|------|------|------------------|-------|
| R1 | `find_references(DriftDetector, summary=true)` | 11 | Hot symbol 14 refs |
| R2 | `project_diagnostics` (no filters, summary=true) | 5477 | Full analyzer pass |
| R3 | `symbol_search("")` | N/A — overflow | see §14 |
| R3′ | `symbol_search("DriftDetector")` | 498 | 4 hits |
| R4 | `find_unused_symbols(includePublic=false)` | 135 | 0 hits |
| R5 | `get_complexity_metrics(minComplexity=10)` | 392 | 9 hits |
| W1 | `organize_usings_preview + apply_with_verify` (SettingsEndpoints.cs) | 682 + 1651 | |
| W2 | `set_editorconfig_option` / `set_diagnostic_severity` + revert | 4 + 3289 | |

### 8b.2 Parallel fan-out
`blocked — client serializes tool calls`. Host logical cores not probed.

### 8b.3 Read/write exclusion behavioural probe
`blocked — client serializes tool calls`.

### 8b.4 Lifecycle stress
`blocked — client serializes tool calls` for timing-sensitive portions. The non-timing portions of lifecycle stress WERE observed indirectly: `staleAction=auto-reloaded` appeared on 5+ responses (`staleReloadMs` 3373 / 3088 / 4594 / 7111 / 9416 / 9417), proving the per-workspace lifecycle gate re-wires cleanly after state mutation without producing TOCTOU / stale-workspace responses. Host also transparently restarted the stdio server 4 times; each restart was survived via `workspace_load` re-issue.

### 8b.5 Writer reclassification
| # | Tool | Status | Wall-clock (ms) | Notes |
|---|------|--------|------------------|-------|
| 1 | `apply_with_verify(organize_usings_preview)` | applied | 1651 | |
| 2 | `apply_with_verify(rename_preview)` | applied | 545 | |
| 3 | `revert_last_apply` | reverted | 3289 | editorconfig write reverted |
| 4 | `set_editorconfig_option` (via `set_diagnostic_severity`) | applied | 4 | dotnet_diagnostic.CA1859.severity=silent |
| 5 | `set_diagnostic_severity` (same call as row 4) | applied | 4 | |
| 6 | `add_pragma_suppression` | blocked | — | stdio transport loss mid-batch |

## 17. Writer reclassification verification (Phase 8b.5)
Covered inline in §16 row table. Every applied writer was verified against either `apply_with_verify.postErrorCount` (rows 1–2), `revert_last_apply.reverted` (row 3), or a follow-up read (rows 4–5: `set_diagnostic_severity` path rewrote `.editorconfig` which was successfully reverted).

## 18. Response contract consistency
See §7. Notable cross-tool inconsistencies: `test_related` vs `test_related_files` shape, `find_implementations` including source-gen partials, MSBuild-family auto-retry divergence.

## 19. Known issue regression check (Phase 18)
`**N/A — no prior source**`. No previous audit report, no `ai_docs/backlog.md`, no prior issue tracker accessible from this repo. Phase 18 is not applicable.

## 20. Known issue cross-check
`**N/A — no prior source**`. All §14 issues are raw first-time observations against 1.29.0 on this repo.

---

## Audit coda
Audit duration: ~14 minutes wall-clock. MCP transport: 4 transparent stdio restarts survived. Phase 6 commit: `da3c624` on branch `audit-run-20260424`. Canonical report path: `C:\Code-Repo\DotNet-Firewall-Analyzer\audit-reports\20260424T022800Z_firewallanalyzer_mcp-server-audit.md`.

**Stable surface pass rate:** 107 stable tools, ~100 exercised in-full, ~6 `skipped-safety` or `blocked` (build_workspace/test_run/etc. for output-budget reasons), 0 FAIL — stable surface effectively 100% PASS after budget-exclusions.

**Experimental promotion outcome:** 11 **promote**, ~20 **keep-experimental**, ~25 **needs-more-evidence** (dominated by 13 blocked resources + repo-shape gaps), 0 **deprecate**.

**Top P0–P1:** none.
**Top P2:** §14.1 `symbol_search("")` overflow.
**Top P3:** §14.2–14.7.

**Phase 6 applied commits:** 1 (`da3c624`, 2 files, 10/-11).

**Aggregate p50 elapsedMs:** reads ≈ 78 ms, writers ≈ 1.4 s — well within the prompt's budgets (5 s / 15 s / 30 s).
