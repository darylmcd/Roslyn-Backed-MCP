# 02 — C1 Tools

Rows (check C1):

- `compilation-cache-generator-rerun-blinds-unused-analysis` (High) — Resolve cache-compilation symbols back to the solution before SymbolFinder in unused/dead-field/coupling analysis
- `find-type-consumers-mutations-generator-blind` (High) — find_type_consumers / find_type_mutations return empty for generator projects
- `sanctioned-roots-unconfigured-error-misattributed` (High) — Unconfigured sanctioned roots surface as 'Parameter path is invalid'
- `split-service-with-di-facade-drops-sibling-types` (High) — split_service_with_di_preview facade deletes every other type in the file
- `change-signature-add-skips-same-document-impls` (High) — change_signature_preview op=add skips later declarations/callsites in the same document
- `restructure-preview-splice-without-parenthesization` (High) — restructure_preview splices captures without parentheses (silent semantic change)
- `replace-invocation-rewrites-replacement-body` (High) — replace_invocation_preview rewrites calls inside the replacement method (infinite recursion)
- `apply-composite-no-undo-capture` (High) — apply_composite_preview applies are not undoable; revert_last_apply undoes the previous op
- `change-type-namespace-emits-invalid-syntax` (High) — change_type_namespace_preview emits uncompilable namespace syntax
- `argument-exception-throw-sites-lack-public-message` (Medium) — Server-authored ArgumentException messages are redacted to a generic template
- `invalid-operation-throw-sites-lack-public-message` (Medium) — Actionable InvalidOperationException messages redacted to 'operation is not valid in the current state'
- `find-implementations-corlib-guard-blocks-source-anchor` (Medium) — find_implementations(IDisposable) returns 0 even source-anchored (regression)
- `di-registrations-factory-lambda-impl-misattributed` (Medium) — get_di_registrations reports a constructor argument as the implementation type of factory lambdas
- `find-duplicate-helpers-outermost-call-false-positive` (Medium) — find_duplicate_helpers flags work-doing call chains as BCL re-wraps
- `coupling-summary-rollup-limited-to-page` (Medium) — get_coupling_metrics summary counts only the returned page
- `unknown-projectname-silently-empty` (Medium) — Unknown projectName returns empty results instead of an error
- `find-dead-locals-captured-by-local-function` (Medium) — find_dead_locals flags outer locals captured and written by a local function
- `callers-callees-field-initializer-callers-dropped` (Medium) — callers_callees drops callers in field/property initializers
- `validation-tools-error-envelope-not-iserror` (Medium) — build/test tool failures are returned as successful results
- `fix-all-analyzer-assembly-provider-not-found` (Medium) — fix_all_preview cannot find code-fix providers shipped in analyzer assemblies
- `format-range-refuses-on-unrelated-line-count-change` (Medium) — format_range_preview refuses whenever whole-document formatting changes line count anywhere
- `extract-type-breaks-interfaces-and-publicizes-fields` (Medium) — extract_type_preview breaks interface contracts, publicizes fields, reformats whole file
- `editorconfig-write-no-workspace-invalidation` (Medium) — set_diagnostic_severity / set_editorconfig_option writes are not reflected until manual reload
- `extract-method-nullable-return-flow-state` (Medium) — extract_method_preview emits string? return that trips CS8603
- `replace-string-literals-const-self-reference` (Medium) — replace_string_literals_preview rewrites the target const's own initializer
- `record-satellites-line-insert-outside-initializer` (Medium) — record_field_add_with_satellites_preview inserts satellite lines outside initializers
- `migrate-package-removes-shared-central-version` (Medium) — migrate_package / remove_central_package_version drop PackageVersion entries other projects need
- `build-output-parser-drops-locationless-errors` (Medium) — build_project reports errorCount 0 for project-level NU/MSB errors
- `compilation-cache-dedupe-analyzer-references` (Low) — Server runs duplicate analyzer assemblies twice
- `get-source-text-notfound-message-generic` (Low) — get_source_text NotFound does not say the file isn't in the workspace
- `semantic-grep-comment-hit-location` (Low) — semantic_grep comment-scope hits report trivia start, not match position
- `analyze-snippet-declared-symbols-drop-fields` (Low) — analyze_snippet declaredSymbols omits fields, enums, events, ctors
- `source-generated-documents-origin-and-meta` (Low) — source_generated_documents conflates MSBuild .g.cs with generator output and lacks _meta
- `project-diagnostics-cold-scan-over-budget` (Low) — project_diagnostics cold scan 25 s and default page ~190 KB
- `diagnostic-details-unformatted-description` (Low) — diagnostic_details returns the raw MessageFormat with {0}
- `test-related-private-member-no-container-fallback` (Low) — test_related finds no tests for private members
- `trace-exception-flow-catch-ranking` (Low) — trace_exception_flow ranks catch(Exception) equal to nearer base catches
- `callers-callees-counts-cref-as-caller` (Low) — callers_callees counts doc-comment cref references as callers
- `workspace-changes-missing-revert-status` (Low) — workspace_changes lists reverted/rolled-back applies as applied
- `bulk-replace-type-redundant-using` (Low) — bulk_replace_type_preview adds a using for the enclosing namespace
- `refactor-preview-trivia-nits` (Low) — Refactor previews leave trivia defects
- `set-editorconfig-option-creates-parallel-section` (Low) — set_editorconfig_option adds a new [*.{cs,csx,cake}] section beside an existing [*.cs]
- `scaffold-type-mixed-line-endings` (Low) — scaffold_type_apply writes mixed CRLF/LF line endings
- `new-file-apply-writes-bom-ignoring-editorconfig` (Low) — New files written via TryApplyChanges get a BOM regardless of .editorconfig
- `move-type-to-project-unconditional-reference` (Low) — move_type_to_project_preview always adds a source→target reference
- `workspace-reload-restore-failure-not-flagged` (Low) — workspace_reload leaves restoreRequired=false after a restore failure
- `consumer-analysis-cancellation-returns-partial` (Low) — ConsumerAnalysisService breaks on cancellation and returns partial results
- `side-effect-classifier-stale-comments` (Low) — SideEffectClassifier comments no longer match the code
- `shared-expression-probe-stale-comment` (Low) — SharedExpressionProbe fixture comment is stale
- `find-references-closest-matches-rank-anonymous-members` (Low) — NotFound closestMatches ranks anonymous-type members above the real symbol
- `test-run-zero-match-filter-silent-success` (Low) — test_run with a filter matching zero tests reports success silently
- `revert-by-sequence-already-reverted-indistinct` (Low) — revert_apply_by_sequence cannot tell already-reverted from unknown
- `test-reference-map-counts-synthesized-members` (Low) — test_reference_map denominator includes compiler-synthesized members

Evidence: `raw/phase-G1..G6.md` (per-call tables with elapsedMs), `raw/head-negative-probes.json` (missing/wrong-type/unknown-arg probes for every tool), `raw/installed-roots-pass2.json` (unknown args, edge inputs), `raw/installed-find-overloads.json`. Performance: see the deep-harness report § 6.
