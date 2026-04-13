# Next work and backlog

<!-- purpose: Open work only; contract for agents syncing backlog on ship. -->

**updated_at:** 2026-04-13T22:00:00Z

## Agent contract

**Scope:** This file lists **unfinished work only**. It is not a changelog.

| | |
|---|---|
| **MUST** | Remove or update backlog rows when work ships; do it in the **same PR** as the implementation (or an immediate follow-up). |
| **MUST** | End implementation plans with a final todo: `backlog: sync ai_docs/backlog.md` when closing backlog-related items. |
| **MUST** | Use stable, kebab-case `id` values per open row (grep-friendly). |
| **MUST NOT** | Add "Completed" tables, dated history sections, or narrative changelogs here. |
| **MUST NOT** | Leave done items in the open table. |

## Standing rules (ongoing)

These are **not** backlog rows; do not delete them when clearing completed work.

- Reprioritize on each audit pass.
- Bug and tool items: put reproduction and session context in the PR or linked issue when it helps reviewers.
- Broader planning and complexity notes stay in this file; `ai_docs/archive/` holds policy only (see `archive/README.md`).

## Open work

Ordered by severity: **P3** (accuracy, UX, API clarity) → **P4** (docs, hygiene, cosmetic). Within a tier, rows are **alphabetical by `id`**.

| id | pri | blocker | deps | do |
|----|-----|---------|------|-----|
| `dependency-inversion-noisy-diff` | P3 | — | — | **Refactoring previews** (`dependency_inversion_preview`, `extract_method_preview`, `split_class_preview`) collapse multi-line parameters, remove blank lines, and reformat entire files — mixing formatting changes with semantic changes in the diff. Systematic across Roslyn workspace diff pipeline. **Fix:** Separate semantic changes from formatting; apply only targeted edits. **Evidence:** `20260413T174024Z_roslyn-backed-mcp_experimental-promotion.md` §9.4; `20260413T175254Z_firewallanalyzer_experimental-promotion.md` §9.4 / §4. |
| `find-overrides-behavior-review` | P3 | — | — | **`find_overrides`:** (1) Large payload resembles reference enumeration — verify return shape vs spec and `find_references`. (2) On some anchors/interfaces, empty or odd results reported (`20260413T160052Z_itchatbot_mcp-server-audit.md` §6). **Fix:** Reconcile behaviour and descriptions. **Evidence:** roslyn-backed-mcp audit §7; itchatbot audit. |
| `find-references-bulk-schema-docs` | P3 | — | — | **`find_references_bulk`:** Tool requires a `symbols` array of locator objects; examples / transcripts often use wrong shape or name (`symbolHandles`). Wrong args yield transport errors without pointing at the schema. **Fix:** Align MCP descriptions and examples with the live schema; consider aliases with deprecation warnings. **Evidence:** `20260413T154959Z_firewallanalyzer_mcp-server-audit.md` §7; `20260413T160052Z_itchatbot_mcp-server-audit.md` §7. |
| `move-type-to-project-namespace` | P3 | — | — | **`move_type_to_project_preview`:** When moving a type to a different project, the namespace is not updated to match the target project — keeps the source project's namespace. **Fix:** Update namespace by default or add `updateNamespace` parameter. **Evidence:** `20260413T174024Z_roslyn-backed-mcp_experimental-promotion.md` §4 / §9 / §10. |
| `msbuild-tool-parameter-alignment` | P3 | — | — | MSBuild evaluation tools use **`project`** (loaded project name) while other tools use **`projectName`**; `packageId` vs `packageName` across package tools; `destinationFilePath` vs `targetFilePath` naming. Easy to mismatch. **Fix:** Unify naming where breaking-change acceptable, or document cross-tool matrix in tool descriptions. **Evidence:** firewallanalyzer §7; itchatbot §7; `20260413T180408Z_networkdocumentation_experimental-promotion.md` §9.5 / §4. |
| `project-diagnostics-info-default-page` | P3 | — | — | **`project_diagnostics`:** Totals (`totalInfo`, etc.) are non-zero but the first returned page is **empty** unless callers pass explicit `severity=Info` when the solution only has Info-level analyzer diagnostics. **Fix:** Align default severity filter with totals or document the implicit floor in the tool description. **Evidence:** `20260409T165300Z_networkdocumentation_mcp-server-audit.md` §7 / §14 / §19. |
| `rename-preview-caret-resolution` | P3 | — | — | **`rename_preview`:** Caret on the wrong column resolves an adjacent type or tuple field instead of the user-intended local symbol. **Mitigation:** Prefer `symbolHandle` from `enclosing_symbol` when driving rename; improve line/column disambiguation when multiple symbols share a line. **Evidence:** firewallanalyzer §7 / §14; networkdocumentation §7 / §14 / §19. |
| `static-test-reference-map` | P3 | — | — | Feature request: add a tool that statically maps source symbols to test references. Current Roslyn MCP has `test_coverage` (runtime coverlet) and `test_related` (heuristic name matching), but no static reference-based mapping. **Suggested API:** `test_reference_map(workspaceId, project?)` → `{ coveredSymbols, uncoveredSymbols, coveragePercent }` based on reference analysis from test projects to source projects. |
| `test-discover-pagination-ux` | P3 | — | — | `test_discover` on a solution with many tests produced huge JSON, exceeding practical MCP token limits. **Consider:** `projectName` filter (partially mitigated in audits), summary mode (counts per project), or smaller per-test payload. |
| `test-fixall-service` | P3 | — | — | `FixAllService` (529 LOC, `FixAllService.cs`) has zero test coverage and applies bulk code modifications across entire solutions. Highest-risk untested service. **Add:** Integration tests for `PreviewFixAllAsync` with document/project/solution scope, diagnostic ID with no provider, and applying a previewed fix-all. |
| `test-symbol-mapper` | P3 | — | — | `SymbolMapper` (251 LOC) maps all symbol kinds to DTOs and feeds every symbol-related tool response. Zero tests. **Add:** Tests for all symbol kinds: method, property, field, class, interface, enum, event. |
| `target-framework-centralized-tf` | P3 | — | — | **`add_target_framework_preview` / `remove_target_framework_preview`:** Fails with "Project file does not declare TargetFramework" when TF is centralized in `Directory.Build.props`. Common in repos with centralized build config. **Fix:** Support `Directory.Build.props`-centralized TargetFramework or explain the limitation in the error. **Evidence:** `20260413T180408Z_networkdocumentation_experimental-promotion.md` §9.4; `20260413T175254Z_firewallanalyzer_experimental-promotion.md` §8. |
| `apply-text-edit-diff-quality` | P4 | — | — | **`apply_text_edit`:** Occasional stray `;` or noisy diff text on line-replace probes — polish diff formatting for comments/edge columns. **Evidence:** `20260413T154959Z_firewallanalyzer_mcp-server-audit.md` §14. |
| `central-package-version-generic-error` | P4 | — | — | **`add_central_package_version_preview`:** Returns generic "An error occurred invoking" when no `Directory.Packages.props` exists instead of an actionable "No Directory.Packages.props found" message. **Fix:** Catch `FileNotFoundException` and return an actionable message. **Evidence:** `20260413T180408Z_networkdocumentation_experimental-promotion.md` §9.3. |
| `claude-plugin-marketplace-version` | P4 | — | — | Claude Code plugin cache path shows **1.7.0** while server is **1.12.0** — users see stale skill/tool narratives until plugin cache refreshes. Manifest declares correct version; cosmetic. **Fix:** Ship marketplace/plugin version bumps with server releases. **Evidence:** `20260413T160052Z_itchatbot_mcp-server-audit.md` §11; `20260413T174024Z_roslyn-backed-mcp_experimental-promotion.md` §11. |
| `compile-check-no-restore-hint` | P4 | — | — | **`compile_check`:** When loaded workspace lacks `dotnet restore`, reports thousands of CS0234 errors with no indication that packages aren't restored. After `dotnet restore` + `workspace_reload`, errors drop to 0. **Fix:** Detect pattern of many CS0234 "namespace not found" errors and add a hint suggesting `dotnet restore`. **Evidence:** `20260413T180545Z_itchatbot_experimental-promotion.md` §9.7. |
| `fix-all-zero-fixes-ide-diagnostics` | P4 | — | — | **`fix_all_preview`:** Returns FixedCount=0 for common IDE-style diagnostics (IDE0007, IDE0290, IDE0005) even though `project_diagnostics` confirms they exist. Code fixers may not be loading for info-severity diagnostics. **Fix:** Investigate fixer discovery for IDE diagnostics; consider documenting which diagnostics have fixers. **Evidence:** `20260413T180408Z_networkdocumentation_experimental-promotion.md` §9.8. |
| `project-graph-missing-fields` | P4 | — | — | **`project_graph`:** Does not always return `Name` or `ProjectPath` fields in graph entries. `IsTestProject` is present but `Name` is needed by tools like `scaffold_test_preview`. **Fix:** Ensure `Name` and `ProjectPath` are populated in all graph entries. **Evidence:** `20260413T180545Z_itchatbot_experimental-promotion.md` §9.3. |
| `response-key-casing-inconsistency` | P4 | — | — | Tool responses use PascalCase (`WorkspaceId`, `PreviewToken`, `ErrorCount`) while input schemas use camelCase (`workspaceId`, `previewToken`). Agents must handle both casings when parsing. **Fix:** Align response keys with input parameter casing for consistent DX. **Evidence:** `20260413T180545Z_itchatbot_experimental-promotion.md` §9.8. |
| `response-meta-missing-tools` | P4 | — | — | Some tools (e.g. `source_generated_documents`, `test_related` in large-solution audit) omit expected **top-level `_meta`** in responses while siblings emit it — hurts latency/promotion baselines. **Fix:** Audit handlers for consistent `_meta` emission (v1.8+ contract). **Evidence:** `20260413T160052Z_itchatbot_mcp-server-audit.md` §3 / §6. |
| `roslyn-fetch-resource-timing` | P4 | — | — | **`fetch_mcp_resource`** intermittently returns "server not ready" for `roslyn://` workspace URIs in Cursor — determine whether host timing, server readiness gate, or documentation gap. **Evidence:** `20260413T154959Z_firewallanalyzer_mcp-server-audit.md` §9 / §15. |
| `stdio-flush-before-exit` | P4 | — | — | MCP server stdio stdout not flushed before process exit on stdin-EOF. 0 bytes on stdout via `cat \| roslynmcp` despite server log confirming handler completion. `Console.Out` buffering not flushed on shutdown path. **Fix:** Ensure `Console.Out.Flush()` before exit. Only affects non-SDK clients using bash pipe. **Evidence:** `20260413T180545Z_itchatbot_experimental-promotion.md` §9.4. |
| `securitycodescan-currency` | P4 | — | `add-net-analyzers` | SecurityCodeScan.VS2019 (v5.6.7) is archived. Consider migrating to SecurityCodeScan.VS2022 or relying on NetAnalyzers once added. Low urgency — current rules still fire and `TreatWarningsAsErrors` is enabled. |
| `server-info-update-semver` | P4 | — | — | **`server_info`:** `update.latest` can report **older** than `update.current` (e.g. 1.8.2 vs 1.12.0) with `updateAvailable: true`. **Fix:** Correct NuGet/metadata source or version comparison. **Evidence:** `20260413T160605Z_roslyn-backed-mcp_mcp-server-audit.md` §4 / §7 / §14.1; `20260413T174024Z_roslyn-backed-mcp_experimental-promotion.md` §11. |
| `symbol-search-payload-meta` | P4 | — | — | Very large `symbol_search` responses may drop **`_meta`** on the client or exceed bridge limits — confirm server always attaches `_meta`; document client limits. **Evidence:** `20260413T154959Z_firewallanalyzer_mcp-server-audit.md` §3 ledger / §8b. |
| `test-infrastructure-services` | P4 | — | — | Three infrastructure services have zero tests: `SuppressionService` (46 LOC), `SolutionDiffHelper` (118 LOC), `TriviaNormalizationHelper` (98 LOC). Lower priority — thin wrappers or tested indirectly through integration tests. |
| `test-related-empty-docs` | P4 | — | — | **`test_related`:** Sometimes returns `[]` when related tests might exist (heuristic gaps). **Fix:** Document when empty is expected vs failure; tune heuristics if reproducible. **Evidence:** `20260413T154959Z_firewallanalyzer_mcp-server-audit.md` §14. |
| `test-run-file-lock-fast-fail` | P4 | — | — | When `test_run` or `test_coverage` hits an MSBuild file lock (MSB3027/MSB3021), MSBuild retries 10 times with 1-second delays. Consider detecting the lock condition earlier and returning the FailureEnvelope sooner. |

## Refs

| Path | Role |
|------|------|
| `ai_docs/workflow.md` | Branch/PR workflow; **Backlog closure** |
| `ai_docs/backlog.md` | This file (open work + contract) |
| `ai_docs/audit-reports/` | Raw deep-review audits (inputs for prioritization) |
