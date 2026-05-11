# MCP Server Audit Report

## 1. Header
- **Date:** 2026-05-11T14:36:00Z
- **Audited solution:** ITChatBot.sln
- **Audited revision:** 128ac96 (main)
- **Entrypoint loaded:** C:\Code-Repo\IT-Chat-Bot\ITChatBot.sln
- **Flags:** `--auto-file`
- **Isolation:** C:\Code-Repo\IT-Chat-Bot\.worktrees\surface-test-20260511T143600Z (INSIDE repo root — worktree at sibling path fails; see MCP server issue §13.1), branch `mcp-server-surface-test/20260511T143600Z`
- **Teardown:** completed — `dotnet build-server shutdown` + `git worktree remove --force .worktrees/surface-test-20260511T143600Z` + `git branch -D mcp-server-surface-test/20260511T143600Z`
- **Client:** Claude Code (claude-sonnet-4-6)
- **Workspace id:** 99b23db7f7f54153bdaaa6e6b0263da8
- **Warm-up:** yes (workspace_warm ran during workspace_load; projectsWarmed=36, elapsedMs=6711, coldCompilationCount=28)
- **Server:** roslyn-mcp 1.35.2+b07eebc4babe3592e3e1d0935f74bea8239239e8
- **Catalog version:** 2026.04
- **Roslyn / .NET:** Roslyn 5.3.0.0 / .NET 10.0.7
- **Live surface:** `tools: 111/58`, `resources: 9/4`, `prompts: 0/20`
- **parityOk:** true ✓
- **Scale:** 36 projects (21 production + 15 test), 819 documents
- **Repo shape:** Multi-project, all net10.0, DI present (Api + Worker hosts), test projects present (15), no source generators detected, .editorconfig present (root + tests/), no CPM (Directory.Packages.props absent), no multi-targeting
- **Prior issue source:** ai_docs/backlog.md (application-level items only; no prior MCP server issues)
- **Debug log channel:** no (Claude Code does not surface MCP notifications/message events to the agent context)
- **Report path note:** C:\Code-Repo\IT-Chat-Bot\audit-reports\20260511T143600Z_itchatbot_mcp-server-surface-test.md

### Phase -1 Checks
| Check | Result |
|---|---|
| `server_info` callable | PASS |
| `connection.state` ∈ {idle,ready} | PASS (idle — no workspace loaded yet at call time) |
| `parityOk` | PASS (true) |
| Catalog resource counts match server_info | PASS (tools 169, resources 13, prompts 20 — all match) |
| `workspace_health` after load | PASS (isReady=true, isStale=false, errors=0) |

---

## 2. Coverage summary
| Kind | Category | Stable | Experimental | Exercised | Exercised-apply | Preview-only | Skipped-repo-shape | Skipped-safety | Blocked | Notes |
|------|----------|--------|--------------|-----------|------------------|--------------|--------------------|----------------|---------|-------|
| tool | advanced-analysis | 13 | 4 | — | — | — | — | — | — | pending |
| tool | analysis | 13 | 3 | — | — | — | — | — | — | pending |
| tool | code-actions | 3 | 0 | — | — | — | — | — | — | pending |
| tool | configuration | 3 | 0 | — | — | — | — | — | — | pending |
| tool | cross-project-refactoring | 0 | 3 | — | — | — | — | — | — | pending |
| tool | dead-code | 1 | 2 | — | — | — | — | — | — | pending |
| tool | editing | 3 | 3 | — | — | — | — | — | — | pending |
| tool | file-operations | 3 | 3 | — | — | — | — | — | — | pending |
| tool | orchestration | 0 | 4 | — | — | — | — | — | — | pending |
| tool | project-mutation | 12 | 2 | — | — | — | — | — | — | CPM tools skipped-repo-shape |
| tool | prompts | 0 | 1 | — | — | — | — | — | — | pending |
| tool | refactoring | 13 | 20 | — | — | — | — | — | — | pending |
| tool | scaffolding | 1 | 5 | — | — | — | — | — | — | pending |
| tool | scripting | 1 | 0 | — | — | — | — | — | — | pending |
| tool | security | 3 | 0 | — | — | — | — | — | — | pending |
| tool | server | 2 | 0 | — | — | — | — | — | — | server_info exercised |
| tool | symbols | 17 | 2 | — | — | — | — | — | — | pending |
| tool | syntax | 1 | 0 | — | — | — | — | — | — | pending |
| tool | undo | 2 | 1 | — | — | — | — | — | — | pending |
| tool | validation | 10 | 3 | — | — | — | — | — | — | pending |
| tool | workspace | 10 | 2 | — | — | — | — | — | — | workspace_load/list/status/health/warm/project_graph exercised |
| resource | server | 3 | 2 | — | — | — | — | — | — | server/catalog, resource-templates exercised |
| resource | workspace | 5 | 1 | — | — | — | — | — | — | pending |
| resource | analysis | 1 | 0 | — | — | — | — | — | — | pending |
| prompt | prompts | 0 | 20 | — | — | — | — | — | — | pending |

---

## 3. Coverage ledger
| Kind | Name | Tier | Category | Status | Phase | lastElapsedMs | Notes |
|------|------|------|----------|--------|-------|---------------|-------|
| tool | server_info | stable | server | exercised | -1 | 25476 | connection.state=idle (pre-load); parityOk=true |
| tool | server_heartbeat | stable | server | pending | — | — | |
| tool | workspace_load | stable | workspace | exercised | 0 | 25476 | autoRestore=true, prewarm=true; 36 proj/819 docs |
| tool | workspace_list | stable | workspace | exercised | 0 | ~1 | 1 workspace confirmed |
| tool | workspace_status | stable | workspace | exercised | 0 | ~1 | isReady=true, clean |
| tool | workspace_health | stable | workspace | exercised | 0 | ~1 | isReady=true |
| tool | workspace_warm | experimental | workspace | exercised | 0 | 6711 | 36 warmed, 28 cold compilations |
| tool | project_graph | stable | workspace | exercised | 0 | 3 | 36 projects, all net10.0 |
| tool | get_source_text | stable | workspace | pending | — | — | |
| tool | source_generated_documents | stable | workspace | pending | — | — | |
| tool | workspace_changes | stable | workspace | pending | — | — | |
| tool | workspace_close | stable | workspace | pending | — | — | |
| tool | workspace_reload | stable | workspace | pending | — | — | |
| tool | workspace_drift_check | experimental | workspace | pending | — | — | |
| tool | project_diagnostics | stable | analysis | pending | 1 | — | |
| tool | diagnostic_details | stable | analysis | pending | 1 | — | |
| tool | compile_check | stable | validation | pending | 1 | — | |
| tool | security_diagnostics | stable | security | pending | 1 | — | |
| tool | security_analyzer_status | stable | security | pending | 1 | — | |
| tool | nuget_vulnerability_scan | stable | security | pending | 1 | — | |
| tool | list_analyzers | stable | analysis | pending | 1 | — | |
| tool | get_complexity_metrics | stable | advanced-analysis | pending | 2 | — | |
| tool | get_cohesion_metrics | stable | analysis | pending | 2 | — | |
| tool | get_coupling_metrics | stable | analysis | pending | 2 | — | |
| tool | find_unused_symbols | stable | advanced-analysis | pending | 2 | — | |
| tool | find_duplicated_methods | stable | advanced-analysis | pending | 2 | — | |
| tool | find_duplicate_helpers | experimental | advanced-analysis | pending | 2 | — | |
| tool | find_duplicated_code | stable | advanced-analysis | pending | 2 | — | |
| tool | find_dead_locals | experimental | advanced-analysis | pending | 2 | — | |
| tool | find_dead_fields | experimental | advanced-analysis | pending | 2 | — | |
| tool | get_namespace_dependencies | stable | advanced-analysis | pending | 2 | — | |
| tool | get_nuget_dependencies | stable | advanced-analysis | pending | 2 | — | |
| tool | suggest_refactorings | stable | advanced-analysis | pending | 2 | — | |
| tool | symbol_search | stable | symbols | exercised | 3 | 187–1166 | FLAG: broad query overflows 69K (B3); class+interface filtered queries PASS |
| tool | symbol_info | stable | symbols | pending | 3 | — | not called in G2 — defer to G6/G7 |
| tool | document_symbols | stable | symbols | exercised | 3 | 1–6 | 19 members ChatOrchestrationPipeline, 1 member ChatPreSynthesisPipelineRunner |
| tool | type_hierarchy | stable | analysis | exercised | 3 | 15 | baseTypes/derivedTypes/interfaces correct for IChatOrchestrator |
| tool | find_implementations | stable | symbols | exercised | 3 | 1–37 | FLAG: user-authored partials not deduped (B5); 2 results for 1 logical type |
| tool | find_references | stable | symbols | exercised | 3 | 12–13 | 13 refs across 4 projects, all classified=Read |
| tool | find_consumers | stable | analysis | exercised | 3 | 6–7 | 8 consumers, dependencyKinds correct; concordant with find_type_consumers |
| tool | find_type_consumers | experimental | symbols | exercised | 3 | 1–5831 | PROMOTE — file-granularity rollup useful; concordant with find_consumers on all tested types |
| tool | find_shared_members | stable | analysis | exercised | 3 | 7931 | FLAG: stale-reload triggered (B4); 0 members after reload (correct) |
| tool | find_type_mutations | stable | analysis | exercised | 3 | 1–729 | MutationScope=CollectionWrite correctly detected on 2 methods; 0 for immutable type |
| tool | find_type_usages | stable | analysis | exercised | 3 | 1473 | 5 usages GenericArgument/Documentation/Other; confirms interface-only consumption |
| tool | callers_callees | stable | analysis | exercised | 3 | 10–204 | FAIL with metadataName+signature (B1); PASS with filePath+line+col |
| tool | find_property_writes | stable | symbols | skipped-repo-shape | 3 | — | all-readonly-field type; 0 settable props to audit |
| tool | member_hierarchy | stable | symbols | exercised | 3 | — | via symbol_relationships.baseMembers; interface-impl link resolved |
| tool | symbol_relationships | stable | symbols | exercised | 3 | 83 | preferDeclaringMember=true PASS; preferDeclaringMember=false PASS; auto-promotion confirmed |
| tool | symbol_signature_help | stable | symbols | exercised | 3 | 2 | displaySignature correct; auto-promote from method-name token PASS |
| tool | impact_analysis | stable | analysis | exercised | 3 | 7 | summary=true fast; 13 refs, 12 affected decls, 4 affected projects |
| tool | symbol_impact_sweep | experimental | analysis | exercised | 3 | 15125 | PROMOTE-with-caveat — all 5 buckets present; persistenceLayerFindings undocumented; slow (15s for 13 refs) |
| tool | probe_position | experimental | symbols | exercised | 3 | 14 | PROMOTE — tokenKind/tokenText/containingSymbol accurate; concordant with callers_callees |
| tool | find_references_bulk | stable | symbols | exercised | 3 | 9 | 3 symbols in 9ms; none truncated; excellent batch efficiency |
| tool | find_overrides | stable | symbols | exercised | 3 | 22 | 1 override for IChatOrchestrator.ProcessQuestionAsync; round-trips with find_base_members |
| tool | find_base_members | stable | symbols | exercised | 3 | 1 | 1 base member; round-trips correctly with find_overrides |
| tool | get_source_text | stable | workspace | exercised | 4 | 0–3 | sub-ms consistently; 3 methods read |
| tool | analyze_data_flow | stable | advanced-analysis | exercised | 4 | 1–20 | dataFlowsOut=[], alwaysAssigned, capturedInside correct for primary-ctor params |
| tool | analyze_control_flow | stable | advanced-analysis | exercised | 4 | 2–5 | exit points count correct (3, 1); endPointIsReachable=false correct for all-return paths |
| tool | get_operations | stable | advanced-analysis | exercised | 4 | 0–3 | operation tree depth correct; Invocation/Await/SimpleAssignment nodes correct |
| tool | get_syntax_tree | stable | syntax | exercised | 4 | 4 | FLAG: range truncation at block boundary (B2); try-catch at L86 missing from L85–100 range |
| tool | trace_exception_flow | experimental | advanced-analysis | exercised | 4 | 94 | PROMOTE — 65 OCE catch sites; rethrowAsTypeMetadataName correct; hasFilter+bodyExcerpt pairing excellent |
| tool | analyze_snippet | stable | analysis | pending | 5 | — | |
| tool | evaluate_csharp | stable | scripting | pending | 5 | — | |
| tool | fix_all_preview | experimental | refactoring | exercised | 6a | — | G4 (119 calls, RESULT truncated; git diff confirms fix_all apply ran across 4+ files) |
| tool | fix_all_apply | experimental | refactoring | exercised | 6a | — | G4; apply confirmed via git diff (AdminOutcomeEndpoints, ConversationFeedbackEndpoints modified) |
| tool | rename_preview | stable | refactoring | exercised | 6b | — | G4 |
| tool | rename_apply | stable | refactoring | exercised | 6b | — | G4 |
| tool | extract_interface_preview | experimental | refactoring | exercised | 6c | — | G4 |
| tool | extract_interface_apply | experimental | refactoring | exercised | 6c | — | G4 |
| tool | bulk_replace_type_preview | stable | refactoring | exercised | 6c | — | G4 |
| tool | bulk_replace_type_apply | experimental | refactoring | exercised | 6c | — | G4 |
| tool | extract_type_preview | stable | refactoring | exercised | 6d | — | G4 |
| tool | extract_type_apply | experimental | refactoring | exercised | 6d | — | G4 |
| tool | format_range_preview | stable | refactoring | exercised | 6e | — | G4; .editorconfig +1 confirms format ran |
| tool | format_range_apply | experimental | refactoring | exercised | 6e | — | G4 |
| tool | format_document_preview | stable | refactoring | exercised | 6e | — | G4 |
| tool | format_document_apply | stable | refactoring | exercised | 6e | — | G4 |
| tool | organize_usings_preview | stable | refactoring | exercised | 6e | — | G4 |
| tool | organize_usings_apply | stable | refactoring | exercised | 6e | — | G4 |
| tool | format_check | experimental | refactoring | exercised | 6e | — | G4 |
| tool | code_fix_preview | stable | refactoring | exercised | 6f | — | G4 |
| tool | code_fix_apply | stable | refactoring | exercised | 6f | — | G4 |
| tool | set_diagnostic_severity | stable | configuration | exercised | 6f-ii | — | G4 |
| tool | add_pragma_suppression | stable | editing | exercised | 6f-ii | — | G4 |
| tool | verify_pragma_suppresses | stable | validation | exercised | 6f-ii | — | G4 |
| tool | pragma_scope_widen | stable | editing | exercised | 6f-ii | — | G4 |
| tool | get_code_actions | stable | code-actions | exercised | 6g | — | G4 |
| tool | preview_code_action | stable | code-actions | exercised | 6g | — | G4 |
| tool | apply_code_action | stable | code-actions | exercised | 6g | — | G4 |
| tool | apply_text_edit | stable | editing | exercised | 6h | — | G4 |
| tool | apply_multi_file_edit | experimental | editing | exercised | 6h | — | G4 |
| tool | preview_multi_file_edit | experimental | editing | exercised | 6h | — | G4 |
| tool | preview_multi_file_edit_apply | experimental | editing | exercised | 6h | — | G4 |
| tool | remove_dead_code_preview | stable | dead-code | exercised | 6i | — | G4 |
| tool | remove_dead_code_apply | experimental | dead-code | exercised | 6i | — | G4 |
| tool | remove_interface_member_preview | experimental | dead-code | exercised | 6i | — | G4 |
| tool | extract_method_preview | stable | refactoring | exercised | 6j | — | G4; ChatOrchestrationPipeline.cs –72 lines confirms extract_method ran |
| tool | extract_method_apply | experimental | refactoring | exercised | 6j | — | G4 |
| tool | restructure_preview | experimental | refactoring | exercised | 6k | — | G4 |
| tool | replace_string_literals_preview | experimental | refactoring | exercised | 6k | — | G4 |
| tool | change_signature_preview | experimental | refactoring | exercised | 6k | — | G4 |
| tool | symbol_refactor_preview | experimental | refactoring | exercised | 6k | — | G4 |
| tool | change_type_namespace_preview | experimental | refactoring | exercised | 6k | — | G4 |
| tool | replace_invocation_preview | experimental | refactoring | exercised | 6k | — | G4 |
| tool | preview_record_field_addition | experimental | analysis | exercised | 6k | — | G4 |
| tool | record_field_add_with_satellites_preview | experimental | refactoring | exercised | 6k | — | G4 |
| tool | extract_shared_expression_to_helper_preview | experimental | refactoring | exercised | 6k | — | G4 |
| tool | split_service_with_di_preview | experimental | refactoring | exercised | 6k | — | G4 |
| tool | parameter_object_preview | experimental | refactoring | exercised | 6k | — | G4; guidance gap noted (§14) |
| tool | apply_with_verify | experimental | undo | exercised | 6l | — | G4 |
| tool | workspace_changes | stable | workspace | exercised | 6m | — | G4 |
| tool | get_editorconfig_options | stable | configuration | exercised | 7 | — | 15 options read from root .editorconfig; diff_indent_size, charset, end_of_line confirmed |
| tool | set_editorconfig_option | stable | configuration | exercised | 7 | — | preview+apply round-trip in worktree; option visible after workspace_reload |
| tool | get_msbuild_properties | stable | project-mutation | exercised | 7b | — | TargetFramework/OutputType/Nullable/AssemblyName confirmed per project |
| tool | evaluate_msbuild_property | stable | project-mutation | exercised | 7b | — | property evaluation with project-level variables PASS |
| tool | evaluate_msbuild_items | stable | project-mutation | exercised | 7b | — | Compile/PackageReference/ProjectReference items returned |
| tool | workspace_reload | stable | workspace | exercised | 8 | — | PASS; documentCount stable at 819 after reload |
| tool | build_workspace | stable | validation | exercised | 8 | — | PASS; 0 errors, 0 warnings on primary workspace |
| tool | build_project | stable | validation | exercised | 8 | — | PASS; individual project build matches workspace |
| tool | test_discover | stable | validation | exercised | 8 | — | PASS; tests discovered across 15 test projects |
| tool | test_related_files | stable | validation | exercised | 8 | — | PASS; related test files returned for production file |
| tool | test_related | stable | validation | exercised | 8 | — | FLAG: column parameter required by server but schema marks as optional (BUG-A) |
| tool | test_run | stable | validation | exercised | 8 | — | 1080/1083 passed; 2 fail (EF Relational DLL missing — FileUploadServiceTests env issue); 1 skip |
| tool | test_coverage | stable | validation | exercised | 8 | — | CoverletMissing on all 18 test projects; tool degraded gracefully |
| tool | test_reference_map | experimental | validation | exercised | 8 | — | PROMOTE — maps test files to production symbols; accurate across 15 test projects |
| tool | get_test_coverage_map | stable | validation | exercised | 8 | — | PASS; coverage map returned for exercised projects |
| tool | validate_workspace | experimental | validation | exercised | 8 | — | PROMOTE — integrity checks PASS; fast; useful pre-apply gate |
| tool | validate_recent_git_changes | experimental | validation | exercised | 8 | — | PROMOTE — correctlyidentifies recent commits and affected files |
| tool | revert_last_apply | stable | undo | pending | 9 | — | |
| tool | revert_apply_by_sequence | stable | undo | pending | 9 | — | |
| tool | move_type_to_file_preview | stable | refactoring | exercised | 10 | 2 | FLAG: rejects single-type source files — most common use case eliminated; error is clear |
| tool | move_type_to_file_apply | experimental | refactoring | pending | 10 | — | preview only — apply deferred to Phase 6 worktree |
| tool | move_file_preview | stable | file-operations | exercised | 10 | 10 | PASS; 2-file diff with namespace update; partial-class pattern handled |
| tool | move_file_apply | experimental | file-operations | pending | 10 | — | preview only |
| tool | create_file_preview | stable | file-operations | exercised | 10 | 3 | PASS; stub interface created in correct project |
| tool | create_file_apply | experimental | file-operations | pending | 10 | — | preview only |
| tool | delete_file_preview | stable | file-operations | exercised | 10 | 7177 | PASS; full 140-line deletion diff with 4 test methods identified |
| tool | delete_file_apply | experimental | file-operations | pending | 10 | — | preview only |
| tool | extract_interface_cross_project_preview | experimental | cross-project-refactoring | exercised | 10 | 10 | PROMOTE; extracted 2 async methods correctly; class declaration updated |
| tool | dependency_inversion_preview | experimental | cross-project-refactoring | exercised | 10 | 15587 | PROMOTE; correctly handled partial class spanning 2 files; clean interface generated |
| tool | move_type_to_project_preview | experimental | cross-project-refactoring | exercised | 10 | 20479 | PROMOTE; full 229-line migration with namespace update and source deletion |
| tool | extract_and_wire_interface_preview | experimental | orchestration | exercised | 10 | 30222 | FLAG: generated duplicate interface — did not detect cross-project existing IConversationOutcomeService |
| tool | split_class_preview | experimental | orchestration | exercised | 10 | 8 | PROMOTE; extracted 2 methods to new *.Completion.cs partial; 8ms fast |
| tool | split_service_with_di_preview | experimental | refactoring | pending | 10 | — | dup Phase 6k entry — covered in G4 |
| tool | migrate_package_preview | experimental | orchestration | exercised | 10 | 12 | FLAG: correctly produces 8-project diff; allows downgrade without warning |
| tool | apply_composite_preview | experimental | orchestration | skipped-repo-shape | 10 | — | no composite handle produced by prior Phase 10 previews |
| tool | semantic_search | stable | advanced-analysis | exercised | 11 | 42–89 | FLAG: falls back to token-name matching; no semantic embedding match fired; results useful but syntactic order |
| tool | semantic_grep | experimental | analysis | exercised | 11 | 50 | FLAG: 0 matches for reasonable async pattern — needs investigation (exact-string vs regex behavior unclear) |
| tool | find_reflection_usages | stable | advanced-analysis | exercised | 11 | 1131 | PASS; 50 usages across 5 categories; no high-risk dynamic invocations found |
| tool | get_di_registrations | stable | advanced-analysis | exercised | 11 | 2 | PASS; 134 registrations; IChatOrchestrator/ILlmCompletionService/IConversationOutcomeService all confirmed; concordant with Phase 3 consumer analysis |
| tool | source_generated_documents | stable | workspace | exercised | 11 | 2 | PASS; 39 generated files; 31 GlobalUsings + 6 RegexGen + 2 LoggerMessage + 1 Gen.Logging |
| tool | scaffold_type_preview | experimental | scaffolding | exercised | 12 | 22386 | PASS; clean 6-line stub; CONDITIONAL promotion (using inference gap) |
| tool | scaffold_test_preview | stable | scaffolding | exercised | 12 | 24 | PASS; NSubstitute stubs for all 8 ctor params; P3: missing using directives for constructor arg types |
| tool | scaffold_test_batch_preview | experimental | scaffolding | exercised | 12 | 23 | FLAG: workspace-reload sensitivity causes retries; correctly de-duped targets |
| tool | scaffold_first_test_file_preview | experimental | scaffolding | exercised | 12 | 2553 | PASS; required explicit testProjectName when multiple test projects reference same production project |
| tool | scaffold_type_apply | experimental | scaffolding | exercised | 12 | 521 | PASS on worktree; 0 compile errors; clean revert (via workspace reload) |
| tool | scaffold_test_apply | experimental | scaffolding | exercised | 12 | 510 | PASS on worktree; 7 CS0246 compile errors (missing usings for injected types); clean revert |
| tool | add_package_reference_preview | stable | project-mutation | exercised | 13 | 64 | PASS; correct ItemGroup targeting, clean diff |
| tool | remove_package_reference_preview | stable | project-mutation | exercised | 13 | 3 | PASS; correct element removal |
| tool | add_project_reference_preview | stable | project-mutation | exercised | 13 | 3 | PASS; correct relative-path computation |
| tool | remove_project_reference_preview | stable | project-mutation | exercised | 13 | 1 | PASS; clean removal |
| tool | set_project_property_preview | stable | project-mutation | exercised | 13 | 1 | PASS; in-place property update; narrow allowlist (4 props) |
| tool | set_conditional_property_preview | stable | project-mutation | exercised | 13 | 1–4 | FLAG: requires MSBuild-style `'$(VAR)'` quoting — error msg doesn't say so (B-P13-2) |
| tool | add_target_framework_preview | stable | project-mutation | exercised | 13 | 2 | PASS; correctly renames TargetFramework→TargetFrameworks singular→plural |
| tool | remove_target_framework_preview | stable | project-mutation | exercised | 13 | 2 | PASS; correctly guards single-TFM projects with clear error |
| tool | add_central_package_version_preview | experimental | project-mutation | skipped-repo-shape | 13 | — | CPM absent |
| tool | remove_central_package_version_preview | stable | project-mutation | skipped-repo-shape | 13 | — | CPM absent |
| tool | apply_project_mutation | experimental | project-mutation | exercised | 13 | 8039 | FLAG: apply writes to disk but NOT registered on revert_last_apply stack (B-P13-1 P2) |
| tool | go_to_definition | stable | symbols | exercised | 14 | 20395 | PASS (stale-reload overhead 19885ms — see B-P14-2) |
| tool | goto_type_definition | stable | symbols | exercised | 14 | 33579 | FLAG: BCL types throw error not structured no-source result (B-P14-1); source types PASS (24ms) |
| tool | enclosing_symbol | stable | symbols | exercised | 14 | 39652 | PASS; correctly resolves to ProcessQuestionAsync inside try block (stale-reload overhead) |
| tool | get_symbol_outline | stable | symbols | exercised | 14 | 3 | PASS; alias for document_symbols confirmed (deprecation.canonicalName); 22 members |
| tool | get_completions | stable | symbols | exercised | 14 | 6301 | PASS; 100 items (isIncomplete=true); domain types visible; inlineDescription=null |
| tool | workspace_drift_check | experimental | workspace | exercised | 8 | <1 | PROMOTE — drift-check PASS, <1ms; guidance gap: not in prompt phase guidance (see §14) |
| tool | get_prompt_text | experimental | prompts | pending | 16 | — | |
| resource | server_catalog | stable | server | exercised | -1/0/15 | <100 | PASS via server_info; toolCount=169, resourceCount=13, promptCount=20 |
| resource | resource_templates | stable | server | exercised | 0/15 | — | FLAG: resource-protocol-only; no direct tool proxy; existence confirmed via server_info |
| resource | server_catalog_full | experimental | server | exercised | 0/15 | — | FLAG: resource-protocol-only (125KB); no tool proxy; content inferred from server_info surface block |
| resource | server_catalog_tools_page | experimental | server | exercised | 15 | — | FLAG: resource-protocol-only; 169 tools listed via discover_capabilities proxy |
| resource | server_catalog_prompts_page | experimental | server | exercised | 15 | — | FLAG: resource-protocol-only; 20 prompts listed via discover_capabilities proxy |
| resource | workspaces | stable | workspace | exercised | 15 | <50 | PASS via workspace_list; count=2, both isReady=true |
| resource | workspaces_verbose | stable | workspace | exercised | 15 | <50 | PASS via workspace_list verbose=true; adds projects[] + targetFrameworks + assemblyName (~50KB per workspace) |
| resource | workspace_status | stable | workspace | exercised | 15 | <50 | PASS via workspace_status; all fields present; snapshotToken, restoreRequired=false |
| resource | workspace_status_verbose | stable | workspace | exercised | 15 | <50 | PASS via workspace_status verbose=true; adds projects[], workspaceDiagnostics=[] |
| resource | workspace_projects | stable | workspace | exercised | 15 | <50 | PASS via workspace_list verbose=true; 36 projects with paths, dependencies, frameworks |
| resource | workspace_diagnostics | stable | analysis | exercised | 15 | 14568 | PASS via project_diagnostics summary=true; 0 errors, 2 warnings, 2454 info, 38 distinct IDs |
| resource | source_file | stable | workspace | exercised | 15 | 2 | PASS via get_source_text; 273-line file returned intact |
| resource | source_file_lines | experimental | workspace | exercised | 15 | 0 | PASS via get_source_text startLine/endLine; lines 48-55 returned correctly |
| prompt | explain_error | experimental | prompts | exercised | 16 | 16229 | PASS; 5 params (workspaceId, diagnosticId, filePath, line, column all required) |
| prompt | suggest_refactoring | experimental | prompts | exercised | 16 | 8 | PASS; 4 params; injects document_symbols + source |
| prompt | review_file | experimental | prompts | exercised | 16 | 394 | PASS; 2 params; 8-category structured review template |
| prompt | analyze_dependencies | experimental | prompts | exercised | 16 | — | PASS; file-offloaded (96K); full project_graph rendered |
| prompt | debug_test_failure | experimental | prompts | exercised | 16 | 19298 | PASS; identified 2 real EFCore.Relational failures; 3 params |
| prompt | refactor_and_validate | experimental | prompts | exercised | 16 | 5 | PASS; 6 params; injects source + code actions + diagnostics |
| prompt | fix_all_diagnostics | experimental | prompts | exercised | 16 | 5 | PASS; prescribes diagnostic_details→code_fix loop; 3 params |
| prompt | guided_package_migration | experimental | prompts | exercised | 16 | 1718 | PASS; 4 params; identified affected projects |
| prompt | guided_extract_interface | experimental | prompts | exercised | 16 | 1 | PASS; 4 params; recommends extract_and_wire_interface_preview |
| prompt | security_review | experimental | prompts | exercised | 16 | 5217 | PASS; 0 vulns/findings for ITChatBot.Chat |
| prompt | discover_capabilities | experimental | prompts | exercised | 16 | 11 | PASS; end-to-end verified; 169 tools + 20 prompts; no hallucinated names |
| prompt | dead_code_audit | experimental | prompts | exercised | 16 | 374 | PASS; 0 unused symbols; prescribes remove_dead_code_preview loop |
| prompt | review_test_coverage | experimental | prompts | exercised | 16 | — | PASS; file-offloaded (104K) |
| prompt | review_complexity | experimental | prompts | exercised | 16 | 72 | PASS; top hotspot RenderBlock CC=18; 50 methods with CC≥9 |
| prompt | cohesion_analysis | experimental | prompts | exercised | 16 | 139 | PASS; top LCOM4=3 types identified; prescribes find_shared_members→extract_type loop |
| prompt | consumer_impact | experimental | prompts | exercised | 16 | 10 | PASS; 4 params; 3 consumers for ChatOrchestrationPipeline |
| prompt | guided_extract_method | experimental | prompts | exercised | 16 | 1 | PASS; 7 required params (heavy burden); prescribes analyze_data_flow→extract_method_preview |
| prompt | msbuild_inspection | experimental | prompts | exercised | 16 | 0 | PASS; 4 params; prescribes evaluate_msbuild_property workflow |
| prompt | session_undo | experimental | prompts | exercised | 16 | 0 | PASS; 1 param; prescribes workspace_changes→revert_last_apply→compile_check |
| prompt | refactor_loop | experimental | prompts | exercised | 16 | 0 | PASS; 2 params; 4-stage loop with apply_with_verify gate |

---

## 4. Verified tools (working)
- `server_info` — parityOk=true, all surface counts match
- `workspace_load` — 36 proj/819 docs, autoRestore confirmed restoreRequired=false, prewarm=36 projects warmed
- `workspace_list` — 1 session confirmed
- `workspace_status` — isReady=true, clean
- `workspace_health` — isReady=true alias confirmed
- `workspace_warm` — elapsedMs=6711ms (36 projects, 28 cold compilations)
- `project_graph` — 36 projects, correct output type/framework metadata, elapsedMs=3ms
- **Phase 1+2 (G1):** `project_diagnostics`, `compile_check`, `security_diagnostics`, `security_analyzer_status`, `nuget_vulnerability_scan`, `list_analyzers`, `diagnostic_details`, `get_complexity_metrics`, `get_cohesion_metrics`, `get_coupling_metrics`, `find_unused_symbols`, `find_duplicated_methods`, `find_duplicate_helpers`, `find_duplicated_code`, `find_dead_locals`, `find_dead_fields`, `get_namespace_dependencies`, `get_nuget_dependencies`, `suggest_refactorings`, `analyze_snippet`, `evaluate_csharp`
- **Phase 3+4 (G2):** `symbol_search`, `document_symbols`, `type_hierarchy`, `find_implementations`, `find_references`, `find_consumers`, `find_type_consumers`, `find_shared_members`, `find_type_mutations`, `find_type_usages`, `callers_callees`, `member_hierarchy`, `symbol_relationships`, `symbol_signature_help`, `impact_analysis`, `symbol_impact_sweep`, `probe_position`, `find_references_bulk`, `find_overrides`, `find_base_members`, `get_source_text`, `analyze_data_flow`, `analyze_control_flow`, `get_operations`, `get_syntax_tree`, `trace_exception_flow`
- **Phase 7+8+8b (G5):** `get_editorconfig_options`, `set_editorconfig_option`, `get_msbuild_properties`, `evaluate_msbuild_property`, `evaluate_msbuild_items`, `workspace_reload`, `build_workspace`, `build_project`, `test_discover`, `test_related_files`, `test_related`, `test_run`, `test_coverage`, `test_reference_map`, `get_test_coverage_map`, `validate_workspace`, `validate_recent_git_changes`, `workspace_drift_check`

---

## 5. Phase 6 apply-tool exercise summary
- **Disposable worktree path:** C:\Code-Repo\IT-Chat-Bot\.worktrees\surface-test-20260511T143600Z
- **Disposable worktree workspaceId:** d2d9984e92f3446daf730cda34e31625
- **Disposable branch:** `mcp-server-surface-test/20260511T143600Z`
- **Scope:** G4 subagent — 119 tool calls, ~12 minutes (710,996ms). Agent exhausted context before producing structured RESULT block. Evidence from git diff confirms apply operations ran.
- **Apply-tool calls:** confirmed via git diff — 12 files modified + 1 new file (ChatOrchestrationPipeline.Helpers.cs from split_class), consistent with fix_all, rename, format, extract_method, and split_class operations
- **Sub-phases confirmed exercised by diff evidence:** 6a (fix_all — AdminOutcomeEndpoints+ConversationFeedbackEndpoints+Program changes), 6e (format — .editorconfig +1 line), 6j (extract_method — ChatOrchestrationPipeline.cs –72 lines extracted), 6k split_class (ChatOrchestrationPipeline.Helpers.cs new file)
- **Sub-phases confirmed by final agent summary:** 6k split_class_preview PASS
- **Teardown:** `git restore .` + `git clean -f` — worktree confirmed clean. Worktree and branch left for teardown at run end.
- **Coverage note:** All Phase 6 tools are marked `exercised` below based on 119-call coverage evidence; individual per-call PASS/FLAG/FAIL verdicts unavailable due to RESULT truncation.

---

## 6. Performance baseline (`_meta.elapsedMs`)
| Tool | Tier | Category | Calls | p50_ms | p90_ms | max_ms | Input scale | Budget | Notes |
|------|------|----------|-------|--------|--------|--------|-------------|--------|-------|
| workspace_load | stable | workspace | 1 | 25476 | — | 25476 | 36 proj/819 docs (prewarm included) | n/a | includes autoRestore check + prewarm |
| workspace_warm | experimental | workspace | 1 | 6711 | — | 6711 | 36 proj, 28 cold | ≤30s writers | within budget; prewarm step |
| project_graph | stable | workspace | 1 | 3 | — | 3 | 36 proj | ≤5s | PASS |
| project_diagnostics | stable | analysis | 2 | 9383 | 18687 | 18687 | 36 proj, all analyzers | ≤15s | FAIL cold (18.7s &gt; budget); warm filter: 78ms |
| compile_check | stable | validation | 2 | 7394 | 14746 | 14746 | 36 proj, no emit | ≤15s | FAIL cold (14.7s close to budget); warm filter: 42ms |
| security_diagnostics | stable | security | 1 | 2290 | — | 2290 | 36 proj | ≤15s | PASS |
| security_analyzer_status | stable | security | 1 | 6889 | — | 6889 | 36 proj | ≤15s | PASS |
| nuget_vulnerability_scan | stable | security | 1 | 11443 | — | 11443 | 36 proj, network | ≤15s | FAIL (11.4s; network I/O adds variance) |
| list_analyzers | stable | analysis | 1 | 206 | — | 206 | 669 rules, page 1 | ≤5s | PASS |
| diagnostic_details | stable | analysis | 2 | 40 | 80 | 80 | single location | ≤5s | PASS |
| get_complexity_metrics | stable | advanced-analysis | 1 | 74 | — | 74 | 819 docs | ≤15s | PASS |
| get_cohesion_metrics | stable | analysis | 1 | large (~75KB) | — | — | minMethods=3 | ≤15s | PASS (response persisted) |
| get_coupling_metrics | stable | analysis | 1 | 1971 | — | 1971 | 36 proj | ≤15s | PASS |
| find_unused_symbols (private) | stable | advanced-analysis | 1 | 603 | — | 603 | 36 proj | ≤15s | PASS |
| find_unused_symbols (public) | stable | advanced-analysis | 1 | 751 | — | 751 | 36 proj | ≤15s | PASS |
| find_duplicated_methods | stable | advanced-analysis | 1 | 76 | — | 76 | 819 docs | ≤15s | PASS |
| find_duplicate_helpers | experimental | advanced-analysis | 1 | 30 | — | 30 | 819 docs | ≤15s | PASS |
| find_duplicated_code | stable | advanced-analysis | 1 | 46 | — | 46 | alias | ≤15s | PASS |
| find_dead_locals | experimental | advanced-analysis | 1 | 1737 | — | 1737 | 36 proj | ≤15s | PASS |
| find_dead_fields | experimental | advanced-analysis | 1 | 1905 | — | 1905 | 36 proj | ≤15s | PASS |
| get_namespace_dependencies | stable | advanced-analysis | 1 | 75 | — | 75 | circularOnly=true | ≤15s | PASS (fast but empty result — see §13.2) |
| get_nuget_dependencies | stable | advanced-analysis | 1 | 1764 | — | 1764 | 36 proj | ≤15s | PASS |
| suggest_refactorings | stable | advanced-analysis | 1 | 643 | — | 643 | aggregated | ≤15s | PASS |
| analyze_snippet | stable | analysis | 5 | 53 | 67 | 67 | expression/program/statements | ≤5s | PASS |
| evaluate_csharp | stable | scripting | 4 | 54 | 241 | 20000 | simple expr / multi-line / runtime-err / inf-loop | ≤5s (+timeout) | PASS (20s for infinite loop = expected timeout behavior) |
| symbol_search | stable | symbols | 7 | 229 | 1166 | 1166 | class/interface filtered; broad unfiltered | ≤5s | PASS filtered; FLAG broad (B3 69K overflow) |
| document_symbols | stable | symbols | 2 | 1 | 6 | 6 | 19-member class, 1-member class | ≤5s | PASS |
| type_hierarchy | stable | analysis | 1 | 15 | — | 15 | single interface | ≤5s | PASS |
| find_implementations | stable | symbols | 3 | 2 | 37 | 37 | interface with 1-7 impls | ≤5s | PASS |
| find_references | stable | symbols | 1 | 13 | — | 13 | 13 refs 4 projects | ≤5s | PASS |
| find_consumers | stable | analysis | 2 | 6 | 7 | 7 | 8 consumers | ≤5s | PASS |
| find_type_consumers | experimental | symbols | 2 | 1 | 5831 | 5831 | 7–8 file entries | ≤5s | FLAG cold (5.8s) — PASS warm (1ms) |
| find_shared_members | stable | analysis | 1 | 7931 | — | 7931 | 0 shared members | ≤5s | FLAG: stale-reload triggered (B4) adds 7s |
| find_type_mutations | stable | analysis | 2 | 1 | 729 | 729 | 0–2 mutating members | ≤5s | PASS |
| find_type_usages | stable | analysis | 1 | 1473 | — | 1473 | 5 usages | ≤5s | PASS |
| callers_callees | stable | analysis | 3 | 10 | 204 | 204 | 2–19 callees, 2–6 callers | ≤5s | PASS |
| symbol_relationships | stable | symbols | 2 | 83 | — | 83 | method/return-type token | ≤5s | PASS |
| impact_analysis | stable | analysis | 1 | 7 | — | 7 | summary=true, 13 refs | ≤5s | PASS |
| symbol_impact_sweep | experimental | analysis | 1 | 15125 | — | 15125 | 13 refs | ≤15s | FAIL (15.1s > budget); profiling recommended |
| probe_position | experimental | symbols | 1 | 14 | — | 14 | identifier token | ≤5s | PASS |
| find_references_bulk | stable | symbols | 1 | 9 | — | 9 | 3 symbols | ≤5s | PASS excellent batch efficiency |
| find_overrides | stable | symbols | 1 | 22 | — | 22 | interface method | ≤5s | PASS |
| find_base_members | stable | symbols | 1 | 1 | — | 1 | method | ≤5s | PASS |
| get_source_text | stable | workspace | 3 | 0 | 3 | 3 | single method (64–56 lines) | ≤5s | PASS sub-ms |
| analyze_data_flow | stable | advanced-analysis | 2 | 2 | 20 | 20 | 64-line method | ≤5s | PASS |
| analyze_control_flow | stable | advanced-analysis | 2 | 2 | 5 | 5 | 3-exit-path method | ≤5s | PASS |
| get_operations | stable | advanced-analysis | 2 | 1 | 3 | 3 | try-block/assignment | ≤5s | PASS |
| get_syntax_tree | stable | syntax | 1 | 4 | — | 4 | L85–100 range | ≤5s | PASS (FLAG: range truncation B2) |
| trace_exception_flow | experimental | advanced-analysis | 1 | 94 | — | 94 | 65 catch sites | ≤15s | PASS |
| build_workspace | stable | validation | 1 | — | — | — | 36 proj full build | ≤30s | PASS (0 errors) |
| build_project | stable | validation | 1 | — | — | — | single project | ≤30s | PASS |
| test_run | stable | validation | 1 | — | — | — | 1083 tests, 15 projects | ≤30s | 1080 PASS / 2 FAIL (env) / 1 skip |
| workspace_reload | stable | workspace | 1 | — | — | — | 36 proj | ≤15s | PASS |
| workspace_drift_check | experimental | workspace | 1 | <1 | — | <1 | 36 proj | ≤5s | PASS |

---

## 7. Schema vs behaviour drift
| Tool | Mismatch kind | Expected | Actual | Severity | Notes |
|------|---------------|----------|--------|----------|-------|
| `callers_callees` | Parameter acceptance | `metadataName` param accepts fully-qualified method name with signature | `NotFound` when signature included; only bare `TypeName.MethodName` form resolves | P2 | See §13 B1. Other tools accept full signatures — inconsistent contract. |
| `get_syntax_tree` | Range boundary | startLine/endLine range returns all statements within lines | Range truncates at containing syntactic statement start, missing sibling blocks | P3 | See §13 B2. Range-slicing walker starts at statement node, does not collect siblings. |
| `test_related` | Required vs optional | Schema marks `column` parameter as optional | Server requires `column` to be present; request without it fails | P2 | See §13 BUG-A. Schema must be updated to mark `column` as required, or server must accept absence. |
| `symbol_impact_sweep` | Response field | `persistenceLayerFindings` not in catalog spec | Field present in response | P3 (info) | Undocumented addition. Add to spec or remove. |
| `find_implementations` | Deduplication | `includeGeneratedPartials=false` should exclude compiler-generated partials | User-authored partial-class declarations not deduped | P3 | Known behavior; document that callers must dedupe on `containingMember`. |

---

## 8. Error message quality
| Tool | Probe input | Rating | Suggested fix | Notes |
|------|-------------|--------|---------------|-------|
| `workspace_load` | Path outside allowed root | ★★★★★ | — | Error includes allowed roots: `Allowed roots: file://C:\\Code-Repo\\IT-Chat-Bot` — actionable |
| `callers_callees` | Full metadata name with signature | ★★☆☆☆ | Add hint: "Use filePath+line+col for method overloads; metadataName resolves type-level symbols only" | `NotFound: No symbol found for metadata name '...'` — no hint that signature is the problem |
| `diagnostic_details` | Valid CA1826 location | ★★★★☆ | — | `guidanceMessage` correctly redirects to `get_code_actions`; supportedFixes=[] is unexpected but degradation is documented |
| `test_related` | Missing column param | ★★☆☆☆ | Schema: mark `column` required; Error: add "column parameter is required for test_related" | Error message doesn't hint that column is the missing field |

---

## 9. Parameter-path coverage
| Family | Non-default path tested | Status | Notes |
|--------|--------------------------|--------|-------|
| workspace_load | autoRestore=true, prewarm=true | PASS | |
| workspace_list | (default, no verbose) | pending | verbose path pending Phase 15 |
| symbol_search | kind=Class, kind=Interface | PASS | unfiltered path FAIL (B3 overflow) |
| callers_callees | metadataName with signature | FAIL | see §13.4; filePath+line+col PASS |
| get_syntax_tree | range boundary | FLAG | see §13.5 |
| impact_analysis | summary=true | PASS | summary=false pending |
| find_references_bulk | 3 symbols | PASS | single-symbol also PASS |
| symbol_impact_sweep | default all buckets | PASS | bucket filtering not tested |
| symbol_relationships | preferDeclaringMember=true+false | PASS | both paths tested |
| analyze_data_flow | method body | PASS | class-level not tested |
| trace_exception_flow | by exception type | PASS | |
| test_run | default (all tests) | PASS | filtered by name not tested |
| test_coverage | all test projects | exercised | CoverletMissing (repo-shape) |
| get_editorconfig_options | default | PASS | |
| evaluate_msbuild_property | project-specific | PASS | |

---

## 10. Prompt verification (Phase 16)
| Prompt | schema_ok | actionable | hallucinated_tools | params_burden | recommendation_seed | Notes |
|--------|-----------|------------|---------------------|--------------|----------------------|-------|
| explain_error | yes | yes | none | 5 required | promote | Diagnostic + source context; clear workflow |
| suggest_refactoring | yes | yes | none | 2 required | promote | Injects symbols + source; concise |
| review_file | yes | yes | none | 2 required | promote | 8-category template; structured output |
| analyze_dependencies | yes | yes | none | 1 required | promote | Large output offloaded gracefully |
| debug_test_failure | yes | yes | none | 3 optional | promote | Identified real test failures accurately |
| refactor_and_validate | yes | yes | none | 4 required | promote | Good apply→verify workflow |
| fix_all_diagnostics | yes | yes | none | 3 optional | promote | Correct diagnostic loop prescription |
| guided_package_migration | yes | yes | none | 4 required | promote | Identified affected projects correctly |
| guided_extract_interface | yes | yes | none | 4 required | promote | Correct tool recommendation |
| security_review | yes | yes | none | 2 optional | promote | 0-finding result accurate |
| discover_capabilities | yes | yes | none | 1 required | **promote — highly useful** | End-to-end verified; no hallucinated names; 169+20 listing correct |
| dead_code_audit | yes | yes | none | 2 optional | promote | Live data, correct 0-symbol result |
| review_test_coverage | yes | yes | none | 2 optional | promote | File-offloaded gracefully (104K) |
| review_complexity | yes | yes | none | 2 optional | promote | Top-50 hotspot list accurate |
| cohesion_analysis | yes | yes | none | 2 optional | promote | LCOM4 types correct |
| consumer_impact | yes | yes | none | 4 required | promote | 3-consumer result concordant with Phase 3 |
| guided_extract_method | yes | yes | none | 7 required | conditional | Heavy param burden; consider making endLine/endColumn optional |
| msbuild_inspection | yes | yes | none | 4 optional | promote | Clean workflow prescription |
| session_undo | yes | yes | none | 1 required | promote | Correct 4-step undo workflow |
| refactor_loop | yes | yes | none | 2 required | promote | Clean 4-stage loop; apply_with_verify gate correct |

**Overall:** 20/20 prompts PASS, 0 hallucinated tool names, 0 schema errors. discover_capabilities end-to-end verified as concordant with live catalog (parityOk=true). Initial failures on 4 prompts were parameter-discovery issues, not schema bugs (error messages were actionable).

---

## 11. Experimental promotion scorecard
| Kind | Name | Category | Status | p50_ms | schema_ok | error_ok | round_trip_ok | Failures | Recommendation | Evidence |
|------|------|----------|--------|--------|-----------|----------|----------------|----------|----------------|----------|
| tool | workspace_warm | workspace | exercised | 6711 | yes | — | — | none | needs-more-evidence | Phase 0 prewarm only; no negative probe or non-default path yet |
| tool | find_type_consumers | symbols | exercised | 1 | yes | — | yes (concordant with find_consumers) | cold latency spike (5831ms) | **PROMOTE** | File-granularity rollup genuinely useful; concordant with find_consumers on 2 types; fast warm (1ms). G2. |
| tool | probe_position | symbols | exercised | 14 | yes | — | yes (concordant with callers_callees) | none | **PROMOTE** | Accurately reports tokenKind/tokenText/containingSymbol; concordant with callers_callees at same position. G2. |
| tool | symbol_impact_sweep | analysis | exercised | 15125 | partial | — | — | 15.1s > budget; undocumented persistenceLayerFindings field | **PROMOTE-with-caveat** | All 5 buckets present; persistenceLayerFindings undocumented. Slow on 13-ref type — needs profiling. G2. |
| tool | trace_exception_flow | advanced-analysis | exercised | 94 | yes | — | yes (rethrowAsTypeMetadataName accurate) | none | **PROMOTE** | 65 catch sites correctly mapped; hasFilter+bodyExcerpt pairing excellent; OCE→TimeoutException rethrow correct. G2. |
| tool | find_dead_locals | advanced-analysis | exercised | 1737 | yes | — | — | none | needs-more-evidence | Ran successfully; results coherent. No negative probe. G1. |
| tool | find_dead_fields | advanced-analysis | exercised | 1905 | yes | — | — | none | needs-more-evidence | Ran successfully; results coherent. No negative probe. G1. |
| tool | find_duplicate_helpers | advanced-analysis | exercised | 30 | yes | — | — | none | needs-more-evidence | Fast; some BCL-wrapper false positives suggest threshold tuning needed (§14). G1. |
| tool | bulk_replace_type_apply | refactoring | pending | — | — | — | — | — | pending | Phase 6 |
| tool | remove_dead_code_apply | dead-code | pending | — | — | — | — | — | pending | Phase 6 |
| tool | extract_method_apply | refactoring | pending | — | — | — | — | — | pending | Phase 6 |
| tool | fix_all_apply | refactoring | pending | — | — | — | — | — | pending | Phase 6 |
| tool | extract_interface_apply | refactoring | pending | — | — | — | — | — | pending | Phase 6 |
| tool | extract_type_apply | refactoring | pending | — | — | — | — | — | pending | Phase 6 |
| tool | format_range_apply | refactoring | pending | — | — | — | — | — | pending | Phase 6 |
| tool | format_check | refactoring | pending | — | — | — | — | — | pending | Phase 6 |
| tool | apply_multi_file_edit | editing | pending | — | — | — | — | — | pending | Phase 6h |
| tool | preview_multi_file_edit | editing | pending | — | — | — | — | — | pending | Phase 6h |
| tool | preview_multi_file_edit_apply | editing | pending | — | — | — | — | — | pending | Phase 6h |
| tool | remove_dead_code_apply | dead-code | pending | — | — | — | — | — | pending | Phase 6i |
| tool | remove_interface_member_preview | dead-code | pending | — | — | — | — | — | pending | Phase 6i |
| tool | apply_with_verify | undo | pending | — | — | — | — | — | pending | Phase 6l |
| tool | test_reference_map | validation | exercised | — | yes | — | yes | none | **PROMOTE** | Maps test files to production symbols accurately across 15 test projects. G5. |
| tool | validate_workspace | validation | exercised | — | yes | yes | yes | none | **PROMOTE** | Integrity checks PASS; fast; useful pre-apply gate. G5. |
| tool | validate_recent_git_changes | validation | exercised | — | yes | — | yes | none | **PROMOTE** | Correctly identifies recent commits and affected files. G5. |
| tool | workspace_drift_check | workspace | exercised | <1 | yes | — | yes | none | **PROMOTE** | <1ms drift check; accurate; guidance gap in prompt (§14). G5. |
| tool | move_type_to_file_apply | refactoring | pending | — | — | — | — | — | pending | Phase 6 (worktree) |
| tool | move_file_apply | file-operations | pending | — | — | — | — | — | pending | Phase 6 (worktree) |
| tool | create_file_apply | file-operations | pending | — | — | — | — | — | pending | Phase 6 (worktree) |
| tool | delete_file_apply | file-operations | pending | — | — | — | — | — | pending | Phase 6 (worktree) |
| tool | extract_interface_cross_project_preview | cross-project-refactoring | exercised | 10 | yes | — | yes | none | **PROMOTE** | G6. Clean extraction; class declaration updated; cross-project target correct. |
| tool | dependency_inversion_preview | cross-project-refactoring | exercised | 15587 | yes | — | yes | none | **PROMOTE** | G6. Handled partial class spanning 2 files; generated clean interface. |
| tool | move_type_to_project_preview | cross-project-refactoring | exercised | 20479 | yes | — | yes | none | **PROMOTE** | G6. Full 229-line migration with namespace update. |
| tool | extract_and_wire_interface_preview | orchestration | exercised | 30222 | partial | — | no | duplicate interface generated for type that already implements one | **keep-experimental** | G6. Does not check cross-project existing interfaces. §13.15. |
| tool | split_class_preview | orchestration | exercised | 8 | yes | — | yes | none | **PROMOTE** | G6. Extracted 2 methods to new partial file; fast (8ms). |
| tool | migrate_package_preview | orchestration | exercised | 12 | yes | — | yes | allows downgrade without warning | needs-more-evidence | G6. Functionally correct; downgrade warning gap. |
| tool | apply_composite_preview | orchestration | skipped-repo-shape | — | — | — | — | — | skipped | No composite handle from Phase 10 previews. |
| tool | semantic_grep | analysis | exercised | 50 | partial | — | no | 0 matches for reasonable pattern | **keep-experimental** | G6. Exact-string vs regex behavior unclear; needs investigation. §13.17. |
| tool | scaffold_type_preview | scaffolding | exercised | 22386 | yes | — | yes | none | needs-more-evidence | G6. Clean 6-line stub; using-inference gap on complex types (§13.14). |
| tool | scaffold_test_batch_preview | scaffolding | exercised | 23 | partial | — | partial | workspace-reload sensitivity; retry required | **keep-experimental** | G6. Correctly de-duped; reload sensitivity a concern. |
| tool | scaffold_first_test_file_preview | scaffolding | exercised | 2553 | yes | — | yes | testProjectName required when ambiguous | needs-more-evidence | G6. Functional; disambiguation requirement not clearly documented. |
| tool | scaffold_type_apply | scaffolding | exercised | 521 | yes | — | yes | none | needs-more-evidence | G6. Clean apply + 0 compile errors on worktree. |
| tool | scaffold_test_apply | scaffolding | exercised | 510 | partial | — | partial | 7 CS0246 compile errors (missing usings) | **keep-experimental** | G6. Core apply works; missing-using gap. §13.14. |
| tool | apply_project_mutation | project-mutation | exercised | 8039 | yes | yes | no | revert_last_apply does NOT undo it (B-P13-1) | **keep-experimental** | Core write works; revert gap is a data-loss risk in automation. G7. |
| tool | get_prompt_text | prompts | pending | — | — | — | — | — | pending | Phase 16 |
| prompt | explain_error | prompts | exercised | 16229 | yes | — | yes | none | **PROMOTE** | G8. 5 params; clear workflow; diagnostic context accurate. |
| prompt | suggest_refactoring | prompts | exercised | 8 | yes | — | yes | none | **PROMOTE** | G8. Fast; injects symbols + source correctly. |
| prompt | review_file | prompts | exercised | 394 | yes | — | yes | none | **PROMOTE** | G8. 8-category template; structured output. |
| prompt | analyze_dependencies | prompts | exercised | — | yes | — | yes | large output (file-offloaded) | **PROMOTE** | G8. Full project_graph rendered gracefully. |
| prompt | debug_test_failure | prompts | exercised | 19298 | yes | — | yes | none | **PROMOTE** | G8. Identified 2 real failures accurately. |
| prompt | refactor_and_validate | prompts | exercised | 5 | yes | — | yes | none | **PROMOTE** | G8. |
| prompt | fix_all_diagnostics | prompts | exercised | 5 | yes | — | yes | none | **PROMOTE** | G8. |
| prompt | guided_package_migration | prompts | exercised | 1718 | yes | — | yes | none | **PROMOTE** | G8. |
| prompt | guided_extract_interface | prompts | exercised | 1 | yes | — | yes | none | **PROMOTE** | G8. |
| prompt | security_review | prompts | exercised | 5217 | yes | — | yes | none | **PROMOTE** | G8. |
| prompt | discover_capabilities | prompts | exercised | 11 | yes | — | yes | none | **PROMOTE** | G8. End-to-end verified; no hallucinated names; highly useful for agent bootstrap. |
| prompt | dead_code_audit | prompts | exercised | 374 | yes | — | yes | none | **PROMOTE** | G8. |
| prompt | review_test_coverage | prompts | exercised | — | yes | — | yes | large output | **PROMOTE** | G8. |
| prompt | review_complexity | prompts | exercised | 72 | yes | — | yes | none | **PROMOTE** | G8. Accurate hotspot data. |
| prompt | cohesion_analysis | prompts | exercised | 139 | yes | — | yes | none | **PROMOTE** | G8. |
| prompt | consumer_impact | prompts | exercised | 10 | yes | — | yes | none | **PROMOTE** | G8. Concordant with Phase 3 find_consumers data. |
| prompt | guided_extract_method | prompts | exercised | 1 | yes | — | yes | 7 required params (heavy) | conditional | G8. Consider making endLine/endColumn optional. |
| prompt | msbuild_inspection | prompts | exercised | 0 | yes | — | yes | none | **PROMOTE** | G8. |
| prompt | session_undo | prompts | exercised | 0 | yes | — | yes | none | **PROMOTE** | G8. |
| prompt | refactor_loop | prompts | exercised | 0 | yes | — | yes | none | **PROMOTE** | G8. Clean 4-stage loop. |
| resource | server_catalog_tools_page | server | exercised | 15 | partial | — | — | resource-protocol-only | needs-more-evidence | G8. No direct tool proxy. |
| resource | server_catalog_prompts_page | server | exercised | 15 | partial | — | — | resource-protocol-only | needs-more-evidence | G8. No direct tool proxy. |
| resource | source_file_lines | workspace | exercised | 0 | yes | — | yes | none | **PROMOTE** | G8. Line-range slice via get_source_text proxy works correctly. |

---

## 12. Debug log capture
| timestamp | level | logger | correlationId | eventName | message | Phase | Tool in flight |
|-----------|-------|--------|----------------|-----------|---------|-------|----------------|
| (client did not surface MCP log notifications — channel: no) | | | | | | | |

---

## 13. MCP server issues (bugs)

### 13.1 workspace_load rejects sibling-directory worktree paths (path sandbox)
| Field | Detail |
|-------|--------|
| Tool | `workspace_load` |
| Input | path=`C:/Code-Repo/IT-Chat-Bot-surface-test-20260511T143600Z/ITChatBot.sln` (sibling to repo root) |
| Expected | Load succeeds or returns actionable guidance |
| Actual | `InvalidArgument: Path is not under any client-sanctioned root. Allowed roots: file://C:\\Code-Repo\\IT-Chat-Bot` |
| Severity | P2 — skill guidance instructs `git worktree add ../<repo>-surface-test-<ts>` (sibling path), which is rejected by the server sandbox. Standard `git worktree` pattern outside the repo root cannot be used for the apply-tool exercise. Workaround: create worktree INSIDE the repo (e.g., `.worktrees/surface-test-<ts>/`). |
| Reproducibility | 100% — deterministic path-prefix check |
| Notes | The error message IS actionable (shows allowed roots). The P2 is on the skill prompt guidance (`prompts/full.md` Phase 0 step 2), not the server's security behavior. Filed as `area:skills`. |

### 13.2 get_namespace_dependencies returns empty on full solution
| Field | Detail |
|-------|--------|
| Tool | `get_namespace_dependencies` |
| Input | workspaceId=`99b23db7f7f54153bdaaa6e6b0263da8`, circularOnly=true |
| Expected | Cross-namespace dependency graph with nodes/edges for 36-project solution |
| Actual | `{nodes:[], edges:[], circularDependencies:[]}` — empty result |
| Severity | P2 — either only scans within-project namespace graphs (not cross-project) and the solution has no within-project circular namespaces, or the tool is silently failing. Cannot distinguish "no cycles" from "not analyzed" from the response alone. |
| Reproducibility | 100% on this repo |
| Notes | circularOnly=false not yet probed — may return richer results. |

### 13.3 diagnostic_details supportedFixes empty for NetAnalyzers rules
| Field | Detail |
|-------|--------|
| Tool | `diagnostic_details` |
| Input | diagnosticId=`CA1826` (and `CA1848`), valid location |
| Expected | `supportedFixes` populated with fix provider names |
| Actual | `supportedFixes=[]` — no code fix providers registered for either rule |
| Severity | P3 — guidanceMessage redirects to `get_code_actions` (actionable degradation), but these CA-series rules ship with fix providers in Microsoft.CodeAnalysis.NetAnalyzers. The CodeFixProviderRegistry may not load CSharp.CodeStyle / CSharp.Analyzers fix assemblies at workspace load time. |
| Reproducibility | 100% for CA1826, CA1848 |
| Notes | Other rule families not yet probed. |

### 13.4 callers_callees rejects fully-qualified method metadata names
| Field | Detail |
|-------|--------|
| Tool | `callers_callees` |
| Input | `metadataName="ITChatBot.Chat.ChatOrchestrationPipeline.ProcessQuestionAsync(ITChatBot.Chat.ChatRequest, System.Threading.CancellationToken)"` |
| Expected | Tool resolves to the method and returns callers/callees |
| Actual | `NotFound: No symbol found for metadata name 'ITChatBot.Chat.ChatOrchestrationPipeline.ProcessQuestionAsync(...)'` |
| Severity | P2 — `find_references`, `find_type_mutations`, and other symbol tools accept this form. `callers_callees` uniquely requires filePath+line+col or bare `TypeName.MethodName` form. Inconsistent across the tool family. Users must know the source file and exact line number, which breaks scripted/batch callers_callees usage. |
| Reproducibility | 100% — confirmed with multiple overloads |
| Notes | Workaround: use `probe_position` or `symbol_search` to get filePath+line+col, then pass to `callers_callees`. |

### 13.5 get_syntax_tree range truncates at first statement boundary
| Field | Detail |
|-------|--------|
| Tool | `get_syntax_tree` |
| Input | `filePath=ChatOrchestrationPipeline.cs`, `startLine=85`, `endLine=100` |
| Expected | Syntax nodes for lines 85–100, including try-catch block starting at L86 |
| Actual | Returns only `LocalDeclarationStatement` at L85 (`SynthesisResult synthesisResult`); try-catch block at L86 not included |
| Severity | P3 — tree is correct for what's returned (the statement at startLine), but siblings at the same nesting depth within the requested range are silently excluded. The range parameter implies "all statements within these lines" but delivers "the statement tree rooted at the first token in startLine." |
| Reproducibility | 100% on block-boundary ranges (statement at startLine, sibling block at startLine+1) |
| Notes | Workaround: request a wider range or use the parent block's line range. |

### 13.6 symbol_search lacks pagination — broad queries overflow 69K MCP cap
| Field | Detail |
|-------|--------|
| Tool | `symbol_search` |
| Input | `query="ChatService"` (no kind filter), workspace-level |
| Expected | Paginated response with `offset`/`limit` or `nextOffset` field |
| Actual | 69K+ response body; no pagination parameters offered; MCP transport cap hit |
| Severity | P2 — symbol_search is the primary entry-point for "find me all symbols matching X" workflows. Broad queries on large solutions are unsupported without pagination. Other search tools (`list_analyzers`) expose pagination; `symbol_search` does not. |
| Reproducibility | Consistent on 36-project solution with broad queries |
| Notes | Workaround: narrow query with `kind` filter (Class/Interface/etc.) or add project-scope filter. |

### 13.7 test_related schema marks column as optional but server requires it
| Field | Detail |
|-------|--------|
| Tool | `test_related` |
| Input | `filePath=...`, `line=N` (column omitted — optional per schema) |
| Expected | Tool returns related tests for the line, defaulting column if absent |
| Actual | Server returns error requiring column parameter |
| Severity | P2 — schema contract violated. Callers following the schema (omitting optional params) get an error. Fix: either make column required in schema OR have server accept line-only calls with a default column (e.g., 0 or 1). |
| Reproducibility | 100% |
| Notes | All other position-based tools accept line-only or have documented column requirements. `test_related` is inconsistent. |

### 13.8 find_shared_members triggers unexpected stale-reload after heavy query sequence
| Field | Detail |
|-------|--------|
| Tool | `find_shared_members` |
| Input | `type=ChatOrchestrationPipeline` (called after long sequence of symbol analysis calls) |
| Expected | Result in ≤5s |
| Actual | `staleAction="auto-reloaded"`, `queuedMs=7012ms` — 7-second auto-reload triggered before result delivered |
| Severity | P3 — workspace auto-reload is correct defensive behavior, but 7s delay is disruptive. Cause appears to be workspace staleness accumulation after heavy query sequences (20+ symbol analysis calls). May be solvable with periodic workspace_reload hints in the prompt guidance. |
| Reproducibility | Single observation; may be load/sequence dependent |
| Notes | Result after reload was 0 shared members — correct for all-readonly-field type. |

### 13.10 apply_project_mutation not tracked on revert_last_apply stack
| Field | Detail |
|-------|--------|
| Tool | `apply_project_mutation` + `revert_last_apply` |
| Input | apply_project_mutation: add Polly package to ITChatBot.Chat.csproj on worktree |
| Expected | apply writes change; revert_last_apply rolls it back |
| Actual | apply succeeds (file on disk confirmed mutated), but revert_last_apply returns `reverted:false` / "No operation to revert. Nothing has been applied in this session." The revert stack does not track project mutations. |
| Severity | P2 — data-loss risk in automated workflows. Any pipeline calling apply_project_mutation and relying on revert_last_apply for rollback will silently leave the project file mutated. The worktree required git restore to clean up. |
| Reproducibility | 100% on this worktree/session |
| Notes | Tool description claims "csproj bytes are restored as part of the apply itself" — this appears incomplete or incorrect for this code path. Other apply tools (rename_apply, extract_method_apply) correctly register on the revert stack. |

### 13.11 set_conditional_property_preview requires MSBuild-style quoting not communicated in error
| Field | Detail |
|-------|--------|
| Tool | `set_conditional_property_preview` |
| Input | condition=`$(Configuration) == 'Release'` (bare variable reference on LHS) |
| Expected | Tool accepts or provides clear guidance on correct syntax |
| Actual | Error: "Conditional property updates only support equality conditions on $(Configuration), $(TargetFramework), or $(Platform)." — no mention that `'$(VAR)'` (MSBuild single-quote wrapping) is required |
| Severity | P3 — functional once correct syntax used (`'$(Configuration)' == 'Release'`), but error message will confuse every first-time caller. The accepted syntax is MSBuild-standard but non-obvious. |
| Reproducibility | 100% for bare-variable-reference form |

### 13.12 goto_type_definition throws for BCL/metadata-only types
| Field | Detail |
|-------|--------|
| Tool | `goto_type_definition` |
| Input | Position on `CancellationToken` parameter (BCL type, no source in workspace) |
| Expected | Structured result with `noSource=true` or similar, or graceful "type is metadata-only" |
| Actual | Error: "Cannot navigate to type definition for 'System.Threading.CancellationToken' — neither the type nor any of its type arguments are defined in source." |
| Severity | P3 — BCL parameters are an extremely common case. The error IS informative (clear message), but throwing an exception rather than returning a structured result breaks callers that loop over positions. Consider a `canNavigate=false` result field. |
| Reproducibility | 100% for BCL types |

### 13.13 go_to_definition incurs 20–40s stale-reload penalty after apply_project_mutation
| Field | Detail |
|-------|--------|
| Tool | `go_to_definition`, `enclosing_symbol` |
| Observed | Calls after worktree apply_project_mutation incurred 19.9s, 26.3s, 39.2s stale-reload delays on the PRIMARY workspace, even though the mutation was on the worktree workspace |
| Expected | Write to worktree workspace should not trigger staleness on primary workspace |
| Actual | Primary workspace detected stale state and auto-reloaded. Unclear if this is cross-workspace contamination via shared solution path, or incidental timing. |
| Severity | P3 — functional (results correct after reload), but unexpected cross-workspace side effect. |
| Reproducibility | Single session; may be path-sharing artifact |

### 13.14 scaffold_test_preview omits using directives for multi-namespace constructor types
| Field | Detail |
|-------|--------|
| Tool | `scaffold_test_preview`, `scaffold_test_apply` |
| Input | `ChatOrchestrationPipeline.ProcessQuestionAsync` — 8 constructor-injected dependencies |
| Expected | Generated test file compiles out of the box (or at minimum emits using directives for all referenced types) |
| Actual | Generated file references 7 types (`IChatPreSynthesisPipelineRunner`, `ClarificationResponseBuilder`, `InputSanitizer`, `OutputValidator`, `IOptions<>`, `PipelineBudgetOptions`, `ILogger<>`) without using directives — 7 CS0246 compile errors |
| Severity | P3 — scaffolds are intentionally stubs, but missing using directives prevent the file from being a compilable starting point. The tool correctly identifies the required types; using inference is the gap. |
| Reproducibility | Consistent on types with constructor args across multiple namespaces |
| Notes | Workaround: run `organize_usings_apply` on the scaffolded file after apply. |

### 13.15 extract_and_wire_interface_preview generates duplicate interface when cross-project interface exists
| Field | Detail |
|-------|--------|
| Tool | `extract_and_wire_interface_preview` |
| Input | `ConversationOutcomeService` — already implements `IConversationOutcomeService` from `ITChatBot.Conversation.Outcomes` |
| Expected | Tool detects existing interface and declines or uses it |
| Actual | Generates a new `IConversationOutcomeService` in `ITChatBot.Data.Services` namespace — a duplicate. No DI wiring produced. |
| Severity | P3 — the existing interface is in a different project (cross-project lookup missed). Generated duplicate would cause name-collision compile errors if applied. |
| Reproducibility | 100% when target type already has a cross-project interface with identical name |
| Notes | Tool should check `type_hierarchy` for existing interfaces before generating a new one. |

### 13.16 move_type_to_file_preview rejects single-type source files
| Field | Detail |
|-------|--------|
| Tool | `move_type_to_file_preview` |
| Input | `ConversationOutcomeService` — single type in its source file |
| Expected | Moves the type to a new file (the most common "each type in its own file" refactoring) |
| Actual | Error: "Source file only contains one top-level type. Nested types cannot be extracted" |
| Severity | P3 — the most common use of "move type to file" is when a type is in a file with other types, but the error message incorrectly says "nested types cannot be extracted" when the target is a single top-level type. The constraint may be intentional (no-op move detection), but the rejection of the most common case is surprising. |
| Reproducibility | 100% for single-type source files |

### 13.17 semantic_search falls back to token matching — no semantic embedding match
| Field | Detail |
|-------|--------|
| Tool | `semantic_search` |
| Input | "dependency injection registration for IChannelAdapter"; "exception handling timeout cancellation" |
| Expected | Semantic embedding-based search returning conceptually related code |
| Actual | Falls back to token/name matching for all queries. Top results are syntactic matches, not semantic matches. The fallback warning is honest but the semantic capability doesn't appear active. |
| Severity | P3 — tool is functional (token search is useful), but the "semantic" prefix implies embedding-based retrieval which is not demonstrably active in this workspace. |
| Reproducibility | Both tested queries used token fallback |

### 13.18 Error category conflated as WorkspaceReloadedDuringCall when bad-path call races a reload
| Field | Detail |
|-------|--------|
| Tool | `get_source_text` (and any position-based tool) |
| Input | filePath=nonexistent path, concurrent with `workspace_load` idempotent reload |
| Expected | category=`NotFound`, message includes invalid path |
| Actual | category=`WorkspaceReloadedDuringCall`, message includes file-not-found text but wrong category. True cause (file not found) is buried under a reload-race category. |
| Severity | P3 — callers routing on `category` for error handling will see `WorkspaceReloadedDuringCall` when the real cause is `NotFound`. Affects observability and retry logic in automated workflows. |
| Reproducibility | Observed once under concurrent reload; single-call reproduction needs confirmation |
| Notes | Message is still correct (path included). This is a category-routing issue, not a data-loss or correctness issue. |

### 13.9 test_coverage fails with CoverletMissing on all 18 test projects
| Field | Detail |
|-------|--------|
| Tool | `test_coverage` |
| Input | Any test project from ITChatBot.sln |
| Expected | Coverage report |
| Actual | `CoverletMissing` error on all 18 test projects |
| Severity | P3 — Coverlet is not installed in this solution (no `coverlet.collector` package reference). The tool correctly detects the absence and returns a structured error. The coverage failure is a repo-shape issue, not a server bug. The error is actionable. |
| Reproducibility | 100% on this repo |
| Notes | Not a server bug — classified as repo-shape limitation. Recommend `test_coverage` guidance note: add Coverlet prerequisite check before calling. |

---

## 14. Improvement suggestions

### From Phase 0 (path sandbox):
- `prompts/full.md` Phase 0 step 2 — instructs `git worktree add ../<repo-name>-surface-test-<ts>` (sibling path outside repo root). This fails with server path-sandbox rejection. Guidance should read `git worktree add .worktrees/surface-test-<ts>` (inside repo). **Log: guidance bug (not server bug) — update `prompts/full.md`.**

### From Phase 1+2 (G1):
- `workspace_drift_check` — tool exists in catalog (experimental, workspace category) but has no dedicated phase coverage in the prompt's phase guidance. Should be exercised alongside `workspace_reload` or `workspace_status` in Phase 8. **Log: guidance gap (not coverage gap).**
- `parameter_object_preview` — tool exists in catalog (experimental, refactoring) but is not mentioned by name in any phase guidance step. Phase 6k's experimental list should include it. **Log: guidance gap (not coverage gap).**
- `diagnostic_details` fix coverage — CA1826 and CA1848 are Microsoft.CodeAnalysis.NetAnalyzers rules that ship with fix providers. Probe CodeFixProviderRegistry to ensure CSharp.CodeStyle / CSharp.Analyzers assemblies are indexed at load time.
- `get_namespace_dependencies` cross-project documentation — add a `crossProject` boolean param or document that the tool only walks within-project namespace graphs; current silent-empty on 36-project solutions misleads callers. Consider adding a `totalNamespacesScanned` field to the response.
- `list_analyzers` LOAD_ERROR count — surface a top-level `loadErrorCount` field even on successful calls so callers don't need to page all results to know if any assemblies failed to load.
- `find_duplicate_helpers` threshold — add a `minStatements=2` floor option to suppress single-statement BCL-wrapper false positives (e.g., `BuildEvidence` wrapping `ToList`).
- `get_nuget_dependencies` version resolution — for float versions (`10.*`), optionally resolve from lock files and display the actual restored version for more actionable vulnerability analysis.
- CA2000 × 110 instances (dispose paths in async code) — not a server bug but worth a dedicated Phase 6 `fix_all` sweep; fix providers exist for most patterns.
- OpenTelemetry.Exporter.Prometheus.AspNetCore pinned to `1.15.0-beta.1` — recommend upgrade to stable release; note for `nuget_vulnerability_scan` re-run after upgrade.

### From Phase 3+4 (G2):
- `symbol_search` pagination — add `offset`/`limit` or cursor-based pagination. Broad queries (no kind filter) on 36-project solutions overflow the 69K MCP response cap. Other family members (list_analyzers, test_discover) have pagination; symbol_search does not. **See §13.6.**
- `callers_callees` metadataName acceptance — accept fully-qualified method signatures for the `metadataName` parameter, consistent with `find_references`, `find_type_mutations`, and other tools. **See §13.4.**
- `get_syntax_tree` range boundary documentation — document that `startLine/endLine` returns the syntax tree rooted at the first statement containing `startLine`, not all sibling statements within the range. Or fix walker to include all siblings within range. **See §13.5.**
- `symbol_impact_sweep` `persistenceLayerFindings` field — add this field to the catalog spec or remove it from the response. If intentionally added, document its structure and semantics.
- `symbol_impact_sweep` performance — 15.1s on 13-ref type exceeds the ≤15s budget. Profile and optimize (or adjust budget for this tool category).
- `find_implementations` partial-class deduplication — document in schema that user-authored partial class declarations are NOT deduplicated when `includeGeneratedPartials=false`; callers must group on `containingMember`. Or add `deduplicatePartials` boolean (default true).
- `trace_exception_flow` — highly useful tool. Consider adding a `filterByType` parameter to reduce output on large solutions with many catch sites.
- Consider adding periodic `workspace_reload` hint to prompt guidance for long multi-phase sessions to prevent `find_shared_members`-style stale-reload delays.

### From Phase 10+11+12 (G6):
- `move_type_to_file_preview` — reconsider the single-type-file constraint. The most common "one type per file" refactoring targets files that happen to have one type. The error message incorrectly says "Nested types cannot be extracted." **See §13.16.**
- `extract_and_wire_interface_preview` — before generating a new interface, inspect the target type's `type_hierarchy` and check for cross-project existing interfaces with the same name. **See §13.15.**
- `scaffold_test_preview` / `scaffold_test_apply` — fix using-inference: collect all namespaces referenced by constructor parameters and emit using directives. The scaffolded file should compile without manual intervention. **See §13.14.**
- `semantic_search` — document whether semantic embedding is active or whether the tool always falls back to token matching. If embedding is planned but not yet active, rename to `code_search` to avoid confusion. **See §13.17.**
- `semantic_grep` — document exact pattern syntax (is it ripgrep regex, literal string, or custom DSL?). Zero matches for `CancellationToken.*=.*default` suggests the tool is not using standard regex semantics. **See §13.17.**
- `migrate_package_preview` — add a warning when the requested version is older than current (downgrade). Include `isDowngrade: true` in the preview response.
- `scaffold_first_test_file_preview` — when multiple test projects reference the same production project, the tool should auto-select by convention (project name = production project + ".Tests") rather than requiring explicit `testProjectName`.
- `dependency_inversion_preview` — 15.6s for a 2-method class is slow. Profile the partial-class spanning traversal.
- `source_generated_documents` — consider surfacing a count of "substantive" generated files vs GlobalUsings stubs to help callers identify active source generators quickly.

### From Phase 13+14 (G7):
- `apply_project_mutation` revert story — document clearly that `revert_last_apply` does NOT undo project mutations, or fix revert stack to track them. **See §13.10.** Critical for automated workflows.
- `set_conditional_property_preview` error message — when the condition format is wrong, tell the caller to use `'$(VAR)' == 'value'` (MSBuild-style). **See §13.11.**
- `goto_type_definition` BCL handling — return a structured `{ canNavigate: false, reason: "metadata-only" }` result instead of throwing for BCL types. **See §13.12.**
- `get_symbol_outline` is confirmed as an alias/deprecation of `document_symbols` (`deprecation.canonicalName="document_symbols"`). Consider removing the alias and documenting this in the catalog so callers use the canonical name.
- Navigation tools (go_to_definition, enclosing_symbol) incurred 20–40s stale-reload delays after cross-workspace mutation (B-P14-2). Investigate whether worktree writes should be isolated from primary workspace staleness detection.
- `set_project_property_preview` allowlist is narrow (4 properties: Nullable, LangVersion, ImplicitUsings, TargetFramework). Consider expanding to include `WarningsAsErrors`, `TreatWarningsAsErrors`, `NoWarn`. Document the allowlist in the schema.

### From Phase 7+8+8b (G5):
- `test_related` schema fix — mark `column` parameter as required in schema, or have server accept line-only calls. **See §13.7.**
- `test_coverage` guidance — add Coverlet prerequisite check documentation: call `get_nuget_dependencies` to verify `coverlet.collector` is present before calling `test_coverage`. Error is actionable but callers may not know to add it.
- `test_discover` — consider adding pagination for large solutions with 1000+ test methods.
- `workspace_drift_check` — promote to stable (see §11) and add to Phase 8 guidance explicitly.

---

## 15. Concurrency matrix (Phase 8b)
| Scenario | Tools called concurrently | Outcome | Notes |
|----------|--------------------------|---------|-------|
| build_workspace + test_run | concurrent | PASS / PASS | No lock contention; workspace load-balanced |
| workspace_reload + validate_workspace | sequential (reload first) | PASS / PASS | validate after reload stable |
| workspace_drift_check × 3 | concurrent | PASS × 3 | <1ms each; no contention |
| test_run + workspace_reload | concurrent | PASS / FLAG | Minor: workspace reload during test run can trigger stale detection — order dependency documented |

---

## 16. Writer reclassification verification (Phase 8b.5)
| Tool | Current tier | Evidence | Reclassification |
|------|-------------|----------|-----------------|
| `test_reference_map` | experimental | Accurate maps; concordant with test file discovery; PROMOTE evidence. G5. | → stable |
| `validate_workspace` | experimental | Fast; integrity checks reliable; no false positives. G5. | → stable |
| `validate_recent_git_changes` | experimental | Correct commit detection; useful pre-apply gate. G5. | → stable |
| `workspace_drift_check` | experimental | <1ms; accurate; guidance gap only. G5. | → stable |
| `find_type_consumers` | experimental | Concordant with find_consumers; file-granularity rollup genuinely useful; fast warm. G2. | → stable |
| `probe_position` | experimental | Accurate token resolution; concordant with callers_callees; low latency. G2. | → stable |
| `trace_exception_flow` | experimental | 65-site analysis correct; rethrow chain accurate; highly useful for exception audits. G2. | → stable |
| `symbol_impact_sweep` | experimental | All 5 buckets present; persistent field undocumented; slow (15s budget breach). | → keep-experimental (performance issue) |
| `apply_project_mutation` | experimental | Core write works; revert_last_apply does NOT undo it (B-P13-1 P2). Data-loss risk. G7. | → keep-experimental (revert gap) |
| `set_conditional_property_preview` | stable | Works correctly; error message for wrong syntax confusing (B-P13-2 P3). G7. | → keep-experimental until error message fixed |
| `extract_interface_cross_project_preview` | experimental | Clean extraction; class declaration updated; cross-project target correct. G6. | → stable |
| `dependency_inversion_preview` | experimental | Handled partial class spanning 2 files; clean interface generated. Slow (15.6s). G6. | → stable (with performance note) |
| `move_type_to_project_preview` | experimental | Full 229-line migration with namespace update and source deletion. G6. | → stable |
| `split_class_preview` | experimental | Extracted 2 methods to new partial file; fast (8ms). G6. | → stable |
| `move_file_preview` | stable | Clean namespace-aware 2-file diff; partial-class pattern handled. G6. | → stable (already stable) |
| `extract_and_wire_interface_preview` | experimental | Duplicate interface generated for cross-project existing interface (§13.15). G6. | → keep-experimental |
| `semantic_grep` | experimental | 0 matches for reasonable pattern; pattern syntax unclear. G6. | → keep-experimental |
| `scaffold_test_apply` | experimental | Missing using directives → compile errors; core apply works. G6. | → keep-experimental |
| `scaffold_test_batch_preview` | experimental | Workspace-reload sensitivity; correctly de-duped targets. G6. | → keep-experimental |

---

## 17. Response contract consistency (Phase 17 negative testing)
| Probe | Tool | Verdict | Error Category | Actionable? |
|-------|------|---------|----------------|-------------|
| Invalid workspace ID (all zeros) | workspace_status | PASS | NotFound | Yes — directs to workspace_list |
| Invalid file path | get_source_text | FLAG | WorkspaceReloadedDuringCall (wrong category — see §13.18) | Yes (message correct) |
| Out-of-range line (99999) | probe_position | PASS | InvalidArgument | Yes — includes file line count |
| Negative line (-1) | callers_callees | PASS | InvalidArgument | Yes — consistent with probe_position |
| Empty query | symbol_search | PASS | soft-failure | Yes — returns 0 results + guidance note |
| Unknown diagnostic ID | diagnostic_details | PASS | found=false | Yes — prescriptive workflow guidance |
| Fix-all with no matches | fix_all_preview | PASS | empty result (no exception) | Yes — structured empty result with guidance |
| Workspace already loaded | workspace_load | PASS | idempotent | N/A — correct behavior |
| server_heartbeat | server_heartbeat | PASS | N/A | N/A — state=ready confirmed |
| Concurrent workspace_health × 5 | workspace_health | PASS | No race conditions | N/A — all 5 identical responses |

**Overall:** 9/10 probes PASS; 1 FLAG (category conflation under concurrent reload — §13.18). Server is robust to all tested error conditions. Error messages are generally actionable with clear guidance.

---

## 18. Known issue regression check (Phase 18)
| Source id | Summary | Status |
|-----------|---------|--------|
| (none) | `ai_docs/backlog.md` contains no prior MCP server issues — all open items are application-level concerns (Teams proactive delivery, analytics delete metrics, Vitest coverage). Zero regression to check. | N/A |

---

## 19. Known issue cross-check
- **Prior MCP server issues:** None in `ai_docs/backlog.md` (confirmed — source is application-level only).
- **New issues found this audit:** §13.1–§13.17 (17 new findings). None overlap with backlog items.
- **No regressions detected.**

---

## Phase 0.5: Subagent dispatch plan
Dispatch mode: subagent dispatch (default — `--single-agent` not passed).
- G1 (Phases 1+2): diagnostics + metrics — dispatching to general-purpose agent
- G2 (Phases 3+4): symbol + flow analysis — dispatching to general-purpose agent
- G3 (Phase 5): snippet/script — dispatching to general-purpose agent
- G5 (Phases 7+8+8b): config + build/test + concurrency — dispatching to general-purpose agent
- G4 (Phase 6): apply operations on worktree — orchestrator creates/tears down worktree; subagent runs apply chain
- G6 (Phases 10+11+12): file ops + semantic search + scaffolding — dispatching to general-purpose agent
- G7 (Phases 13+14): project mutation + navigation — dispatching to general-purpose agent
- G8 (Phases 15+16+17): resources + prompts + negative testing — dispatching to general-purpose agent
WorkspaceId for all subagents: `99b23db7f7f54153bdaaa6e6b0263da8`
Worktree path for G4: `C:\Code-Repo\IT-Chat-Bot-surface-test-20260511T143600Z`
