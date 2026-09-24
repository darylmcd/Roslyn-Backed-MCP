# 01 — Inventory (tools/list, resources/list, resources/templates/list, prompts/list)

Raw lists: `raw/head-tools.json`, `raw/head-resources.json`, `raw/head-templates.json`, `raw/head-prompts.json`; lint `raw/head-schema-lint.json`, `raw/installed-schema-lint.json`.
Installed 4.2.1: 174 tools / 5 static resources + 9 templates (14 catalog entries) / 20 prompts. HEAD: 175 tools (+`apply_composite`).
Capabilities advertised at initialize: tools, resources, prompts (all listChanged) and logging (see `logging-capability-parity`).

## Coverage ledger

| Kind | Name | Tier | Category | Status | Phase | Notes |
|---|---|---|---|---|---|---|
| tool | `server_info` | stable | server | exercised | 5,11,14;8,8b,9;15,16 |  |
| tool | `server_heartbeat` | stable | server | exercised | 8,8b,9;15,16 |  |
| tool | `workspace_load` | stable | workspace | exercised | 15,16 |  |
| tool | `workspace_reload` | stable | workspace | exercised | 8,8b,9;10,12,13 |  |
| tool | `workspace_close` | stable | workspace | exercised | 17 |  |
| tool | `workspace_warm` | experimental | workspace | exercised | 8,8b,9 |  |
| tool | `workspace_list` | stable | workspace | exercised | 3,4,18;5,11,14;6,7;8,8b,9;10,12,13;15,16 |  |
| tool | `workspace_status` | stable | workspace | exercised | 1,2;8,8b,9;15,16 |  |
| tool | `workspace_health` | stable | workspace | exercised | 8,8b,9 |  |
| tool | `workspace_readiness_report` | stable | workspace | exercised | 8,8b,9 |  |
| tool | `workspace_support_bundle` | stable | workspace | exercised | 8,8b,9 |  |
| tool | `project_graph` | stable | workspace | exercised | 10,12,13;15,16 |  |
| tool | `source_generated_documents` | stable | workspace | exercised | 3,4,18;5,11,14 |  |
| tool | `get_source_text` | stable | workspace | exercised | 3,4,18;8,8b,9;15,16 |  |
| tool | `workspace_changes` | stable | workspace | exercised | 6,7;8,8b,9;10,12,13 |  |
| tool | `workspace_drift_check` | experimental | workspace | exercised | 8,8b,9 |  |
| tool | `symbol_search` | stable | symbols | exercised | 3,4,18;6,7;8,8b,9 |  |
| tool | `symbol_info` | stable | symbols | exercised | 3,4,18 |  |
| tool | `go_to_definition` | stable | symbols | exercised | 5,11,14 |  |
| tool | `find_references` | stable | symbols | exercised | 3,4,18;5,11,14;6,7;8,8b,9 |  |
| tool | `find_implementations` | stable | symbols | exercised | 3,4,18;5,11,14 |  |
| tool | `document_symbols` | stable | symbols | exercised | 3,4,18;5,11,14 |  |
| tool | `find_overrides` | stable | symbols | exercised | 5,11,14 |  |
| tool | `find_base_members` | stable | symbols | exercised | 5,11,14 |  |
| tool | `member_hierarchy` | stable | symbols | exercised | 3,4,18 |  |
| tool | `symbol_signature_help` | stable | symbols | exercised | 3,4,18 |  |
| tool | `find_overloads` | experimental | symbols | exercised | closure | closure probe: base 2, derived+inherited 1 (private excluded), NotFound on bad type |
| tool | `symbol_relationships` | stable | symbols | exercised | 3,4,18 |  |
| tool | `find_references_bulk` | stable | symbols | exercised | 5,11,14 |  |
| tool | `find_property_writes` | stable | symbols | exercised | 3,4,18 |  |
| tool | `find_type_consumers` | experimental | symbols | exercised | 3,4,18 |  |
| tool | `probe_position` | experimental | symbols | exercised | 3,4,18;5,11,14 |  |
| tool | `enclosing_symbol` | stable | symbols | exercised | 5,11,14 |  |
| tool | `goto_type_definition` | stable | symbols | exercised | 5,11,14 |  |
| tool | `get_completions` | stable | symbols | exercised | 5,11,14 |  |
| tool | `get_symbol_outline` | stable | symbols | exercised | 5,11,14;8,8b,9 |  |
| tool | `project_diagnostics` | stable | analysis | exercised | 1,2;6,7;8,8b,9;15,16 |  |
| tool | `diagnostic_details` | stable | analysis | exercised | 1,2 |  |
| tool | `type_hierarchy` | stable | analysis | exercised | 3,4,18 |  |
| tool | `callers_callees` | stable | analysis | exercised | 3,4,18 |  |
| tool | `impact_analysis` | stable | analysis | exercised | 3,4,18 |  |
| tool | `find_type_mutations` | stable | analysis | exercised | 3,4,18 |  |
| tool | `find_type_usages` | stable | analysis | exercised | 3,4,18 |  |
| tool | `build_workspace` | stable | validation | exercised | 8,8b,9 |  |
| tool | `build_project` | stable | validation | exercised | 8,8b,9;10,12,13 |  |
| tool | `test_discover` | stable | validation | exercised | 3,4,18;8,8b,9;10,12,13 |  |
| tool | `test_run` | stable | validation | exercised | 5,11,14;8,8b,9;10,12,13;15,16 |  |
| tool | `test_related` | stable | validation | exercised | 3,4,18;5,11,14;8,8b,9 |  |
| tool | `test_related_files` | stable | validation | exercised | 3,4,18;5,11,14;8,8b,9 |  |
| tool | `find_unused_symbols` | stable | advanced-analysis | exercised | 1,2;6,7;8,8b,9 |  |
| tool | `get_di_registrations` | stable | advanced-analysis | exercised | 5,11,14;10,12,13 |  |
| tool | `get_complexity_metrics` | stable | advanced-analysis | exercised | 1,2;8,8b,9 |  |
| tool | `find_reflection_usages` | stable | advanced-analysis | exercised | 5,11,14 |  |
| tool | `trace_exception_flow` | experimental | advanced-analysis | exercised | 3,4,18 |  |
| tool | `get_namespace_dependencies` | stable | advanced-analysis | exercised | 1,2;15,16 |  |
| tool | `get_nuget_dependencies` | stable | advanced-analysis | exercised | 1,2 |  |
| tool | `semantic_search` | stable | advanced-analysis | exercised | 5,11,14 |  |
| tool | `find_duplicated_methods` | stable | advanced-analysis | exercised | 1,2 |  |
| tool | `find_duplicate_helpers` | experimental | advanced-analysis | exercised | 1,2 |  |
| tool | `find_dead_locals` | experimental | advanced-analysis | exercised | 1,2 |  |
| tool | `find_dead_fields` | experimental | advanced-analysis | exercised | 1,2 |  |
| tool | `symbol_impact_sweep` | experimental | analysis | exercised | 3,4,18 |  |
| tool | `test_reference_map` | experimental | validation | exercised | 8,8b,9 |  |
| tool | `validate_workspace` | experimental | validation | exercised | 8,8b,9 |  |
| tool | `validate_recent_git_changes` | experimental | validation | exercised | 5,11,14;8,8b,9 |  |
| tool | `workspace_fork_apply` | experimental | validation | exercised-apply | 10,12,13 |  |
| tool | `test_coverage` | stable | validation | exercised | 8,8b,9 |  |
| tool | `find_consumers` | stable | analysis | exercised | 3,4,18 |  |
| tool | `get_cohesion_metrics` | stable | analysis | exercised | 1,2 |  |
| tool | `get_coupling_metrics` | stable | analysis | exercised | 1,2 |  |
| tool | `preview_record_field_addition` | experimental | analysis | exercised | 6,7 |  |
| tool | `find_shared_members` | stable | analysis | exercised | 3,4,18;6,7 |  |
| tool | `suggest_refactorings` | stable | advanced-analysis | exercised | 1,2 |  |
| tool | `analyze_data_flow` | stable | advanced-analysis | exercised | 1,2;3,4,18;6,7 |  |
| tool | `analyze_control_flow` | stable | advanced-analysis | exercised | 3,4,18 |  |
| tool | `compile_check` | stable | validation | exercised | 1,2;5,11,14;6,7;8,8b,9;10,12,13;15,16 |  |
| tool | `list_analyzers` | stable | analysis | exercised | 1,2;6,7 |  |
| tool | `get_operations` | stable | advanced-analysis | exercised | 3,4,18 |  |
| tool | `analyze_snippet` | stable | analysis | exercised | 5,11,14;15,16 |  |
| tool | `verify_pragma_suppresses` | stable | validation | exercised | 6,7 |  |
| tool | `find_duplicated_code` | stable | advanced-analysis | exercised | 1,2;8,8b,9 |  |
| tool | `get_test_coverage_map` | stable | validation | exercised | 8,8b,9 |  |
| tool | `semantic_grep` | experimental | analysis | exercised | 5,11,14 |  |
| tool | `rename_preview` | stable | refactoring | exercised | 5,11,14;6,7 |  |
| tool | `rename_apply` | stable | refactoring | exercised-apply | 5,11,14;6,7 |  |
| tool | `organize_usings_preview` | stable | refactoring | exercised | 6,7 |  |
| tool | `organize_usings_apply` | stable | refactoring | exercised-apply | 6,7 |  |
| tool | `format_document_preview` | stable | refactoring | exercised | 6,7;8,8b,9 |  |
| tool | `format_document_apply` | stable | refactoring | exercised-apply | 6,7;8,8b,9 |  |
| tool | `format_check` | experimental | refactoring | exercised | 6,7 |  |
| tool | `code_fix_preview` | stable | refactoring | exercised | 6,7 |  |
| tool | `code_fix_apply` | stable | refactoring | exercised-apply | 6,7 |  |
| tool | `restructure_preview` | experimental | refactoring | exercised | 6,7 |  |
| tool | `replace_string_literals_preview` | experimental | refactoring | exercised | 6,7 |  |
| tool | `change_signature_preview` | experimental | refactoring | exercised | 6,7 |  |
| tool | `parameter_object_preview` | experimental | refactoring | exercised | 6,7 |  |
| tool | `symbol_refactor_preview` | experimental | refactoring | exercised | 6,7 |  |
| tool | `split_service_with_di_preview` | experimental | refactoring | exercised | 6,7 |  |
| tool | `record_field_add_with_satellites_preview` | experimental | refactoring | exercised | 6,7 |  |
| tool | `get_code_actions` | stable | code-actions | exercised | 6,7 |  |
| tool | `preview_code_action` | stable | code-actions | exercised | 6,7 |  |
| tool | `apply_code_action` | stable | code-actions | exercised-apply | 6,7 |  |
| tool | `move_type_to_file_preview` | stable | refactoring | exercised | 10,12,13 |  |
| tool | `move_type_to_file_apply` | experimental | refactoring | exercised-apply | 10,12,13 |  |
| tool | `change_type_namespace_preview` | experimental | refactoring | exercised | 6,7 |  |
| tool | `extract_interface_preview` | experimental | refactoring | exercised | 6,7;10,12,13 |  |
| tool | `extract_interface_apply` | experimental | refactoring | exercised-apply | 6,7 |  |
| tool | `bulk_replace_type_preview` | stable | refactoring | exercised | 6,7 |  |
| tool | `bulk_replace_type_apply` | experimental | refactoring | exercised-apply | 6,7 |  |
| tool | `replace_invocation_preview` | experimental | refactoring | exercised | 6,7 |  |
| tool | `extract_type_preview` | stable | refactoring | exercised | 6,7 |  |
| tool | `extract_type_apply` | experimental | refactoring | exercised-apply | 6,7 |  |
| tool | `extract_method_preview` | stable | refactoring | exercised | 1,2;6,7 |  |
| tool | `extract_method_apply` | experimental | refactoring | exercised-apply | 6,7 |  |
| tool | `extract_shared_expression_to_helper_preview` | experimental | refactoring | exercised | 6,7 |  |
| tool | `revert_last_apply` | stable | undo | exercised-apply | 6,7;8,8b,9 |  |
| tool | `revert_apply_by_sequence` | stable | undo | exercised-apply | 8,8b,9 |  |
| tool | `apply_with_verify` | experimental | undo | exercised-apply | 6,7 |  |
| tool | `fix_all_preview` | experimental | refactoring | exercised | 6,7 |  |
| tool | `fix_all_apply` | experimental | refactoring | exercised-apply | 6,7 |  |
| tool | `format_range_preview` | stable | refactoring | exercised | 6,7 |  |
| tool | `format_range_apply` | experimental | refactoring | exercised-apply | 6,7 |  |
| tool | `apply_text_edit` | stable | editing | exercised-apply | 6,7;8,8b,9 |  |
| tool | `apply_multi_file_edit` | experimental | editing | exercised-apply | 6,7;8,8b,9 |  |
| tool | `preview_multi_file_edit` | experimental | editing | exercised | 6,7 |  |
| tool | `preview_multi_file_edit_apply` | experimental | editing | exercised-apply | 6,7;10,12,13 |  |
| tool | `create_file_preview` | stable | file-operations | exercised | 6,7;10,12,13 |  |
| tool | `create_file_apply` | experimental | file-operations | exercised-apply | 6,7;10,12,13 |  |
| tool | `delete_file_preview` | stable | file-operations | exercised | 10,12,13 |  |
| tool | `delete_file_apply` | experimental | file-operations | exercised-apply | 10,12,13 |  |
| tool | `move_file_preview` | stable | file-operations | exercised | 10,12,13 |  |
| tool | `move_file_apply` | experimental | file-operations | exercised-apply | 10,12,13 |  |
| tool | `remove_dead_code_preview` | stable | dead-code | exercised | 1,2;6,7 |  |
| tool | `remove_dead_code_apply` | experimental | dead-code | exercised-apply | 6,7 |  |
| tool | `remove_interface_member_preview` | experimental | dead-code | exercised | 6,7 |  |
| tool | `add_pragma_suppression` | stable | editing | exercised-apply | 6,7;8,8b,9 |  |
| tool | `pragma_scope_widen` | stable | editing | exercised-apply | 6,7 |  |
| tool | `get_prompt_text` | experimental | prompts | exercised | 5,11,14;15,16 |  |
| tool | `recommend_workflow` | experimental | orchestration | exercised | 5,11,14 |  |
| tool | `add_package_reference_preview` | stable | project-mutation | exercised | 10,12,13 |  |
| tool | `remove_package_reference_preview` | stable | project-mutation | exercised | 10,12,13 |  |
| tool | `add_project_reference_preview` | stable | project-mutation | exercised | 10,12,13 |  |
| tool | `remove_project_reference_preview` | stable | project-mutation | exercised | 10,12,13 |  |
| tool | `set_project_property_preview` | stable | project-mutation | exercised-apply | 10,12,13 |  |
| tool | `add_target_framework_preview` | stable | project-mutation | exercised | 10,12,13 |  |
| tool | `remove_target_framework_preview` | stable | project-mutation | exercised | 10,12,13 |  |
| tool | `set_conditional_property_preview` | stable | project-mutation | exercised-apply | 10,12,13 |  |
| tool | `add_central_package_version_preview` | experimental | project-mutation | exercised | 10,12,13 |  |
| tool | `remove_central_package_version_preview` | stable | project-mutation | exercised | 10,12,13 |  |
| tool | `apply_project_mutation` | experimental | project-mutation | exercised-apply | 10,12,13 |  |
| tool | `scaffold_type_preview` | experimental | scaffolding | exercised | 10,12,13 |  |
| tool | `scaffold_type_apply` | experimental | scaffolding | exercised-apply | 10,12,13 |  |
| tool | `scaffold_test_preview` | stable | scaffolding | exercised | 10,12,13 |  |
| tool | `scaffold_test_batch_preview` | experimental | scaffolding | exercised | 10,12,13 |  |
| tool | `scaffold_first_test_file_preview` | experimental | scaffolding | exercised | 10,12,13 |  |
| tool | `scaffold_test_apply` | experimental | scaffolding | exercised-apply | 10,12,13 |  |
| tool | `move_type_to_project_preview` | experimental | cross-project-refactoring | exercised | 10,12,13 |  |
| tool | `extract_interface_cross_project_preview` | experimental | cross-project-refactoring | exercised | 10,12,13 |  |
| tool | `dependency_inversion_preview` | experimental | cross-project-refactoring | exercised | 10,12,13 |  |
| tool | `migrate_package_preview` | experimental | orchestration | exercised | 10,12,13 |  |
| tool | `split_class_preview` | experimental | orchestration | exercised | 10,12,13 |  |
| tool | `extract_and_wire_interface_preview` | experimental | orchestration | exercised | 10,12,13 |  |
| tool | `apply_composite_preview` | experimental | orchestration | exercised-apply | 6,7;10,12,13 |  |
| tool | `get_syntax_tree` | stable | syntax | exercised | 3,4,18 |  |
| tool | `security_diagnostics` | stable | security | exercised | 1,2 |  |
| tool | `security_analyzer_status` | stable | security | exercised | 1,2 |  |
| tool | `nuget_vulnerability_scan` | stable | security | exercised | 1,2 |  |
| tool | `evaluate_csharp` | stable | scripting | exercised | 5,11,14 |  |
| tool | `get_editorconfig_options` | stable | configuration | exercised | 6,7;8,8b,9 |  |
| tool | `set_editorconfig_option` | stable | configuration | exercised-apply | 6,7;8,8b,9 |  |
| tool | `evaluate_msbuild_property` | stable | project-mutation | exercised | 6,7 |  |
| tool | `evaluate_msbuild_items` | stable | project-mutation | exercised | 6,7 |  |
| tool | `get_msbuild_properties` | stable | project-mutation | exercised | 6,7;10,12,13 |  |
| tool | `set_diagnostic_severity` | stable | configuration | exercised-apply | 6,7;8,8b,9 |  |
| resource | `server_catalog` | stable | server | exercised | 15 |  |
| resource | `server_catalog_full` | experimental | server | exercised | 15 |  |
| resource | `server_catalog_tools_page` | experimental | server | exercised | 15 |  |
| resource | `server_catalog_prompts_page` | experimental | server | exercised | 15 |  |
| resource | `server_catalog_version_diff` | experimental | server | exercised | 15 |  |
| resource | `resource_templates` | stable | server | exercised | 15 |  |
| resource | `workspaces` | stable | workspace | exercised | 15 |  |
| resource | `workspaces_verbose` | stable | workspace | exercised | 15 |  |
| resource | `workspace_status` | stable | workspace | exercised | 15 |  |
| resource | `workspace_status_verbose` | stable | workspace | exercised | 15 |  |
| resource | `workspace_projects` | stable | workspace | exercised | 15 |  |
| resource | `workspace_diagnostics` | stable | analysis | exercised | 15 |  |
| resource | `source_file` | stable | workspace | exercised | 15 |  |
| resource | `source_file_lines` | experimental | workspace | exercised | 15 |  |
| prompt | `explain_error` | experimental | prompts | exercised | 16 | via get_prompt_text + raw prompts/get |
| prompt | `suggest_refactoring` | experimental | prompts | exercised | 16 | via get_prompt_text + raw prompts/get |
| prompt | `review_file` | experimental | prompts | exercised | 16 | via get_prompt_text + raw prompts/get |
| prompt | `analyze_dependencies` | experimental | prompts | exercised | 16 | via get_prompt_text + raw prompts/get |
| prompt | `debug_test_failure` | experimental | prompts | exercised | 16 | via get_prompt_text + raw prompts/get |
| prompt | `refactor_and_validate` | experimental | prompts | exercised | 16 | via get_prompt_text + raw prompts/get |
| prompt | `fix_all_diagnostics` | experimental | prompts | exercised | 16 | via get_prompt_text + raw prompts/get |
| prompt | `guided_package_migration` | experimental | prompts | exercised | 16 | via get_prompt_text + raw prompts/get |
| prompt | `guided_extract_interface` | experimental | prompts | exercised | 16 | via get_prompt_text + raw prompts/get |
| prompt | `security_review` | experimental | prompts | exercised | 16 | via get_prompt_text + raw prompts/get |
| prompt | `discover_capabilities` | experimental | prompts | exercised | 16 | via get_prompt_text + raw prompts/get |
| prompt | `dead_code_audit` | experimental | prompts | exercised | 16 | via get_prompt_text + raw prompts/get |
| prompt | `review_test_coverage` | experimental | prompts | exercised | 16 | via get_prompt_text + raw prompts/get |
| prompt | `review_complexity` | experimental | prompts | exercised | 16 | via get_prompt_text + raw prompts/get |
| prompt | `cohesion_analysis` | experimental | prompts | exercised | 16 | via get_prompt_text + raw prompts/get |
| prompt | `consumer_impact` | experimental | prompts | exercised | 16 | via get_prompt_text + raw prompts/get |
| prompt | `guided_extract_method` | experimental | prompts | exercised | 16 | via get_prompt_text + raw prompts/get |
| prompt | `msbuild_inspection` | experimental | prompts | exercised | 16 | via get_prompt_text + raw prompts/get |
| prompt | `session_undo` | experimental | prompts | exercised | 16 | via get_prompt_text + raw prompts/get |
| prompt | `refactor_loop` | experimental | prompts | exercised | 16 | via get_prompt_text + raw prompts/get |
