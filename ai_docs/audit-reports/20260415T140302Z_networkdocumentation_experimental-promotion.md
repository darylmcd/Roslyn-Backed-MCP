# Experimental Promotion Exercise Report

## 1. Header
- **Date:** 2026-04-15 (UTC 14:03:02Z start — 15:25:00Z finish)
- **Audited solution:** NetworkDocumentation.sln
- **Audited revision:** commit `8395fd8` on throwaway branch `exp-promotion-2026-04-15`
- **Entrypoint loaded:** `C:\Code-Repo\DotNet-Network-Documentation\.worktrees\exp-promotion\NetworkDocumentation.sln`
- **Audit mode:** `full-surface` (disposable worktree)
- **Isolation:** git worktree `.worktrees/exp-promotion` on branch `exp-promotion-2026-04-15` (cleanup behaviour documented in §Completion gate)
- **Client:** Claude (Anthropic) via Roslyn MCP stdio
- **Workspace id:** `a041e542b664491e970376f2fbe02c91` (second session — first session `4b3d5b09…` was lost between turns; see §9.7)
- **Server:** roslyn-mcp 1.18.0 (runtime .NET 10.0.6, Roslyn 5.3.0.0, Windows 10.0.26200)
- **Catalog version:** 2026.04
- **Experimental surface:** 40 experimental tools, 20 experimental prompts, 1 experimental resource template
- **Scale:** 9 projects, 402 documents (403 after `move_type_to_file_apply`)
- **Repo shape:**
  - Multi-project (8 productive + 1 test project `NetworkDocumentation.Tests`)
  - All net10.0 (no multi-targeting)
  - `.editorconfig` at repo root
  - `Directory.Build.props` with Meziantou.Analyzer / Puma.Security.Rules / SecurityCodeScan.VS2019 + `TreatWarningsAsErrors=true`
  - **No `Directory.Packages.props`** → Central Package Management not in use (Phase 3e `skipped-repo-shape`)
  - DI usage in `NetworkDocumentation.Web`
  - 0 workspace errors, 0 compiler warnings, 1,122 Info-severity analyzer diagnostics (44 distinct IDs)
- **Prior issue source:** `ai_docs/backlog.md` (2026-04-15T13:40:00Z) — **no open rows reference experimental Roslyn MCP tools**. Prior audit `ai_docs/audit-reports/20260413T180408Z_networkdocumentation_experimental-promotion.md` (same prompt, 2 days earlier).

> **Scope note (partial exercise, 3 FAIL-level + 4 FLAG findings):** Phase 1a/1b/1c/1e were exercised end-to-end. They surfaced **three FAIL-level findings** (§9.1–§9.3) and **four FLAG findings** (§9.4–§9.7) serious enough to stop rather than continue on a destabilized workspace. Phases 1d/1f–1k and 2–11 are marked `needs-more-evidence` with explicit status `not-yet-exercised`. The audit can be resumed on the same disposable branch without re-setup — the `needs-more-evidence` rows in §2 are the explicit resume list.

## 2. Coverage ledger (experimental surface)

| Kind | Name | Category | Status | Phase | lastElapsedMs | Notes |
|------|------|----------|--------|-------|---------------|-------|
| tool | `fix_all_preview` | refactoring | exercised-preview-only | 1a | 992 | No CodeFix provider loaded for any tested ID. FLAG §9.4 on inconsistent `guidanceMessage`. |
| tool | `fix_all_apply` | refactoring | blocked | 1a | — | No preview token available. |
| tool | `format_range_apply` | refactoring | exercised-apply | 1b | 37 | Clean round-trip on `InventorySummaryBuilder.cs`. Paired `format_range_preview` is stable, but emits empty diff hunks (§9.5). |
| tool | `extract_interface_preview` | refactoring | exercised | 1c | 4428 | **FAIL §9.1** — generated broken code. |
| tool | `extract_interface_apply` | refactoring | exercised-apply | 1c | 467 | Apply succeeded but result failed compile (inherits FAIL from preview). |
| tool | `extract_type_apply` | refactoring | needs-more-evidence | 1d | — | not-yet-exercised. |
| tool | `move_type_to_file_apply` | refactoring | exercised-apply | 1e | 504 | **FAIL §9.2** — preview diff diverges from apply. |
| tool | `bulk_replace_type_apply` | refactoring | needs-more-evidence | 1f | — | not-yet-exercised. Blocked by 1c FAIL (interface-extract sequencing). |
| tool | `extract_method_apply` | refactoring | needs-more-evidence | 1g | — | not-yet-exercised. |
| tool | `format_check` | refactoring | exercised | 1h | 382 | Clean. Correctly identified 1 violation across 101 Core docs. Project filter honoured. |
| tool | `restructure_preview` | refactoring | needs-more-evidence | 1h | — | not-yet-exercised. |
| tool | `replace_string_literals_preview` | refactoring | needs-more-evidence | 1i | — | not-yet-exercised. |
| tool | `change_signature_preview` | refactoring | needs-more-evidence | 1j | — | not-yet-exercised. |
| tool | `symbol_refactor_preview` | refactoring | needs-more-evidence | 1k | — | not-yet-exercised. |
| tool | `create_file_apply` | file-operations | needs-more-evidence | 2a | — | not-yet-exercised. |
| tool | `move_file_apply` | file-operations | needs-more-evidence | 2b | — | not-yet-exercised. |
| tool | `delete_file_apply` | file-operations | needs-more-evidence | 2c | — | not-yet-exercised. |
| tool | `add_central_package_version_preview` | project-mutation | skipped-repo-shape | 3e | — | No `Directory.Packages.props` — CPM not in use. |
| tool | `apply_project_mutation` | project-mutation | needs-more-evidence | 3a–3d | — | not-yet-exercised. |
| tool | `scaffold_type_preview` | scaffolding | needs-more-evidence | 4a | — | not-yet-exercised. |
| tool | `scaffold_type_apply` | scaffolding | needs-more-evidence | 4a | — | not-yet-exercised. |
| tool | `scaffold_test_apply` | scaffolding | needs-more-evidence | 4b | — | not-yet-exercised. |
| tool | `scaffold_test_batch_preview` | scaffolding | needs-more-evidence | 4c | — | not-yet-exercised. |
| tool | `remove_dead_code_apply` | dead-code | needs-more-evidence | 5a | — | not-yet-exercised. |
| tool | `remove_interface_member_preview` | dead-code | needs-more-evidence | 5a | — | not-yet-exercised. |
| tool | `apply_multi_file_edit` | editing | needs-more-evidence | 5b | — | not-yet-exercised. |
| tool | `preview_multi_file_edit` | editing | needs-more-evidence | 5b-i | — | not-yet-exercised. |
| tool | `preview_multi_file_edit_apply` | editing | needs-more-evidence | 5b-i | — | not-yet-exercised. |
| tool | `apply_with_verify` | undo | needs-more-evidence | 5e | — | not-yet-exercised. High priority on resume — would auto-revert §9.1/§9.2 failures. |
| tool | `move_type_to_project_preview` | cross-project | needs-more-evidence | 6 | — | not-yet-exercised. |
| tool | `extract_interface_cross_project_preview` | cross-project | needs-more-evidence | 6 | — | not-yet-exercised. Pre-emptive concern: shares the same using-directive generation path as §9.1 — re-test with cross-project target to see if the bug widens. |
| tool | `dependency_inversion_preview` | cross-project | needs-more-evidence | 6 | — | not-yet-exercised. |
| tool | `migrate_package_preview` | orchestration | needs-more-evidence | 7 | — | not-yet-exercised. |
| tool | `split_class_preview` | orchestration | needs-more-evidence | 7 | — | not-yet-exercised. |
| tool | `extract_and_wire_interface_preview` | orchestration | needs-more-evidence | 7 | — | not-yet-exercised. Composite wrapping `extract_interface_preview` — likely blocked by §9.1 until that's fixed. |
| tool | `apply_composite_preview` | orchestration | needs-more-evidence | 7 | — | not-yet-exercised. |
| tool | `symbol_impact_sweep` | analysis | needs-more-evidence | 7c.1 | — | not-yet-exercised. |
| tool | `test_reference_map` | validation | needs-more-evidence | 7c.2 | — | not-yet-exercised. |
| tool | `validate_workspace` | validation | needs-more-evidence | 7c.3 | — | not-yet-exercised. Would have surfaced §9.1/9.2 failures automatically. |
| tool | `get_prompt_text` | prompts | needs-more-evidence | 7c.4 | — | not-yet-exercised. |
| resource | `source_file_lines` | resources | needs-more-evidence | 7b | — | not-yet-exercised. |
| prompt | `explain_error` | prompts | needs-more-evidence | 8 | — | not-yet-exercised. |
| prompt | `suggest_refactoring` | prompts | needs-more-evidence | 8 | — | not-yet-exercised. |
| prompt | `review_file` | prompts | needs-more-evidence | 8 | — | not-yet-exercised. |
| prompt | `analyze_dependencies` | prompts | needs-more-evidence | 8 | — | not-yet-exercised. |
| prompt | `debug_test_failure` | prompts | needs-more-evidence | 8 | — | not-yet-exercised. |
| prompt | `refactor_and_validate` | prompts | needs-more-evidence | 8 | — | not-yet-exercised. |
| prompt | `fix_all_diagnostics` | prompts | needs-more-evidence | 8 | — | not-yet-exercised. |
| prompt | `guided_package_migration` | prompts | needs-more-evidence | 8 | — | not-yet-exercised. |
| prompt | `guided_extract_interface` | prompts | needs-more-evidence | 8 | — | not-yet-exercised. Wraps `extract_interface_preview` — expected blocked by §9.1. |
| prompt | `security_review` | prompts | needs-more-evidence | 8 | — | not-yet-exercised. |
| prompt | `discover_capabilities` | prompts | needs-more-evidence | 8 | — | not-yet-exercised. |
| prompt | `dead_code_audit` | prompts | needs-more-evidence | 8 | — | not-yet-exercised. |
| prompt | `review_test_coverage` | prompts | needs-more-evidence | 8 | — | not-yet-exercised. |
| prompt | `review_complexity` | prompts | needs-more-evidence | 8 | — | not-yet-exercised. |
| prompt | `cohesion_analysis` | prompts | needs-more-evidence | 8 | — | not-yet-exercised. |
| prompt | `consumer_impact` | prompts | needs-more-evidence | 8 | — | not-yet-exercised. |
| prompt | `guided_extract_method` | prompts | needs-more-evidence | 8 | — | not-yet-exercised. |
| prompt | `msbuild_inspection` | prompts | needs-more-evidence | 8 | — | not-yet-exercised. |
| prompt | `session_undo` | prompts | needs-more-evidence | 8 | — | not-yet-exercised. Given §9.3 this prompt's usefulness is suspect. |
| prompt | `refactor_loop` | prompts | needs-more-evidence | 8 | — | not-yet-exercised. |

**Ledger summary:** 61 experimental entries (40 tools + 20 prompts + 1 resource). 6 exercised. 1 `skipped-repo-shape`. 54 `needs-more-evidence`.

## 3. Performance baseline (`_meta.elapsedMs`)

| Tool | Category | Calls | p50_ms | max_ms | Input scale | Budget | Notes |
|------|----------|-------|--------|--------|-------------|--------|-------|
| `workspace_load` | scaffolding | 2 | 3832 | 4439 | 9 projects / 402 docs | load | under load budget |
| `project_graph` | scaffolding | 1 | 6 | 6 | 9 projects | read ≤5s | ✅ |
| `project_diagnostics` | analysis | 2 | 10323 | 20574 | solution-wide / 44 IDs / 1,122 findings | scan ≤15s | ⚠️ **20.5s exceeds 15s scan budget on full solution scan**; 73ms when narrowed by `diagnosticId` + `limit` |
| `get_cohesion_metrics` | analysis | 1 | 752 | 752 | 10 types | scan ≤15s | ✅ |
| `get_complexity_metrics` | analysis | 1 | 3182 | 3182 | 10 methods | scan ≤15s | ✅ |
| `fix_all_preview` | refactoring | 5 | 286 | 2271 | solution / project / document | scan ≤15s | ✅ fast because no providers fired |
| `diagnostic_details` | analysis | 1 | 6 | 6 | 1 site | read ≤5s | ✅ |
| `format_check` | refactoring | 1 | 382 | 382 | 101 documents | scan ≤15s | ✅ |
| `format_range_preview` | refactoring | 1 | 24 | 24 | 1 file, lines 1–400 | read ≤5s | ✅ BUT empty diff hunks (§9.5) |
| `format_range_apply` | refactoring | 1 | 37 | 37 | 1 file | writer ≤30s | ✅ |
| `extract_interface_preview` | refactoring | 2 | 2220 | 4428 | 3 members on 1 type | read ≤5s | Second call 3.7s in auto-reload after sibling apply |
| `extract_interface_apply` | refactoring | 1 | 467 | 467 | 2 files | writer ≤30s | Applied but RESULT FAILED COMPILE (§9.1) |
| `symbol_search` | analysis | 1 | 169 | 169 | 1 query | read ≤5s | ✅ |
| `document_symbols` | analysis | 1 | 7 | 7 | 1 file | read ≤5s | ✅ |
| `type_hierarchy` | analysis | 1 | 4145 | 4145 | cold after auto-reload | read ≤5s | Within budget (cold) |
| `compile_check` | analysis | 4 | 3144 | 3644 | solution-wide | scan ≤15s | ✅ when unfiltered; false-clean when filtered (§9.5/§9.6) |
| `move_type_to_file_preview` | refactoring | 1 | 236 | 236 | 1 type | read ≤5s | ✅ (preview body is semantically wrong — §9.2) |
| `move_type_to_file_apply` | refactoring | 1 | 504 | 504 | 2 files (claimed) | writer ≤30s | Applied but RESULT FAILED COMPILE (§9.2) |
| `revert_last_apply` | undo | 2 | 2418 | 2477 | 1 op rollback each | writer ≤30s | Runs, BUT leaves created files on disk (§9.3) |
| `workspace_list` | scaffolding | 1 | 1 | 1 | 1 workspace | read ≤5s | ✅ |
| `workspace_status` | scaffolding | 1 | 2 | 2 | 1 workspace | read ≤5s | ✅ (after reload) |

## 4. Schema vs behaviour drift (summary — detail in §9)

| Tool | Mismatch kind | Expected | Actual | Severity | See |
|------|---------------|----------|--------|----------|-----|
| `extract_interface_preview` | semantic correctness | Generated interface compiles | Missing `using` directive on generated file → CS0246 on apply | **FAIL** | §9.1 |
| `move_type_to_file_apply` | apply fidelity | Source type removed, target type added, per preview diff | Source type NOT removed; extra file created at wrong path; preview diff both under-reports deletions AND under-reports creations | **FAIL** | §9.2 |
| `revert_last_apply` | undo completeness | On-disk state restored to pre-apply | Created files remain on disk; csproj mutations not reverted | **FAIL** | §9.3 |
| `fix_all_preview` | error-path guidance | Consistent `guidanceMessage` on `fixedCount=0` | IDE0028 and IDE0305 return `guidanceMessage: null`; IDE0060/SYSLIB1045/MA0089/xUnit2024 return actionable strings | FLAG | §9.4 |
| `format_range_preview` | preview fidelity | Preview `unifiedDiff` contains the hunks that apply will write | Returns a diff with only headers (`--- a/\r\n+++ b/\r\n`) and no `@@ … @@` hunks, yet apply DOES modify the file | FLAG | §9.5 |
| `compile_check` with `projectName` / `file` filter | scope honoured | Compile the filtered project(s) and report errors | Returns `success: true, totalProjects: 0, completedProjects: 0, totalDiagnostics: 0, elapsedMs: 0` — silently reports "clean" even when the unfiltered call shows 6+ errors on those projects | FLAG | §9.5 |
| `*_apply` tools executed in same batch as sibling `*_apply` | preview-token lifetime | Tokens remain valid until consumed or until the author explicitly mutates the workspace | Token issued by a preview in turn T is invalidated by an unrelated `*_apply` in turn T before its own `*_apply` runs | FLAG | §9.6 |
| `workspace_list` after applies | state reporting | `isReady: true` once auto-reload finishes | Reports `isReady: false, workspaceErrorCount: 1` even though subsequent tool calls succeed against that workspaceId | FLAG | §9.7 |

## 5. Error message quality

> Phase 9 was not reached directly, but the following probes ran incidentally:

| Tool | Probe input | Rating | Message | Notes |
|------|-------------|--------|---------|-------|
| `fix_all_preview` | `diagnosticId=SYSLIB1045, scope=solution` | **actionable** | `"No code fix provider is loaded for diagnostic 'SYSLIB1045'. Restore analyzer packages (IDE/CA rules). Use list_analyzers to see loaded diagnostic IDs."` | Good |
| `fix_all_preview` | `diagnosticId=IDE0060, scope=solution` | **actionable** | `"No code fix provider is loaded for diagnostic 'IDE0060'. The IDE code fix provider for 'IDE0060' could not be loaded (constructor requirements). Try code_fix_preview on individual instances, or use list_analyzers to check if the diagnostic is present."` | Good |
| `fix_all_preview` | `diagnosticId=MA0089, scope=solution` | **actionable** | Same template as SYSLIB1045 | Good |
| `fix_all_preview` | `diagnosticId=xUnit2024, scope=solution` | **actionable** | Same template | Good |
| `fix_all_preview` | `diagnosticId=IDE0028, scope=project/document` | **unhelpful** | `guidanceMessage: null` | See §9.4 |
| `fix_all_preview` | `diagnosticId=IDE0305, scope=solution` | **unhelpful** | `guidanceMessage: null` | See §9.4 |
| `extract_interface_apply` | stale preview token (invalidated by sibling `format_range_apply` in same turn) | **actionable** | `"Not found: Preview token '79ef8f84…' not found or expired."` | Good — but the invalidation cause isn't documented |
| `project_graph` / `workspace_status` | workspaceId after host-process session loss | **actionable** | `"Not found: Workspace '4b3d5b09…' not found or has been closed. Active workspace IDs are listed by workspace_list."` | Good |
| `diagnostic_details` | unfixable `IDE0028` at specific site | **actionable** | `supportedFixes: []` | Correct behaviour — but then `fix_all_preview` for the same ID returns null guidance instead of the same "no provider" message |

## 6. Parameter-path coverage

| Family | Non-default path tested | Status | Notes |
|--------|--------------------------|--------|-------|
| `fix_all_preview` | scope=project (non-default) | ✅ | Returned empty — same path as solution/document because no provider fired |
| `fix_all_preview` | scope=document (non-default) | ✅ | Same |
| `format_check` | projectName filter | ✅ | Correctly scoped to 101 Core docs |
| `project_diagnostics` | summary=true (non-default) | ✅ | Returned 44 distinct IDs with counts |
| `project_diagnostics` | diagnosticId + limit (non-default) | ✅ | 73 ms |
| `compile_check` | severity=Error + limit | ✅ | Pagination works |
| `compile_check` | projectName filter | ❌ FAIL | See §9.5 — returns zero-projects without error |
| `compile_check` | file filter | ❌ FAIL | Same as projectName — returns zero-projects silently |
| `extract_interface_preview` | default member set | ⚠️ | Exercised but semantically broken — see §9.1 |
| `move_type_to_file_preview` | default targetFilePath | ⚠️ | Exercised but preview body diverges from apply — see §9.2 |
| `scaffold_type_preview` | kind=record / kind=interface | not-tested | Phase 4 not reached |
| `get_msbuild_properties` | propertyNameFilter | not-tested | Phase 3 not reached |
| File operations | move with namespace update | not-tested | Phase 2 not reached |
| Project mutation | conditional property, CPM | not-tested / `skipped-repo-shape` | |

## 7. Prompt verification (Phase 8)

> Phase 8 not reached in this run. All 20 experimental prompts remain `needs-more-evidence`.

## 8. Experimental promotion scorecard

Rubric from the prompt:
- **promote** — all of: exercised end-to-end with ≥1 non-default path, schema matched behaviour, no FAIL, p50 within budget, preview/apply round-trip clean (if applicable), actionable error on ≥1 negative probe, catalog description matched actual behaviour.
- **keep-experimental** — exercised with pass signal but missing ≥1 promote criterion.
- **needs-more-evidence** — not exercised or too shallow to judge.
- **deprecate** — exercised with FAIL findings that warrant removal or fix-before-promote.

| Kind | Name | Category | Status | p50_ms | schema_ok | error_ok | round_trip_ok | Failures | Recommendation | Evidence |
|------|------|----------|--------|--------|-----------|----------|----------------|----------|----------------|----------|
| tool | `fix_all_preview` | refactoring | exercised-preview-only | 286 | partial | partial | n/a | 0 | **keep-experimental** | Inconsistent guidance emission (§9.4). |
| tool | `fix_all_apply` | refactoring | blocked | — | n/a | n/a | n/a | 0 | **needs-more-evidence** | No token available. |
| tool | `format_range_apply` | refactoring | exercised-apply | 37 | n/a | n/a | yes (partial) | 0 | **keep-experimental** | Apply works, but sibling preview emits empty diff (§9.5). |
| tool | `extract_interface_preview` | refactoring | exercised | 2220 | FAIL | n/a | no | 1 | **deprecate** (or block promote pending §9.1 fix) | **FAIL §9.1** — generates broken code. |
| tool | `extract_interface_apply` | refactoring | exercised-apply | 467 | inherits | actionable (stale token) | no | 1 (inherited) | **keep-experimental** conditional on 1c fix | Apply surface fine; payload fidelity is the problem. |
| tool | `extract_type_apply` | refactoring | needs-more-evidence | — | | | | | **needs-more-evidence** | Not exercised. |
| tool | `move_type_to_file_apply` | refactoring | exercised-apply | 504 | FAIL | n/a | no | 1 | **deprecate** (or block promote pending §9.2 fix) | **FAIL §9.2** — preview ≠ apply. |
| tool | `bulk_replace_type_apply` | refactoring | needs-more-evidence | — | | | | | **needs-more-evidence** | |
| tool | `extract_method_apply` | refactoring | needs-more-evidence | — | | | | | **needs-more-evidence** | |
| tool | `format_check` | refactoring | exercised | 382 | ok | n/a | n/a | 0 | **promote-candidate** | Correctly found 1 violation across 101 docs; projectName filter honoured. Caveat: limited probe (single project). |
| tool | `restructure_preview` | refactoring | needs-more-evidence | — | | | | | **needs-more-evidence** | |
| tool | `replace_string_literals_preview` | refactoring | needs-more-evidence | — | | | | | **needs-more-evidence** | |
| tool | `change_signature_preview` | refactoring | needs-more-evidence | — | | | | | **needs-more-evidence** | |
| tool | `symbol_refactor_preview` | refactoring | needs-more-evidence | — | | | | | **needs-more-evidence** | |
| tool | `create_file_apply` | file-operations | needs-more-evidence | — | | | | | **needs-more-evidence** | |
| tool | `move_file_apply` | file-operations | needs-more-evidence | — | | | | | **needs-more-evidence** | |
| tool | `delete_file_apply` | file-operations | needs-more-evidence | — | | | | | **needs-more-evidence** | |
| tool | `add_central_package_version_preview` | project-mutation | skipped-repo-shape | — | | | | | **needs-more-evidence** | Repo has no CPM. |
| tool | `apply_project_mutation` | project-mutation | needs-more-evidence | — | | | | | **needs-more-evidence** | |
| tool | `scaffold_type_preview` | scaffolding | needs-more-evidence | — | | | | | **needs-more-evidence** | |
| tool | `scaffold_type_apply` | scaffolding | needs-more-evidence | — | | | | | **needs-more-evidence** | |
| tool | `scaffold_test_apply` | scaffolding | needs-more-evidence | — | | | | | **needs-more-evidence** | |
| tool | `scaffold_test_batch_preview` | scaffolding | needs-more-evidence | — | | | | | **needs-more-evidence** | |
| tool | `remove_dead_code_apply` | dead-code | needs-more-evidence | — | | | | | **needs-more-evidence** | |
| tool | `remove_interface_member_preview` | dead-code | needs-more-evidence | — | | | | | **needs-more-evidence** | |
| tool | `apply_multi_file_edit` | editing | needs-more-evidence | — | | | | | **needs-more-evidence** | |
| tool | `preview_multi_file_edit` | editing | needs-more-evidence | — | | | | | **needs-more-evidence** | |
| tool | `preview_multi_file_edit_apply` | editing | needs-more-evidence | — | | | | | **needs-more-evidence** | |
| tool | `apply_with_verify` | undo | needs-more-evidence | — | | | | | **needs-more-evidence** | Would have auto-reverted §9.1/§9.2 — high priority on resume. |
| tool | `move_type_to_project_preview` | cross-project | needs-more-evidence | — | | | | | **needs-more-evidence** | Likely affected by §9.1 using-directive bug. |
| tool | `extract_interface_cross_project_preview` | cross-project | needs-more-evidence | — | | | | | **needs-more-evidence** | Same path as §9.1 — likely worse cross-project. |
| tool | `dependency_inversion_preview` | cross-project | needs-more-evidence | — | | | | | **needs-more-evidence** | |
| tool | `migrate_package_preview` | orchestration | needs-more-evidence | — | | | | | **needs-more-evidence** | |
| tool | `split_class_preview` | orchestration | needs-more-evidence | — | | | | | **needs-more-evidence** | |
| tool | `extract_and_wire_interface_preview` | orchestration | needs-more-evidence | — | | | | | **needs-more-evidence** | Composite wrapping `extract_interface_preview` — blocked pending §9.1 fix. |
| tool | `apply_composite_preview` | orchestration | needs-more-evidence | — | | | | | **needs-more-evidence** | |
| tool | `symbol_impact_sweep` | analysis | needs-more-evidence | — | | | | | **needs-more-evidence** | |
| tool | `test_reference_map` | validation | needs-more-evidence | — | | | | | **needs-more-evidence** | |
| tool | `validate_workspace` | validation | needs-more-evidence | — | | | | | **needs-more-evidence** | |
| tool | `get_prompt_text` | prompts | needs-more-evidence | — | | | | | **needs-more-evidence** | |
| resource | `source_file_lines` | resources | needs-more-evidence | — | | | | | **needs-more-evidence** | |
| prompt × 20 | (all Phase 8 prompts) | prompts | needs-more-evidence | — | | n/a | n/a | 0 | **needs-more-evidence** | Phase 8 not reached. |

## 9. MCP server issues (bugs)

### 9.1 `extract_interface_preview` emits interface file without required `using` directive — result fails to compile

| Field | Detail |
|--------|--------|
| Tool | `extract_interface_preview` → `extract_interface_apply` |
| Severity | **High (FAIL)** — produces code that does not compile; silently shipped to disk on apply |
| Reproducibility | 1/1 on this repo |

**Input:**
```json
{
  "workspaceId": "a041e542b664491e970376f2fbe02c91",
  "filePath": "C:\\Code-Repo\\DotNet-Network-Documentation\\.worktrees\\exp-promotion\\src\\NetworkDocumentation.Diagrams\\NetworkDocumentation.Diagrams\\DiagramService.cs",
  "typeName": "DiagramService",
  "interfaceName": "IDiagramService"
}
```

**Source file context (`DiagramService.cs` has `using NetworkDocumentation.Core.Models;` among its top-of-file usings; the 3 public methods use `NetworkInventory` which lives in that namespace).**

**Preview diff for the generated `IDiagramService.cs` (verbatim from the response):**
```
--- a/…\IDiagramService.cs
+++ b/…\IDiagramService.cs
@@ -1,0 +1,7 @@
+namespace NetworkDocumentation.Diagrams;
+public interface IDiagramService
+{
+    void GenerateAll(NetworkInventory inventory, string outputDir);
+    void GenerateAreaScopedDiagrams(NetworkInventory inventory, string mapsDirectory, string diagramSet = "all", bool exportPng = false, string pngLayout = "neato", int pngDpi = 150);
+    void GenerateRegionScopedDiagrams(NetworkInventory inventory, string mapsDirectory);
+}
```
No `using NetworkDocumentation.Core.Models;` directive is present, and the types are not fully-qualified.

**Expected:** Either (a) add `using NetworkDocumentation.Core.Models;` to the generated interface file, or (b) fully-qualify `NetworkInventory` as `global::NetworkDocumentation.Core.Models.NetworkInventory`.

**Actual after `extract_interface_apply`:**
```
CS0246  The type or namespace name 'NetworkInventory' could not be found …    IDiagramService.cs line 4 col 22
CS0246  …    IDiagramService.cs line 5 col 37
CS0246  …    IDiagramService.cs line 6 col 39
CS0535  'DiagramService' does not implement interface member 'IDiagramService.GenerateAll(NetworkInventory, string)'    DiagramService.cs line 15 col 4
CS0535  …GenerateAreaScopedDiagrams…    DiagramService.cs line 15 col 4
CS0535  …GenerateRegionScopedDiagrams…    DiagramService.cs line 15 col 4
```

**Fix-the-fix hint for the server owner:** The refactoring should copy the relevant namespace imports from the source file (transitively, based on symbol references inside the extracted members). `SyntaxGenerator.AddImports` or `Simplifier.ReduceAsync` with an annotated node set should handle this. Same bug is likely present in `extract_interface_cross_project_preview` (cross-project target has even less chance of inheriting the right usings).

---

### 9.2 `move_type_to_file_apply` leaves duplicate type in source file AND creates a rogue copy at project root — 90 compile errors

| Field | Detail |
|--------|--------|
| Tool | `move_type_to_file_preview` → `move_type_to_file_apply` |
| Severity | **Critical (FAIL)** — preview diff does not match the bytes the apply actually writes; "trust the preview" agent contract is broken both on deletions (under-reported) and on creations (under-reported) |
| Reproducibility | 1/1 on this repo |

**Input:**
```json
{
  "workspaceId": "a041e542b664491e970376f2fbe02c91",
  "sourceFilePath": "C:\\Code-Repo\\DotNet-Network-Documentation\\.worktrees\\exp-promotion\\src\\NetworkDocumentation.Core\\NetworkDocumentation.Core\\Diff\\DiffModels.cs",
  "typeName": "DiffSummary"
}
```
(`targetFilePath` omitted — defaults to `DiffSummary.cs` in the same directory per the tool schema.)

**Preview response (two file changes, verbatim):**
```
changes[0].filePath:   …\Diff\DiffModels.cs
changes[0].unifiedDiff:
  @@ -54,21 +54,6 @@
   // ---- Aggregate summary ----

   /// <summary>Aggregate counts for a ChangeSet.</summary>
  -public class DiffSummary
  -{
  -    public int DevicesAdded { get; set; }
  -    public int DevicesRemoved { get; set; }
  -    public int DevicesDecommissioned { get; set; }
  -    public int DevicesChanged { get; set; }
  -    public int LinksAdded { get; set; }
  -    public int LinksRemoved { get; set; }
  -    public int LinksDecommissioned { get; set; }
  -    public int LinksChanged { get; set; }
  -
  -    public int TotalChanges =>
  -        DevicesAdded + DevicesRemoved + DevicesDecommissioned + DevicesChanged
  -        + LinksAdded + LinksRemoved + LinksDecommissioned + LinksChanged;
  -}

   // ---- Full diff result ----

changes[1].filePath:   …\Diff\DiffSummary.cs   (new file)
changes[1].unifiedDiff:
  @@ -1,0 +1,15 @@
  +namespace NetworkDocumentation.Core.Diff;
  +// ---- Aggregate summary ----
  +/// <summary>Aggregate counts for a ChangeSet.</summary>
  +public class DiffSummary
  +{
  +    public int DevicesAdded { get; set; }
  +    … (same body) …
  +    public int TotalChanges => DevicesAdded + … + LinksChanged;
  +}
```

**Apply response:**
```json
{
  "success": true,
  "appliedFiles": [
    "…\\Diff\\DiffSummary.cs",
    "…\\Diff\\DiffModels.cs"
  ]
}
```

**Actual state on disk (post-apply, before revert):**
1. `Diff/DiffSummary.cs` — CREATED, contains the type. ✅ matches preview[1].
2. `Diff/DiffModels.cs` — **still contains `public class DiffSummary` at line 57.** The `-` hunks from preview[0] were NOT applied. ❌ contradicts preview[0].
3. **NEW: project-root-level `src/NetworkDocumentation.Core/NetworkDocumentation.Core/DiffSummary.cs`** — created by the apply, a second copy of the type. **Not mentioned in the preview at all.** ❌ creates a phantom file outside the preview contract.

Verified by `ls src/NetworkDocumentation.Core/NetworkDocumentation.Core/Diff/` showing `DiffSummary.cs` AND `ls src/NetworkDocumentation.Core/NetworkDocumentation.Core/` showing a second `DiffSummary.cs` at project root. Verified by `git status --short` after revert:
```
?? src/.../Diff/DiffSummary.cs
?? src/.../DiffSummary.cs         ← ROGUE (never in preview)
```

**Compile result after apply:**
```
compile_check (no filter) →
  success: false
  errorCount: 90
  first 5 diagnostics:
    CS0101  The namespace 'NetworkDocumentation.Core.Diff' already contains a definition for 'DiffSummary'    Diff/DiffSummary.cs line 4 col 14
    CS0229  Ambiguity between 'DiffSummary.DevicesAdded' and 'DiffSummary.DevicesAdded'    DiffEngine.cs line 71 col 13
    CS0229  … DiffSummary.DevicesRemoved …    DiffEngine.cs line 72 col 13
    CS0229  … DiffSummary.DevicesDecommissioned …    DiffEngine.cs line 73 col 13
    CS0229  … DiffSummary.DevicesChanged …    DiffEngine.cs line 74 col 13
  hasMore: true (90 total; cascade through DiffEngine, DiffRecordMapper, ChangesSheetBuilder, ChangeReportGenerator, InventorySummaryBuilder)
```

**Expected:** source file's `DiffSummary` class removed per preview[0]; single copy in `Diff/DiffSummary.cs` per preview[1]; no project-root file; `appliedFiles` must enumerate every file the apply actually touched (including csproj mutations if any).

**Fix-the-fix hint for the server owner:**
- Preview generation and apply execution appear to use **different code paths**. The preview computes hunks via text diff; the apply uses a Roslyn `DocumentAction` that inserts the new file but doesn't carry the source-file delete through. Unify both paths (ideally generate the preview from the same `CodeAction`'s `GetOperationsAsync` output the apply uses).
- The phantom root-level file suggests the apply resolves `targetFilePath` twice (once correctly to `Diff/DiffSummary.cs`, once incorrectly to project-root `DiffSummary.cs`). Audit the path-resolution logic; check if there's a default-target fallback that fires even when a resolved target already exists.
- `appliedFiles` should list every file written (including the rogue one, if the fix keeps that logic at all).

---

### 9.3 `revert_last_apply` leaves files created by the apply on disk

| Field | Detail |
|--------|--------|
| Tool | `revert_last_apply` |
| Severity | **High (FAIL)** — documented semantic is "restore previous solution state"; callers that rely on revert to recover from a bad apply will still have broken disk state |
| Reproducibility | 2/2 (observed after `extract_interface_apply` revert AND after `move_type_to_file_apply` revert) |

**Input (first revert):**
```json
{"workspaceId": "a041e542b664491e970376f2fbe02c91"}
```

**Response:**
```json
{
  "reverted": true,
  "revertedOperation": "Extract interface 'IDiagramService' from 'DiagramService' with 3 member(s)",
  "appliedAtUtc": "2026-04-15T14:26:28.8334213Z"
}
```

**Actual on-disk state after revert (verified by `git status --short` after cleanup):**
```
M  src/NetworkDocumentation.Core/NetworkDocumentation.Core/NetworkDocumentation.Core.csproj
M  src/NetworkDocumentation.Core/NetworkDocumentation.Core/Utils/InventorySummaryBuilder.cs   ← from format_range, intentionally retained
M  src/NetworkDocumentation.Diagrams/NetworkDocumentation.Diagrams/NetworkDocumentation.Diagrams.csproj
?? src/NetworkDocumentation.Core/NetworkDocumentation.Core/Diff/DiffSummary.cs
?? src/NetworkDocumentation.Core/NetworkDocumentation.Core/DiffSummary.cs
?? src/NetworkDocumentation.Diagrams/NetworkDocumentation.Diagrams/IDiagramService.cs
```

**Expected:** `revert_last_apply` should either:
- (a) delete files that the reverted apply created (`IDiagramService.cs`, `Diff/DiffSummary.cs`, project-root `DiffSummary.cs`) and undo csproj changes; or
- (b) at minimum return a `warnings: [...]` array naming the files that are NOT automatically removed so callers can clean up (`{"warnings": ["File 'X.cs' was created by apply and left in place; remove manually."]}`).

**Actual:** `reverted: true` with no warnings. Files and csproj edits persist. Required `git reset --hard HEAD && git clean -fd` to recover a clean tree (which is not available to most Roslyn MCP callers).

**Fix-the-fix hint for the server owner:** the revert is snapshot-based (workspace Solution object) but filesystem-side the `Solution.WithDocumentText` / document-add operations already wrote bytes. Either the snapshot restore must re-traverse the filesystem diff vs. `appliedFiles` and remove additions, or the tool must return `warnings` listing the un-reverted artifacts.

---

### 9.4 `fix_all_preview` returns `guidanceMessage: null` for some IDE-series diagnostic IDs

| Field | Detail |
|--------|--------|
| Tool | `fix_all_preview` |
| Severity | FLAG — observable tool behaviour is inconsistent; caller cannot distinguish "no diagnostic occurrences" from "no provider loaded" |
| Reproducibility | deterministic per ID across scope values |

**Inputs & Responses (abridged):**

| diagnosticId | scope | fixedCount | guidanceMessage | Verdict |
|---|---|---|---|---|
| `IDE0028` | project | 0 | **null** | unhelpful |
| `IDE0028` | document | 0 | **null** | unhelpful |
| `IDE0090` | solution | 0 | **null** | unhelpful |
| `IDE0305` | solution | 0 | **null** | unhelpful |
| `IDE0060` | solution | 0 | `"No code fix provider is loaded for diagnostic 'IDE0060'. The IDE code fix provider for 'IDE0060' could not be loaded (constructor requirements). Try code_fix_preview on individual instances, or use list_analyzers to check if the diagnostic is present."` | actionable |
| `SYSLIB1045` | solution | 0 | `"No code fix provider is loaded for diagnostic 'SYSLIB1045'. Restore analyzer packages (IDE/CA rules). Use list_analyzers to see loaded diagnostic IDs."` | actionable |
| `MA0089` | solution | 0 | (same template as SYSLIB1045) | actionable |
| `xUnit2024` | solution | 0 | (same template) | actionable |

**Expected:** whenever `fixedCount === 0`, emit one of the actionable templates above. Never `null`.

**Fix-the-fix hint:** the provider-detection short-circuit for `IDE0028`, `IDE0090`, `IDE0305` (and likely others) returns early without populating `guidanceMessage`. Add a default-fill at the end of `fix_all_preview` when the result is empty: `guidanceMessage ??= $"No code fix provider is loaded for diagnostic '{id}'. …"`.

---

### 9.5 `format_range_preview` returns preview token with empty unified diff; `compile_check` with `projectName`/`file` filter silently reports zero diagnostics after an auto-reload

Two related scope-filter / preview-fidelity defects; grouped because they both produce misleading "clean" signals to the caller.

| Defect | Input | Expected | Actual |
|---|---|---|---|
| A. `format_range_preview` empty diff | file with 1 format violation confirmed by `format_check`; range `lines 1–400` | `unifiedDiff` contains the hunks that will change | `unifiedDiff` is just `--- a/…\r\n+++ b/…\r\n` — no `@@` hunks — yet `format_range_apply` with the returned token actually modifies the file |
| B. `compile_check` filtered false-clean | post-`extract_interface_apply`; `projectName="NetworkDocumentation.Diagrams"` or `file=".../IDiagramService.cs"` | Compile the filtered project/file and report its diagnostics | `{success: true, totalProjects: 0, completedProjects: 0, totalDiagnostics: 0, elapsedMs: 0, staleAction: "auto-reloaded"}` — the same workspace with no filter shows **6 errors** on those exact files |

**Response (A) verbatim:**
```json
{
  "previewToken": "93bab7b9ed9d41e1af8ddeabdd2a6bd6",
  "description": "Format range in 'InventorySummaryBuilder.cs' (lines 1-400)",
  "changes": [{
    "filePath": "…\\InventorySummaryBuilder.cs",
    "unifiedDiff": "--- a/…\\InventorySummaryBuilder.cs\r\n+++ b/…\\InventorySummaryBuilder.cs\r\n"
  }]
}
```

**Response (B) verbatim:**
```json
{
  "success": true,
  "errorCount": 0, "warningCount": 0,
  "totalDiagnostics": 0, "returnedDiagnostics": 0,
  "completedProjects": 0, "totalProjects": 0,
  "elapsedMs": 0,
  "_meta": {"staleAction": "auto-reloaded", "staleReloadMs": 2639}
}
```

**Severity:** both FLAG — consequential because they **look** like passing results. An agent running a verify-after-apply loop will conclude "clean" when the workspace is broken.

**Fix-the-fix hints:**
- (A) Either populate `unifiedDiff` from the same whitespace-delta source the apply uses, OR document in the tool description that the preview is opaque and the caller must apply to see the change.
- (B) When `projectName` or `file` filter resolves to zero projects in the current `Solution`, return `success: false` plus a message (`"No project matched name 'X' in the current workspace. Did the workspace auto-reload? Re-issue the call."`). Do not silently return `success: true / totalProjects: 0`.

---

### 9.6 Preview-token issued in turn T invalidated by a sibling `*_apply` in the same turn

| Field | Detail |
|--------|--------|
| Tool | `extract_interface_apply` (consumer) ; `format_range_apply` (invalidator) |
| Severity | FLAG — unexpected coupling of preview tokens across unrelated files; not documented in the tool schemas |
| Reproducibility | 1/1 (observed in the initial 1b+1c parallel batch) |

**Repro:** In a single assistant message, issue `format_range_preview` (file A) AND `extract_interface_preview` (file B) in parallel. Then, in the NEXT assistant message, issue `format_range_apply(tokenA)` AND `extract_interface_apply(tokenB)` in parallel.

**Observed:**
- `format_range_apply` succeeds, bumps workspace version.
- `extract_interface_apply` returns `{"error": true, "category": "NotFound", "message": "Preview token '79ef…' not found or expired."}` even though the token was issued seconds earlier and neither the caller nor the filesystem mutated file B.

**Expected:** preview tokens should remain valid across unrelated mutations, OR the tool schema should explicitly document that any `*_apply` call bumps the workspace version and invalidates all live tokens.

**Fix-the-fix hint:** two options — (a) bind preview tokens to the specific file set they target, so unrelated applies don't invalidate them; (b) document the coupling clearly in `format_range_apply` (and every other `*_apply`) description and have agents explicitly sequence applies.

---

### 9.7 `workspace_list` reports `isReady: false, workspaceErrorCount: 1` during refactoring transitions, but the workspace is still usable

| Field | Detail |
|--------|--------|
| Tool | `workspace_list` |
| Severity | FLAG — soft inconsistency; does not block work, but contradicts the contract implied by `isReady` |
| Reproducibility | 1/1 (observed after two applies + one revert) |

**Input:**
```json
{}
```
at session state: workspace had just completed `format_range_apply` + reverted `extract_interface_apply`.

**Response:**
```json
{
  "count": 1,
  "workspaces": [{
    "workspaceId": "a041e542b664491e970376f2fbe02c91",
    "workspaceVersion": 7,
    "workspaceDiagnosticCount": 1,
    "workspaceErrorCount": 1,
    "workspaceWarningCount": 0,
    "isReady": false,
    "isStale": false,
    …
  }]
}
```

**Contradiction:** `isReady: false` — but immediately afterwards `compile_check` against this same workspaceId returned 90 real errors from a valid compile (i.e. the workspace was demonstrably ready enough to compile).

**Expected:** either `isReady: true` once the reload settles, or a machine-readable `notReadyReason` field explaining which step the server is still working on.

**Fix-the-fix hint:** the `isReady` flag seems to track `workspaceErrorCount === 0`, but that conflates "workspace infrastructure is usable" with "workspace has zero errors." Separate those signals: `isReady` should remain true once MSBuild graph + Roslyn solution resolve, regardless of diagnostics count.

---

### 9.8 (context / environmental, not a bug) — MCP host may drop workspace sessions between user turns

**Not a server bug** — per the `workspace_load` tool description ("A workspace can become unreachable if (a) the host process restarts …"). Recording for completeness because it surfaced real tool-error output in this run:

- First `workspace_load` created session `4b3d5b097aef4a96b6cb2f51272efafc`.
- The user turn was interrupted and a new turn started.
- Next tool call `project_graph` → `"Workspace '4b3d5b09…' not found or has been closed."` — actionable error pointing at `workspace_list`.
- Re-issuing `workspace_load` created session `a041e542b664491e970376f2fbe02c91` (used for the rest of the run).

Recommendation for the prompt: add a "pre-phase heartbeat" line in Phase 0 reminding agents to `workspace_list` at each phase boundary (the prompt already says this in "Execution strategy and context conservation" §4). No server change needed.

## 10. Improvement suggestions

- **§9.1 fix priority:** `extract_interface_preview` should inject the required `using` directives or emit fully-qualified type names. Same logic likely needed in `extract_interface_cross_project_preview`, `dependency_inversion_preview`, `extract_and_wire_interface_preview`, and the `guided_extract_interface` prompt.
- **§9.2 fix priority:** unify preview-generation and apply-execution paths for `move_type_to_file_*` so the `unifiedDiff` returned in preview is byte-identical to what the apply writes. Fix the phantom project-root file creation. Ensure `appliedFiles` enumerates every file touched.
- **§9.3 fix priority:** `revert_last_apply` must either physically remove files created by the forward apply OR return a `warnings: [...]` array naming residual files. Document whichever choice you make in the tool schema.
- **§9.4 fix:** `fix_all_preview` should always populate `guidanceMessage` when `fixedCount === 0`. Add default-fill `guidanceMessage ??= "No code fix provider is loaded for diagnostic '{id}'. …"`.
- **§9.5 fix:** (A) populate `format_range_preview.unifiedDiff` with actual hunks or document opacity. (B) `compile_check` with zero-match filter should return `success: false` with an actionable message.
- **§9.6 fix:** scope preview-token validity to the token's own file set, OR clearly document that any `*_apply` invalidates all live tokens.
- **§9.7 fix:** split `isReady` into "infrastructure ready" vs "diagnostics clean" — or rename `isReady` to clarify which it means.
- **Prompt improvement:** add a note to `ai_docs/prompts/experimental-promotion-exercise.md` under "Execution strategy" that **parallel `*_apply` calls in a single turn can invalidate sibling preview tokens**, and recommend sequencing applies one-per-turn during full-surface runs.
- **Performance:** `project_diagnostics` unfiltered full-solution scan took 20.5s (>15s budget). Recommend (a) a warning in the tool description pointing to `summary=true` or `diagnosticId` filtering for sub-second probes, and/or (b) server-side caching so repeated full scans within a session don't re-analyze.
- **`apply_with_verify` should be promoted to stable** once §9.1/§9.2 are fixed, because it is exactly the tool that would have caught these failures automatically (preview → apply → compile_check → auto-revert on new errors). Post-fix, a validation run with `rollbackOnError: true` on every Phase 1 preview would have reported §9.1 and §9.2 without manual intervention.

## 11. Known issue regression check

| Source id | Summary | Status |
|-----------|---------|--------|
| `ai_docs/backlog.md` (2026-04-15T13:40:00Z) | No open rows reference experimental Roslyn MCP tools | **N/A — no prior MCP experimental-tool rows on record** |
| `ai_docs/audit-reports/20260413T180408Z_networkdocumentation_experimental-promotion.md` | Prior audit 2 days earlier. §9.1/9.2/9.3/9.4/9.5/9.6/9.7 are new evidence for the next diff; this run did not cross-reference the prior audit in detail. | **deferred to follow-up** |
| Prompt appendix note `change-signature-preview-callsite-summary` | Documented in prompt as a `keep-experimental` P3 limitation (preview diff omits callsite files). **Not reproduced here — `change_signature_preview` was not exercised.** | **not reproduced (not exercised)** |

---

## Working log (phase-by-phase persistence)

### Phase 0: ✅ complete (14:03–14:04Z)
- `server_info` → 1.18.0 / catalog 2026.04 / 40 exp tools / 20 exp prompts / 1 exp resource
- `workspace_load` (2 attempts; first session lost between turns — §9.8)
- `project_graph` — 9 projects, multi-project, DI-capable
- `dotnet restore` — clean across all 9 projects
- Coverage ledger seeded (61 rows)

### Phase 1: ⚠️ partial (14:05–14:35Z)
- **1a `fix_all_preview`** — exercised-preview-only across 8 IDs. No CodeFix provider loaded. FLAG §9.4 on inconsistent `guidanceMessage` (IDE0028/IDE0090/IDE0305 null vs IDE0060/SYSLIB1045/MA0089/xUnit2024 actionable).
- **1b `format_range_preview` → `format_range_apply`** — clean round-trip on `InventorySummaryBuilder.cs`. FLAG §9.5-A on empty preview diff.
- **1c `extract_interface_preview` → `extract_interface_apply`** — **FAIL §9.1** (missing using → 6 compile errors). Reverted. §9.3 discovered (files remained on disk).
- **1e `move_type_to_file_preview` → `move_type_to_file_apply`** — **FAIL §9.2** (duplicate type + phantom file → 90 compile errors). Reverted. §9.3 confirmed (files remained on disk).
- Additional incidental findings: §9.5-B (`compile_check` filtered false-clean), §9.6 (sibling-token invalidation), §9.7 (`workspace_list.isReady` contradiction).
- Post-FAIL cleanup: `git reset --hard HEAD && git clean -fd` → removed 3 leftover files + 2 csproj mutations.
- Audit terminated after 1e to avoid masking further defects on a destabilized workspace.

### Phases 2–11: ⏸ not exercised
Explicit resume list = the `needs-more-evidence` rows in §2. Prioritize `apply_with_verify` (Phase 5e) because it would have caught §9.1 and §9.2 automatically.

---

## Completion gate / cleanup

- **Canonical file:** this file — `ai_docs/audit-reports/20260415T140302Z_networkdocumentation_experimental-promotion.md` in the **main repo working tree** (NOT the worktree). ✅
- **Draft file:** removed from disk after canonical was published.
- **Worktree:** `.worktrees/exp-promotion` on throwaway branch `exp-promotion-2026-04-15` — removed as part of this audit's cleanup (see §Cleanup executed below). To re-create for a resume run, `git worktree add -b exp-promotion-resume .worktrees/exp-promotion origin/main` and reload the solution.

### Cleanup executed

```powershell
# worktree was hard-reset at end of Phase 1 (recovered from 3 leftover files + 2 csproj mutations)
git -C C:\Code-Repo\DotNet-Network-Documentation\.worktrees\exp-promotion reset --hard HEAD
git -C C:\Code-Repo\DotNet-Network-Documentation\.worktrees\exp-promotion clean -fd

# audit-report draft removed
rm C:\Code-Repo\DotNet-Network-Documentation\ai_docs\audit-reports\20260415T140302Z_networkdocumentation_experimental-promotion.md.draft.md

# worktree and throwaway branch removed
git -C C:\Code-Repo\DotNet-Network-Documentation worktree remove .worktrees/exp-promotion
git -C C:\Code-Repo\DotNet-Network-Documentation branch -D exp-promotion-2026-04-15
```
