# Next work and backlog

<!-- purpose: Open work only; contract for agents syncing backlog on ship. -->

**updated_at:** 2026-04-13T18:05:00Z

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
| `add-net-analyzers` | P3 | — | — | `Microsoft.CodeAnalysis.NetAnalyzers` (CA2xxx/CA3xxx/CA5xxx security rules) and `Microsoft.CodeAnalysis.BannedApiAnalyzers` are not installed. SecurityCodeScan.VS2019 is present but archived (v5.6.7). **Add:** Both analyzer packages to `Directory.Build.props`. Consider creating `BannedSymbols.txt` for dangerous APIs (BinaryFormatter, Process.Start with shell execute). |
| `ca1873-logging-perf` | P3 | — | — | ~25 CA1873 instances: expensive string interpolation in logging calls evaluated even when log level is disabled. `WorkspaceManager` already uses `LoggerMessage.Define` (lines 23-46). **Fix:** Extend the pattern to `ToolErrorHandler`, `ScriptingService`, `UndoService`, `CodeActionService`, `FileOperationService`, `FileWatcherService`, `SecurityDiagnosticService`. Candidate for systematic batch fix. |
| `complexity-callers-callees` | P3 | — | — | `SymbolRelationshipService.GetCallersCalleesAsync` (`SymbolRelationshipService.cs:191`, CC=20) has nesting depth 5 — deepest in the codebase. Nested `foreach` over references, locations, then invocations. **Refactor:** Extract callee-resolution loop into a helper method. |
| `complexity-diagnostic-service` | P3 | — | — | `DiagnosticService.GetDiagnosticsAsync` (`DiagnosticService.cs:35`) is the worst complexity hotspot: CC=25, MI=29.6, 146 LOC. Handles workspace diags, compiler diags, analyzer diags, severity filtering, caching, count aggregation, and severity hints in one method. **Refactor:** Extract helpers for workspace diagnostic collection, project diagnostic collection, count aggregation, severity-hint computation. |
| `complexity-scripting-service` | P3 | — | — | `ScriptingService.EvaluateAsync` (`ScriptingService.cs:80`) has the lowest maintainability index: CC=20, MI=26.3, 204 LOC. Manages threads, timers, cancellation, heartbeats, and hard deadlines. **Refactor:** Extract thread-management, timer-management, and outcome-translation phases. Also mark `CapacityFailure` and `BuildScriptOptions` as `static` (CA1822). |
| `dead-code-cleanup` | P3 | — | — | Two confirmed dead symbols: (1) `MsBuildMetadataHelper.FindDirectoryBuildProps` (`MsBuildMetadataHelper.cs:26`) — zero references; sibling `FindDirectoryPackagesProps` is well-used. (2) `CorrelationContext.BeginScope()` + inner `CorrelationScope` class (`McpLoggingProvider.cs:104,113`) — zero call sites. **Remove** both and compile-check. |
| `diagnostics-filter-by-id` | P3 | — | — | `project_diagnostics` has `severity`, `file`, and `project` filters but no `diagnosticId` filter. When looking for all instances of a specific diagnostic, callers must page through the entire Info-severity diagnostic set and filter client-side. **Suggested API:** Add optional `diagnosticId: string?` parameter that filters server-side before pagination. |
| `find-base-members-anchor` | P3 | — | — | `find_base_members` returns `[]` at an override method anchor while `member_hierarchy` returns data for the same symbol — callers get inconsistent navigation. **Fix:** Align resolver/anchor rules with `member_hierarchy` or document required caret/symbol handle. **Evidence:** `20260413T160605Z_roslyn-backed-mcp_mcp-server-audit.md` §3 ledger / §15. |
| `find-overrides-behavior-review` | P3 | — | — | **`find_overrides`:** (1) Large payload resembles reference enumeration — verify return shape vs spec and `find_references`. (2) On some anchors/interfaces, empty or odd results reported (`20260413T160052Z_itchatbot_mcp-server-audit.md` §6). **Fix:** Reconcile behaviour and descriptions. **Evidence:** roslyn-backed-mcp audit §7; itchatbot audit. |
| `find-references-bulk-schema-docs` | P3 | — | — | **`find_references_bulk`:** Tool requires a `symbols` array of locator objects; examples / transcripts often use wrong shape or name (`symbolHandles`). Wrong args yield transport errors without pointing at the schema. **Fix:** Align MCP descriptions and examples with the live schema; consider aliases with deprecation warnings. **Evidence:** `20260413T154959Z_firewallanalyzer_mcp-server-audit.md` §7; `20260413T160052Z_itchatbot_mcp-server-audit.md` §7. |
| `high-param-count-services` | P3 | — | — | Two service methods have 8 parameters each: `CompileCheckService.CheckAsync` (`CompileCheckService.cs:22`, CC=18) and `CohesionAnalysisService.GetCohesionMetricsAsync` (`CohesionAnalysisService.cs:22`, CC=15). **Refactor:** Introduce options records. |
| `mcp-parameter-validation-messages` | P3 | — | — | When MCP tool JSON fails validation (wrong property names, e.g. `get_code_actions` with `line`/`column` vs `startLine`/`startColumn`), surface **schema-shaped hints** listing required parameter names instead of generic "error invoking" text. **Evidence:** `20260409T165300Z_networkdocumentation_mcp-server-audit.md` §8 / §15. |
| `msbuild-tool-parameter-alignment` | P3 | — | — | MSBuild evaluation tools use **`project`** (loaded project name) while other tools use **`projectName`**; easy to mismatch. **Fix:** Unify naming where breaking-change acceptable, or document cross-tool matrix in tool descriptions. **Evidence:** firewallanalyzer §7; itchatbot §7. |
| `project-diagnostics-info-default-page` | P3 | — | — | **`project_diagnostics`:** Totals (`totalInfo`, etc.) are non-zero but the first returned page is **empty** unless callers pass explicit `severity=Info` when the solution only has Info-level analyzer diagnostics. **Fix:** Align default severity filter with totals or document the implicit floor in the tool description. **Evidence:** `20260409T165300Z_networkdocumentation_mcp-server-audit.md` §7 / §14 / §19. |
| `rename-preview-caret-resolution` | P3 | — | — | **`rename_preview`:** Caret on the wrong column resolves an adjacent type or tuple field instead of the user-intended local symbol. **Mitigation:** Prefer `symbolHandle` from `enclosing_symbol` when driving rename; improve line/column disambiguation when multiple symbols share a line. **Evidence:** firewallanalyzer §7 / §14; networkdocumentation §7 / §14 / §19. |
| `revert-preview-token-coupling` | P3 | — | — | **`revert_last_apply`:** After undoing a writer, **unrelated** preview tokens (e.g. `scaffold_type_preview`) become NotFound until re-previewed — preview store coupling not documented. **`revert_last_apply` LIFO:** Firewall audit notes stack semantics vs expected cleanup order. **Fix:** Isolate preview token namespaces per operation or document ordering hazards (interleaved applies / undo). **Evidence:** `20260413T160605Z_roslyn-backed-mcp_mcp-server-audit.md` §14.2 / §15; `20260413T154959Z_firewallanalyzer_mcp-server-audit.md` ledger / §14. |
| `static-test-reference-map` | P3 | — | — | Feature request: add a tool that statically maps source symbols to test references. Current Roslyn MCP has `test_coverage` (runtime coverlet) and `test_related` (heuristic name matching), but no static reference-based mapping. **Suggested API:** `test_reference_map(workspaceId, project?)` → `{ coveredSymbols, uncoveredSymbols, coveragePercent }` based on reference analysis from test projects to source projects. |
| `test-client-root-validator` | P3 | — | — | `ClientRootPathValidator` (145 LOC) is the security boundary for path traversal and has zero direct tests. **Add:** Tests for null server, no roots capability, path inside/outside roots, symlink resolution, fail-open vs fail-closed modes. |
| `test-coverage-error-contract` | P3 | — | — | **`test_coverage`:** (1) When Coverlet/collector is missing, response is **success with Error text in body** — clients miss failures; return explicit non-success / `FailureEnvelope` with a stable code. (2) On some hosts, invocation fails with **generic MCP error and no JSON body** — improve structured host errors where possible. **Evidence:** itchatbot §7–8 / §14; `20260413T160605Z_roslyn-backed-mcp_mcp-server-audit.md` §14.3 / §15. |
| `test-discover-pagination-ux` | P3 | — | — | `test_discover` on a solution with many tests produced huge JSON, exceeding practical MCP token limits. **Consider:** `projectName` filter (partially mitigated in audits), summary mode (counts per project), or smaller per-test payload. |
| `test-fixall-service` | P3 | — | — | `FixAllService` (529 LOC, `FixAllService.cs`) has zero test coverage and applies bulk code modifications across entire solutions. Highest-risk untested service. **Add:** Integration tests for `PreviewFixAllAsync` with document/project/solution scope, diagnostic ID with no provider, and applying a previewed fix-all. |
| `test-pure-utilities` | P3 | — | — | Five pure utility classes have zero unit tests despite pervasive use: **IdentifierValidation** (54 LOC, guards every rename), **DiffGenerator** (168 LOC, every preview/apply diff), **SymbolHandleSerializer** (138 LOC, every symbol handle round-trip), **ParameterValidation** (46 LOC, guards severity/pagination params), **SymbolLocatorFactory** (72 LOC, every symbol tool). All pure functions — highly testable quick wins. ~50 focused unit tests estimated. |
| `test-symbol-mapper` | P3 | — | — | `SymbolMapper` (251 LOC) maps all symbol kinds to DTOs and feeds every symbol-related tool response. Zero tests. **Add:** Tests for all symbol kinds: method, property, field, class, interface, enum, event. |
| `apply-text-edit-diff-quality` | P4 | — | — | **`apply_text_edit`:** Occasional stray `;` or noisy diff text on line-replace probes — polish diff formatting for comments/edge columns. **Evidence:** `20260413T154959Z_firewallanalyzer_mcp-server-audit.md` §14. |
| `claude-plugin-marketplace-version` | P4 | — | — | Claude Code plugin (e.g. cached **1.7.0** skills) lags **server 1.11.2** — users see stale skill/tool narratives. **Fix:** Ship marketplace/plugin version bumps with server releases. **Evidence:** `20260413T160052Z_itchatbot_mcp-server-audit.md` §11. |
| `response-meta-missing-tools` | P4 | — | — | Some tools (e.g. `source_generated_documents`, `test_related` in large-solution audit) omit expected **top-level `_meta`** in responses while siblings emit it — hurts latency/promotion baselines. **Fix:** Audit handlers for consistent `_meta` emission (v1.8+ contract). **Evidence:** `20260413T160052Z_itchatbot_mcp-server-audit.md` §3 / §6. |
| `roslyn-fetch-resource-timing` | P4 | — | — | **`fetch_mcp_resource`** intermittently returns "server not ready" for `roslyn://` workspace URIs in Cursor — determine whether host timing, server readiness gate, or documentation gap. **Evidence:** `20260413T154959Z_firewallanalyzer_mcp-server-audit.md` §9 / §15. |
| `securitycodescan-currency` | P4 | — | `add-net-analyzers` | SecurityCodeScan.VS2019 (v5.6.7) is archived. Consider migrating to SecurityCodeScan.VS2022 or relying on NetAnalyzers once added. Low urgency — current rules still fire and `TreatWarningsAsErrors` is enabled. |
| `server-info-update-semver` | P4 | — | — | **`server_info`:** `update.latest` can report **older** than `update.current` (e.g. 1.8.2 vs 1.11.2) with `updateAvailable: true`. **Fix:** Correct NuGet/metadata source or version comparison. **Evidence:** `20260413T160605Z_roslyn-backed-mcp_mcp-server-audit.md` §4 / §7 / §14.1. |
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
