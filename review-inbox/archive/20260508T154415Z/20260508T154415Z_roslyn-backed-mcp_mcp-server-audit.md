---
generated_at: 2026-05-08T16:15:25Z
window: 20260508T154415Z
mode: mcp-server-stress
source_repo: roslyn-backed-mcp
---

# Roslyn MCP Server Stress Audit

## 1. Header

Audit target: `C:\Code-Repo\Roslyn-Backed-MCP`.

Skill: `.claude/skills/mcp-server-stress/SKILL.md`.

Prompt: `.claude/skills/mcp-server-stress/prompts/prompt.md`, followed verbatim for phase ordering. The skill references a phase-runner subagent, but this run executed phases inline because the current host policy only permits subagents when the user explicitly asks for delegation.

Isolation: default disposable worktree was used at `C:\Code-Repo\Roslyn-Backed-MCP-audit-deep-20260508T154415Z` on branch `audit-deep/20260508T154415Z`. The worktree was removed after the run.

## 2. Live Server And Workspace

Server precondition passed. Live `server_info` reported:

| Field | Value |
|---|---|
| server | `roslyn-mcp` |
| version | `1.34.2+e01c2f97dfc80e6c1a9888aed70c71191d8666c0` |
| runtime | `.NET 10.0.7` |
| OS | `Microsoft Windows 10.0.26200` |
| Roslyn | `5.3.0.0` |
| catalog | `2026.04` |
| surface | 111 stable tools, 57 experimental tools; 9 stable resources, 4 experimental resources; 20 experimental prompts |
| parity | `registered.tools=168`, `resources=13`, `prompts=20`, `parityOk=true` |

Primary workspace loaded `C:\Code-Repo\Roslyn-Backed-MCP\RoslynMcp.slnx` as `d1a968aa0bc74ee4abc3e26d0c747edc`, version 1, 5 projects, 583 documents, no workspace diagnostics. Disposable worktree workspace loaded `C:\Code-Repo\Roslyn-Backed-MCP-audit-deep-20260508T154415Z\RoslynMcp.slnx` as `c01925f3c1b744cb839df536385a431e`, eventually version 59, 5 projects, 586 documents, no workspace diagnostics.

## 3. Phase Ledger

| Phase | Status | Evidence |
|---|---|---|
| -1 preflight | PASS | `.mcp.json` declared `roslynmcp`; live server was ready and catalog parity passed |
| 0 catalog/workspace | PASS | workspace load/list/status/health/project graph succeeded; `workspace_warm` warmed 4 cold compilations in 1628 ms |
| 1 diagnostics | PASS | `project_diagnostics(summary=true)` found 0 errors, 0 warnings, 509 info; `compile_check` and security/vulnerability probes passed |
| 2 static analysis | PASS | complexity, cohesion, coupling, duplicate, dead local/field, dependency, NuGet, and refactoring suggestion probes returned bounded results |
| 3 semantic relationships | FAIL | two P2 symbol relationship/reference findings were reproduced |
| 4 flow analysis | PASS | source, data flow, control flow, operations, syntax tree, and exception-flow probes returned coherent results |
| 5 snippets/scripts | PASS | snippets, scripts, runtime exception, and infinite-loop timeout returned structured responses |
| 6 writer/refactor apply | PASS with gaps | preview/apply, stale token, undo, format, rename, extract method/interface, string literal, namespace, multi-file, and dead-code flows worked; some advanced orchestration tools were skipped for repo-shape/safety |
| 7 config/MSBuild | PASS with note | editorconfig writes required reload before `get_editorconfig_options` reflected the new value; MSBuild property/item probes worked |
| 8 build/test/validation | FAIL | `build_workspace` and `test_run` failed on analyzer DLL file lock; `validate_recent_git_changes` timed out |
| 8b concurrency | BLOCKED | current host could not issue true concurrent MCP calls; sequential reader baselines passed |
| 10 file/cross-project | PASS with finding | create/move/delete file succeeded; project mutation preview allowed a self project reference |
| 9 undo | PASS | tip revert, non-tip `revert_apply_by_sequence`, unknown sequence, and compile verification behaved as expected |
| 11 semantic search | PASS | `semantic_search`, `semantic_grep`, reflection scan, DI summary, and source-generated docs returned structured results |
| 12 scaffolding | FAIL | type scaffolding compiled; method-test scaffolding generated inaccessible test code for an internal target, and batch preview emitted a compile-fragile nullable constructor expression |
| 13 project mutation | PASS with finding | central package add/remove applied and reversed; previews were bounded; self-reference preview was not rejected |
| 14 navigation/completion | FAIL | navigation/completion mostly passed; `find_overrides` missed an interface-member implementation that `find_base_members` could see |
| 15 resources | PASS | catalog, template, workspace, project, diagnostics, source file, valid line slice, and invalid line range resources worked |
| 16 prompts | PASS with finding | all 20 prompts rendered through `get_prompt_text`; unknown prompt and malformed JSON returned structured errors; prompt guidance should be file-lock-aware |
| 17 boundaries | PASS | invalid workspace, invalid handle, out-of-range positions, bad regex, consumed preview token, and post-close workspace calls returned structured errors |
| 18 regression | PASS | backlog scan found no existing rows for this run's concrete failures; only prior promotion review and deferred workspace-process rows matched related keywords |
| 19 fragments | PASS | nine `backlog.d` fragments emitted for actionable findings |

## 4. Findings

| ID | Severity | Area | Summary | Fragment |
|---|---|---|---|---|
| `roslyn-backed-mcp-build-test-self-analyzer-file-lock` | P1 | tools | `build_workspace`, `test_run`, and the `debug_test_failure` prompt reproduce `MSB3027`/`MSB3021` because `roslynmcp.exe` holds the analyzer DLL from the loaded repo. | yes |
| `roslyn-backed-mcp-file-lock-aware-prompt-validation-guidance` | P2 | prompts | Audit and built-in prompt workflows recommend `build_workspace` / `test_run` without a FileLock-aware stop/fallback path, causing repeated self-host analyzer-lock repros. | yes |
| `roslyn-backed-mcp-validate-recent-git-changes-timeout` | P2 | tools | `validate_recent_git_changes(summary=true, runTests=false)` timed out after 120 seconds on a small dirty set while the server stayed ready. | yes |
| `roslyn-backed-mcp-find-references-duplicate-metadata-candidates` | P2 | tools | `find_references(metadataName=WorkspaceManager)` returned two identical ambiguity candidates for the same declaration. | yes |
| `roslyn-backed-mcp-symbol-relationships-return-token-bucket-mix` | P2 | tools | `symbol_relationships` promoted a return-type token to `LoadAsync`, but references/base members still described `Task`. | yes |
| `roslyn-backed-mcp-scaffold-test-internal-target-accessibility` | P2 | tools | `scaffold_test_preview/apply` generated a test for an internal class/member that failed compile with `CS0122`. | yes |
| `roslyn-backed-mcp-scaffold-test-batch-nullable-constructor-output` | P2 | tools | `scaffold_test_batch_preview` generated a `WorkspaceManager` test snippet containing `new WorkspaceManagerOptions?()`, a compile-fragile nullable-constructor expression. | yes |
| `roslyn-backed-mcp-add-project-reference-self-reference-preview` | P2 | tools | `add_project_reference_preview(RoslynMcp.Core, RoslynMcp.Core)` produced a self-reference diff instead of rejecting it. | yes |
| `roslyn-backed-mcp-find-overrides-interface-root-empty` | P2 | tools | `find_base_members` found the interface base member for `AuditScratch.Echo`, but `find_overrides` on the interface member returned zero. | yes |

Non-fragment notes:

- `set_editorconfig_option` wrote the expected `.editorconfig` override, but `get_editorconfig_options` did not show the new value until `workspace_reload`. This is usable but worth documenting or auto-invalidating.
- True concurrent MCP fan-out could not be exercised from this host, so Phase 8b is evidence-limited rather than a server pass.
- The disposable workspace initially reported restore/analyzer readiness issues; `dotnet restore` plus `dotnet build` cleared them before mutation phases. This was treated as bootstrap evidence, not a server finding, because the reloaded workspace became ready.

## 5. Diagnostics And Analysis Evidence

`project_diagnostics(summary=true)` returned 0 errors, 0 warnings, 509 info. Top info IDs were `MSTEST0039` 118, `MSTEST0034` 107, `CA1873` 81, `CA1861` 50, and `MSTEST0032` 45. `compile_check`, `compile_check(severity=Error)`, file-scoped compile, and `compile_check(emitValidation=true)` all passed on the primary workspace.

Security probes reported no findings: `security_diagnostics` returned 0, `security_analyzer_status` found .NET analyzers present and SecurityCodeScan absent, and `nuget_vulnerability_scan(includeTransitive=true)` reported 0 vulnerabilities across 5 projects.

Static analysis probes behaved coherently. Examples: highest complexity was `SideEffectClassifier.ClassifyMethod` at 22; highest cohesion split was `ChangeSignatureService` with LCOM4 4; `find_dead_locals` found an unused `text` local in `FixAllService.GetEquivalenceKeyAsync`; `get_namespace_dependencies(circularOnly=true)` found the known `Host.Stdio.Middleware` / `Host.Stdio.Tools` cycle.

## 6. Writer Evidence

The disposable workspace exercised create/organize/format/rename/extract/apply flows on audit-only files. Successful writer flows included:

- `create_file_preview/apply`, `organize_usings_preview/apply`, `apply_text_edit`, `format_range_preview/apply`, `format_document_preview/apply`.
- `rename_preview/apply` with a fresh `mutatedSymbol` and follow-up `find_references`.
- `set_diagnostic_severity`, `add_pragma_suppression`, `verify_pragma_suppresses`.
- `extract_method_preview/apply`, `extract_interface_preview/apply`, `replace_string_literals_preview/apply`, `preview_multi_file_edit/apply`, `apply_multi_file_edit`, `apply_with_verify`.
- `remove_dead_code_preview/apply`, `change_signature_preview` with call-site update, `restructure_preview`, `change_type_namespace_preview/apply`.
- `move_file_preview/apply`, `delete_file_preview/apply`.

Negative writer evidence also looked good: a stale composite preview token was rejected after a workspace version change, an already-consumed scaffold token returned `NotFound`, and bad text edit spans returned precise line-length errors.

## 7. Build, Test, And Validation

`build_project(RoslynMcp.Core)` passed. `test_discover(RoslynMcp.Tests, limit=10)` returned 1109 tests with pagination metadata. `test_related_files` and `test_related` on the audit fixture returned empty but structured related-test results.

`build_workspace` and `test_run` failed because the MCP host locked `analyzers/ServerSurfaceCatalogAnalyzer/bin/Debug/netstandard2.0/RoslynMcp.Analyzers.ServerSurfaceCatalog.dll`. The failure was structured for `test_run` (`failureEnvelope.errorKind=FileLock`, `isRetryable=true`) and reproduced again through the `debug_test_failure` prompt. This blocks the advertised build/test workflow for this repo while self-hosted.

`validate_workspace(summary=true, runTests=false)` returned `overallStatus=clean`, and an unknown file in `changedFilePaths` returned cleanly with `unknownFilePaths`. `validate_recent_git_changes(summary=true, runTests=false)` timed out at the client after 120 seconds despite only three relevant dirty paths.

## 8. Resources And Prompts

Resources passed. `roslyn://server/catalog`, `roslyn://server/resource-templates`, `roslyn://workspaces`, workspace status/projects/diagnostics, source-file line slices, and invalid line-range envelopes all returned as expected. The invalid line range `10-5` returned a structured `InvalidArgument` envelope.

Prompts passed. All 20 registered prompts rendered through `get_prompt_text`: `explain_error`, `suggest_refactoring`, `review_file`, `analyze_dependencies`, `debug_test_failure`, `refactor_and_validate`, `fix_all_diagnostics`, `guided_package_migration`, `guided_extract_interface`, `security_review`, `discover_capabilities`, `dead_code_audit`, `review_test_coverage`, `review_complexity`, `cohesion_analysis`, `consumer_impact`, `guided_extract_method`, `msbuild_inspection`, `session_undo`, and `refactor_loop`. Unknown prompt names and malformed `parametersJson` returned structured `InvalidArgument` errors with available prompt/schema hints. `debug_test_failure` is not a pure text-render smoke path: this run showed it can invoke `test_run` and therefore shares the self-hosted analyzer file-lock failure tracked in Section 7. Prompt mitigation is tracked separately in `roslyn-backed-mcp-file-lock-aware-prompt-validation-guidance`.

## 9. Experimental Promotion Scorecard Summary

Machine-readable scorecard: `_latest-promotion-scorecard.json`.

Recommended as promote-ready based on this run: `workspace_warm`, `find_type_consumers`, `trace_exception_flow`, `find_duplicate_helpers`, `find_dead_locals`, `symbol_impact_sweep`, `semantic_grep`, `validate_workspace`, `test_reference_map`, `get_prompt_text`, `server_catalog_tools_page`, `server_catalog_prompts_page`, `apply_multi_file_edit`, `preview_multi_file_edit`, `apply_with_verify`, `change_signature_preview`, `replace_string_literals_preview`, `restructure_preview`, `change_type_namespace_preview`, `scaffold_type_preview`, `scaffold_type_apply`, and `apply_project_mutation`.

Recommended hold or needs-fix: `validate_recent_git_changes` (timeout), `scaffold_test_apply` (internal accessibility failure), `scaffold_test_batch_preview` (nullable constructor expression output), `scaffold_first_test_file_preview` (insufficient positive evidence), and broad orchestration previews that were skipped for repo-shape/safety.

## 10. Backlog Fragments

Fragments emitted:

- `backlog.d/roslyn-backed-mcp-build-test-self-analyzer-file-lock.md`
- `backlog.d/roslyn-backed-mcp-file-lock-aware-prompt-validation-guidance.md`
- `backlog.d/roslyn-backed-mcp-validate-recent-git-changes-timeout.md`
- `backlog.d/roslyn-backed-mcp-find-references-duplicate-metadata-candidates.md`
- `backlog.d/roslyn-backed-mcp-symbol-relationships-return-token-bucket-mix.md`
- `backlog.d/roslyn-backed-mcp-scaffold-test-internal-target-accessibility.md`
- `backlog.d/roslyn-backed-mcp-scaffold-test-batch-nullable-constructor-output.md`
- `backlog.d/roslyn-backed-mcp-add-project-reference-self-reference-preview.md`
- `backlog.d/roslyn-backed-mcp-find-overrides-interface-root-empty.md`

## 11. Regression Against Existing Backlog

`ai_docs/backlog.md` only matched related terms in:

- `promotion-scorecard-20260427-review`: still open; this run provides fresh accept/hold evidence for several candidates.
- `workspace-process-pool-or-daemon`: still deferred; this run did not produce worse large-solution workspace profiling evidence.

No existing open row matched the concrete failures listed in Section 4.

## 12. Coverage Ledger

Catalog parity baseline: 168 tools, 13 resources, 20 prompts.

| Surface family | Coverage result |
|---|---|
| server/workspace | `server_info`, workspace load/list/status/health/graph/warm/close covered |
| diagnostics/security/validation | diagnostics, compile, analyzer, vulnerability, build, test, coverage-map, related-test, validation covered; build/test lock and recent-git timeout found |
| symbols/navigation | search, info, definitions, references, bulk refs, consumers, implementations, overrides, base members, relationships, completions, signatures, hierarchies covered; three symbol findings found |
| static/semantic analysis | complexity, cohesion, coupling, unused/duplicate/dead code, namespace/NuGet, semantic search/grep, reflection, DI, generated docs covered |
| flow/syntax/snippet/script | source, syntax tree, operations, data/control flow, exception flow, snippets, scripting, timeout covered |
| refactoring/editing/writers | representative preview/apply, stale token, verify, undo, file ops, formatting, rename, extract, replacement, restructure, namespace, project mutation, scaffolding covered; two scaffolding findings found |
| resources | all 13 live resource entries/templates were listed or read directly; static plus template-backed workspace resources covered |
| prompts | all 20 prompt entries rendered; two negative prompt cases covered; one prompt-guidance finding found |

Tools not directly applied were marked skipped for safety or repo shape when they required large cross-project moves, production DI rewiring, package migration, broad split-service orchestration, or destructive application beyond audit-only fixtures.

## 13. Teardown

Both loaded Roslyn workspaces were closed. A post-close `workspace_status` call on the disposable workspace returned structured `NotFound`.

`dotnet build-server shutdown` completed successfully. `git worktree remove --force` deregistered the disposable worktree and the branch `audit-deep/20260508T154415Z` was deleted. Windows initially could not delete the worktree directory because `roslynmcp.exe` still held the analyzer DLL; after stopping that completed MCP host process, the directory was removed. `git worktree list` now shows only `C:/Code-Repo/Roslyn-Backed-MCP`.

Final primary dirty set is intentionally limited to this audit report, `_latest-promotion-scorecard.json`, and `backlog.d` fragments.

## 14. Session Log Cross-Check

After reviewing the Codex session log for this run, all material issues encountered are now represented in this audit as either Section 4 findings with `backlog.d` fragments or explicit non-fragment notes. The cross-check added `roslyn-backed-mcp-scaffold-test-batch-nullable-constructor-output` for the `scaffold_test_batch_preview` nullable constructor expression and `roslyn-backed-mcp-file-lock-aware-prompt-validation-guidance` for prompt/operator mitigation around the self-hosted analyzer file lock. Expected negative probes were not elevated: invalid line ranges, invalid handles, bad regex, consumed preview tokens, post-close workspace calls, and operator-corrected navigation coordinates all returned structured or expected outcomes.
