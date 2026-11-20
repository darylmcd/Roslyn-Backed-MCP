# surface-audit-20261120-remaining-findings-intake — Intake the remaining 2026-11-20 surface-audit findings

**row:** `surface-audit-20261120-remaining-findings-intake` · **pri:** `Low` · **size:** `S` · **verified:** `d3ab02228685f3cc015691f32c0292c6e6526b43`

## Anchors

- `audit-reports/_latest-promotion-scorecard.json`

## Acceptance

- [ ] Run /backlog-intake over the local report on the machine that holds it; dedupe against the rows filed on 2026-11-20 and the existing refusal-message, token-reason and newline rows.
- [ ] Unfiled P2 items: G2-F4 (symbol_info strict mode at whitespace), G2-F5 (find_type_consumers silent truncation), TEARDOWN (worktree lock after workspace_close drain).

## Evidence

- Sections 13, 14 and 17 of the report.
- Finding id and full call evidence: audit-reports/20261120T171538Z_roslynmcp_mcp-server-surface-test.md (local, git-ignored).

## Notes

## Remaining findings written out (2026-11-20), so this row no longer depends on the local report

Filed separately on 2026-11-20 and no longer part of this row: G2-F4 (symbol-info-strict-mode-resolves-at-whitespace), G2-F5 (find-type-consumers-limit-silent-truncation), TEARDOWN (note on workspace-close-isolated-nuget-handles).

Each line below: tool, input, observed. Severity P3 unless noted. All single-run observations against RoslynMcp.slnx on server 5.1.1.

Paging and validation
- get_complexity_metrics, find_unused_symbols, find_dead_locals, find_dead_fields, find_duplicated_methods, find_duplicate_helpers, get_cohesion_metrics, suggest_refactorings, semantic_search, find_implementations, find_overrides, symbol_impact_sweep: `limit` or caps truncate with no total / hasMore.
- find_reflection_usages(summary=true): envelope count=0, limit=200 next to totalCount=698.
- suggest_refactorings(limit=0), find_duplicate_helpers(limit=-1): silent empty; get_cohesion_metrics(minMethods=-1) accepted; find_duplicated_methods(similarityThreshold=2.5) accepted, returns 0 groups. compile_check rejects limit=0.
- find_duplicated_methods: group lineCount reflects one member.
- validate_recent_git_changes(summary=true): dotnetTestFilter string of about 5 KB.

Silent or vague negatives
- find_overrides at a blank line: count 0 instead of NotFound. find_overrides by metadataName of a base member: result includes the queried symbol itself.
- trace_exception_flow(unknown type): success with empty lists; maxResults also caps throw sites though documented for catch sites.
- find_overloads(missing member): empty success. symbol_impact_sweep(unknown symbol): closestMatches are anonymous-type properties, 3.8 s.
- Nine handle-based readers (find_references, symbol_info, find_consumers, find_type_usages, find_implementations, find_overrides, find_base_members, impact_analysis, find_type_mutations) return the generic NotFound text for an unresolvable symbolHandle; rename_preview returns handle-specific guidance.
- Generic "The operation is not valid in the current state." hides the refusal reason on extract_type_preview, extract_method_preview, restructure_preview (zero matches), replace_invocation_preview, bulk_replace_type_preview(scope=fields), change_type_namespace_preview(unknown type), migrate_package_preview(unknown package), extract_shared_expression_to_helper_preview(single occurrence), remove_interface_member_preview(wrong-kind handle). Related: the parked refusal-message rows.
- get_complexity_metrics(unknown filePath): silent empty; compile_check(unknown file) explains in a field named restoreHint.
- analyze_snippet(code="") and evaluate_csharp(code=""): success with no note.
- workspace_readiness_report(unknown id): success payload status "workspace-not-found" where workspace_drift_check returns the error envelope.
- workspace_status after workspace_close: WorkspaceNotFound text says "never loaded, or lost after a restart".
- revert_apply_by_sequence(already-reverted): reason unknown-sequence, same as a never-issued number.
- recommend_workflow: unknown intent ignored silently; valid labels not enumerated.
- get_prompt_text: malformed JSON error has no parse position; missing-parameter error names the C# method (ConsumerImpact) and ignores unknown keys and a non-integer line.

Classification and correctness
- symbol_search(kind=Class) returns Record items; a type-name query returns every member (matches the containing-type FQN).
- semantic_search("virtual methods") returns default interface methods with no virtual keyword.
- find_consumers emits kinds Other, PropertyType, MethodParameter not in its schema list; find_type_usages classifies a global::-qualified static call as Other; find_type_consumers kinds describe enclosing syntax.
- analyze_snippet(kind=program): spurious CS0105 on a user `using System;`; fields and constructors missing from declaredSymbols.
- analyze_data_flow / analyze_control_flow: warning "narrowed to lines X-Y ... 0 statement(s) not analyzed" for an identical range; readOutside/writtenOutside list about 250 locals; control flow omits `throw` exits.
- get_syntax_tree returns maxDepth+2 levels, get_operations maxDepth+1.
- get_cohesion_metrics: types whose state lives in a base class (PreviewStore, ProjectMutationPreviewStore on BoundedStore<T>) score LCOM4 5 and 3.
- find_duplicate_helpers: sole hit OrchestrationMsBuildXml.RemoveNodeAndAdjacentWhitespace is not a pure wrapper.
- security_analyzer_status: securityCodeScanPresent false yet missingRecommendedPackages empty. compile_check description says CS* only but returns MCP002.
- evaluate_csharp infinite loop ends at 20 s (10 s budget + 10 s watchdog grace); grace not in the tool description.
- preview_record_field_addition accepts a field name that duplicates an existing positional parameter.
- record_field_add_with_satellites_preview: expression-bodied Clone/Reset satellites reported as unsupported syntax.
- scaffold_test_preview default reference-file inference copied TypeExtractionTestBase from an unrelated fixture; scaffold_first_test_file_preview instantiates a static-only class; scaffold_type_preview(implementInterface=false) emits a type that does not compile, with no warning.
- add_target_framework_preview on a project whose TargetFramework comes from Directory.Build.props adds TargetFrameworks while the imported TargetFramework stays set (build effect not verified).
- extract_and_wire_interface_preview(updateDiRegistrations=true): no DI delta and no note saying none was found.
- extract_type_apply: new file carries the source file's unused usings. remove_dead_code_preview leaves a whitespace-only line. split_class_preview leaves a doubled blank line.
- test_run unfiltered on RoslynMcp.ShardDiscoveryFixtures (compact=true): total 0, succeeded true, no warning; the filtered zero-test path does warn.
- workspace_fork_apply: refuses this solution ("copied project graph refers to files or outputs in the source workspace") without naming the path; rejects a composite token though the description says any preview token. Related row: workspace-fork-project-mutation-preview.
- fix_all_preview reports 224 occurrences of CA1826 where project_diagnostics reports 28 (tracked in project-diagnostics-totals-collapse-diagnostic-id acceptance).

Contract consistency
- No _meta block on workspace_health, workspace_status, workspace_list, source_generated_documents, workspace_drift_check, workspace_readiness_report, workspace_support_bundle, server_heartbeat.
- Unknown projectName: NotFound from evaluate_msbuild_property, InvalidArgument from compile_check and most others.
- Preview diffs: absolute paths in some tools, workspace-relative in others; D:\ and D:/ mixed.
- workspace status resource: verbose form omits isReady, analyzersReady, the diagnostic counts, restoreHint, solutionFileName.
- Naming: line vs startLine; containingMember / containingMethod / containingSymbol; symbol_search flat vs nested location by summary flag; modifier order differs between symbol_info and document_symbols; document_symbols leaves use children null, empty records use []; record positional members absent from the outline.
- Diagnostics carry filePath twice plus three always-null location fields.

Resources and prompts
- roslyn://server/catalog/full is 173 KB against a documented "~80 KB".
- source_file / source_file_lines: workspace-relative path and an invalid line range both surface as a generic "Resource not found" at the client (server vs client attribution unknown); lines marker ends CRLF while the body is LF.
- Prompts: discover_capabilities silently falls back to the full catalog for an unknown taskCategory and says "All tools require a workspaceId"; analyze_dependencies fills its 50-node cap with external namespaces; guided_extract_interface and guided_package_migration steps carry 8 leaked leading spaces; refactor_loop says four-stage but renders five and cites v1.17; dead_code_audit says "Review the unused symbols above" when there are none; explain_error, suggest_refactoring, review_file name no tools; fix_all_diagnostics omits fix_all_preview; session_undo omits revert_apply_by_sequence.

Performance observations
- project_diagnostics cold: 37.7 and 39.4 s with other readers active (0-127 ms cached). find_reflection_usages and get_di_registrations first call: 12.8 s each.
- Reads after an apply pay an auto-reload: staleReloadMs 2.9-4.6 s typically, 12.6 s once.
- set_diagnostic_severity with an invalid severity took 14.7 s to reject.
