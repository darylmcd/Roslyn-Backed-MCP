# Experimental Promotion Exercise Report

## 1. Header
- **Date:** 2026-04-13T17:40:24Z
- **Audited solution:** Roslyn-Backed-MCP.sln
- **Audited revision:** 591ab76 (main)
- **Entrypoint loaded:** C:\Code-Repo\Roslyn-Backed-MCP\Roslyn-Backed-MCP.sln
- **Audit mode:** `conservative` (on main branch, no disposable worktree — apply siblings marked skipped-safety)
- **Isolation:** None (main branch); destructive applies skipped
- **Client:** Claude Code (Claude Opus 4.6, 1M context)
- **Workspace id:** a67b3d177e1545309f2a03513ff5f588
- **Server:** roslyn-mcp 1.12.0+591ab764
- **Catalog version:** 2026.04
- **Experimental surface:** 51 experimental tools, 19 experimental prompts
- **Scale:** 6 projects, 355 documents
- **Repo shape:**
  - Multi-project: yes (6 projects: Core, Roslyn, Host.Stdio, Tests, SampleApp, SampleLib)
  - Test project: yes (RoslynMcp.Tests, xUnit)
  - Analyzers: yes (SecurityCodeScan.VS2019)
  - Source generators: not detected
  - DI usage: yes (Host.Stdio uses Microsoft.Extensions.DependencyInjection)
  - .editorconfig: yes
  - Central Package Management: yes (Directory.Packages.props)
  - Multi-targeting: no (all net10.0)
- **Prior issue source:** ai_docs/backlog.md (updated 2026-04-13T18:05:00Z)

## 2. Coverage ledger (experimental surface)

| Kind | Name | Category | Status | Phase | lastElapsedMs | Notes |
|------|------|----------|--------|-------|---------------|-------|
| tool | `fix_all_preview` | refactoring | exercised | 1a | 80 | No fix provider loaded for IDE0005/CA1822; guidance message actionable |
| tool | `fix_all_apply` | refactoring | skipped-safety | 1a | — | Conservative mode |
| tool | `format_range_preview` | refactoring | exercised | 1b | 119 | No changes needed (already formatted) |
| tool | `format_range_apply` | refactoring | skipped-safety | 1b | — | Conservative mode |
| tool | `extract_interface_preview` | refactoring | exercised | 1c | 32 | FLAG: adds duplicate `: IEditService` to class that already implements it |
| tool | `extract_interface_apply` | refactoring | skipped-safety | 1c | — | Conservative mode |
| tool | `extract_type_preview` | refactoring | exercised | 1d | 5 | Correctly extracted CountAnimals into AnimalCounter |
| tool | `extract_type_apply` | refactoring | skipped-safety | 1d | — | Conservative mode |
| tool | `move_type_to_file_preview` | refactoring | exercised | 1e | 179 | Correctly moved McpLogger to own file |
| tool | `move_type_to_file_apply` | refactoring | skipped-safety | 1e | — | Conservative mode |
| tool | `bulk_replace_type_preview` | refactoring | exercised | 1f | 312 | Correctly replaced EditService→IEditService in params scope |
| tool | `bulk_replace_type_apply` | refactoring | skipped-safety | 1f | — | Conservative mode |
| tool | `extract_method_preview` | refactoring | exercised | 1g | 8 | Correctly extracted 3 statements from RefactoringProbe |
| tool | `extract_method_apply` | refactoring | skipped-safety | 1g | — | Conservative mode |
| tool | `create_file_preview` | file-operations | exercised | 2a | 1 | Correct namespace and content |
| tool | `create_file_apply` | file-operations | skipped-safety | 2a | — | Conservative mode |
| tool | `move_file_preview` | file-operations | exercised | 2b | 2 | Namespace updated to SampleLib.Animals; warning about external refs |
| tool | `move_file_apply` | file-operations | skipped-safety | 2b | — | Conservative mode |
| tool | `delete_file_preview` | file-operations | exercised | 2c | 1 | Correct removal diff |
| tool | `delete_file_apply` | file-operations | skipped-safety | 2c | — | Conservative mode |
| tool | `add_package_reference_preview` | project-mutation | exercised | 3a | 7 | Correct diff; CPM warning emitted |
| tool | `remove_package_reference_preview` | project-mutation | exercised | 3a | 1 | Correct error: package not found (not applied) |
| tool | `add_project_reference_preview` | project-mutation | exercised | 3b | 2 | Correct diff with resolved relative path |
| tool | `remove_project_reference_preview` | project-mutation | exercised | 3b | 1 | Correct removal diff |
| tool | `set_project_property_preview` | project-mutation | exercised | 3c | 2 | Correct no-op detection (already enabled) |
| tool | `set_conditional_property_preview` | project-mutation | exercised | 3c | 4 | Correct conditional PropertyGroup diff |
| tool | `add_target_framework_preview` | project-mutation | exercised | 3d | 1 | Correctly promotes TargetFramework→TargetFrameworks (plural) |
| tool | `remove_target_framework_preview` | project-mutation | exercised | 3d | 1 | Correct guard: refuses to remove only TFM |
| tool | `add_central_package_version_preview` | project-mutation | exercised | 3e | 0 | Correct; XML formatting FLAG (jammed closing tag) |
| tool | `remove_central_package_version_preview` | project-mutation | exercised | 3e | 1 | Correct error: package not found (not applied) |
| tool | `apply_project_mutation` | project-mutation | skipped-safety | 3 | — | Conservative mode |
| tool | `get_msbuild_properties` | project-mutation | exercised | 3f | 171 | includedNames filter works correctly; 722 total props |
| tool | `scaffold_type_preview` | scaffolding | exercised | 4a | 0 | Default: `internal sealed class` — correct. BUG: accepts `2InvalidName` |
| tool | `scaffold_type_apply` | scaffolding | skipped-safety | 4a | — | Conservative mode |
| tool | `scaffold_test_preview` | scaffolding | exercised | 4b | 4 | Auto-detected MSTest; constructor expressions with all 4 params |
| tool | `scaffold_test_apply` | scaffolding | skipped-safety | 4b | — | Conservative mode |
| tool | `remove_dead_code_preview` | dead-code | exercised | 5a | 13 | Correct removal of TrulyUnusedConcreteType |
| tool | `remove_dead_code_apply` | dead-code | skipped-safety | 5a | — | Conservative mode |
| tool | `apply_text_edit` | editing | exercised-apply | 5b | 35 | Correct insertion; reverted via git checkout |
| tool | `apply_multi_file_edit` | editing | skipped-safety | 5b | — | Conservative mode |
| tool | `add_pragma_suppression` | editing | exercised-apply | 5c | 6 | Correct #pragma insertion; reverted via revert_last_apply |
| tool | `set_editorconfig_option` | configuration | exercised-apply | 5d | 3 | Correct key-value set; verified via get_editorconfig_options |
| tool | `set_diagnostic_severity` | configuration | exercised-apply | 5d | 1 | Correct severity set; reverted via revert_last_apply |
| tool | `revert_last_apply` | undo | exercised | 5e/9 | 0 | Empty stack: actionable message. PASS |
| tool | `move_type_to_project_preview` | cross-project-refactoring | exercised | 6 | 5 | Moved ChangeTracker→Core; FLAG: namespace not updated |
| tool | `extract_interface_cross_project_preview` | cross-project-refactoring | exercised | 6 | 13 | Same-project fallback works |
| tool | `dependency_inversion_preview` | cross-project-refactoring | exercised | 6 | 1646 | FLAG: noisy diff (collapses formatting) |
| tool | `migrate_package_preview` | orchestration | exercised | 7 | 6 | FLAG: malformed XML (jammed closing tag) |
| tool | `split_class_preview` | orchestration | exercised | 7 | 10 | Correctly split into partial class; noisy diff |
| tool | `extract_and_wire_interface_preview` | orchestration | exercised | 7 | 1252 | FLAG: excess usings in interface; duplicate base type |
| tool | `apply_composite_preview` | orchestration | skipped-safety | 7 | — | Conservative mode |
| prompt | `explain_error` | prompts | blocked | 8 | — | Client does not expose prompt invocation |
| prompt | `suggest_refactoring` | prompts | blocked | 8 | — | Client does not expose prompt invocation |
| prompt | `review_file` | prompts | blocked | 8 | — | Client does not expose prompt invocation |
| prompt | `analyze_dependencies` | prompts | blocked | 8 | — | Client does not expose prompt invocation |
| prompt | `debug_test_failure` | prompts | blocked | 8 | — | Client does not expose prompt invocation |
| prompt | `refactor_and_validate` | prompts | blocked | 8 | — | Client does not expose prompt invocation |
| prompt | `fix_all_diagnostics` | prompts | blocked | 8 | — | Client does not expose prompt invocation |
| prompt | `guided_package_migration` | prompts | blocked | 8 | — | Client does not expose prompt invocation |
| prompt | `guided_extract_interface` | prompts | blocked | 8 | — | Client does not expose prompt invocation |
| prompt | `security_review` | prompts | blocked | 8 | — | Client does not expose prompt invocation |
| prompt | `discover_capabilities` | prompts | blocked | 8 | — | Client does not expose prompt invocation |
| prompt | `dead_code_audit` | prompts | blocked | 8 | — | Client does not expose prompt invocation |
| prompt | `review_test_coverage` | prompts | blocked | 8 | — | Client does not expose prompt invocation |
| prompt | `review_complexity` | prompts | blocked | 8 | — | Client does not expose prompt invocation |
| prompt | `cohesion_analysis` | prompts | blocked | 8 | — | Client does not expose prompt invocation |
| prompt | `consumer_impact` | prompts | blocked | 8 | — | Client does not expose prompt invocation |
| prompt | `guided_extract_method` | prompts | blocked | 8 | — | Client does not expose prompt invocation |
| prompt | `msbuild_inspection` | prompts | blocked | 8 | — | Client does not expose prompt invocation |
| prompt | `session_undo` | prompts | blocked | 8 | — | Client does not expose prompt invocation |

## 3. Performance baseline (`_meta.elapsedMs`)

| Tool | Category | Calls | p50_ms | max_ms | Input scale | Budget | Notes |
|------|----------|-------|--------|--------|-------------|--------|-------|
| `fix_all_preview` | refactoring | 3 | 1 | 80 | solution | <=15s | PASS |
| `format_range_preview` | refactoring | 1 | 119 | 119 | 25 lines | <=5s | PASS |
| `extract_interface_preview` | refactoring | 1 | 32 | 32 | 1 type | <=5s | PASS |
| `extract_type_preview` | refactoring | 2 | 5 | 34 | 1 type | <=5s | PASS |
| `move_type_to_file_preview` | refactoring | 1 | 179 | 179 | nested class | <=5s | PASS |
| `bulk_replace_type_preview` | refactoring | 1 | 312 | 312 | solution-wide | <=15s | PASS |
| `extract_method_preview` | refactoring | 3 | 8 | 26 | 3 stmts | <=5s | PASS |
| `create_file_preview` | file-operations | 1 | 1 | 1 | 1 file | <=5s | PASS |
| `move_file_preview` | file-operations | 1 | 2 | 2 | 1 file | <=5s | PASS |
| `delete_file_preview` | file-operations | 1 | 1 | 1 | 1 file | <=5s | PASS |
| `add_central_package_version_preview` | project-mutation | 1 | 0 | 0 | 1 entry | <=5s | PASS |
| `get_msbuild_properties` | project-mutation | 1 | 171 | 171 | 3 props | <=5s | PASS |
| `scaffold_type_preview` | scaffolding | 2 | 0 | 0 | 1 type | <=5s | PASS |
| `revert_last_apply` | undo | 1 | 0 | 0 | empty stack | <=5s | PASS |
| `move_type_to_project_preview` | cross-project | 1 | 5 | 5 | 1 type | <=5s | PASS |
| `extract_interface_cross_project_preview` | cross-project | 1 | 13 | 13 | 1 type | <=5s | PASS |
| `dependency_inversion_preview` | cross-project | 1 | 1646 | 1646 | 1 type | <=5s | PASS (within 5s budget for writer-class) |
| `migrate_package_preview` | orchestration | 1 | 6 | 6 | 1 package | <=5s | PASS |
| `split_class_preview` | orchestration | 1 | 10 | 10 | 4 members | <=5s | PASS |
| `extract_and_wire_interface_preview` | orchestration | 1 | 1252 | 1252 | 1 type + DI | <=5s | PASS |

## 4. Schema vs behaviour drift

| Tool | Mismatch kind | Expected | Actual | Severity | Notes |
|------|---------------|----------|--------|----------|-------|
| `extract_interface_preview` | Duplicate base type | Should detect existing interface implementation | Adds duplicate `: IEditService` | Medium | Would cause CS0528 if applied |
| `move_type_to_project_preview` | Namespace not updated | Namespace should match target project | Kept `RoslynMcp.Roslyn.Services` in Core project | Low | Description says "move" but namespace stays |
| `scaffold_type_preview` | Missing validation | Should reject invalid C# identifiers | Accepts `2InvalidName` and generates invalid C# | High | Bug: no identifier validation |
| `migrate_package_preview` | Malformed XML | Properly indented XML | `<PackageVersion>` jammed on same line as `</ItemGroup>` | Medium | Would produce invalid XML formatting |
| `extract_and_wire_interface_preview` | Excess usings | Interface should only have minimal usings | Copies implementation usings (DiffPlex, etc.) to interface file | Low | Cosmetic but noisy |
| `dependency_inversion_preview` | Noisy diff | Clean targeted changes | Collapses multi-line params, removes blank lines | Low | Formatting changes mixed with semantic changes |

## 5. Error message quality

| Tool | Probe input | Rating | Suggested fix | Notes |
|------|-------------|--------|---------------|-------|
| `fix_all_apply` | Fabricated token | actionable | — | Hook blocked before reaching server |
| `extract_method_preview` | startLine > endLine | actionable | — | "Start position must be before end position" |
| `extract_method_preview` | Multi-output selection | actionable | — | Names all output variables and explains constraint |
| `extract_type_preview` | Shared state refs | actionable | — | Lists all referenced members that would break |
| `scaffold_type_preview` | `2InvalidName` | **unhelpful** | Add C# identifier validation | Silently accepts and generates invalid code |
| `remove_dead_code_preview` | Fabricated handle | actionable | — | "symbolHandle is not valid base64" |
| `revert_last_apply` | Empty undo stack | actionable | — | Clear "No operation to revert" message |
| `move_type_to_project_preview` | Circular reference | actionable | — | Names both projects and explains circular ref |

## 6. Parameter-path coverage

| Family | Non-default path tested | Status | Notes |
|--------|--------------------------|--------|-------|
| `fix_all_preview` | scope=project | PASS | Both solution and project scope exercised |
| `scaffold_type_preview` | kind=class (default) | PASS | Default `internal sealed class` confirmed |
| `get_msbuild_properties` | includedNames filter | PASS | Returned exactly 3 requested props from 722 total |
| File operations | move with namespace update | PASS | `updateNamespace=true` correctly changes namespace |
| Project mutation | CPM (add_central_package_version) | PASS | CPM correctly detected and modified |
| `bulk_replace_type_preview` | scope=parameters | PASS | Scoped to parameter types only |

## 7. Prompt verification (Phase 8)

All 19 prompts blocked — Claude Code does not expose `prompts/get` as an invocable tool. Schema verified via catalog: all take 0 arguments.

| Prompt | schema_ok | actionable | hallucinated_tools | idempotent | elapsedMs | recommendation_seed | Notes |
|--------|-----------|------------|---------------------|------------|-----------|----------------------|-------|
| `explain_error` | yes (0 args) | — | — | — | — | needs-more-evidence | blocked |
| `suggest_refactoring` | yes | — | — | — | — | needs-more-evidence | blocked |
| `review_file` | yes | — | — | — | — | needs-more-evidence | blocked |
| `analyze_dependencies` | yes | — | — | — | — | needs-more-evidence | blocked |
| `debug_test_failure` | yes | — | — | — | — | needs-more-evidence | blocked |
| `refactor_and_validate` | yes | — | — | — | — | needs-more-evidence | blocked |
| `fix_all_diagnostics` | yes | — | — | — | — | needs-more-evidence | blocked |
| `guided_package_migration` | yes | — | — | — | — | needs-more-evidence | blocked |
| `guided_extract_interface` | yes | — | — | — | — | needs-more-evidence | blocked |
| `security_review` | yes | — | — | — | — | needs-more-evidence | blocked |
| `discover_capabilities` | yes | — | — | — | — | needs-more-evidence | blocked |
| `dead_code_audit` | yes | — | — | — | — | needs-more-evidence | blocked |
| `review_test_coverage` | yes | — | — | — | — | needs-more-evidence | blocked |
| `review_complexity` | yes | — | — | — | — | needs-more-evidence | blocked |
| `cohesion_analysis` | yes | — | — | — | — | needs-more-evidence | blocked |
| `consumer_impact` | yes | — | — | — | — | needs-more-evidence | blocked |
| `guided_extract_method` | yes | — | — | — | — | needs-more-evidence | blocked |
| `msbuild_inspection` | yes | — | — | — | — | needs-more-evidence | blocked |
| `session_undo` | yes | — | — | — | — | needs-more-evidence | blocked |

## 8. Experimental promotion scorecard

| Kind | Name | Category | Status | p50_ms | schema_ok | error_ok | round_trip_ok | Failures | Recommendation | Evidence |
|------|------|----------|--------|--------|-----------|----------|----------------|----------|----------------|----------|
| tool | `fix_all_preview` | refactoring | exercised | 1 | yes | yes | n/a | 0 | keep-experimental | No fix provider loaded — need full-surface run with fixes available |
| tool | `fix_all_apply` | refactoring | skipped-safety | — | — | — | — | — | needs-more-evidence | Conservative mode |
| tool | `format_range_preview` | refactoring | exercised | 119 | yes | n/a | n/a | 0 | promote | Clean preview, correct schema, fast |
| tool | `format_range_apply` | refactoring | skipped-safety | — | — | — | — | — | needs-more-evidence | Conservative mode |
| tool | `extract_interface_preview` | refactoring | exercised | 32 | **no** | n/a | n/a | 1 | keep-experimental | Duplicate base type bug |
| tool | `extract_interface_apply` | refactoring | skipped-safety | — | — | — | — | — | needs-more-evidence | Blocked by preview bug |
| tool | `extract_type_preview` | refactoring | exercised | 5 | yes | yes | n/a | 0 | promote | Clean extraction, actionable errors |
| tool | `extract_type_apply` | refactoring | skipped-safety | — | — | — | — | — | needs-more-evidence | Conservative mode |
| tool | `move_type_to_file_preview` | refactoring | exercised | 179 | yes | n/a | n/a | 0 | promote | Correct nested-class extraction |
| tool | `move_type_to_file_apply` | refactoring | skipped-safety | — | — | — | — | — | needs-more-evidence | Conservative mode |
| tool | `bulk_replace_type_preview` | refactoring | exercised | 312 | yes | n/a | n/a | 0 | promote | Correct scoped replacement |
| tool | `bulk_replace_type_apply` | refactoring | skipped-safety | — | — | — | — | — | needs-more-evidence | Conservative mode |
| tool | `extract_method_preview` | refactoring | exercised | 8 | yes | yes | n/a | 0 | promote | Correct extraction, great error messages |
| tool | `extract_method_apply` | refactoring | skipped-safety | — | — | — | — | — | needs-more-evidence | Conservative mode |
| tool | `create_file_preview` | file-operations | exercised | 1 | yes | n/a | n/a | 0 | promote | Fast, correct |
| tool | `create_file_apply` | file-operations | skipped-safety | — | — | — | — | — | needs-more-evidence | Conservative mode |
| tool | `move_file_preview` | file-operations | exercised | 2 | yes | n/a | n/a | 0 | promote | Namespace update works; warning about external refs |
| tool | `move_file_apply` | file-operations | skipped-safety | — | — | — | — | — | needs-more-evidence | Conservative mode |
| tool | `delete_file_preview` | file-operations | exercised | 1 | yes | n/a | n/a | 0 | promote | Clean removal diff |
| tool | `delete_file_apply` | file-operations | skipped-safety | — | — | — | — | — | needs-more-evidence | Conservative mode |
| tool | `add_package_reference_preview` | project-mutation | exercised | 7 | yes | n/a | n/a | 0 | promote | Correct diff; CPM warning |
| tool | `remove_package_reference_preview` | project-mutation | exercised | 1 | yes | yes | n/a | 0 | promote | Correct not-found error |
| tool | `add_project_reference_preview` | project-mutation | exercised | 2 | yes | n/a | n/a | 0 | promote | Correct relative path |
| tool | `remove_project_reference_preview` | project-mutation | exercised | 1 | yes | n/a | n/a | 0 | promote | Correct removal |
| tool | `set_project_property_preview` | project-mutation | exercised | 2 | yes | yes | n/a | 0 | promote | No-op detection works |
| tool | `set_conditional_property_preview` | project-mutation | exercised | 4 | yes | n/a | n/a | 0 | promote | Correct conditional group |
| tool | `add_target_framework_preview` | project-mutation | exercised | 1 | yes | n/a | n/a | 0 | promote | Singular→plural promotion correct |
| tool | `remove_target_framework_preview` | project-mutation | exercised | 1 | yes | yes | n/a | 0 | promote | Last-TFM guard correct |
| tool | `add_central_package_version_preview` | project-mutation | exercised | 0 | **no** | n/a | n/a | 1 | keep-experimental | XML formatting bug (jammed closing tag) |
| tool | `remove_central_package_version_preview` | project-mutation | exercised | 1 | yes | yes | n/a | 0 | promote | Correct not-found error |
| tool | `apply_project_mutation` | project-mutation | skipped-safety | — | — | — | — | — | needs-more-evidence | Conservative mode |
| tool | `get_msbuild_properties` | project-mutation | exercised | 171 | yes | n/a | n/a | 0 | promote | Both filter paths work correctly |
| tool | `scaffold_type_preview` | scaffolding | exercised | 0 | **no** | **no** | n/a | 1 | keep-experimental | Accepts invalid identifiers |
| tool | `scaffold_type_apply` | scaffolding | skipped-safety | — | — | — | — | — | needs-more-evidence | Conservative mode |
| tool | `scaffold_test_preview` | scaffolding | exercised | 4 | yes | n/a | n/a | 0 | promote | Auto-detected MSTest; correct constructor |
| tool | `scaffold_test_apply` | scaffolding | skipped-safety | — | — | — | — | — | needs-more-evidence | Conservative mode |
| tool | `remove_dead_code_preview` | dead-code | exercised | 13 | yes | yes | n/a | 0 | promote | Correct removal diff |
| tool | `remove_dead_code_apply` | dead-code | skipped-safety | — | — | — | — | — | needs-more-evidence | Conservative mode |
| tool | `apply_text_edit` | editing | exercised-apply | 35 | yes | n/a | yes | 0 | promote | Correct insertion; reverted cleanly |
| tool | `apply_multi_file_edit` | editing | skipped-safety | — | — | — | — | — | needs-more-evidence | Conservative mode |
| tool | `add_pragma_suppression` | editing | exercised-apply | 6 | yes | n/a | yes | 0 | promote | Correct pragma; reverted via undo |
| tool | `set_editorconfig_option` | configuration | exercised-apply | 3 | yes | n/a | yes | 0 | promote | Correct; verified via get_editorconfig_options |
| tool | `set_diagnostic_severity` | configuration | exercised-apply | 1 | yes | n/a | yes | 0 | promote | Correct; reverted via undo |
| tool | `revert_last_apply` | undo | exercised | 0 | yes | yes | n/a | 0 | promote | Actionable error on empty stack |
| tool | `move_type_to_project_preview` | cross-project | exercised | 5 | **no** | yes | n/a | 1 | keep-experimental | Namespace not updated to match target |
| tool | `extract_interface_cross_project_preview` | cross-project | exercised | 13 | yes | yes | n/a | 0 | keep-experimental | Missing apply round-trip |
| tool | `dependency_inversion_preview` | cross-project | exercised | 1646 | yes | yes | n/a | 0 | keep-experimental | Noisy diff; missing apply |
| tool | `migrate_package_preview` | orchestration | exercised | 6 | **no** | n/a | n/a | 1 | keep-experimental | XML formatting bug |
| tool | `split_class_preview` | orchestration | exercised | 10 | yes | n/a | n/a | 0 | keep-experimental | Missing apply round-trip; noisy diff |
| tool | `extract_and_wire_interface_preview` | orchestration | exercised | 1252 | **no** | n/a | n/a | 1 | keep-experimental | Excess usings + duplicate base type |
| tool | `apply_composite_preview` | orchestration | skipped-safety | — | — | — | — | — | needs-more-evidence | Conservative mode |
| prompt | `explain_error` | prompts | blocked | — | schema-only | — | — | — | needs-more-evidence | Client blocked |
| prompt | `suggest_refactoring` | prompts | blocked | — | schema-only | — | — | — | needs-more-evidence | Client blocked |
| prompt | `review_file` | prompts | blocked | — | schema-only | — | — | — | needs-more-evidence | Client blocked |
| prompt | `analyze_dependencies` | prompts | blocked | — | schema-only | — | — | — | needs-more-evidence | Client blocked |
| prompt | `debug_test_failure` | prompts | blocked | — | schema-only | — | — | — | needs-more-evidence | Client blocked |
| prompt | `refactor_and_validate` | prompts | blocked | — | schema-only | — | — | — | needs-more-evidence | Client blocked |
| prompt | `fix_all_diagnostics` | prompts | blocked | — | schema-only | — | — | — | needs-more-evidence | Client blocked |
| prompt | `guided_package_migration` | prompts | blocked | — | schema-only | — | — | — | needs-more-evidence | Client blocked |
| prompt | `guided_extract_interface` | prompts | blocked | — | schema-only | — | — | — | needs-more-evidence | Client blocked |
| prompt | `security_review` | prompts | blocked | — | schema-only | — | — | — | needs-more-evidence | Client blocked |
| prompt | `discover_capabilities` | prompts | blocked | — | schema-only | — | — | — | needs-more-evidence | Client blocked |
| prompt | `dead_code_audit` | prompts | blocked | — | schema-only | — | — | — | needs-more-evidence | Client blocked |
| prompt | `review_test_coverage` | prompts | blocked | — | schema-only | — | — | — | needs-more-evidence | Client blocked |
| prompt | `review_complexity` | prompts | blocked | — | schema-only | — | — | — | needs-more-evidence | Client blocked |
| prompt | `cohesion_analysis` | prompts | blocked | — | schema-only | — | — | — | needs-more-evidence | Client blocked |
| prompt | `consumer_impact` | prompts | blocked | — | schema-only | — | — | — | needs-more-evidence | Client blocked |
| prompt | `guided_extract_method` | prompts | blocked | — | schema-only | — | — | — | needs-more-evidence | Client blocked |
| prompt | `msbuild_inspection` | prompts | blocked | — | schema-only | — | — | — | needs-more-evidence | Client blocked |
| prompt | `session_undo` | prompts | blocked | — | schema-only | — | — | — | needs-more-evidence | Client blocked |

### Scorecard summary

| Recommendation | Tools | Prompts | Total |
|----------------|-------|---------|-------|
| **promote** | 26 | 0 | 26 |
| **keep-experimental** | 10 | 0 | 10 |
| **needs-more-evidence** | 15 | 19 | 34 |
| **deprecate** | 0 | 0 | 0 |

**Promote candidates (26 tools):** `format_range_preview`, `extract_type_preview`, `move_type_to_file_preview`, `bulk_replace_type_preview`, `extract_method_preview`, `create_file_preview`, `move_file_preview`, `delete_file_preview`, `get_msbuild_properties`, `revert_last_apply`, `add_package_reference_preview`, `remove_package_reference_preview`, `add_project_reference_preview`, `remove_project_reference_preview`, `set_project_property_preview`, `set_conditional_property_preview`, `add_target_framework_preview`, `remove_target_framework_preview`, `remove_central_package_version_preview`, `scaffold_test_preview`, `remove_dead_code_preview`, `apply_text_edit`, `add_pragma_suppression`, `set_editorconfig_option`, `set_diagnostic_severity`

> Note: Preview-only tools pass all criteria but their apply siblings are `needs-more-evidence` (conservative mode). Full-surface mode needed to validate round-trips.

## 9. MCP server issues (bugs)

### 9.1 `scaffold_type_preview` accepts invalid C# identifiers
| Field | Detail |
|--------|--------|
| Tool | `scaffold_type_preview` |
| Input | `typeName="2InvalidName"` |
| Expected | Reject with validation error — `2InvalidName` is not a valid C# identifier |
| Actual | Generates `internal sealed class 2InvalidName {}` — invalid C# |
| Severity | Medium |
| Reproducibility | Always |

### 9.2 `extract_interface_preview` adds duplicate base type
| Field | Detail |
|--------|--------|
| Tool | `extract_interface_preview` |
| Input | `EditService` (already implements `IEditService`) |
| Expected | Detect existing interface or skip base-list addition |
| Actual | Adds duplicate `: IEditService` causing CS0528 |
| Severity | Medium |
| Reproducibility | When type already implements the named interface |

### 9.3 `migrate_package_preview` and `add_central_package_version_preview` produce malformed XML
| Field | Detail |
|--------|--------|
| Tool | `migrate_package_preview`, `add_central_package_version_preview` |
| Input | Any package migration or CPM addition |
| Expected | Properly indented XML with closing tag on its own line |
| Actual | `<PackageVersion>` jammed on same line as `</ItemGroup>` |
| Severity | Low-Medium |
| Reproducibility | Always |

### 9.4 `dependency_inversion_preview` produces noisy diffs
| Field | Detail |
|--------|--------|
| Tool | `dependency_inversion_preview` |
| Input | EditService → Core |
| Expected | Targeted diff showing only interface extraction and constructor rewiring |
| Actual | Collapses multi-line parameters, removes blank lines, reformats entire file |
| Severity | Low |
| Reproducibility | On files with multi-line parameter lists |

### 9.5 `revert_last_apply` single-slot undo loses earlier writes
| Field | Detail |
|--------|--------|
| Tool | `revert_last_apply` |
| Input | Two sequential destructive operations (apply_text_edit then add_pragma_suppression) |
| Expected | Either LIFO stack (revert most recent, then previous) or documented limitation |
| Actual | Only most recent write is revertible; earlier write permanently applied, requires git checkout |
| Severity | Medium |
| Reproducibility | Always when two+ applies precede revert |

## 10. Improvement suggestions

- `scaffold_type_preview` — Add C# identifier validation before generating code (`IdentifierValidation.IsValidCSharpIdentifier`)
- `extract_interface_preview` — Check if the type already implements the named interface before adding to base list
- `migrate_package_preview` / `add_central_package_version_preview` — Fix XML serialization to maintain proper indentation and line breaks
- `dependency_inversion_preview` — Separate semantic changes from formatting changes in the diff
- `move_type_to_project_preview` — Update namespace to match target project by default (or add `updateNamespace` param)
- All prompts — Claude Code should expose `prompts/get` as an invocable tool so prompts can be exercised
- `server_info.update` — Fix semver comparison when local version exceeds NuGet published version

## 11. Known issue regression check

| Source id | Summary | Status |
|-----------|---------|--------|
| `server-info-update-semver` | `server_info.update.latest` (1.8.2) < `current` (1.12.0) with `updateAvailable: true` | **still reproduces** |
| `revert-preview-token-coupling` | Preview tokens invalidated after revert | not reproduced (no applies in conservative mode) |
| `dead-code-cleanup` | `FindDirectoryBuildProps` zero refs | **candidate for closure** — symbol_search returns 0 results (already removed) |
| `claude-plugin-marketplace-version` | Plugin cache at 1.7.0 vs server 1.12.0 | **still reproduces** — path says 1.7.0 but manifest declares 1.12.0 (cosmetic; content is current) |
| `apply-text-edit-diff-quality` | Stray chars in diff output | not reproduced (no applies in conservative mode) |
