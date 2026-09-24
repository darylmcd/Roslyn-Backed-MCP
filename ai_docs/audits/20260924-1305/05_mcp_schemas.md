# 05 — C4 Schemas and annotations

Rows (check C4):

- `workspace-close-schema-leaks-test-seams` (Medium) — workspace_close publishes test seams getProcessesByName / processDrainTimeout
- `unknown-tool-arguments-silently-ignored` (Medium) — Misspelled/unknown tool arguments are silently ignored
- `nuget-vulnerability-scan-openworld-annotation` (Low) — nuget_vulnerability_scan annotated openWorldHint=false despite network access
- `tool-output-schema-coverage` (Low) — Only 8 of 175 tools declare outputSchema / structuredContent
- `schema-hint-truncates-on-abbreviation` (Low) — schemaHint first-sentence trim cuts 'e.g.' and drops constraints
- `structured-content-tools-omit-meta` (Low) — Status-family tools drop _meta from structuredContent
- `symbol-locator-mixed-input-silently-accepted` (Low) — Symbol locator silently accepts mixed/partial locator input

Evidence: `raw/head-schema-lint.json` (175 tools). Lint result: 175/175 lack additionalProperties:false, 167/175 have no outputSchema, and workspace_close has 2 undocumented params. The prose-enum hits on find_dead_fields.usageKind, restructure_preview.goal and test_related.column are lint false positives, not filed. Annotations match the catalog 174/174.
