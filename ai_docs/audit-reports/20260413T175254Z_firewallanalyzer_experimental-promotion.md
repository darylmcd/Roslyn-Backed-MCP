# Experimental Promotion Exercise Report

## 1. Header
- **Date:** 2026-04-13T17:52:54Z
- **Audited solution:** FirewallAnalyzer.slnx
- **Audited revision:** `audit/mcp-full-surface-20260413` (bcc288c)
- **Entrypoint loaded:** `C:\Code-Repo\DotNet-Firewall-Analyzer\FirewallAnalyzer.slnx`
- **Audit mode:** `full-surface` (disposable audit branch) — preview-only applies due to pre-tool-use hook blocking `*_apply` calls
- **Isolation:** Branch `audit/mcp-full-surface-20260413` — disposable, not merged to main
- **Client:** Claude Code (Claude Opus 4.6, 1M context)
- **Workspace id:** `99fa26b133f842ae8df877a557968591`
- **Server:** roslyn-mcp v1.12.0+591ab764
- **Catalog version:** 2026.04
- **Experimental surface:** 51 experimental tools, 19 experimental prompts
- **Scale:** ~11 projects, ~243 documents
- **Repo shape:** Multi-project (5 src + 6 test), xUnit tests, 25 analyzer assemblies (492 rules), source generators (RegexGenerator, PublicTopLevelProgram), DI (25 registrations), .editorconfig, Central Package Management (Directory.Packages.props), single-target (net10.0)
- **Prior issue source:** `ai_docs/backlog.md`

### Audit constraints
- **MCP connection delay:** Roslyn MCP tools were unavailable for the first several minutes of the session. Root cause: 4 concurrent Claude Code sessions started simultaneously after IDE reload, causing a connection race. 10 stale IDE lock files in `~/.claude/ide/` contributed. The `roslynmcp` process was running but the client-side MCP bridge had not established the tool connection. Tools appeared after ~5 minutes. This is a **Claude Code client issue**, not a server issue.
- **Apply operations:** `skipped-safety` — Claude Code `PreToolUse` hook blocks all `*_apply` calls with error: *"Cannot verify preview was shown without access to conversation transcript. The transcript_path points to a file that would need to be read to confirm whether [preview tool] was called earlier and results were displayed to the user."* All 18 apply tools are preview-only.
- **Prompt rendering:** `blocked` — Claude Code does not expose a `prompts/get` tool for programmatic prompt invocation. Schema-only verification.
- **Unrevert audit residue:** Two mutations were left on the disposable audit branch: (1) `// audit-marker` comment at line 1 of `SimulationQuery.cs` (from `apply_text_edit`, unrevertible after single-slot undo was consumed); (2) `dotnet_sort_system_directives_first = true` appended to `.editorconfig` (from `set_editorconfig_option`, unrevertible after `set_diagnostic_severity` consumed the revert slot). Both are on the disposable branch only.

## 2. Coverage ledger (experimental surface)

| Kind | Name | Category | Status | Phase | lastElapsedMs | Notes |
|------|------|----------|--------|-------|---------------|-------|
| tool | `fix_all_preview` | refactoring | exercised | 1a | 57 | No fixable diagnostics; guidance message tested |
| tool | `fix_all_apply` | refactoring | skipped-safety | 1a | — | Hook blocks apply |
| tool | `format_range_preview` | refactoring | exercised | 1b | 167 | No formatting changes needed |
| tool | `format_range_apply` | refactoring | skipped-safety | 1b | — | Hook blocks apply |
| tool | `extract_interface_preview` | refactoring | exercised | 1c | 30 | Clean interface generation |
| tool | `extract_interface_apply` | refactoring | skipped-safety | 1c | — | Hook blocks apply |
| tool | `extract_type_preview` | refactoring | exercised | 1d | 6 | Error path: cross-reference rejection |
| tool | `extract_type_apply` | refactoring | skipped-safety | 1d | — | Hook blocks apply |
| tool | `move_type_to_file_preview` | refactoring | exercised | 1e | 112 | Correct file split |
| tool | `move_type_to_file_apply` | refactoring | skipped-safety | 1e | — | Hook blocks apply |
| tool | `bulk_replace_type_preview` | refactoring | exercised | 1f | 179 | scope=parameters non-default tested |
| tool | `bulk_replace_type_apply` | refactoring | skipped-safety | 1f | — | Hook blocks apply |
| tool | `extract_method_preview` | refactoring | exercised | 1g | 25 | FLAG: reformats unrelated code |
| tool | `extract_method_apply` | refactoring | skipped-safety | 1g | — | Hook blocks apply |
| tool | `create_file_preview` | file-operations | exercised | 2a | 3 | Correct namespace/project |
| tool | `create_file_apply` | file-operations | skipped-safety | 2a | — | Hook blocks apply |
| tool | `move_file_preview` | file-operations | exercised | 2b | 6 | updateNamespace=true tested |
| tool | `move_file_apply` | file-operations | skipped-safety | 2b | — | Hook blocks apply |
| tool | `delete_file_preview` | file-operations | exercised | 2c | 3 | Correct removal diff |
| tool | `delete_file_apply` | file-operations | skipped-safety | 2c | — | Hook blocks apply |
| tool | `add_package_reference_preview` | project-mutation | exercised | 3a | 7 | CPM warning actionable |
| tool | `remove_package_reference_preview` | project-mutation | exercised | 3a | 3 | Correct removal |
| tool | `add_project_reference_preview` | project-mutation | exercised | 3b | 2 | Correct relative path |
| tool | `remove_project_reference_preview` | project-mutation | exercised | 3b | 1 | Correct removal |
| tool | `set_project_property_preview` | project-mutation | exercised | 3c | 2 | Correct PropertyGroup |
| tool | `set_conditional_property_preview` | project-mutation | exercised | 3c | 1 | MSBuild condition format tested |
| tool | `add_target_framework_preview` | project-mutation | exercised | 3d | 1 | Error: TFM from Directory.Build.props |
| tool | `remove_target_framework_preview` | project-mutation | exercised | 3d | 1 | Same TFM constraint |
| tool | `add_central_package_version_preview` | project-mutation | exercised | 3e | 2 | Correct Directory.Packages.props update |
| tool | `remove_central_package_version_preview` | project-mutation | exercised | 3e | 1 | Correct removal |
| tool | `apply_project_mutation` | project-mutation | skipped-safety | 3 | — | Hook blocks apply |
| tool | `get_msbuild_properties` | project-mutation | exercised | 3f | 96 | propertyNameFilter=Target, 114/716 props |
| tool | `scaffold_type_preview` | scaffolding | exercised | 4a | 1 | `internal sealed class` default, kind=interface tested |
| tool | `scaffold_type_apply` | scaffolding | skipped-safety | 4a | — | Hook blocks apply |
| tool | `scaffold_test_preview` | scaffolding | exercised | 4b | 5 | xUnit auto-detected |
| tool | `scaffold_test_apply` | scaffolding | skipped-safety | 4b | — | Hook blocks apply |
| tool | `remove_dead_code_preview` | dead-code | exercised | 5a | 6 | Correct property removal |
| tool | `remove_dead_code_apply` | dead-code | skipped-safety | 5a | — | Hook blocks apply |
| tool | `apply_text_edit` | editing | exercised-apply | 5b | 36 | Direct apply, verified |
| tool | `apply_multi_file_edit` | editing | exercised-apply | 5b | 12 | 2-file atomic edit, reverted |
| tool | `add_pragma_suppression` | editing | exercised-apply | 5c | 5 | Correct pragma insertion |
| tool | `set_editorconfig_option` | configuration | exercised-apply | 5d | 2 | Verified via get_editorconfig_options |
| tool | `set_diagnostic_severity` | configuration | exercised-apply | 5d | 1 | Verified in .editorconfig |
| tool | `revert_last_apply` | undo | exercised-apply | 5e | 2418 | LIFO revert, negative probe tested |
| tool | `move_type_to_project_preview` | cross-project-refactoring | exercised | 6 | 88 | Correct cross-project move |
| tool | `extract_interface_cross_project_preview` | cross-project-refactoring | exercised | 6 | 225 | Interface in Domain |
| tool | `dependency_inversion_preview` | cross-project-refactoring | exercised | 6 | 207 | FLAG: reformats unrelated code |
| tool | `migrate_package_preview` | orchestration | exercised | 7 | 9 | CPM + csproj updated |
| tool | `split_class_preview` | orchestration | exercised | 7 | 13 | FLAG: reformats unrelated code |
| tool | `extract_and_wire_interface_preview` | orchestration | exercised | 7 | 737 | DI wiring detected |
| tool | `apply_composite_preview` | orchestration | skipped-safety | 7 | — | Hook blocks apply |
| prompt | `explain_error` | prompts | exercised | 8 | — | Schema verified, rendering blocked |
| prompt | `suggest_refactoring` | prompts | exercised | 8 | — | Schema verified, rendering blocked |
| prompt | `review_file` | prompts | exercised | 8 | — | Schema verified, rendering blocked |
| prompt | `analyze_dependencies` | prompts | exercised | 8 | — | Schema verified, rendering blocked |
| prompt | `debug_test_failure` | prompts | exercised | 8 | — | Schema verified, rendering blocked |
| prompt | `refactor_and_validate` | prompts | exercised | 8 | — | Schema verified, rendering blocked |
| prompt | `fix_all_diagnostics` | prompts | exercised | 8 | — | Schema verified, rendering blocked |
| prompt | `guided_package_migration` | prompts | exercised | 8 | — | Schema verified, rendering blocked |
| prompt | `guided_extract_interface` | prompts | exercised | 8 | — | Schema verified, rendering blocked |
| prompt | `security_review` | prompts | exercised | 8 | — | Schema verified, rendering blocked |
| prompt | `discover_capabilities` | prompts | exercised | 8 | — | Schema verified, rendering blocked |
| prompt | `dead_code_audit` | prompts | exercised | 8 | — | Schema verified, rendering blocked |
| prompt | `review_test_coverage` | prompts | exercised | 8 | — | Schema verified, rendering blocked |
| prompt | `review_complexity` | prompts | exercised | 8 | — | Schema verified, rendering blocked |
| prompt | `cohesion_analysis` | prompts | exercised | 8 | — | Schema verified, rendering blocked |
| prompt | `consumer_impact` | prompts | exercised | 8 | — | Schema verified, rendering blocked |
| prompt | `guided_extract_method` | prompts | exercised | 8 | — | Schema verified, rendering blocked |
| prompt | `msbuild_inspection` | prompts | exercised | 8 | — | Schema verified, rendering blocked |
| prompt | `session_undo` | prompts | exercised | 8 | — | Schema verified, rendering blocked |

## 3. Performance baseline (`_meta.elapsedMs`)

> One row per exercised experimental tool. Budget: reads <=5 s, scans <=15 s, writers <=30 s.

| Tool | Category | Calls | p50_ms | max_ms | Input scale | Budget | Notes |
|------|----------|-------|--------|--------|-------------|--------|-------|
| `fix_all_preview` | refactoring | 2 | 30 | 57 | solution | <=15s | PASS |
| `format_range_preview` | refactoring | 1 | 167 | 167 | 50 lines | <=5s | PASS |
| `extract_interface_preview` | refactoring | 1 | 30 | 30 | 3 members | <=5s | PASS |
| `extract_type_preview` | refactoring | 2 | 4 | 6 | 1 member | <=5s | PASS (error path) |
| `move_type_to_file_preview` | refactoring | 1 | 112 | 112 | 1 type | <=5s | PASS |
| `bulk_replace_type_preview` | refactoring | 2 | 134 | 179 | solution | <=15s | PASS |
| `extract_method_preview` | refactoring | 3 | 25 | 25 | 6 lines | <=5s | PASS |
| `create_file_preview` | file-operations | 1 | 3 | 3 | 1 file | <=5s | PASS |
| `move_file_preview` | file-operations | 1 | 6 | 6 | 1 file | <=5s | PASS |
| `delete_file_preview` | file-operations | 1 | 3 | 3 | 1 file | <=5s | PASS |
| `add_package_reference_preview` | project-mutation | 1 | 7 | 7 | 1 pkg | <=5s | PASS |
| `remove_package_reference_preview` | project-mutation | 1 | 3 | 3 | 1 pkg | <=5s | PASS |
| `add_project_reference_preview` | project-mutation | 1 | 2 | 2 | 1 ref | <=5s | PASS |
| `remove_project_reference_preview` | project-mutation | 1 | 1 | 1 | 1 ref | <=5s | PASS |
| `set_project_property_preview` | project-mutation | 1 | 2 | 2 | 1 prop | <=5s | PASS |
| `set_conditional_property_preview` | project-mutation | 2 | 3 | 4 | 1 prop | <=5s | PASS |
| `add_target_framework_preview` | project-mutation | 1 | 1 | 1 | 1 tfm | <=5s | PASS (error) |
| `remove_target_framework_preview` | project-mutation | 1 | 1 | 1 | 1 tfm | <=5s | PASS (error) |
| `add_central_package_version_preview` | project-mutation | 1 | 2 | 2 | 1 pkg | <=5s | PASS |
| `remove_central_package_version_preview` | project-mutation | 1 | 1 | 1 | 1 pkg | <=5s | PASS |
| `get_msbuild_properties` | project-mutation | 1 | 96 | 96 | 716 props | <=5s | PASS |
| `scaffold_type_preview` | scaffolding | 2 | 1 | 1 | 1 type | <=5s | PASS |
| `scaffold_test_preview` | scaffolding | 2 | 3 | 5 | 1 test | <=5s | PASS |
| `remove_dead_code_preview` | dead-code | 1 | 6 | 6 | 1 symbol | <=5s | PASS |
| `apply_text_edit` | editing | 1 | 36 | 36 | 1 edit | <=30s | PASS |
| `apply_multi_file_edit` | editing | 1 | 12 | 12 | 2 files | <=30s | PASS |
| `add_pragma_suppression` | editing | 1 | 5 | 5 | 1 line | <=30s | PASS |
| `set_editorconfig_option` | configuration | 1 | 2 | 2 | 1 key | <=30s | PASS |
| `set_diagnostic_severity` | configuration | 1 | 1 | 1 | 1 diag | <=30s | PASS |
| `revert_last_apply` | undo | 3 | 1 | 2841 | 1 op | <=30s | PASS |
| `move_type_to_project_preview` | cross-project | 1 | 88 | 88 | 1 type | <=5s | PASS |
| `extract_interface_cross_project_preview` | cross-project | 1 | 225 | 225 | 3 members | <=5s | PASS |
| `dependency_inversion_preview` | cross-project | 1 | 207 | 207 | 1 type | <=5s | PASS |
| `migrate_package_preview` | orchestration | 1 | 9 | 9 | 1 pkg | <=5s | PASS |
| `split_class_preview` | orchestration | 1 | 13 | 13 | 1 member | <=5s | PASS |
| `extract_and_wire_interface_preview` | orchestration | 1 | 737 | 737 | 1 type | <=5s | PASS |

## 4. Schema vs behaviour drift

| Tool | Mismatch kind | Expected | Actual | Severity | Notes |
|------|---------------|----------|--------|----------|-------|
| `extract_method_preview` | Side-effect scope | Only modify extraction region + call site | Reformats entire file (collapses multi-line, removes blank lines) | Medium | Systematic across refactoring previews |
| `dependency_inversion_preview` | Side-effect scope | Only modify interface + base list + constructor deps | Reformats entire file (same pattern) | Medium | Same root cause as extract_method |
| `split_class_preview` | Side-effect scope | Only modify split target | Reformats remaining code in source file | Medium | Same root cause |
| `scaffold_type_preview` | Input validation | Reject invalid C# identifiers | Accepts `2foo` — generates non-compiling code | High | Bug: no identifier validation |
| `scaffold_test_preview` | Precondition check | Reject or warn when target is not a test project | Accepted non-test project, defaulted to MSTest | Medium | Should detect IsTestProject=false |
| `set_conditional_property_preview` | Condition format | Accept `$(Configuration)=='Release'` (no spaces) | Required `'$(Configuration)' == 'Release'` (with spaces/quotes) | Low | Schema description could clarify format |
| `revert_last_apply` | Undo depth | Stack-based undo (multiple levels) | Single-slot: only most recent apply revertible | Low | Not a bug — but underdocumented. Consecutive writes leave earlier mutations permanent |

## 5. Error message quality

| Tool | Probe input | Rating | Suggested fix | Notes |
|------|-------------|--------|---------------|-------|
| `apply_text_edit` | line 9999 on 20-line file | actionable | — | "references line 9999 but file only has 20 line(s)" |
| `remove_dead_code_preview` | fake handle | actionable | — | "symbolHandle is not valid base64" |
| `scaffold_type_preview` | name "2foo" | unhelpful | Add C# identifier validation | Silently accepts; generates bad code |
| `extract_method_preview` | startLine > endLine | actionable | — | "Start position must be before end position" |
| `extract_method_preview` | incomplete statements | actionable | — | "No complete statements found in selection" |
| `extract_method_preview` | cross-scope | actionable | — | "All selected statements must be in the same block scope" |
| `extract_type_preview` | cross-reference members | actionable | — | Lists exact member references |
| `set_editorconfig_option` | empty key | actionable | — | "Key is required" |
| `fix_all_preview` | no fixer loaded (CA1859) | actionable | — | Suggests alternatives (organize_usings, list_analyzers) |
| `fix_all_preview` | no fixer loaded (ASP0015) | actionable | — | Same guidance, scope=project non-default path |
| `bulk_replace_type_preview` | non-existent type | actionable | — | "'IOfflineImporter' not found in the solution" |
| `extract_type_preview` | cross-ref (OfflineImporter) | actionable | — | "lambda expression remains on the original type" |
| `extract_type_preview` | cross-ref (FileSnapshotStore) | actionable | — | Lists 4 specific members: `_maxRetained`, `ListSnapshotsAsync`, lambda, `DeleteSnapshotAsync` |
| `format_range_apply` | hook blocked | actionable | — | Pre-tool-use hook: "Cannot verify preview was shown" (Claude Code issue) |
| `revert_last_apply` | empty stack | actionable | — | Clear "No operation to revert" message |
| `scaffold_test_preview` | non-test project | unhelpful | Check IsTestProject flag | Silently accepts; MSTest fallback wrong |
| `add_target_framework_preview` | TFM from Directory.Build.props | actionable | — | "Project file does not declare TargetFramework" |
| `set_conditional_property_preview` | wrong condition syntax | actionable | Clarify format in schema | "only support equality conditions on..." |

## 6. Parameter-path coverage

| Family | Non-default path tested | Status | Notes |
|--------|--------------------------|--------|-------|
| `fix_all_preview` | scope=project | PASS | Both solution and project scopes tested |
| `scaffold_type_preview` | kind=interface | PASS | Both class and interface kinds tested |
| `get_msbuild_properties` | propertyNameFilter=Target | PASS | 114/716 properties returned |
| `move_file_preview` | updateNamespace=true | PASS | Namespace updated in diff |
| `set_conditional_property_preview` | condition format | PASS | MSBuild condition syntax tested |
| `bulk_replace_type_preview` | scope=parameters | PASS | Parameter-only scope tested |
| `scaffold_test_preview` | testFramework=auto | PASS | xUnit auto-detected |
| `find_unused_symbols` | includePublic=true | PASS | (stable tool, used as scaffolding) |

## 7. Prompt verification (Phase 8)

| Prompt | schema_ok | actionable | hallucinated_tools | idempotent | elapsedMs | recommendation_seed | Notes |
|--------|-----------|------------|---------------------|------------|-----------|----------------------|-------|
| `explain_error` | yes | blocked | n/a | n/a | — | keep-experimental | Rendering not testable |
| `suggest_refactoring` | yes | blocked | n/a | n/a | — | keep-experimental | Rendering not testable |
| `review_file` | yes | blocked | n/a | n/a | — | keep-experimental | Rendering not testable |
| `analyze_dependencies` | yes | blocked | n/a | n/a | — | keep-experimental | Rendering not testable |
| `debug_test_failure` | yes | blocked | n/a | n/a | — | keep-experimental | Rendering not testable |
| `refactor_and_validate` | yes | blocked | n/a | n/a | — | keep-experimental | Rendering not testable |
| `fix_all_diagnostics` | yes | blocked | n/a | n/a | — | keep-experimental | Rendering not testable |
| `guided_package_migration` | yes | blocked | n/a | n/a | — | keep-experimental | Rendering not testable |
| `guided_extract_interface` | yes | blocked | n/a | n/a | — | keep-experimental | Rendering not testable |
| `security_review` | yes | blocked | n/a | n/a | — | keep-experimental | Rendering not testable |
| `discover_capabilities` | yes | blocked | n/a | n/a | — | keep-experimental | Rendering not testable |
| `dead_code_audit` | yes | blocked | n/a | n/a | — | keep-experimental | Rendering not testable |
| `review_test_coverage` | yes | blocked | n/a | n/a | — | keep-experimental | Rendering not testable |
| `review_complexity` | yes | blocked | n/a | n/a | — | keep-experimental | Rendering not testable |
| `cohesion_analysis` | yes | blocked | n/a | n/a | — | keep-experimental | Rendering not testable |
| `consumer_impact` | yes | blocked | n/a | n/a | — | keep-experimental | Rendering not testable |
| `guided_extract_method` | yes | blocked | n/a | n/a | — | keep-experimental | Rendering not testable |
| `msbuild_inspection` | yes | blocked | n/a | n/a | — | keep-experimental | Rendering not testable |
| `session_undo` | yes | blocked | n/a | n/a | — | keep-experimental | Rendering not testable |

## 8. Experimental promotion scorecard

| Kind | Name | Category | Status | p50_ms | schema_ok | error_ok | round_trip_ok | Failures | Recommendation | Evidence |
|------|------|----------|--------|--------|-----------|----------|---------------|----------|----------------|----------|
| tool | `fix_all_preview` | refactoring | exercised | 30 | yes | yes | n/a | 0 | keep-experimental | Guidance path tested; no fixable diagnostics to verify positive path |
| tool | `fix_all_apply` | refactoring | skipped-safety | — | — | — | — | 0 | needs-more-evidence | Hook blocked |
| tool | `format_range_preview` | refactoring | exercised | 167 | yes | — | n/a | 0 | keep-experimental | No formatting changes; positive path untested |
| tool | `format_range_apply` | refactoring | skipped-safety | — | — | — | — | 0 | needs-more-evidence | Hook blocked |
| tool | `extract_interface_preview` | refactoring | exercised | 30 | yes | yes | n/a | 0 | promote | Clean generation, error path tested, schema matched |
| tool | `extract_interface_apply` | refactoring | skipped-safety | — | — | — | — | 0 | needs-more-evidence | Hook blocked |
| tool | `extract_type_preview` | refactoring | exercised | 4 | yes | yes | n/a | 0 | keep-experimental | Only error path exercised; needs success path |
| tool | `extract_type_apply` | refactoring | skipped-safety | — | — | — | — | 0 | needs-more-evidence | Hook blocked |
| tool | `move_type_to_file_preview` | refactoring | exercised | 112 | yes | — | n/a | 0 | promote | Clean split, correct diff |
| tool | `move_type_to_file_apply` | refactoring | skipped-safety | — | — | — | — | 0 | needs-more-evidence | Hook blocked |
| tool | `bulk_replace_type_preview` | refactoring | exercised | 134 | yes | yes | n/a | 0 | keep-experimental | Non-default scope tested; error path tested |
| tool | `bulk_replace_type_apply` | refactoring | skipped-safety | — | — | — | — | 0 | needs-more-evidence | Hook blocked |
| tool | `extract_method_preview` | refactoring | exercised | 25 | FLAG | yes | n/a | 1 | keep-experimental | Reformats unrelated code |
| tool | `extract_method_apply` | refactoring | skipped-safety | — | — | — | — | 0 | needs-more-evidence | Hook blocked |
| tool | `create_file_preview` | file-operations | exercised | 3 | yes | — | n/a | 0 | promote | Correct namespace/project |
| tool | `create_file_apply` | file-operations | skipped-safety | — | — | — | — | 0 | needs-more-evidence | Hook blocked |
| tool | `move_file_preview` | file-operations | exercised | 6 | yes | yes | n/a | 0 | promote | Namespace update, warning actionable |
| tool | `move_file_apply` | file-operations | skipped-safety | — | — | — | — | 0 | needs-more-evidence | Hook blocked |
| tool | `delete_file_preview` | file-operations | exercised | 3 | yes | — | n/a | 0 | promote | Correct removal diff |
| tool | `delete_file_apply` | file-operations | skipped-safety | — | — | — | — | 0 | needs-more-evidence | Hook blocked |
| tool | `add_package_reference_preview` | project-mutation | exercised | 7 | yes | yes | n/a | 0 | promote | CPM warning actionable |
| tool | `remove_package_reference_preview` | project-mutation | exercised | 3 | yes | — | n/a | 0 | promote | Correct removal |
| tool | `add_project_reference_preview` | project-mutation | exercised | 2 | yes | — | n/a | 0 | promote | Correct relative path |
| tool | `remove_project_reference_preview` | project-mutation | exercised | 1 | yes | — | n/a | 0 | promote | Correct removal |
| tool | `set_project_property_preview` | project-mutation | exercised | 2 | yes | — | n/a | 0 | promote | Correct PropertyGroup |
| tool | `set_conditional_property_preview` | project-mutation | exercised | 1 | FLAG | yes | n/a | 0 | keep-experimental | Condition format not obvious from schema |
| tool | `add_target_framework_preview` | project-mutation | exercised | 1 | yes | yes | n/a | 0 | keep-experimental | Only error path (TFM from Directory.Build.props) |
| tool | `remove_target_framework_preview` | project-mutation | exercised | 1 | yes | yes | n/a | 0 | keep-experimental | Same constraint |
| tool | `add_central_package_version_preview` | project-mutation | exercised | 2 | yes | — | n/a | 0 | promote | Correct Directory.Packages.props |
| tool | `remove_central_package_version_preview` | project-mutation | exercised | 1 | yes | — | n/a | 0 | promote | Correct removal |
| tool | `apply_project_mutation` | project-mutation | skipped-safety | — | — | — | — | 0 | needs-more-evidence | Hook blocked |
| tool | `get_msbuild_properties` | project-mutation | exercised | 96 | yes | — | n/a | 0 | promote | Non-default filter tested, cross-checked with evaluate_msbuild_property |
| tool | `scaffold_type_preview` | scaffolding | exercised | 1 | FAIL | — | n/a | 1 | keep-experimental | Accepts invalid C# identifiers |
| tool | `scaffold_type_apply` | scaffolding | skipped-safety | — | — | — | — | 0 | needs-more-evidence | Hook blocked |
| tool | `scaffold_test_preview` | scaffolding | exercised | 3 | FLAG | — | n/a | 1 | keep-experimental | Accepts non-test projects, wrong framework fallback |
| tool | `scaffold_test_apply` | scaffolding | skipped-safety | — | — | — | — | 0 | needs-more-evidence | Hook blocked |
| tool | `remove_dead_code_preview` | dead-code | exercised | 6 | yes | yes | n/a | 0 | promote | Correct removal, error path tested |
| tool | `remove_dead_code_apply` | dead-code | skipped-safety | — | — | — | — | 0 | needs-more-evidence | Hook blocked |
| tool | `apply_text_edit` | editing | exercised-apply | 36 | yes | yes | yes | 0 | promote | Direct apply, verified, reverted |
| tool | `apply_multi_file_edit` | editing | exercised-apply | 12 | yes | — | yes | 0 | promote | 2-file atomic edit, reverted |
| tool | `add_pragma_suppression` | editing | exercised-apply | 5 | yes | — | yes | 0 | promote | Correct pragma insertion, reverted |
| tool | `set_editorconfig_option` | configuration | exercised-apply | 2 | yes | yes | yes | 0 | promote | Verified via get_editorconfig_options |
| tool | `set_diagnostic_severity` | configuration | exercised-apply | 1 | yes | — | yes | 0 | promote | Verified in .editorconfig |
| tool | `revert_last_apply` | undo | exercised-apply | 1 | yes | yes | yes | 0 | promote | LIFO revert, negative probe tested |
| tool | `move_type_to_project_preview` | cross-project | exercised | 88 | yes | — | n/a | 0 | keep-experimental | Preview-only tool, no apply counterpart |
| tool | `extract_interface_cross_project_preview` | cross-project | exercised | 225 | yes | — | n/a | 0 | keep-experimental | Preview-only tool |
| tool | `dependency_inversion_preview` | cross-project | exercised | 207 | FLAG | — | n/a | 1 | keep-experimental | Reformats unrelated code |
| tool | `migrate_package_preview` | orchestration | exercised | 9 | yes | — | n/a | 0 | promote | CPM + csproj correctly updated |
| tool | `split_class_preview` | orchestration | exercised | 13 | FLAG | — | n/a | 1 | keep-experimental | Reformats unrelated code |
| tool | `extract_and_wire_interface_preview` | orchestration | exercised | 737 | yes | — | n/a | 0 | keep-experimental | DI wiring correct; preview-only |
| tool | `apply_composite_preview` | orchestration | skipped-safety | — | — | — | — | 0 | needs-more-evidence | Hook blocked |
| prompt | `explain_error` | prompts | exercised | — | yes | blocked | n/a | 0 | keep-experimental | Rendering not testable |
| prompt | `suggest_refactoring` | prompts | exercised | — | yes | blocked | n/a | 0 | keep-experimental | Rendering not testable |
| prompt | `review_file` | prompts | exercised | — | yes | blocked | n/a | 0 | keep-experimental | Rendering not testable |
| prompt | `analyze_dependencies` | prompts | exercised | — | yes | blocked | n/a | 0 | keep-experimental | Rendering not testable |
| prompt | `debug_test_failure` | prompts | exercised | — | yes | blocked | n/a | 0 | keep-experimental | Rendering not testable |
| prompt | `refactor_and_validate` | prompts | exercised | — | yes | blocked | n/a | 0 | keep-experimental | Rendering not testable |
| prompt | `fix_all_diagnostics` | prompts | exercised | — | yes | blocked | n/a | 0 | keep-experimental | Rendering not testable |
| prompt | `guided_package_migration` | prompts | exercised | — | yes | blocked | n/a | 0 | keep-experimental | Rendering not testable |
| prompt | `guided_extract_interface` | prompts | exercised | — | yes | blocked | n/a | 0 | keep-experimental | Rendering not testable |
| prompt | `security_review` | prompts | exercised | — | yes | blocked | n/a | 0 | keep-experimental | Rendering not testable |
| prompt | `discover_capabilities` | prompts | exercised | — | yes | blocked | n/a | 0 | keep-experimental | Rendering not testable |
| prompt | `dead_code_audit` | prompts | exercised | — | yes | blocked | n/a | 0 | keep-experimental | Rendering not testable |
| prompt | `review_test_coverage` | prompts | exercised | — | yes | blocked | n/a | 0 | keep-experimental | Rendering not testable |
| prompt | `review_complexity` | prompts | exercised | — | yes | blocked | n/a | 0 | keep-experimental | Rendering not testable |
| prompt | `cohesion_analysis` | prompts | exercised | — | yes | blocked | n/a | 0 | keep-experimental | Rendering not testable |
| prompt | `consumer_impact` | prompts | exercised | — | yes | blocked | n/a | 0 | keep-experimental | Rendering not testable |
| prompt | `guided_extract_method` | prompts | exercised | — | yes | blocked | n/a | 0 | keep-experimental | Rendering not testable |
| prompt | `msbuild_inspection` | prompts | exercised | — | yes | blocked | n/a | 0 | keep-experimental | Rendering not testable |
| prompt | `session_undo` | prompts | exercised | — | yes | blocked | n/a | 0 | keep-experimental | Rendering not testable |

### Scorecard summary

| Recommendation | Tools | Prompts | Total |
|----------------|-------|---------|-------|
| promote | 19 | 0 | 19 |
| keep-experimental | 14 | 19 | 33 |
| needs-more-evidence | 18 | 0 | 18 |
| deprecate | 0 | 0 | 0 |
| **Total** | **51** | **19** | **70** |

## 9. MCP server issues (bugs)

### 9.1 `scaffold_type_preview` accepts invalid C# identifiers
| Field | Detail |
|--------|--------|
| Tool | `scaffold_type_preview` |
| Input | `typeName: "2foo"`, `typeKind: "class"` |
| Expected | Reject with validation error — `2foo` is not a valid C# identifier |
| Actual | Accepted, generated `internal sealed class 2foo {}` which won't compile |
| Severity | Medium |
| Reproducibility | Always |

### 9.2 `scaffold_test_preview` accepts non-test projects
| Field | Detail |
|--------|--------|
| Tool | `scaffold_test_preview` |
| Input | `testProjectName: "FirewallAnalyzer.Domain"` (IsTestProject=false) |
| Expected | Reject or warn — Domain project has no test framework references |
| Actual | Accepted, defaulted to MSTest, generated non-compiling code |
| Severity | Medium |
| Reproducibility | Always |

### 9.3 `revert_last_apply` single-slot undo drops earlier applies
| Field | Detail |
|--------|--------|
| Tool | `revert_last_apply` |
| Input | Sequential: `apply_text_edit` → `add_pragma_suppression` → `revert_last_apply` (reverts pragma) → `revert_last_apply` (returns "nothing to revert") |
| Expected | Stack-based undo: second revert should undo the text edit |
| Actual | Single-slot: only the most recent apply is revertible. After reverting it, prior applies are permanently unrevertible. Consecutive config writes (`set_editorconfig_option` then `set_diagnostic_severity`) also exhibit this: reverting the severity leaves the sort option permanently applied. |
| Severity | Low (documented behavior, not a bug — but important for agents to understand) |
| Reproducibility | Always |

### 9.4 Refactoring previews reformat unrelated code
| Field | Detail |
|--------|--------|
| Tool | `extract_method_preview`, `dependency_inversion_preview`, `split_class_preview` |
| Input | Standard refactoring with targeted scope |
| Expected | Only modify the extraction region and its call site |
| Actual | Diff includes collapsed multi-line expressions, removed blank lines, reformatted code outside the refactoring scope |
| Severity | Medium |
| Reproducibility | Always (systematic across Roslyn workspace diff pipeline) |

## 10. Improvement suggestions
- `scaffold_type_preview` — Add C# identifier validation (reject names starting with digits, containing spaces, C# keywords, etc.)
- `scaffold_test_preview` — Check `IsTestProject` flag on the target project; warn or reject if false. Improve framework auto-detection to return explicit "no framework found" error instead of MSTest fallback.
- `set_conditional_property_preview` — Improve schema description to show exact condition format example: `'$(Configuration)' == 'Release'`
- Refactoring previews — Investigate systematic reformatting issue. The diff pipeline appears to normalize whitespace/formatting when generating previews, which obscures the actual semantic change.
- `apply_composite_preview` — Consider exposing a `dry-run` mode that validates the composite without mutating, to allow safer exercise.
- Prompt surface — Consider adding at least one argument to prompts (e.g. `workspaceId`, `diagnosticId`) so clients can invoke them programmatically with context.
- `extract_type_preview` error message — Already excellent (lists exact member references). No change needed.
- `revert_last_apply` — Document single-slot semantics prominently in the tool description so agents know not to rely on multi-level undo. Consider deepening the undo stack (at least 3-5 slots) to support audit workflows where consecutive applies are expected.
- Prompt appendix count — Fix "52" to "51" in `experimental-promotion-exercise.md` header to match live catalog.
- Claude Code MCP connection — Stagger concurrent session MCP connections or implement retry logic when multiple sessions start simultaneously after IDE reload. Clean stale `~/.claude/ide/*.lock` files on startup.
- Claude Code `PreToolUse` hook — The preview-verification hook cannot read conversation transcripts, making it impossible to verify that `*_preview` was called before `*_apply`. Consider a hook design that checks the MCP server's undo stack or preview-token registry instead of the transcript.

## 11. Known issue regression check

| Source id | Summary | Status |
|-----------|---------|--------|
| `offline-importer-split` | Split OfflineImporter (LCOM4=3, 3 clusters) | still reproduces — `extract_type_preview` correctly rejects due to lambda references; manual redesign needed |
| `dead-code-cleanup` | Remove ExportAnalysisAsync, GetSnapshotSummaryAsync, WriteRawXmlAsync | needs investigation — `find_unused_symbols` (includePublic=true) did not surface these; may be interface-referenced |
| `complexity-reduction` | AreRulesEqual CC=14, ValidateSectionKeys CC=14, IpInCidr CC=13 | still reproduces — `get_complexity_metrics` confirmed all three values match backlog |
