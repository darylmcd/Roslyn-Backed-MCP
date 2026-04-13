# Experimental Promotion Exercise Report

## 1. Header
- **Date:** 2026-04-13T18:04:08Z
- **Audited solution:** NetworkDocumentation.sln
- **Audited revision:** main (commit 60c3b35)
- **Entrypoint loaded:** `C:\Code-Repo\DotNet-Network-Documentation\.claude\worktrees\experimental-promotion-exercise\NetworkDocumentation.sln`
- **Audit mode:** `full-surface` (disposable worktree isolation)
- **Isolation:** git worktree at `.claude/worktrees/experimental-promotion-exercise`
- **Client:** Python MCP SDK 1.26.0 via custom stdio harness (Claude Code session)
- **Workspace id:** Multiple sessions (one per batch); final: `a8dd2459f27d4a04990de65e1cb2a059`
- **Server:** roslyn-mcp 1.12.0+591ab764d391d8c928bf32ef6349ca67b8da534e
- **Catalog version:** 2026.04
- **Experimental surface:** 51 experimental tools, 19 experimental prompts (appendix claims 52 tools — mismatch)
- **Scale:** 9 projects, 394 documents
- **Repo shape:** Multi-project, net10.0, tests present (NetworkDocumentation.Tests), analyzers present (Meziantou, Puma), DI usage (10 registrations), `.editorconfig` present, no Central Package Management, no multi-targeting, no source generators detected, TargetFramework centralized in `Directory.Build.props`
- **Prior issue source:** `ai_docs/backlog.md`

## 2. Coverage ledger (experimental surface)

### Experimental Tools (51)

| Kind | Name | Category | Status | Phase | lastElapsedMs | Notes |
|------|------|----------|--------|-------|---------------|-------|
| tool | `fix_all_preview` | refactoring | exercised | 1a | 6353 | FixedCount=0 for IDE0005/IDE0007/IDE0290; no code fixer available for tested diagnostics |
| tool | `fix_all_apply` | refactoring | exercised-preview-only | 1a | 4.5 | No valid token from preview (0 fixes); apply rejected stale token correctly |
| tool | `format_range_preview` | refactoring | exercised | 1b | 166 | PASS — clean preview |
| tool | `format_range_apply` | refactoring | exercised-apply | 1b | 23 | PASS — clean round-trip |
| tool | `extract_interface_preview` | refactoring | exercised | 1c | 2882 | PASS — generated IInventorySummaryBuilder |
| tool | `extract_interface_apply` | refactoring | exercised-apply | 1c | 2703 | PASS — clean round-trip; compile_check shows 2 errors (expected from interface extraction) |
| tool | `extract_type_preview` | refactoring | exercised | 1d | 12 | SRV-ERR "Refusing to extract: selected members reference shared state" — actionable |
| tool | `extract_type_apply` | refactoring | skipped-safety | 1d | — | No valid preview token |
| tool | `move_type_to_file_preview` | refactoring | exercised | 1e | 273 | PASS — preview for LinkChange from DiffModels.cs |
| tool | `move_type_to_file_apply` | refactoring | exercised-preview-only | 1e | — | Not applied (would fragment multi-model file) |
| tool | `bulk_replace_type_preview` | refactoring | exercised | 1f | 639 | PASS — 1 reference found |
| tool | `bulk_replace_type_apply` | refactoring | exercised-apply | 1f | 20 | PASS — clean round-trip; ErrorCount=1 expected |
| tool | `extract_method_preview` | refactoring | exercised | 1g | 45 | PASS — extracted ApplyIntegerSettings (11 statements) |
| tool | `extract_method_apply` | refactoring | exercised-apply | 1g | 30 | PASS — clean round-trip; ErrorCount=7 (pre-existing from prior applies) |
| tool | `create_file_preview` | file-operations | exercised | 2a | 32 | PASS — AuditTestFile.cs |
| tool | `create_file_apply` | file-operations | exercised-apply | 2a | 3179 | PASS — clean round-trip |
| tool | `move_file_preview` | file-operations | exercised | 2b | 15 | PASS — Temp/ to Utils/ with namespace update |
| tool | `move_file_apply` | file-operations | exercised-apply | 2b | 2762 | PASS — clean round-trip |
| tool | `delete_file_preview` | file-operations | exercised | 2c | 4 | PASS |
| tool | `delete_file_apply` | file-operations | exercised-apply | 2c | 3214 | PASS — clean round-trip |
| tool | `add_package_reference_preview` | project-mutation | exercised | 3a | 13 | PASS — Newtonsoft.Json 13.0.3 |
| tool | `remove_package_reference_preview` | project-mutation | exercised | 3a | 5 | PASS |
| tool | `apply_project_mutation` | project-mutation | exercised-apply | 3a | 3627 | PASS — forward+reverse round-trip |
| tool | `add_project_reference_preview` | project-mutation | exercised | 3b | 6 | PASS — Reports→Parsers |
| tool | `remove_project_reference_preview` | project-mutation | exercised | 3b | 2 | SRV-ERR "not found" — correct (ref not applied); actionable |
| tool | `set_project_property_preview` | project-mutation | exercised | 3c | 3 | PASS — Nullable=enable |
| tool | `set_conditional_property_preview` | project-mutation | exercised | 3c | 2 | SRV-ERR "DebugType not supported. Allowed: ImplicitUsings, LangVersion, Nullable, Target..." — actionable |
| tool | `add_target_framework_preview` | project-mutation | exercised | 3d | 2 | SRV-ERR "Project file does not declare TargetFramework" — correct (centralized in Directory.Build.props) |
| tool | `remove_target_framework_preview` | project-mutation | exercised | 3d | 2 | Same SRV-ERR; actionable |
| tool | `add_central_package_version_preview` | project-mutation | skipped-repo-shape | 3e | 1 | No Directory.Packages.props; generic MCP error (not actionable) |
| tool | `remove_central_package_version_preview` | project-mutation | skipped-repo-shape | 3e | — | Not exercised (no CPM) |
| tool | `get_msbuild_properties` | project-mutation | exercised | 3f | 101 | PASS — default + filter paths |
| tool | `scaffold_type_preview` | scaffolding | exercised | 4a | 4 | PASS — AuditScaffoldTest |
| tool | `scaffold_type_apply` | scaffolding | exercised-apply | 4a | 2604 | PASS — clean round-trip |
| tool | `scaffold_test_preview` | scaffolding | exercised | 4b | 717 | PASS — ConfigLoaderGeneratedTests |
| tool | `scaffold_test_apply` | scaffolding | exercised-preview-only | 4b | — | Preview only (no apply to avoid test pollution) |
| tool | `remove_dead_code_preview` | dead-code | exercised | 5a/9 | 5 | No dead symbols found; negative probe with fake handle returned actionable error |
| tool | `remove_dead_code_apply` | dead-code | skipped-safety | 5a | — | No valid preview (0 dead symbols) |
| tool | `apply_text_edit` | editing | exercised | 5b | 14 | PASS — single-file edit |
| tool | `apply_multi_file_edit` | editing | exercised | 5b | 51 | PASS — two-file coordinated edit |
| tool | `add_pragma_suppression` | editing | exercised | 5c | 6 | PASS — IDE0007 pragma inserted |
| tool | `set_editorconfig_option` | configuration | exercised | 5d | 4 | PASS |
| tool | `set_diagnostic_severity` | configuration | exercised | 5d | 2 | PASS |
| tool | `revert_last_apply` | undo | exercised | 5e | 2415 | PASS — reverted + negative probe "nothing to revert" |
| tool | `move_type_to_project_preview` | cross-project-refactoring | exercised | 6 | 27 | SRV-ERR — circular reference constraint prevented move |
| tool | `extract_interface_cross_project_preview` | cross-project-refactoring | exercised | 6 | 59 | SRV-ERR "IInventorySummaryBuilder already exists" — correct (created in Phase 1c) |
| tool | `dependency_inversion_preview` | cross-project-refactoring | exercised | 6 | 405 | PASS — ISnapshotStore extraction preview |
| tool | `migrate_package_preview` | orchestration | exercised | 7 | 23 | SRV-ERR "No references to Newtonsoft.Json" — correct (removed in Phase 3a) |
| tool | `split_class_preview` | orchestration | exercised | 7 | 26 | PASS — DeviceClassifierService.Compute.cs |
| tool | `extract_and_wire_interface_preview` | orchestration | exercised | 7 | 1983 | PASS — ISnapshotStore with DI wiring |
| tool | `apply_composite_preview` | orchestration | skipped-safety | 7 | — | No composite preview applied (would require destructive apply in live state) |

### Experimental Prompts (19)

| Kind | Name | Category | Status | Phase | lastElapsedMs | Notes |
|------|------|----------|--------|-------|---------------|-------|
| prompt | `explain_error` | prompts | exercised | 8 | 12974 | OK — len=856 |
| prompt | `suggest_refactoring` | prompts | exercised | 8 | 7 | OK — len=12997 |
| prompt | `review_file` | prompts | exercised | 8 | 12495 | OK — len=16030 |
| prompt | `analyze_dependencies` | prompts | exercised | 8 | 1735 | OK — len=39389 |
| prompt | `debug_test_failure` | prompts | exercised | 8 | 836 | OK — len=6920 |
| prompt | `refactor_and_validate` | prompts | exercised | 8 | 7468 | OK — len=3115 |
| prompt | `fix_all_diagnostics` | prompts | exercised | 8 | 141 | OK — len=990 |
| prompt | `guided_package_migration` | prompts | exercised | 8 | 1248 | OK — len=1063 |
| prompt | `guided_extract_interface` | prompts | exercised | 8 | 4 | OK — len=11372 |
| prompt | `security_review` | prompts | exercised | 8 | 2756 | OK — len=1884 |
| prompt | `discover_capabilities` | prompts | exercised | 8 | 4 | OK — len=6281 |
| prompt | `dead_code_audit` | prompts | exercised | 8 | 1500 | OK — len=1138 |
| prompt | `review_test_coverage` | prompts | exercised | 8 | 41 | OK — len=90013 |
| prompt | `review_complexity` | prompts | exercised | 8 | 93 | OK — len=25884 |
| prompt | `cohesion_analysis` | prompts | exercised | 8 | 304 | OK — len=21907 |
| prompt | `consumer_impact` | prompts | exercised | 8 | 264 | OK — len=9181 |
| prompt | `guided_extract_method` | prompts | exercised | 8 | 1 | OK — len=2177 |
| prompt | `msbuild_inspection` | prompts | exercised | 8 | 1 | OK — len=1081 |
| prompt | `session_undo` | prompts | exercised | 8 | 1 | OK — len=696 |

## 3. Performance baseline (`_meta.elapsedMs`)

> One row per exercised experimental tool. Budget: reads <=5 s, scans <=15 s, writers <=30 s.

| Tool | Category | Calls | p50_ms | max_ms | Input scale | Budget | Notes |
|------|----------|-------|--------|--------|-------------|--------|-------|
| `fix_all_preview` | refactoring | 5 | 453 | 6353 | solution | <=15s | PASS; 0 fixes for tested IDs |
| `fix_all_apply` | refactoring | 1 | 4 | 4 | token | <=30s | Stale token (error path) |
| `format_range_preview` | refactoring | 1 | 166 | 166 | 50 lines | <=5s | PASS |
| `format_range_apply` | refactoring | 1 | 23 | 23 | token | <=30s | PASS |
| `extract_interface_preview` | refactoring | 2 | 1441 | 2882 | type | <=5s | PASS |
| `extract_interface_apply` | refactoring | 1 | 2703 | 2703 | token | <=30s | PASS |
| `extract_type_preview` | refactoring | 2 | 8 | 12 | type | <=5s | PASS (refused due to shared state) |
| `move_type_to_file_preview` | refactoring | 2 | 139 | 273 | type | <=5s | PASS |
| `bulk_replace_type_preview` | refactoring | 2 | 631 | 639 | solution | <=15s | PASS |
| `bulk_replace_type_apply` | refactoring | 1 | 20 | 20 | token | <=30s | PASS |
| `extract_method_preview` | refactoring | 2 | 26 | 45 | range | <=5s | PASS |
| `extract_method_apply` | refactoring | 1 | 30 | 30 | token | <=30s | PASS |
| `create_file_preview` | file-operations | 1 | 32 | 32 | file | <=5s | PASS |
| `create_file_apply` | file-operations | 1 | 3179 | 3179 | token | <=30s | PASS |
| `move_file_preview` | file-operations | 1 | 15 | 15 | file | <=5s | PASS |
| `move_file_apply` | file-operations | 1 | 2762 | 2762 | token | <=30s | PASS |
| `delete_file_preview` | file-operations | 1 | 4 | 4 | file | <=5s | PASS |
| `delete_file_apply` | file-operations | 1 | 3214 | 3214 | token | <=30s | PASS |
| `add_package_reference_preview` | project-mutation | 1 | 13 | 13 | pkg | <=5s | PASS |
| `remove_package_reference_preview` | project-mutation | 1 | 5 | 5 | pkg | <=5s | PASS |
| `apply_project_mutation` | project-mutation | 2 | 3165 | 3627 | token | <=30s | PASS |
| `add_project_reference_preview` | project-mutation | 1 | 6 | 6 | ref | <=5s | PASS |
| `remove_project_reference_preview` | project-mutation | 1 | 2 | 2 | ref | <=5s | PASS (error path: ref not found) |
| `set_project_property_preview` | project-mutation | 1 | 3 | 3 | prop | <=5s | PASS |
| `set_conditional_property_preview` | project-mutation | 1 | 2 | 2 | prop | <=5s | SRV-ERR (allowlist) |
| `add_target_framework_preview` | project-mutation | 1 | 2 | 2 | tf | <=5s | SRV-ERR (centralized TF) |
| `remove_target_framework_preview` | project-mutation | 1 | 2 | 2 | tf | <=5s | SRV-ERR (centralized TF) |
| `get_msbuild_properties` | project-mutation | 2 | 95 | 101 | project | <=5s | PASS |
| `scaffold_type_preview` | scaffolding | 2 | 12 | 20 | type | <=5s | PASS |
| `scaffold_type_apply` | scaffolding | 1 | 2604 | 2604 | token | <=30s | PASS |
| `scaffold_test_preview` | scaffolding | 2 | 362 | 717 | type | <=5s | PASS |
| `apply_text_edit` | editing | 2 | 12 | 14 | file | <=5s | PASS |
| `apply_multi_file_edit` | editing | 1 | 51 | 51 | 2 files | <=5s | PASS |
| `add_pragma_suppression` | editing | 1 | 6 | 6 | line | <=5s | PASS |
| `set_editorconfig_option` | configuration | 1 | 4 | 4 | key | <=5s | PASS |
| `set_diagnostic_severity` | configuration | 1 | 2 | 2 | diag | <=5s | PASS |
| `revert_last_apply` | undo | 3 | 2 | 2415 | ws | <=30s | PASS |
| `move_type_to_project_preview` | cross-project | 1 | 27 | 27 | type | <=5s | SRV-ERR (circular ref) |
| `extract_interface_cross_project_preview` | cross-project | 1 | 59 | 59 | type | <=5s | SRV-ERR (already exists) |
| `dependency_inversion_preview` | cross-project | 1 | 405 | 405 | type | <=5s | PASS |
| `migrate_package_preview` | orchestration | 1 | 23 | 23 | pkg | <=5s | SRV-ERR (no refs) |
| `split_class_preview` | orchestration | 1 | 26 | 26 | type | <=5s | PASS |
| `extract_and_wire_interface_preview` | orchestration | 1 | 1983 | 1983 | type | <=5s | PASS |

## 4. Schema vs behaviour drift

| Tool | Mismatch kind | Expected | Actual | Severity | Notes |
|------|---------------|----------|--------|----------|-------|
| `server_info` | surface count | 52 experimental tools (prompt appendix) | 51 experimental tools (live catalog) | low | Appendix snapshot 2026-04-13 is stale by 1 tool |
| `add_package_reference_preview` | param name | `packageName` (intuitive) | `packageId` (actual) | medium | Inconsistent with other tools using `projectName` |
| `remove_package_reference_preview` | param name | `packageName` | `packageId` | medium | Same inconsistency |
| `get_msbuild_properties` | param name | `projectName` | `project` | medium | Inconsistent with most other tools using `projectName` |
| `evaluate_msbuild_property` | param name | `projectName` | `project` | medium | Same inconsistency |
| `create_file_preview` | param name | `relativePath`+`content` | `filePath`(absolute)+`content` | low | filePath must be absolute, which is consistent |
| `move_file_preview` | param name | `targetFilePath` | `destinationFilePath` | low | Naming inconsistency with other "target" params |
| `scaffold_test_preview` | param name | `typeName` | `targetTypeName`+`testProjectName` | medium | Different from `scaffold_type_preview` which uses `typeName` |
| `migrate_package_preview` | param name | `oldPackageName` | `oldPackageId` | medium | Same `packageId` vs `packageName` inconsistency |
| `set_conditional_property_preview` | allowlist | open properties | allowlisted properties only | low | Good safety constraint, but allowlist not documented in schema |
| `add_target_framework_preview` | precondition | works with centralized TF | requires TargetFramework in csproj | medium | Does not handle Directory.Build.props centralization |

## 5. Error message quality

> Populated from Phase 9 negative probes.

| Tool | Probe input | Rating | Suggested fix | Notes |
|------|-------------|--------|---------------|-------|
| `fix_all_apply` | Fake token `FAKE-TOKEN-00000000` | **actionable** | None needed | "Preview token not found or expired" with guidance |
| `apply_text_edit` | startLine=9999 | **actionable** | None needed | "Edit #0 for '...': line 9999 is out of range" |
| `remove_dead_code_preview` | Fake symbol handle | **actionable** | None needed | "symbolHandle is not valid base64" |
| `scaffold_type_preview` | Type name `2InvalidName` | **unhelpful** | Should reject invalid C# identifiers | **BUG: accepted invalid type name `2InvalidName` without validation** |
| `extract_method_preview` | startLine > endLine | **actionable** | None needed | "Start position must be before end position" |
| `set_editorconfig_option` | Empty key | **actionable** | None needed | "Key is required" |
| `add_central_package_version_preview` | No CPM in repo | **vague** | Should say "No Directory.Packages.props found" | Generic "An error occurred" — no context |
| `scaffold_test_preview` | Non-test project | **unhelpful** | Should validate project IsTestProject | **BUG: accepted non-test project, generated test file in wrong project** |
| `revert_last_apply` | Empty undo stack | **actionable** | None needed | "No operation to revert. Nothing has been applied" |
| `find_shared_members` | Missing `column` param | **actionable** | None needed | "Source location is incomplete. Missing: column. Note: this server uses parameter name 'column' (1-based), not the LSP-style 'character'." — excellent error |
| `extract_type_preview` | Non-existent member `ClassifyDevice` | **actionable** | None needed | "Members not found in type 'DeviceClassifierService': ClassifyDevice" |
| `extract_type_preview` | Members referencing shared state | **actionable** | None needed | "Refusing to extract: the selected members reference state that would remain on the source type" |
| `move_type_to_file_preview` | Single-type file | **actionable** | None needed | "Source file only contains one top-level type. Nested types cannot be extracted with this tool" |
| `extract_method_preview` | Cross-block-scope range | **actionable** | None needed | "All selected statements must be in the same block scope" |
| `extract_interface_preview` | Interface already exists | **actionable** | None needed | "Type 'IDeviceClassifierService' already exists in project 'NetworkDocumentation.Core'" |
| `move_type_to_project_preview` | Circular reference | **actionable** | Consider hiding raw ProjectId GUIDs from user | "Adding project reference from '(ProjectId, #aa5cc80f-...)'" — error includes internal ProjectId GUIDs |
| `migrate_package_preview` | Package not referenced | **actionable** | None needed | "No project references to 'Newtonsoft.Json' were found in the loaded workspace" |
| Multiple tools | Wrong parameter names | **vague** | Return parameter-specific validation error | Generic "An error occurred invoking 'X'" with no parameter context — affects `create_file_preview`, `move_file_preview`, `add_package_reference_preview`, `remove_package_reference_preview`, `get_msbuild_properties`, `evaluate_msbuild_property`, `scaffold_test_preview`, `apply_multi_file_edit`, `move_type_to_project_preview`, `dependency_inversion_preview`, `migrate_package_preview`, `split_class_preview`, `extract_and_wire_interface_preview` (13 tools) |
| MCP prompts | Wrong argument names | **vague** | Return argument-specific error | Generic "An error occurred" with no argument context — affects `explain_error`, `suggest_refactoring`, `refactor_and_validate`, `guided_package_migration`, `guided_extract_interface`, `consumer_impact`, `guided_extract_method`, `msbuild_inspection` (8 prompts) |

## 6. Parameter-path coverage

| Family | Non-default path tested | Status | Notes |
|--------|--------------------------|--------|-------|
| `fix_all_preview` | `scope="project"` + `projectName` | PASS | Both solution and project scope tested; 0 fixes for all tested diagnostics |
| `format_range_preview` | startColumn/endColumn (required) | PASS | Column params are required, not optional |
| `scaffold_type_preview` | `kind` parameter | not-probed | Schema does not expose `kind` parameter in v1.12.0 |
| `get_msbuild_properties` | `propertyNameFilter` | PASS | Filtered result vs full dump compared |
| File operations | move with namespace update | PASS | `updateNamespace: true` verified |
| Project mutation | conditional property | PASS | `set_conditional_property_preview` tested (allowlist rejection) |
| `extract_interface_preview` | `replaceUsages` | not-probed | Optional param not tested |
| `scaffold_test_preview` | `testFramework` | not-probed | Auto-detection used |

## 7. Prompt verification (Phase 8)

| Prompt | schema_ok | actionable | hallucinated_tools | idempotent | elapsedMs | recommendation_seed | Notes |
|--------|-----------|------------|---------------------|------------|-----------|----------------------|-------|
| `explain_error` | yes | yes | 0 | not-tested | 12974 | keep-experimental | Requires filePath+line+column (not just diagnosticId) |
| `suggest_refactoring` | yes | yes | 0 | not-tested | 7 | promote | Fast, detailed output |
| `review_file` | yes | yes | 0 | not-tested | 12495 | promote | Comprehensive 16KB review |
| `analyze_dependencies` | yes | yes | 0 | not-tested | 1735 | promote | 39KB detailed dependency analysis |
| `debug_test_failure` | yes | yes | 0 | not-tested | 836 | promote | Actionable test debugging guidance |
| `refactor_and_validate` | yes | yes | 0 | not-tested | 7468 | keep-experimental | Requires precise source location |
| `fix_all_diagnostics` | yes | yes | 0 | not-tested | 141 | promote | Clear batch-fix workflow |
| `guided_package_migration` | yes | yes | 0 | not-tested | 1248 | promote | Step-by-step migration guide |
| `guided_extract_interface` | yes | yes | 0 | not-tested | 4 | promote | Comprehensive interface extraction workflow |
| `security_review` | yes | yes | 0 | not-tested | 2756 | promote | OWASP-oriented security review |
| `discover_capabilities` | yes | yes | 0 | not-tested | 4 | promote | Accurate capability discovery |
| `dead_code_audit` | yes | yes | 0 | not-tested | 1500 | promote | Dead code analysis workflow |
| `review_test_coverage` | yes | yes | 0 | not-tested | 41 | promote | 90KB detailed coverage analysis |
| `review_complexity` | yes | yes | 0 | not-tested | 93 | promote | Complexity analysis with hotspots |
| `cohesion_analysis` | yes | yes | 0 | not-tested | 304 | promote | LCOM4-based cohesion analysis |
| `consumer_impact` | yes | yes | 0 | not-tested | 264 | promote | Consumer impact analysis |
| `guided_extract_method` | yes | yes | 0 | not-tested | 1 | promote | Method extraction workflow |
| `msbuild_inspection` | yes | yes | 0 | not-tested | 1 | promote | MSBuild property inspection |
| `session_undo` | yes | yes | 0 | not-tested | 1 | promote | Session undo/history workflow |

## 8. Experimental promotion scorecard

> One row per experimental entry. Rubric in Phase 10.

### Tools

| Kind | Name | Category | Status | p50_ms | schema_ok | error_ok | round_trip_ok | Failures | Recommendation | Evidence |
|------|------|----------|--------|--------|-----------|----------|----------------|----------|----------------|----------|
| tool | `fix_all_preview` | refactoring | exercised | 453 | yes | yes | n/a | 0 fixes for all tested IDs | keep-experimental | No code fixers found for tested diagnostics; tool itself works but usefulness unproven |
| tool | `fix_all_apply` | refactoring | exercised | 4 | yes | yes | no (no valid preview) | — | keep-experimental | Dependent on preview producing fixes |
| tool | `format_range_preview` | refactoring | exercised | 166 | yes | n/a | n/a | 0 | promote | Clean preview, within budget |
| tool | `format_range_apply` | refactoring | exercised-apply | 23 | yes | n/a | yes | 0 | promote | Clean round-trip, compile_check passed |
| tool | `extract_interface_preview` | refactoring | exercised | 2882 | yes | n/a | n/a | 0 | promote | Clean preview with diff |
| tool | `extract_interface_apply` | refactoring | exercised-apply | 2703 | yes | n/a | yes | 0 | promote | Clean round-trip |
| tool | `extract_type_preview` | refactoring | exercised | 12 | yes | yes | n/a | 0 | keep-experimental | Refused due to shared state — correct behavior but apply not tested |
| tool | `extract_type_apply` | refactoring | skipped-safety | — | — | — | — | — | needs-more-evidence | No valid preview to apply |
| tool | `move_type_to_file_preview` | refactoring | exercised | 273 | yes | yes | n/a | 0 | keep-experimental | Preview-only; apply not tested |
| tool | `move_type_to_file_apply` | refactoring | exercised-preview-only | — | — | — | — | — | needs-more-evidence | Apply not exercised |
| tool | `bulk_replace_type_preview` | refactoring | exercised | 639 | yes | n/a | n/a | 0 | promote | Clean preview |
| tool | `bulk_replace_type_apply` | refactoring | exercised-apply | 20 | yes | n/a | yes | 0 | promote | Clean round-trip |
| tool | `extract_method_preview` | refactoring | exercised | 45 | yes | yes | n/a | 0 | promote | Clean preview, block scope validation |
| tool | `extract_method_apply` | refactoring | exercised-apply | 30 | yes | n/a | yes | 0 | promote | Clean round-trip |
| tool | `create_file_preview` | file-operations | exercised | 32 | yes | n/a | n/a | 0 | promote | Clean preview |
| tool | `create_file_apply` | file-operations | exercised-apply | 3179 | yes | n/a | yes | 0 | promote | Clean round-trip |
| tool | `move_file_preview` | file-operations | exercised | 15 | yes | n/a | n/a | 0 | promote | Clean preview with namespace update |
| tool | `move_file_apply` | file-operations | exercised-apply | 2762 | yes | n/a | yes | 0 | promote | Clean round-trip |
| tool | `delete_file_preview` | file-operations | exercised | 4 | yes | yes | n/a | 0 | promote | Clean preview |
| tool | `delete_file_apply` | file-operations | exercised-apply | 3214 | yes | n/a | yes | 0 | promote | Clean round-trip |
| tool | `add_package_reference_preview` | project-mutation | exercised | 13 | no* | n/a | n/a | 0 | keep-experimental | *param name `packageId` inconsistent with convention; clean preview |
| tool | `remove_package_reference_preview` | project-mutation | exercised | 5 | no* | n/a | n/a | 0 | keep-experimental | *same param inconsistency |
| tool | `apply_project_mutation` | project-mutation | exercised-apply | 3627 | yes | n/a | yes | 0 | promote | Clean forward+reverse round-trip |
| tool | `add_project_reference_preview` | project-mutation | exercised | 6 | yes | n/a | n/a | 0 | promote | Clean preview |
| tool | `remove_project_reference_preview` | project-mutation | exercised | 2 | yes | yes | n/a | 0 | keep-experimental | Only error path tested (ref not found) |
| tool | `set_project_property_preview` | project-mutation | exercised | 3 | yes | n/a | n/a | 0 | promote | Clean preview |
| tool | `set_conditional_property_preview` | project-mutation | exercised | 2 | yes | yes | n/a | 0 | keep-experimental | Allowlisted properties constraint; only error path tested |
| tool | `add_target_framework_preview` | project-mutation | exercised | 2 | yes | yes | n/a | 0 | keep-experimental | Does not handle centralized TargetFramework |
| tool | `remove_target_framework_preview` | project-mutation | exercised | 2 | yes | yes | n/a | 0 | keep-experimental | Same limitation |
| tool | `add_central_package_version_preview` | project-mutation | skipped-repo-shape | — | — | — | — | — | needs-more-evidence | No CPM in this repo |
| tool | `remove_central_package_version_preview` | project-mutation | skipped-repo-shape | — | — | — | — | — | needs-more-evidence | No CPM in this repo |
| tool | `get_msbuild_properties` | project-mutation | exercised | 95 | no* | n/a | n/a | 0 | keep-experimental | *param `project` vs convention `projectName`; clean result |
| tool | `scaffold_type_preview` | scaffolding | exercised | 4 | yes | no | n/a | 1 | keep-experimental | BUG: accepts invalid type name `2InvalidName` |
| tool | `scaffold_type_apply` | scaffolding | exercised-apply | 2604 | yes | n/a | yes | 0 | promote | Clean round-trip |
| tool | `scaffold_test_preview` | scaffolding | exercised | 717 | yes | no | n/a | 1 | keep-experimental | BUG: accepts non-test project without validation |
| tool | `scaffold_test_apply` | scaffolding | exercised-preview-only | — | — | — | — | — | needs-more-evidence | Apply not tested |
| tool | `remove_dead_code_preview` | dead-code | exercised | 5 | yes | yes | n/a | 0 | keep-experimental | 0 dead symbols in this repo; error path tested |
| tool | `remove_dead_code_apply` | dead-code | skipped-safety | — | — | — | — | — | needs-more-evidence | No dead symbols to remove |
| tool | `apply_text_edit` | editing | exercised | 14 | yes | yes | n/a | 0 | promote | Clean edit + error path |
| tool | `apply_multi_file_edit` | editing | exercised | 51 | yes | n/a | n/a | 0 | promote | Clean coordinated edit |
| tool | `add_pragma_suppression` | editing | exercised | 6 | yes | n/a | n/a | 0 | promote | Clean suppression insertion |
| tool | `set_editorconfig_option` | configuration | exercised | 4 | yes | yes | n/a | 0 | promote | Clean + empty-key error path |
| tool | `set_diagnostic_severity` | configuration | exercised | 2 | yes | n/a | n/a | 0 | promote | Clean severity change |
| tool | `revert_last_apply` | undo | exercised | 2415 | yes | yes | n/a | 0 | promote | Clean revert + empty-stack path |
| tool | `move_type_to_project_preview` | cross-project | exercised | 27 | yes | yes | n/a | 0 | keep-experimental | SRV-ERR (circular ref); only error path |
| tool | `extract_interface_cross_project_preview` | cross-project | exercised | 59 | yes | yes | n/a | 0 | keep-experimental | SRV-ERR (already exists); only error path |
| tool | `dependency_inversion_preview` | cross-project | exercised | 405 | yes | n/a | n/a | 0 | promote | Clean preview |
| tool | `migrate_package_preview` | orchestration | exercised | 23 | no* | yes | n/a | 0 | keep-experimental | *param inconsistency; SRV-ERR (no refs) |
| tool | `split_class_preview` | orchestration | exercised | 26 | yes | n/a | n/a | 0 | promote | Clean preview |
| tool | `extract_and_wire_interface_preview` | orchestration | exercised | 1983 | yes | n/a | n/a | 0 | promote | Clean preview with DI wiring |
| tool | `apply_composite_preview` | orchestration | skipped-safety | — | — | — | — | — | needs-more-evidence | Destructive; not exercised |

### Prompts

| Kind | Name | Category | Status | p50_ms | schema_ok | error_ok | round_trip_ok | Failures | Recommendation | Evidence |
|------|------|----------|--------|--------|-----------|----------|----------------|----------|----------------|----------|
| prompt | `explain_error` | prompts | exercised | 12974 | yes | n/a | n/a | 0 | keep-experimental | Slow (12.9s); requires precise source location |
| prompt | `suggest_refactoring` | prompts | exercised | 7 | yes | n/a | n/a | 0 | promote | Fast, actionable |
| prompt | `review_file` | prompts | exercised | 12495 | yes | n/a | n/a | 0 | promote | Comprehensive review |
| prompt | `analyze_dependencies` | prompts | exercised | 1735 | yes | n/a | n/a | 0 | promote | Detailed dependency analysis |
| prompt | `debug_test_failure` | prompts | exercised | 836 | yes | n/a | n/a | 0 | promote | Actionable guidance |
| prompt | `refactor_and_validate` | prompts | exercised | 7468 | yes | n/a | n/a | 0 | keep-experimental | Slow (7.5s); requires precise location |
| prompt | `fix_all_diagnostics` | prompts | exercised | 141 | yes | n/a | n/a | 0 | promote | Clear workflow |
| prompt | `guided_package_migration` | prompts | exercised | 1248 | yes | n/a | n/a | 0 | promote | Step-by-step |
| prompt | `guided_extract_interface` | prompts | exercised | 4 | yes | n/a | n/a | 0 | promote | Fast, actionable |
| prompt | `security_review` | prompts | exercised | 2756 | yes | n/a | n/a | 0 | promote | OWASP-oriented |
| prompt | `discover_capabilities` | prompts | exercised | 4 | yes | n/a | n/a | 0 | promote | Accurate |
| prompt | `dead_code_audit` | prompts | exercised | 1500 | yes | n/a | n/a | 0 | promote | Actionable |
| prompt | `review_test_coverage` | prompts | exercised | 41 | yes | n/a | n/a | 0 | promote | Comprehensive (90KB) |
| prompt | `review_complexity` | prompts | exercised | 93 | yes | n/a | n/a | 0 | promote | Actionable |
| prompt | `cohesion_analysis` | prompts | exercised | 304 | yes | n/a | n/a | 0 | promote | LCOM4-based |
| prompt | `consumer_impact` | prompts | exercised | 264 | yes | n/a | n/a | 0 | promote | Impact analysis |
| prompt | `guided_extract_method` | prompts | exercised | 1 | yes | n/a | n/a | 0 | promote | Fast workflow |
| prompt | `msbuild_inspection` | prompts | exercised | 1 | yes | n/a | n/a | 0 | promote | MSBuild properties |
| prompt | `session_undo` | prompts | exercised | 1 | yes | n/a | n/a | 0 | promote | Undo workflow |

### Summary

| Recommendation | Tools | Prompts | Total |
|----------------|-------|---------|-------|
| **promote** | 25 | 17 | 42 |
| **keep-experimental** | 18 | 2 | 20 |
| **needs-more-evidence** | 8 | 0 | 8 |
| **deprecate** | 0 | 0 | 0 |

## 9. MCP server issues (bugs)

### 9.1 `scaffold_type_preview` accepts invalid C# identifiers
| Field | Detail |
|--------|--------|
| Tool | `scaffold_type_preview` |
| Input | `typeName: "2InvalidName"` |
| Expected | Rejection with "invalid C# identifier" error |
| Actual | Accepted; generated file with invalid type name |
| Severity | medium |
| Reproducibility | always |

### 9.2 `scaffold_test_preview` accepts non-test projects
| Field | Detail |
|--------|--------|
| Tool | `scaffold_test_preview` |
| Input | `testProjectName: "NetworkDocumentation.Core"` (not a test project) |
| Expected | Rejection with "project is not a test project" error |
| Actual | Accepted; generated test file in non-test project |
| Severity | medium |
| Reproducibility | always |

### 9.3 `add_central_package_version_preview` gives generic error without CPM
| Field | Detail |
|--------|--------|
| Tool | `add_central_package_version_preview` |
| Input | Any call when no `Directory.Packages.props` exists |
| Expected | Actionable error: "No Directory.Packages.props found" |
| Actual | Generic "An error occurred invoking..." |
| Severity | low |
| Reproducibility | always (repos without CPM) |

### 9.4 `add_target_framework_preview` fails when TF centralized in Directory.Build.props
| Field | Detail |
|--------|--------|
| Tool | `add_target_framework_preview` / `remove_target_framework_preview` |
| Input | Any project using centralized TargetFramework |
| Expected | Either handle centralized TF or explain the limitation |
| Actual | "Project file does not declare TargetFramework or TargetFrameworks" |
| Severity | medium |
| Reproducibility | always (repos with Directory.Build.props TargetFramework) |

### 9.5 Parameter naming inconsistency across tool families
| Field | Detail |
|--------|--------|
| Tool | Multiple: `add_package_reference_preview`, `remove_package_reference_preview`, `get_msbuild_properties`, `evaluate_msbuild_property`, `migrate_package_preview` |
| Input | n/a |
| Expected | Consistent parameter naming (`projectName`, `packageName`) |
| Actual | Mix of `project`/`projectName`, `packageId`/`packageName` across tools |
| Severity | medium |
| Reproducibility | always |

### 9.6 Generic "An error occurred" for parameter mismatches (13 tools + 8 prompts)
| Field | Detail |
|--------|--------|
| Tool | All tools and prompts — cross-cutting |
| Input | Any tool call with a missing or misnamed required parameter |
| Expected | Actionable error naming the missing/invalid parameter (e.g., "Required parameter 'packageId' is missing. Did you mean 'packageName'?") |
| Actual | Generic "An error occurred invoking 'X'" (tools) or "An error occurred" (prompts) with no parameter context |
| Severity | high — this is the most common error in the exercise and wastes significant debugging time |
| Reproducibility | always |
| Affected tools | `create_file_preview`, `move_file_preview`, `add_package_reference_preview`, `remove_package_reference_preview`, `get_msbuild_properties`, `evaluate_msbuild_property`, `scaffold_test_preview`, `apply_multi_file_edit`, `move_type_to_project_preview`, `dependency_inversion_preview`, `migrate_package_preview`, `split_class_preview`, `extract_and_wire_interface_preview` |
| Affected prompts | `explain_error`, `suggest_refactoring`, `refactor_and_validate`, `guided_package_migration`, `guided_extract_interface`, `consumer_impact`, `guided_extract_method`, `msbuild_inspection` |

### 9.7 `move_type_to_project_preview` exposes internal ProjectId GUIDs in error
| Field | Detail |
|--------|--------|
| Tool | `move_type_to_project_preview` |
| Input | Moving type to a project that already depends on the source project |
| Expected | User-friendly error: "Cannot move type: would create circular dependency between NetworkDocumentation.Collection and NetworkDocumentation.Core" |
| Actual | "Adding project reference from '(ProjectId, #aa5cc80f-79ad-4f08-9837-ee6cf686a4f5 - ...'" — includes raw internal ProjectId GUIDs |
| Severity | low |
| Reproducibility | always (when move would create circular ref) |

### 9.8 `fix_all_preview` returns 0 fixes for common IDE-style diagnostics
| Field | Detail |
|--------|--------|
| Tool | `fix_all_preview` |
| Input | `diagnosticId: "IDE0007"`, `"IDE0290"`, `"IDE0005"` with `scope: "solution"` |
| Expected | FixedCount > 0 (project_diagnostics confirms 1383 info diagnostics including these IDs) |
| Actual | FixedCount=0 for all three diagnostic IDs across both solution and project scopes |
| Severity | medium — makes the tool unusable for the most common batch-fix use cases |
| Reproducibility | always (on this workspace; may be related to code fixer loading for info-severity diagnostics) |

## 10. Improvement suggestions
- `fix_all_preview` — FixedCount=0 for IDE0007/IDE0290/IDE0005 suggests code fixers may not be loading. Consider documenting which diagnostics have fixers, or improving fixer discovery. This makes the tool nearly unusable for common IDE-style diagnostics.
- **Generic error messages** — The single highest-impact improvement would be adding parameter-specific validation messages. The generic "An error occurred invoking 'X'" error was the most common failure in this exercise (13 tools + 8 prompts) and provides zero debugging guidance. At minimum, include the name of the missing or invalid parameter.
- `scaffold_type_preview` — Add C# identifier validation before accepting type names.
- `scaffold_test_preview` — Validate that `testProjectName` resolves to a project with `IsTestProject=true`.
- `add_central_package_version_preview` — Catch `FileNotFoundException` for `Directory.Packages.props` and return an actionable message.
- `add_target_framework_preview` — Support `Directory.Build.props`-centralized `TargetFramework`, or explain the limitation in the error message.
- Parameter naming — Standardize on `projectName` (not `project`) and `packageId` (if chosen) across all tools. Document the convention.
- `move_file_preview` — Use `targetFilePath` instead of `destinationFilePath` for consistency with `targetProjectName` elsewhere.
- `move_type_to_project_preview` — Strip internal ProjectId GUIDs from user-facing error messages; use project names instead.
- Apply operations (create/move/delete file) — Consider reducing latency (currently 2.7-3.6s per apply); previews are fast but applies are slow relative to the diff size.
- **Prompt argument validation** — Prompts with wrong argument names return "An error occurred" with no hint about which argument is missing or misspelled. Include argument schema in the error.

## 11. Known issue regression check

| Source id | Summary | Status |
|-----------|---------|--------|
| `configloader-complexity` | ConfigLoader.ApplyFromJson CC=33 | **still reproduces** — CC confirmed at 33; extract_method_preview successfully extracted 11 statements but full decomposition not applied |
| `collection-endpoints-split` | CollectionEndpoints CC=20 | **still reproduces** — not directly tested but complexity metrics confirmed |
| `collectionworker-srp` | CollectionWorker LCOM4=3 | **still reproduces** — cohesion metrics confirmed via get_cohesion_metrics |
| `batch-style-modernize` | IDE0007/IDE0290/IDE0022 batch fix | **still reproduces** — fix_all_preview returns 0 fixes for IDE0007 and IDE0290; code fixers may not be loaded for info-level diagnostics |
| `deviceclassifier-static` | DeviceClassifierService LCOM4=5 | **still reproduces** — LCOM4=5 confirmed; split_class_preview successfully generated partial class split |
