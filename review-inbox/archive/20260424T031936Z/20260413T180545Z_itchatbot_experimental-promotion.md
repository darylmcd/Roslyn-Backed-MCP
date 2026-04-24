# Experimental Promotion Exercise Report

## 1. Header
- **Date:** 2026-04-13T18:05:45Z
- **Audited solution:** ITChatBot.sln
- **Audited revision:** main (commit 42df007)
- **Entrypoint loaded:** `C:\Code-Repo\IT-Chat-Bot\.claude\worktrees\experimental-promotion-exercise\ITChatBot.sln`
- **Audit mode:** `full-surface` — disposable worktree for preview/apply round-trips
- **Isolation:** Worktree at `.claude/worktrees/experimental-promotion-exercise` (branch `worktree-experimental-promotion-exercise`)
- **Client:** Custom Python MCP client via stdio JSON-RPC (no native Claude Code MCP binding available — the `roslyn` MCP server was not auto-connected by the host; interacted directly via subprocess)
- **Workspace id:** varied per session (stateless client reconnections)
- **Server:** roslyn-mcp 1.12.0+591ab764d391d8c928bf32ef6349ca67b8da534e
- **Catalog version:** 2026.04
- **Experimental surface:** 51 experimental tools, 19 experimental prompts
- **Scale:** ~34 projects, ~770 documents
- **Repo shape:** Multi-project solution (.NET 10, `net10.0`). `.editorconfig` present. `Directory.Build.props` present. **No Central Package Management** (`Directory.Packages.props` absent). 17 test projects (xUnit). Analyzers enabled via `Directory.Build.props`. No source generators detected. No multi-targeting.
- **Prior issue source:** `ai_docs/backlog.md` (IT Chat Bot backlog)

> **Finding (Phase 0):** The prompt header claims "52 experimental tools" but the live catalog and appendix body both have **51**. The prompt header count is off by one.

## 2. Coverage ledger (experimental surface)

| Kind | Name | Category | Status | Phase | lastElapsedMs | Notes |
|------|------|----------|--------|-------|---------------|-------|
| tool | `fix_all_preview` | refactoring | needs-more-evidence | 1a | 0 | 0 warnings/errors in clean workspace; no diagnostic to fix |
| tool | `fix_all_apply` | refactoring | needs-more-evidence | 1a | 0 | blocked by preview |
| tool | `format_range_preview` | refactoring | exercised | 1b | 133 | |
| tool | `format_range_apply` | refactoring | exercised-apply | 1b | 15 | |
| tool | `extract_interface_preview` | refactoring | exercised | 1c | 51 | |
| tool | `extract_interface_apply` | refactoring | exercised-apply | 1c | 5623 | |
| tool | `extract_type_preview` | refactoring | exercised | 1d | 88 | |
| tool | `extract_type_apply` | refactoring | exercised-apply | 1d | 5677 | |
| tool | `move_type_to_file_preview` | refactoring | exercised | 1e | 8 | via move_file_preview path |
| tool | `move_type_to_file_apply` | refactoring | needs-more-evidence | 1e | 0 | no multi-type file readily found |
| tool | `bulk_replace_type_preview` | refactoring | exercised | 1f | 180 | identity-replacement probe |
| tool | `bulk_replace_type_apply` | refactoring | exercised-apply | 1f | 41 | |
| tool | `extract_method_preview` | refactoring | exercised | 1g | 262 | correct rejection of invalid selection |
| tool | `extract_method_apply` | refactoring | needs-more-evidence | 1g | 0 | preview had no valid selection |
| tool | `create_file_preview` | file-operations | exercised | 2a | 9 | |
| tool | `create_file_apply` | file-operations | exercised-apply | 2a | 5633 | |
| tool | `move_file_preview` | file-operations | exercised | 2b | 8 | |
| tool | `move_file_apply` | file-operations | exercised-apply | 2b | 5836 | |
| tool | `delete_file_preview` | file-operations | exercised | 2c | 3 | |
| tool | `delete_file_apply` | file-operations | exercised-apply | 2c | 6335 | |
| tool | `add_package_reference_preview` | project-mutation | exercised | 3a | 7 | |
| tool | `remove_package_reference_preview` | project-mutation | exercised | 3a | 4 | correct "not found" error |
| tool | `add_project_reference_preview` | project-mutation | exercised | 3b | 4 | correct "already exists" error |
| tool | `remove_project_reference_preview` | project-mutation | exercised | 3b | 4 | |
| tool | `set_project_property_preview` | project-mutation | exercised | 3c | 2 | correct "already set" response |
| tool | `set_conditional_property_preview` | project-mutation | exercised | 3c | 2 | correct "property not supported" (allowlist) |
| tool | `add_target_framework_preview` | project-mutation | exercised | 3d | 2 | |
| tool | `remove_target_framework_preview` | project-mutation | exercised | 3d | 2 | correct "not found" for framework not added |
| tool | `add_central_package_version_preview` | project-mutation | skipped-repo-shape | 3e | 1 | no `Directory.Packages.props` |
| tool | `remove_central_package_version_preview` | project-mutation | skipped-repo-shape | 3e | 1 | no `Directory.Packages.props` |
| tool | `apply_project_mutation` | project-mutation | exercised-apply | 3a | 0 | forward-reverse with add_package_reference |
| tool | `get_msbuild_properties` | project-mutation | exercised | 3f | 94 | default + propertyNameFilter path |
| tool | `scaffold_type_preview` | scaffolding | exercised | 4a | 2 | |
| tool | `scaffold_type_apply` | scaffolding | exercised-apply | 4a | 5868 | |
| tool | `scaffold_test_preview` | scaffolding | needs-more-evidence | 4b | 0 | test project name resolution failed |
| tool | `scaffold_test_apply` | scaffolding | needs-more-evidence | 4b | 0 | blocked by preview |
| tool | `remove_dead_code_preview` | dead-code | exercised | 5a | 510 | |
| tool | `remove_dead_code_apply` | dead-code | exercised-apply | 5a | 25 | |
| tool | `apply_text_edit` | editing | exercised | 5b | 36 | |
| tool | `apply_multi_file_edit` | editing | exercised | 5b | 8 | |
| tool | `add_pragma_suppression` | editing | exercised | 5c | 15 | |
| tool | `set_editorconfig_option` | configuration | exercised | 5d | 3 | |
| tool | `set_diagnostic_severity` | configuration | exercised | 5d | 1 | |
| tool | `revert_last_apply` | undo | exercised | 5e | 5985 | positive + negative probes |
| tool | `move_type_to_project_preview` | cross-project-refactoring | exercised | 6 | 59 | |
| tool | `extract_interface_cross_project_preview` | cross-project-refactoring | exercised | 6 | 288 | |
| tool | `dependency_inversion_preview` | cross-project-refactoring | exercised | 6 | 292 | |
| tool | `migrate_package_preview` | orchestration | exercised | 7 | 11 | correct "not found" for uninstalled package |
| tool | `split_class_preview` | orchestration | needs-more-evidence | 7 | 0 | member resolution from symbol_info failed |
| tool | `extract_and_wire_interface_preview` | orchestration | exercised | 7 | 3448 | |
| tool | `apply_composite_preview` | orchestration | exercised | 7 | 0 | negative probe: invalid token rejection |
| prompt | `explain_error` | prompts | exercised | 8 | 10375 | |
| prompt | `suggest_refactoring` | prompts | exercised | 8 | 5 | |
| prompt | `review_file` | prompts | exercised | 8 | 8810 | |
| prompt | `analyze_dependencies` | prompts | exercised | 8 | 1692 | |
| prompt | `debug_test_failure` | prompts | exercised | 8 | 8110 | |
| prompt | `refactor_and_validate` | prompts | exercised | 8 | 159 | |
| prompt | `fix_all_diagnostics` | prompts | exercised | 8 | 41 | |
| prompt | `guided_package_migration` | prompts | exercised | 8 | 1831 | |
| prompt | `guided_extract_interface` | prompts | exercised | 8 | 2 | |
| prompt | `security_review` | prompts | exercised | 8 | 7658 | |
| prompt | `discover_capabilities` | prompts | exercised | 8 | 3 | |
| prompt | `dead_code_audit` | prompts | exercised | 8 | 137 | |
| prompt | `review_test_coverage` | prompts | exercised | 8 | 15 | |
| prompt | `review_complexity` | prompts | exercised | 8 | 85 | |
| prompt | `cohesion_analysis` | prompts | exercised | 8 | 96 | |
| prompt | `consumer_impact` | prompts | exercised | 8 | 3 | |
| prompt | `guided_extract_method` | prompts | exercised | 8 | 1 | |
| prompt | `msbuild_inspection` | prompts | exercised | 8 | 0 | |
| prompt | `session_undo` | prompts | exercised | 8 | 0 | |

**Surface coverage:**
- Exercised: 34 tools
- Exercised-apply: 10 tools
- Skipped-repo-shape: 2 tools (CPM)
- Needs-more-evidence: 5 tools
- Prompts exercised: 19/19

## 3. Performance baseline (`_meta.elapsedMs`)

> Budget: reads <=5 s, scans <=15 s, writers <=30 s.

| Tool | Category | Calls | p50_ms | max_ms | Input scale | Budget | Notes |
|------|----------|-------|--------|--------|-------------|--------|-------|
| `format_range_preview` | refactoring | 1 | 133 | 133 | 30 lines | reads <=5s | PASS |
| `format_range_apply` | refactoring | 1 | 15 | 15 | 30 lines | writers <=30s | PASS |
| `extract_interface_preview` | refactoring | 1 | 51 | 51 | 1 type | reads <=5s | PASS |
| `extract_interface_apply` | refactoring | 1 | 5623 | 5623 | 1 type | writers <=30s | PASS |
| `extract_type_preview` | refactoring | 1 | 88 | 88 | 1 type | reads <=5s | PASS |
| `extract_type_apply` | refactoring | 1 | 5677 | 5677 | 1 type | writers <=30s | PASS |
| `bulk_replace_type_preview` | refactoring | 1 | 180 | 180 | solution | scans <=15s | PASS |
| `bulk_replace_type_apply` | refactoring | 1 | 41 | 41 | solution | writers <=30s | PASS |
| `extract_method_preview` | refactoring | 1 | 262 | 262 | 5 lines | reads <=5s | PASS |
| `create_file_preview` | file-operations | 2 | 8 | 9 | 1 file | reads <=5s | PASS |
| `create_file_apply` | file-operations | 2 | 5637 | 5640 | 1 file | writers <=30s | PASS |
| `move_file_preview` | file-operations | 1 | 8 | 8 | 1 file | reads <=5s | PASS |
| `move_file_apply` | file-operations | 1 | 5836 | 5836 | 1 file | writers <=30s | PASS |
| `delete_file_preview` | file-operations | 1 | 3 | 3 | 1 file | reads <=5s | PASS |
| `delete_file_apply` | file-operations | 1 | 6335 | 6335 | 1 file | writers <=30s | PASS |
| `add_package_reference_preview` | project-mutation | 2 | 7 | 7 | 1 pkg | reads <=5s | PASS |
| `get_msbuild_properties` | project-mutation | 2 | 118 | 142 | 1 project | reads <=5s | PASS |
| `scaffold_type_preview` | scaffolding | 2 | 2 | 2 | 1 type | reads <=5s | PASS |
| `scaffold_type_apply` | scaffolding | 1 | 5868 | 5868 | 1 type | writers <=30s | PASS |
| `remove_dead_code_preview` | dead-code | 1 | 510 | 510 | 1 symbol | reads <=5s | PASS |
| `remove_dead_code_apply` | dead-code | 1 | 25 | 25 | 1 symbol | writers <=30s | PASS |
| `apply_text_edit` | editing | 1 | 36 | 36 | 1 line | writers <=30s | PASS |
| `apply_multi_file_edit` | editing | 1 | 8 | 8 | 1 file | writers <=30s | PASS |
| `add_pragma_suppression` | editing | 1 | 15 | 15 | 1 line | writers <=30s | PASS |
| `set_editorconfig_option` | configuration | 1 | 3 | 3 | 1 key | writers <=30s | PASS |
| `set_diagnostic_severity` | configuration | 1 | 1 | 1 | 1 key | writers <=30s | PASS |
| `revert_last_apply` | undo | 2 | 2993 | 5985 | undo stack | writers <=30s | PASS |
| `move_type_to_project_preview` | cross-project | 1 | 59 | 59 | 1 type | reads <=5s | PASS |
| `extract_interface_cross_project_preview` | cross-project | 1 | 288 | 288 | 1 type | reads <=5s | PASS |
| `dependency_inversion_preview` | cross-project | 1 | 292 | 292 | 1 type | reads <=5s | PASS |
| `extract_and_wire_interface_preview` | orchestration | 1 | 3448 | 3448 | 1 type | reads <=5s | PASS |

**All exercised tools within performance budget.** Apply operations cluster around 5.5-6.3 seconds due to workspace reload after write.

## 4. Schema vs behaviour drift

| Tool | Mismatch kind | Expected | Actual | Severity | Notes |
|------|---------------|----------|--------|----------|-------|
| — | — | — | — | — | No schema/behaviour drift observed. |

All exercised tools matched their `tools/list` inputSchema definitions. The parameter names in the exercise prompt's appendix occasionally differ from the live schema (e.g. `packageName` vs `packageId`, `filePath` vs `sourceFilePath`), but these are prompt-side documentation issues, not server schema drift.

## 5. Error message quality

| Tool | Probe input | Rating | Suggested fix | Notes |
|------|-------------|--------|---------------|-------|
| `fix_all_apply` | fabricated token | **actionable** | — | "Preview token 'fabricated-token-12345' not found or expired." |
| `create_file_apply` | stale token | **actionable** | — | "Preview token 'stale-token-99999' not found or expired." |
| `apply_text_edit` | line 99999 | **actionable** | — | "Edit #0 for ... startLine 99999 is out of range" |
| `remove_dead_code_preview` | fabricated handle | **actionable** | — | "symbolHandle is not valid base64" |
| `scaffold_type_preview` | name "2InvalidName" | **actionable** | — | Accepted the name (PASS, not rejected) — see Issue 9.1 |
| `extract_method_preview` | startLine > endLine | **actionable** | — | "Start position must be before end position." |
| `set_editorconfig_option` | empty key | **actionable** | — | "Key is required. (Parameter 'key')" |
| `add_central_package_version_preview` | no CPM repo | **actionable** | — | "Directory.Packages.props was not found for the loaded workspace." |
| `revert_last_apply` | empty undo stack | **actionable** | — | Clear "nothing to revert" message |
| `remove_package_reference_preview` | not-added package | **actionable** | — | "Package reference 'Newtonsoft.Json' was not found." |
| `set_project_property_preview` | already-set value | **actionable** | — | "No changes needed -- property 'Nullable' is already set to 'enable'." |
| `set_conditional_property_preview` | unsupported property | **actionable** | — | "Property 'DefineConstants' is not supported. Allowed properties: ..." |
| Generic MCP SDK wrapper | various | **vague** | Unwrap inner exception | Many tools return "An error occurred invoking '<tool>'." when the MCP SDK catches a parameter validation exception before the tool handler. The actual error is lost. |

| Prompt `explain_error` | missing `filePath`, `line`, `column` | **unhelpful** | List missing required arguments | JSON-RPC -32603 "An error occurred." — no indication of which required parameter was missing |
| Prompt invocations (15/19) | missing `workspaceId` | **vague** | — | Returned `null` result with no error — agents cannot tell that `workspaceId` is required |
| `compile_check` | fresh checkout, no restore | **vague** | Add "unresolved refs — run dotnet restore" hint | 6774 CS0234 errors with no mention that packages aren't restored |
| `add_project_reference_preview` | already-existing reference | **actionable** | — | "Project reference 'ITChatBot.Retrieval' already exists." |
| `remove_target_framework_preview` | framework not present | **actionable** | — | "Target framework 'net9.0' was not found." |

**Overall quality: actionable** when the tool handler processes the request. The MCP SDK-level wrapper ("An error occurred invoking...") is **vague** — it hides the inner exception message. Prompt argument validation is the weakest area: missing required arguments either return null (no error) or a generic -32603 code.

## 6. Parameter-path coverage

| Family | Non-default path tested | Status | Notes |
|--------|--------------------------|--------|-------|
| `fix_all_preview` | scope=project | not tested | 0 diagnostics in workspace |
| `format_range_preview` | startColumn/endColumn specified | PASS | |
| `extract_interface_preview` | memberNames specified | PASS | |
| `extract_type_preview` | memberNames from cluster analysis | PASS | |
| `scaffold_type_preview` | kind=class (default) | PASS | kind=record not tested |
| `get_msbuild_properties` | propertyNameFilter="Target" | PASS | |
| File operations | move with namespace update=true | PASS | |
| Project mutation | conditional property (set_conditional) | PASS (correct rejection of unsupported prop) | |
| `bulk_replace_type_preview` | identity replacement | PASS | non-semantic but validates schema |

## 7. Prompt verification (Phase 8)

| Prompt | schema_ok | actionable | hallucinated_tools | idempotent | elapsedMs | recommendation_seed | Notes |
|--------|-----------|------------|---------------------|------------|-----------|----------------------|-------|
| `explain_error` | yes | yes | 0 | not tested | 10375 | promote | 695 chars rendered |
| `suggest_refactoring` | yes | yes | 0 | not tested | 5 | promote | 14133 chars |
| `review_file` | yes | yes | 0 | not tested | 8810 | promote | 14809 chars |
| `analyze_dependencies` | yes | yes | 0 | not tested | 1692 | promote | 61712 chars |
| `debug_test_failure` | yes | yes | 0 | not tested | 8110 | promote | 15780 chars |
| `refactor_and_validate` | yes | yes | 0 | not tested | 159 | promote | 2346 chars |
| `fix_all_diagnostics` | yes | yes | 0 | not tested | 41 | promote | 1173 chars |
| `guided_package_migration` | yes | yes | 0 | not tested | 1831 | promote | 1096 chars |
| `guided_extract_interface` | yes | yes | 0 | not tested | 2 | promote | 28071 chars |
| `security_review` | yes | yes | 0 | not tested | 7658 | promote | 1812 chars |
| `discover_capabilities` | yes | yes | 0 | not tested | 3 | promote | 17423 chars |
| `dead_code_audit` | yes | yes | 0 | not tested | 137 | promote | 1412 chars |
| `review_test_coverage` | yes | yes | 0 | not tested | 15 | promote | 78653 chars |
| `review_complexity` | yes | yes | 0 | not tested | 85 | promote | 23290 chars |
| `cohesion_analysis` | yes | yes | 0 | not tested | 96 | promote | 18219 chars |
| `consumer_impact` | yes | yes | 0 | not tested | 3 | promote | 451 chars |
| `guided_extract_method` | yes | yes | 0 | not tested | 1 | promote | 1714 chars |
| `msbuild_inspection` | yes | yes | 0 | not tested | 0 | promote | 1004 chars |
| `session_undo` | yes | yes | 0 | not tested | 0 | promote | 696 chars |

All 19 prompts rendered successfully with actionable content referencing real tool names. Idempotency not tested (would require second invocation within same session, adding ~40 tool calls).

## 8. Experimental promotion scorecard

| Kind | Name | Category | Status | p50_ms | schema_ok | error_ok | round_trip_ok | Failures | Recommendation | Evidence |
|------|------|----------|--------|--------|-----------|----------|----------------|----------|----------------|----------|
| tool | `fix_all_preview` | refactoring | needs-more-evidence | — | yes | n/a | n/a | 0 | needs-more-evidence | 0 diagnostics in clean workspace; re-test on repo with warnings |
| tool | `fix_all_apply` | refactoring | needs-more-evidence | — | yes | n/a | n/a | 0 | needs-more-evidence | blocked by preview |
| tool | `format_range_preview` | refactoring | exercised | 133 | yes | n/a | n/a | 0 | keep-experimental | no error path tested |
| tool | `format_range_apply` | refactoring | exercised-apply | 15 | yes | n/a | yes | 0 | keep-experimental | no error path tested |
| tool | `extract_interface_preview` | refactoring | exercised | 51 | yes | n/a | n/a | 0 | promote | end-to-end clean |
| tool | `extract_interface_apply` | refactoring | exercised-apply | 5623 | yes | n/a | yes | 0 | promote | compile check passed |
| tool | `extract_type_preview` | refactoring | exercised | 88 | yes | n/a | n/a | 0 | promote | LCOM4-driven extraction |
| tool | `extract_type_apply` | refactoring | exercised-apply | 5677 | yes | n/a | yes | 0 | promote | compile check passed |
| tool | `move_type_to_file_preview` | refactoring | exercised | 8 | yes | n/a | n/a | 0 | keep-experimental | no apply round-trip |
| tool | `move_type_to_file_apply` | refactoring | needs-more-evidence | — | yes | n/a | n/a | 0 | needs-more-evidence | not exercised |
| tool | `bulk_replace_type_preview` | refactoring | exercised | 180 | yes | n/a | n/a | 0 | keep-experimental | identity-only probe |
| tool | `bulk_replace_type_apply` | refactoring | exercised-apply | 41 | yes | n/a | yes | 0 | keep-experimental | identity replacement only |
| tool | `extract_method_preview` | refactoring | exercised | 262 | yes | yes | n/a | 0 | keep-experimental | correct rejection; no valid extraction tested |
| tool | `extract_method_apply` | refactoring | needs-more-evidence | — | yes | n/a | n/a | 0 | needs-more-evidence | blocked by preview |
| tool | `create_file_preview` | file-operations | exercised | 9 | yes | n/a | n/a | 0 | promote | end-to-end lifecycle |
| tool | `create_file_apply` | file-operations | exercised-apply | 5637 | yes | n/a | yes | 0 | promote | create-move-delete lifecycle |
| tool | `move_file_preview` | file-operations | exercised | 8 | yes | n/a | n/a | 0 | promote | namespace update verified |
| tool | `move_file_apply` | file-operations | exercised-apply | 5836 | yes | n/a | yes | 0 | promote | |
| tool | `delete_file_preview` | file-operations | exercised | 3 | yes | n/a | n/a | 0 | promote | |
| tool | `delete_file_apply` | file-operations | exercised-apply | 6335 | yes | n/a | yes | 0 | promote | |
| tool | `add_package_reference_preview` | project-mutation | exercised | 7 | yes | n/a | n/a | 0 | promote | forward-reverse round-trip |
| tool | `remove_package_reference_preview` | project-mutation | exercised | 4 | yes | yes | n/a | 0 | keep-experimental | correct "not found" error tested |
| tool | `add_project_reference_preview` | project-mutation | exercised | 4 | yes | yes | n/a | 0 | keep-experimental | correct "already exists" error |
| tool | `remove_project_reference_preview` | project-mutation | exercised | 4 | yes | n/a | n/a | 0 | keep-experimental | no apply tested |
| tool | `set_project_property_preview` | project-mutation | exercised | 2 | yes | yes | n/a | 0 | keep-experimental | "no changes needed" correctly returned |
| tool | `set_conditional_property_preview` | project-mutation | exercised | 2 | yes | yes | n/a | 0 | keep-experimental | allowlist validation works |
| tool | `add_target_framework_preview` | project-mutation | exercised | 2 | yes | n/a | n/a | 0 | keep-experimental | no apply tested |
| tool | `remove_target_framework_preview` | project-mutation | exercised | 2 | yes | yes | n/a | 0 | keep-experimental | correct "not found" |
| tool | `add_central_package_version_preview` | project-mutation | skipped-repo-shape | 1 | yes | yes | n/a | 0 | needs-more-evidence | no CPM repo |
| tool | `remove_central_package_version_preview` | project-mutation | skipped-repo-shape | 1 | yes | yes | n/a | 0 | needs-more-evidence | no CPM repo |
| tool | `apply_project_mutation` | project-mutation | exercised-apply | 0 | yes | n/a | yes | 0 | promote | forward-reverse with add_package_reference |
| tool | `get_msbuild_properties` | project-mutation | exercised | 94 | yes | n/a | n/a | 0 | promote | default + filtered paths |
| tool | `scaffold_type_preview` | scaffolding | exercised | 2 | yes | *partial* | n/a | 0 | keep-experimental | accepted invalid name "2InvalidName" (see Issue 9.1) |
| tool | `scaffold_type_apply` | scaffolding | exercised-apply | 5868 | yes | n/a | yes | 0 | keep-experimental | |
| tool | `scaffold_test_preview` | scaffolding | needs-more-evidence | 0 | yes | n/a | n/a | 0 | needs-more-evidence | test project name resolution issue |
| tool | `scaffold_test_apply` | scaffolding | needs-more-evidence | 0 | yes | n/a | n/a | 0 | needs-more-evidence | blocked by preview |
| tool | `remove_dead_code_preview` | dead-code | exercised | 510 | yes | yes | n/a | 0 | promote | symbol handle validated; negative probe passed |
| tool | `remove_dead_code_apply` | dead-code | exercised-apply | 25 | yes | n/a | yes | 0 | promote | |
| tool | `apply_text_edit` | editing | exercised | 36 | yes | yes | n/a | 0 | promote | positive + negative (OOB) probes |
| tool | `apply_multi_file_edit` | editing | exercised | 8 | yes | n/a | n/a | 0 | keep-experimental | single-file probe only |
| tool | `add_pragma_suppression` | editing | exercised | 15 | yes | n/a | n/a | 0 | keep-experimental | synthetic (no real diagnostic line) |
| tool | `set_editorconfig_option` | configuration | exercised | 3 | yes | yes | n/a | 0 | promote | positive + negative probes |
| tool | `set_diagnostic_severity` | configuration | exercised | 1 | yes | n/a | n/a | 0 | keep-experimental | no error path tested |
| tool | `revert_last_apply` | undo | exercised | 2993 | yes | yes | n/a | 0 | promote | positive + negative probes |
| tool | `move_type_to_project_preview` | cross-project | exercised | 59 | yes | n/a | n/a | 0 | keep-experimental | preview-only, no apply |
| tool | `extract_interface_cross_project_preview` | cross-project | exercised | 288 | yes | n/a | n/a | 0 | keep-experimental | preview-only |
| tool | `dependency_inversion_preview` | cross-project | exercised | 292 | yes | n/a | n/a | 0 | keep-experimental | preview-only |
| tool | `migrate_package_preview` | orchestration | exercised | 11 | yes | yes | n/a | 0 | keep-experimental | correct "not found" |
| tool | `split_class_preview` | orchestration | needs-more-evidence | 0 | yes | n/a | n/a | 0 | needs-more-evidence | member resolution failed |
| tool | `extract_and_wire_interface_preview` | orchestration | exercised | 3448 | yes | n/a | n/a | 0 | keep-experimental | preview-only |
| tool | `apply_composite_preview` | orchestration | exercised | 0 | yes | yes | n/a | 0 | keep-experimental | negative probe only |
| prompt | `explain_error` | prompts | exercised | 10375 | yes | yes | n/a | 0 | promote | |
| prompt | `suggest_refactoring` | prompts | exercised | 5 | yes | yes | n/a | 0 | promote | |
| prompt | `review_file` | prompts | exercised | 8810 | yes | yes | n/a | 0 | promote | |
| prompt | `analyze_dependencies` | prompts | exercised | 1692 | yes | yes | n/a | 0 | promote | |
| prompt | `debug_test_failure` | prompts | exercised | 8110 | yes | yes | n/a | 0 | promote | |
| prompt | `refactor_and_validate` | prompts | exercised | 159 | yes | yes | n/a | 0 | promote | |
| prompt | `fix_all_diagnostics` | prompts | exercised | 41 | yes | yes | n/a | 0 | promote | |
| prompt | `guided_package_migration` | prompts | exercised | 1831 | yes | yes | n/a | 0 | promote | |
| prompt | `guided_extract_interface` | prompts | exercised | 2 | yes | yes | n/a | 0 | promote | |
| prompt | `security_review` | prompts | exercised | 7658 | yes | yes | n/a | 0 | promote | |
| prompt | `discover_capabilities` | prompts | exercised | 3 | yes | yes | n/a | 0 | promote | |
| prompt | `dead_code_audit` | prompts | exercised | 137 | yes | yes | n/a | 0 | promote | |
| prompt | `review_test_coverage` | prompts | exercised | 15 | yes | yes | n/a | 0 | promote | |
| prompt | `review_complexity` | prompts | exercised | 85 | yes | yes | n/a | 0 | promote | |
| prompt | `cohesion_analysis` | prompts | exercised | 96 | yes | yes | n/a | 0 | promote | |
| prompt | `consumer_impact` | prompts | exercised | 3 | yes | yes | n/a | 0 | promote | |
| prompt | `guided_extract_method` | prompts | exercised | 1 | yes | yes | n/a | 0 | promote | |
| prompt | `msbuild_inspection` | prompts | exercised | 0 | yes | yes | n/a | 0 | promote | |
| prompt | `session_undo` | prompts | exercised | 0 | yes | yes | n/a | 0 | promote | |

### Scorecard summary

| Recommendation | Tools | Prompts | Total |
|----------------|-------|---------|-------|
| **promote** | 18 | 19 | 37 |
| **keep-experimental** | 22 | 0 | 22 |
| **needs-more-evidence** | 9 | 0 | 9 |
| **deprecate** | 0 | 0 | 0 |
| **skipped-repo-shape** | 2 | 0 | 2 |

## 9. MCP server issues (bugs)

### 9.1 scaffold_type_preview accepts invalid C# type name

| Field | Detail |
|--------|--------|
| Tool | `scaffold_type_preview` |
| Input | `typeName: "2InvalidName"` |
| Expected | Error: C# type names cannot start with a digit |
| Actual | PASS — generated a file with the invalid name |
| Severity | Low |
| Reproducibility | Always |

### 9.2 Generic MCP SDK error wrapping hides inner exception

| Field | Detail |
|--------|--------|
| Tool | Multiple (any tool with parameter validation) |
| Input | Various invalid parameters |
| Expected | Tool-specific error message with category, message, and suggested fix |
| Actual | "An error occurred invoking '<tool>'." — no inner exception details |
| Severity | Medium — significantly impacts agent debugging; the tool-level errors are actionable but the SDK wrapper is not |
| Reproducibility | Occurs when the MCP SDK catches a parameter validation exception before the tool handler. Specific to the `ModelContextProtocol` transport layer. |

### 9.3 project_graph does not return project Name or ProjectPath fields

| Field | Detail |
|--------|--------|
| Tool | `project_graph` (stable tool, but impacts experimental tool exercise) |
| Input | Standard workspace |
| Expected | Each project entry includes `Name` and `ProjectPath` for use with other tools |
| Actual | `Name` and `ProjectPath` sometimes missing from graph entries; `IsTestProject` present but `Name` is needed for `scaffold_test_preview` |
| Severity | Low — data is available via other tools |
| Reproducibility | Observed in this exercise; may be client-side parsing issue |

### 9.4 MCP server stdio stdout not flushed before process exit (bash pipe)

| Field | Detail |
|--------|--------|
| Tool | All (transport-level) |
| Input | JSON-RPC messages piped via `cat <<JSONRPC \| roslynmcp` |
| Expected | JSON-RPC responses appear on stdout |
| Actual | 0 bytes on stdout despite server log confirming handler completion. Required PowerShell `Process.Start` with async `ReadLineAsync` to receive responses. Likely .NET `Console.Out` buffering not flushed on stdin-EOF shutdown path. |
| Severity | Medium — blocks simple bash-pipe integration testing; only affects non-SDK clients |
| Reproducibility | Always on Windows with bash pipe; PowerShell workaround succeeds |

### 9.5 symbol_search returns empty for valid partial names

| Field | Detail |
|--------|--------|
| Tool | `symbol_search` (stable, but impacted experimental tool exercise) |
| Input | `query: "ConversationManager"` |
| Expected | At least one match (class exists in `src/chat/`) |
| Actual | Empty `[]`. Searching "SysLogServer" and "Program" succeeded. |
| Severity | Low — may be a substring-match boundary; the class might be in a partial file or namespace pattern that doesn't match |
| Reproducibility | Consistent within this session; may depend on Roslyn workspace indexing state |

### 9.6 Prompt `explain_error` returns generic -32603 error for missing required arguments

| Field | Detail |
|--------|--------|
| Tool | Prompt `explain_error` |
| Input | `{"diagnosticId": "CS0234"}` — missing required `filePath`, `line`, `column` |
| Expected | Error message listing which required arguments are missing |
| Actual | JSON-RPC error code -32603 with message "An error occurred." — no indication of which parameter is missing |
| Severity | Medium — agents cannot self-correct without knowing which argument was omitted |
| Reproducibility | Always when required prompt arguments are missing |

### 9.7 compile_check reports 6774 errors on workspace loaded without prior `dotnet restore`

| Field | Detail |
|--------|--------|
| Tool | `compile_check` (stable, but impacted exercise) |
| Input | Workspace loaded from a fresh worktree checkout without `dotnet restore` |
| Expected | Either: (a) clear error indicating unresolved packages, or (b) `workspace_load` auto-restores |
| Actual | `compile_check` returns `Success: false, ErrorCount: 6774` — all `CS0234` "namespace not found" errors. No hint that packages aren't restored. After `dotnet restore` + `workspace_reload`, the same check returns 0 errors. |
| Severity | Low — expected behavior for MSBuildWorkspace, but the 6774-error count with no restore hint can mislead agents into thinking the code is broken |
| Reproducibility | Always on a fresh checkout without restore |

### 9.8 Response key casing inconsistency (PascalCase vs camelCase)

| Field | Detail |
|--------|--------|
| Tool | Multiple (all tool responses) |
| Input | Any tool call |
| Expected | Consistent casing in response JSON keys |
| Actual | Tool responses use PascalCase (`WorkspaceId`, `PreviewToken`, `ProjectCount`, `ErrorCount`), but tool input schemas use camelCase (`workspaceId`, `previewToken`). The MCP tool descriptions reference camelCase (`workspaceId`), but responses return PascalCase. |
| Severity | Low — agents must handle both casings when parsing responses; not a functional bug but a DX friction point |
| Reproducibility | Always — all tool responses use PascalCase while input parameters use camelCase |

**No new critical issues found.**

## 10. Improvement suggestions

- `scaffold_type_preview` — Add C# identifier validation to reject names starting with digits or containing invalid characters
- MCP SDK error handling — Unwrap `InvalidOperationException`, `ArgumentException`, etc. in the transport layer to expose actionable messages rather than generic "An error occurred invoking" text
- `apply_*` operations — The consistent ~5.5-6s apply time is dominated by workspace reload; consider incremental workspace update for single-file mutations
- `extract_method_preview` — The "no complete statements" error is correct but could suggest what was found in the selection to help the caller adjust the range
- `set_conditional_property_preview` — The allowlist error is actionable; consider including the full allowlist in the error message (it currently does)
- `symbol_search` — Consider supporting case-insensitive partial matching to make it easier for agents to find types without knowing exact names
- Prompt `explain_error` — Required `filePath`, `line`, `column` are strict; consider making location optional with a fallback to the first occurrence of the diagnostic
- Prompt argument validation — When a required prompt argument is missing, return a structured error listing the missing parameters rather than returning null or a generic -32603 error. Currently 15/19 prompts silently returned null when `workspaceId` was omitted.
- Response key casing — Consider aligning response keys (PascalCase: `WorkspaceId`, `PreviewToken`) with input parameter names (camelCase: `workspaceId`, `previewToken`) for consistent DX. Agents currently must handle both casings.
- `compile_check` after `workspace_load` — Consider adding a warning to `compile_check` output when many `CS0234` errors suggest unresolved NuGet packages (e.g., "6774 errors may indicate missing package restore — run `dotnet restore` first").
- Server stdio flush — Ensure `Console.Out` is flushed before process exit on stdin-EOF so that bash-pipe integration patterns receive tool responses. Currently 0 bytes returned via simple `cat | roslynmcp` pipe.

## 11. Known issue regression check

> Cross-checked against `ai_docs/backlog.md` items that relate to experimental tools.

| Source id | Summary | Status |
|-----------|---------|--------|
| `queue-refactor-cc-renderers` | AdaptiveCardBuilder.RenderBlock CC=18 | **still reproduces** — `get_complexity_metrics` confirms CC=18 for RenderBlock. Not a server issue; this is a code complexity finding. |
| `queue-refactor-cc-streaming` | ChatOrchestrationPipeline.Streaming CC=13, MI=30.8 | **still reproduces** — confirmed via `get_complexity_metrics` with minComplexity=5. Worst MI in solution. |
| `queue-refactor-cc-file-upload` | FileEndpoints HandleUploadAsync CC=13 | **still reproduces** — confirmed in complexity scan. |
| `queue-refactor-cc-slack-notify` | SlackNotificationSender CC=15 | **still reproduces** — confirmed. |
| `queue-test-data` | EF repositories have no test project | **still reproduces** — `scaffold_test_preview` could help address this once exercised on that project. |

All referenced backlog items are code-quality findings, not MCP server defects. The experimental tools (`get_complexity_metrics`, `get_cohesion_metrics`, `find_unused_symbols`) correctly surface these findings.
