# Backlog sweep plan — 20260504T203132Z
<!-- purpose: Backlog sweep plan for the 20260504T203132Z initiative batch. -->
<!-- scope: in-repo -->

**Generated:** 2026-05-04T20:31:32Z
**Backlog snapshot:** 2026-05-04T20:01:53Z
**Initiative count:** 6
**Anchor verification:** performed

This plan supersedes `ai_docs/plans/20260504T191653Z_backlog-sweep/` (single-row plan covering `navigation-tools-misnamed-locator-error`). The backlog now carries 5 additional rows from today's multi-session retro; replanning to cover all 6 open rows in one ordered batch.

Sort: priority band (High → Medium → Low), cost ASC within band, hotspot-touching initiatives non-adjacent. Hotspot-touching are #3 (`workspace-drift-check-tool`) and #6 (`validate-locator-preflight-tool`) — both register a new tool in `ServerSurfaceCatalog`.

## Initiatives (in order)

### 1. inv-arg-envelope-schema-hint

| Field | Content |
|---|---|
| Status | merged (PR #483, 2026-05-05) |
| Backlog rows closed | `inv-arg-envelope-schema-hint` |
| Diagnosis | Verified live. `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs` line 49 maps `ArgumentException → "InvalidArgument"` envelope; lines 264–288 build envelopes for `MissingFieldException` / `FormatException` shapes. The current envelope shape is `{ category, tool, message, exceptionType }` — no `schemaHint` or schema reference. Cold-context subagents (frequent in `/backlog-sweep:execute` parallel mode) cannot reference prior turns and re-derive the call shape from the error alone. The catalog (`src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.*.cs` partials) carries the full parameter list per tool — sourcing a one-line hint at error-build time is a single catalog lookup. |
| Approach | (a) Extend the envelope record/anonymous-object shape in `ToolErrorHandler.cs` to include `schemaHint: string?`. (b) Where the failing parameter is known (the `ArgumentException` and `Missing*` paths already capture the parameter name), look up the tool's catalog entry, find the matching parameter, and format `"<tool-name>(<param>: <type> [<one-line description>])"`. (c) Where parameter name is not known, omit `schemaHint` (don't emit an empty key — keep the envelope shape stable for downstream parsers). (d) Cache the catalog → tool-name lookup in a static `FrozenDictionary` to avoid per-error overhead. |
| Scope | Production files: 2 — `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs` (envelope builder), `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs` (only if the lookup helper needs to be exposed; if existing public surface suffices, drop to 1 prod file). Test files: 1 extension — extend `tests/RoslynMcp.Tests/ErrorResponseObservabilityTests.cs` with cases for (a) `workspace_load(missing path)` → schemaHint present and well-formed, (b) `find_references(missing locator)` → schemaHint mentions `metadataName` / `filePath+line+column` alternatives, (c) `get_prompt_text(missing required param)` → schemaHint names the missing prompt parameter. Within Rule 3 (≤4 prod files) and Rule 4 (≤3 test files). Tool-surface-only exemption does NOT apply — this is a behavior change to error envelopes, not a single tool's surface. |
| Tool policy | edit-only |
| Estimated context cost | 35000 |
| Risks | (1) `ServerSurfaceCatalog.cs` is on the addenda hotspot list; if the catalog lookup helper requires a new public method on the catalog, we touch the hotspot. Aim for using an existing accessor. (2) Downstream JSON parsers in tests / observability tools may assume a fixed envelope shape — verify `tests/RoslynMcp.Tests/ErrorResponseObservabilityTests.cs` baseline assertions still pass with the added optional field. (3) The `schemaHint` text must not leak internal exception details (parameter values, stack frames). Use catalog metadata only — never echo input. |
| Validation | (a) `mcp__roslyn__compile_check` after each edit. (b) New cases in `ErrorResponseObservabilityTests.cs` covering the three example failure modes plus one negative test (no parameter info available → no `schemaHint` key). (c) `./eng/verify-release.ps1 -Configuration Release` for the CI gate. (d) Manual: provoke each failure mode against the running server, confirm envelope contains `schemaHint` and the hint is helpful enough to retry the call without consulting `server_info`. |
| Performance review | N/A — error path; not hot-path. The catalog-lookup `FrozenDictionary` is one-time-init. |
| CHANGELOG category | Changed |
| CHANGELOG entry (draft) | Changed: `InvalidArgument` error envelopes now include a `schemaHint` field naming the tool and the failing parameter's type and description, sourced from the live tool catalog. Closes `inv-arg-envelope-schema-hint`. Cold-context subagents and parallel-mode executor sessions can self-correct without round-tripping through `server_info`. |
| Backlog sync | Close rows: [`inv-arg-envelope-schema-hint`]. Mark obsolete: []. Update related: re-evaluate `validate-locator-preflight-tool` (initiative #6) — if `schemaHint` lets agents self-correct on locator errors, that row may close obsolete. |

### 2. apply-with-verify-false-positive-audit

| Field | Content |
|---|---|
| Status | merged (PR #485, 2026-05-05) |
| Backlog rows closed | `apply-with-verify-false-positive-audit` |
| Diagnosis | Verified live. `src/RoslynMcp.Host.Stdio/Tools/ApplyWithVerifyTool.cs` and `src/RoslynMcp.Roslyn/Services/EditService.cs` are the implementation anchors. Today's multi-session retro (`ai_docs/reports/20260504T200153Z_roslyn-backed-mcp_roslyn-mcp-multisession-retro.md` §3#4) recorded 36 rollback events across 14 sessions and estimated ~5 false positives from a sample read — that estimate is soft. This row is **investigation-first**: the deliverable is a measurement report, not a behavior change. If the audit confirms false-positive rate ≥10%, spin off an implementation row that ports the diff-based logic from `validate_recent_git_changes` into `apply_with_verify`. |
| Approach | (a) Read both `ApplyWithVerifyTool.cs` and `EditService.cs` end-to-end to document current verify semantics (count-based vs diagnostic-id-based). (b) Read `validate_recent_git_changes` implementation as the diff-based reference. (c) Pull the 14 rollback-affected session JSONLs (paths in retro report's CSV sibling) and extract for each: pre-apply diagnostic baseline, post-apply diagnostic set, the rolled-back diff. (d) Categorize each rollback as true-positive (apply introduced new diagnostics) or false-positive (verify tripped on pre-existing). (e) Write `ai_docs/reports/<ts>_apply-verify-rollback-audit.md` with the count, ratio, and recommendation. |
| Scope | Production files: 0 (investigation only). Documentation files: 1 new — `ai_docs/reports/<ts>_apply-verify-rollback-audit.md`. Test files: 0. The output is a report; if implementation is warranted, a follow-on row with its own initiative ships the code change. |
| Tool policy | edit-only |
| Estimated context cost | 25000 |
| Risks | (1) Sample size — only 14 sessions had rollbacks. If false-positive count is under 3, the signal is too weak to justify a behavior change. The audit must report this honestly rather than rounding up. (2) The retro report's "~5 of 36" estimate was extrapolated from a sample read; the audit may find the rate is lower (or higher) than estimated. (3) Some session JSONLs may be in the truncated 169-session sibling (not deep-read in the retro); extracting from those requires re-running the `Get-ChildItem` enumeration. |
| Validation | (a) Output report exists at `ai_docs/reports/<ts>_apply-verify-rollback-audit.md` and parses as valid markdown. (b) Report enumerates each of the 14 rollback sessions with classification (TP / FP / inconclusive). (c) Report's recommendation is one of: `ship-implementation-row` / `close-obsolete` / `widen-window-and-re-audit`. (d) `./eng/verify-ai-docs.ps1` passes (the report is in `ai_docs/`). |
| Performance review | N/A — investigation only. |
| CHANGELOG category | Maintenance |
| CHANGELOG entry (draft) | Maintenance: investigated `apply_with_verify` rollback false-positive rate against 14 affected sessions. Report at `ai_docs/reports/<ts>_apply-verify-rollback-audit.md` with [recommendation: ship | close | re-audit]. Closes `apply-with-verify-false-positive-audit`. |
| Backlog sync | Close rows: [`apply-with-verify-false-positive-audit`]. If audit recommends implementation: add row `apply-with-verify-diff-based-rollback-criterion` to High; if audit closes obsolete: no follow-on. |

### 3. workspace-drift-check-tool

| Field | Content |
|---|---|
| Status | merged (PR #486, 2026-05-05) |
| Backlog rows closed | `workspace-drift-check-tool` |
| Diagnosis | Verified live. `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` exists (hotspot per addenda — touch through interface only). `workspace_health` already reports degraded state but does NOT enumerate drifted files or recommend conditional reload — confirmed by reading `tests/RoslynMcp.Tests/WorkspaceManagerEvictionTests.cs` and existing tool surface. The retro (`ai_docs/reports/20260504T200153Z_roslyn-backed-mcp_roslyn-mcp-multisession-retro.md` §3#1) shows 5 explicit silent-stale sessions; under-counted because the failure mode is wrong-data not thrown-error. Adding a fast `workspace_drift_check` tool that compares file mtime vs workspace-snapshot-time without performing a full reload lets agents branch — call `workspace_reload` only when needed. |
| Approach | New tool following the addenda's structural-unit shape (Core+Roslyn+Host.Stdio): (a) `src/RoslynMcp.Core/Services/IWorkspaceDriftService.cs` — interface returning `WorkspaceDriftResult { stale: bool, files_drifted: string[], recommended: "reload" \| "noop" }` plus `src/RoslynMcp.Core/Models/WorkspaceDriftResult.cs` for the DTO. (b) `src/RoslynMcp.Roslyn/Services/WorkspaceDriftService.cs` — implementation; iterates loaded documents, compares filesystem mtime vs snapshot ingest time, returns the diff. Uses a read-only accessor on `WorkspaceManager` (do not mutate hotspot). (c) `src/RoslynMcp.Host.Stdio/Tools/WorkspaceDriftTool.cs` — `[McpServerTool]` wrapper. (d) Catalog registration in `ServerSurfaceCatalog.Workspace.cs` partial (RMCP001/RMCP002 analyzers will enforce). (e) DI registration in `src/RoslynMcp.Host.Stdio/Extensions/ServiceCollectionExtensions.cs`. |
| Scope | Production files: 5 (Core interface, Core DTO, Roslyn impl, Host.Stdio tool, catalog partial). DI registration in `ServiceCollectionExtensions.cs` is the 6th. **This exceeds Rule 3's 4-prod-file cap UNLESS we apply the structural-unit exemption.** Counting structural units: Core contract = 1 (interface + DTO in same unit), Roslyn impl = 1, Host.Stdio tool surface = 1, Registration (catalog + DI) = 1. Total: 4 structural units, within the exemption cap. **Rule 3 exemption: structural-unit (new tool) — 4 units, 5 files including DI line.** Mandatory addenda: TestBase.cs DI registration (counted in file budget — 6 files total). Test files: 1 new — `tests/RoslynMcp.Tests/Services/WorkspaceDriftServiceTests.cs` covering (a) clean workspace → noop, (b) edited file → stale + reload, (c) deleted file → stale. README surface-count NOT bumped (`workspace_drift_check` is Experimental on first ship; bump Y count if shipping Stable). |
| Tool policy | edit-only |
| Estimated context cost | 55000 |
| Risks | (1) `WorkspaceManager.cs` is on the hotspot list. The new service must access it via a read-only accessor only — do not refactor `WorkspaceManager` itself. (2) Snapshot ingest time is not currently exposed by `WorkspaceManager`; if the implementation needs to add an `IngestedAt` accessor to the workspace, that's a hotspot touch — single read-only property is acceptable. (3) Performance: drift check must be fast (target <50ms for 200-project solutions). Avoid touching the filesystem more than once per loaded document — batch via `Parallel.ForEachAsync` if needed (see PR #476's coupling-metrics parallelization for the pattern). (4) `ServerSurfaceCatalog.cs` hotspot — adding a new tool here is by definition required. Schedule this initiative in a different parallel wave from initiative #6 (also new-tool catalog touch). |
| Validation | (a) New unit test fixture covering 3+ scenarios. (b) `mcp__roslyn__compile_check` per edit. (c) `./eng/verify-release.ps1 -Configuration Release` (gates README surface count via `ReadmeSurfaceCountTests`). (d) Manual: load OrchardCore profile, edit a file via `Edit`, call `workspace_drift_check`, confirm response names the edited file. |
| Performance review | Touched hot-path — `WorkspaceManager` accessor. Target P95 <50ms on 200-project workspaces. Measure against the OrchardCore profile (`docs/large-solution-profiling-baseline.md`). If over budget, switch the snapshot-time read to a one-shot capture rather than per-document comparison. |
| CHANGELOG category | Added |
| CHANGELOG entry (draft) | Added: `workspace_drift_check` tool — fast comparison of in-memory workspace snapshot against filesystem mtime, returns `{ stale, files_drifted[], recommended }`. Lets agents conditionally reload before reads, eliminating silent stale-snapshot reads after out-of-band `Edit`/`Write` mutations. Closes `workspace-drift-check-tool`. |
| Backlog sync | Close rows: [`workspace-drift-check-tool`]. Mark obsolete: []. Update related: []. |

### 4. navigation-tools-misnamed-locator-error

| Field | Content |
|---|---|
| Status | merged (PR #488, 2026-05-05) |
| Backlog rows closed | `navigation-tools-misnamed-locator-error` |
| Diagnosis | Verified live (per yesterday's planning verification, re-verified today): 4 throw sites with the legacy literal `"No symbol found at the specified location"` across 3 files — `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs:258` (callers_callees), `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs:308` (find_consumers), `src/RoslynMcp.Host.Stdio/Tools/ConsumerAnalysisTools.cs:32`, `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs:330`. Each site has a `SymbolLocator` in scope (built one or two lines above the throw). Helper `SymbolLocatorFactory.FormatSymbolNotFoundMessage(SymbolLocator)` exists at `src/RoslynMcp.Host.Stdio/Tools/SymbolLocatorFactory.cs:100` (added by PR #474). Backlog row text cites lines `232/258/308/358` in `AnalysisTools.cs` and tool names `symbol_relationships`, `goto_type_definition`, `find_consumers`; live grep finds throws only at 258/308 in `AnalysisTools.cs`. Executor should ship against live throw sites regardless of backlog text drift. |
| Approach | Replace the literal at each of the 4 throw sites with `SymbolLocatorFactory.FormatSymbolNotFoundMessage(locator)`. Where the locator local is named differently, hoist or rename to a local before the service call. No service-layer changes. |
| Scope | Production files: 3 — `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs`, `src/RoslynMcp.Host.Stdio/Tools/ConsumerAnalysisTools.cs`, `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`. Test files: 1 new — `tests/RoslynMcp.Tests/Services/NavigationToolsNotFoundMessageTests.cs` modeled on existing `tests/RoslynMcp.Tests/Services/SymbolInfoNotFoundMessageTests.cs`. Within Rule 3 (3/4) and Rule 4 (1/3). |
| Tool policy | edit-only |
| Estimated context cost | 30000 |
| Risks | (1) Two of the 4 sites in `AnalysisTools.cs` build the locator inline as the second arg to the service call; those need a local var introduced. (2) Hotspot: none touched. (3) The existing `SymbolInfoNotFoundMessageTests.cs` must continue to pass unchanged — verify the new test fixture doesn't shadow or conflict with its naming. |
| Validation | (a) `mcp__roslyn__compile_check` per edit. (b) New `NavigationToolsNotFoundMessageTests.cs` covering each tool × each locator shape (filePath+line+col, symbolHandle, metadataName). (c) `./eng/verify-release.ps1 -Configuration Release`. (d) Existing `SymbolInfoNotFoundMessageTests.cs` continues to pass. |
| Performance review | N/A — error path, not hot-path. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed: navigation tools `callers_callees`, `find_consumers`, and the `SymbolTools` resolver now emit a locator-aware "no symbol found" message naming the field the caller supplied (`filePath:line:column`, `symbolHandle`, or `metadataName`), matching the fix shipped for `symbol_info` in PR #474. Closes `navigation-tools-misnamed-locator-error`. |
| Backlog sync | Close rows: [`navigation-tools-misnamed-locator-error`]. Mark obsolete: []. Update related: []. |

### 5. find-references-project-filter

| Field | Content |
|---|---|
| Status | merged (PR #490, 2026-05-05) |
| Backlog rows closed | `find-references-project-filter` |
| Diagnosis | Verified live. `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs` carries the `find_references` and `find_consumers` tool wrappers (4 mentions confirmed via grep). The backlog row's anchor `src/RoslynMcp.Roslyn/Services/SymbolReferenceService.cs` is **stale** — actual file is `src/RoslynMcp.Roslyn/Services/ReferenceService.cs` (verified via `ls src/RoslynMcp.Roslyn/Services/`). `src/RoslynMcp.Roslyn/Services/ConsumerAnalysisService.cs` exists as cited. `semantic_grep` already accepts a `projectFilter` parameter — established precedent for the param shape. |
| Approach | (a) Add optional `projectFilter: string?` (single project name; comma-separated for multi) to `find_references` and `find_consumers` `[McpServerTool]` wrappers in `AnalysisTools.cs`. (b) Thread the filter through to `ReferenceService.FindReferencesAsync` and `ConsumerAnalysisService` — accept an optional `IReadOnlyCollection<string>?` of project names; when non-null, filter the result enumeration by `Project.Name`. (c) Match `semantic_grep`'s parameter description text for consistency. |
| Scope | Production files: 4 — `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs` (2 tool wrappers updated), `src/RoslynMcp.Roslyn/Services/ReferenceService.cs` (filter logic), `src/RoslynMcp.Roslyn/Services/ConsumerAnalysisService.cs` (filter logic), and a tool description tweak in `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Analysis.cs` (hotspot — minimal description-only edit). At Rule 3's 4-prod-file cap. Test files: 2 — extend `tests/RoslynMcp.Tests/` `ReferenceServiceTests` and `ConsumerAnalysisServiceTests` with `projectFilter` cases (unfiltered baseline, filter-to-single-project narrows correctly). |
| Tool policy | edit-only |
| Estimated context cost | 40000 |
| Risks | (1) Catalog hotspot — description text only, no new tool registration. (2) Filter must be case-sensitive on `Project.Name` to match `semantic_grep` semantics; document this in the parameter description. (3) When `projectFilter` is null/absent, behavior must be byte-identical to current (no regression). Add a baseline-equivalence test. (4) Backlog row's stale `SymbolReferenceService.cs` anchor — executor uses live `ReferenceService.cs`. |
| Validation | (a) `mcp__roslyn__compile_check` per edit. (b) New tests covering filter-on / filter-off equivalence and multi-project filter. (c) `./eng/verify-release.ps1 -Configuration Release`. (d) Manual: against a >2-project solution, call `find_references(metadataName=X)` once with no filter and once filtered to one project — verify the filtered result is a subset of the unfiltered. |
| Performance review | Hot-path adjacent — `find_references` against large solutions is a known long-tail (16 of 40 retro sessions hit timeouts). Filter applies *after* the reference walk in this initiative's scope — not a perf optimization. If a future row wants pre-filter project scoping (cut the walk itself), spin a separate row. |
| CHANGELOG category | Added |
| CHANGELOG entry (draft) | Added: `find_references` and `find_consumers` accept an optional `projectFilter` parameter (comma-separated project names) for scoping the reference walk to a project subset, matching `semantic_grep`'s existing surface. Closes `find-references-project-filter`. |
| Backlog sync | Close rows: [`find-references-project-filter`]. Mark obsolete: []. Update related: []. |

### 6. validate-locator-preflight-tool

| Field | Content |
|---|---|
| Status | deferred (re-measurement window not elapsed; re-evaluate after 2026-05-12 — see state.json notes) |
| Backlog rows closed | `validate-locator-preflight-tool` |
| Diagnosis | Verified live. `src/RoslynMcp.Host.Stdio/Tools/SymbolLocatorFactory.cs` exists (added by PR #474) with `Create(...)` factory and `FormatSymbolNotFoundMessage(locator)` helper — both reusable. The retro (§4#4) cites 6 sessions that hit post-hoc shape errors and would have benefited from pre-flight validation. **Deps note:** This row depends on initiative #1 (`inv-arg-envelope-schema-hint`) — if `schemaHint` lets agents self-correct from `find_references` errors directly, this row may close obsolete and never ship. Plan-time recommendation: ship initiative #1 first, re-measure error-recovery patterns over a 7-day window, then decide whether to ship this row. **If re-measurement closes this obsolete, skip with `obsolete:` rather than ship.** |
| Approach | New read-only tool following the structural-unit shape: (a) `src/RoslynMcp.Core/Services/ISymbolLocatorValidator.cs` — interface. (b) `src/RoslynMcp.Core/Models/SymbolLocatorValidationResult.cs` — DTO `{ valid: bool, mode: "filePath" \| "metadataName" \| "symbolHandle" \| null, normalized: string?, hint: string? }`. (c) `src/RoslynMcp.Roslyn/Services/SymbolLocatorValidator.cs` — implementation; reuses `SymbolLocatorFactory.Create()` in a try/catch and returns shape rather than throwing. Validates parseability of `metadataName` against Roslyn's `SymbolKey`/`MetadataName` parser — does NOT resolve the symbol (that's `find_references`' job). (d) `src/RoslynMcp.Host.Stdio/Tools/SymbolLocatorTool.cs` — `[McpServerTool]` wrapper. (e) Catalog registration in `ServerSurfaceCatalog.Symbols.cs` partial. (f) DI in `ServiceCollectionExtensions.cs`. |
| Scope | Production files: 5 (Core interface + DTO, Roslyn impl, Host.Stdio tool, catalog partial, DI line). **Rule 3 exemption: structural-unit (new tool) — 4 units, 5 files.** Same shape as initiative #3. Mandatory addenda: TestBase.cs DI registration (6th file). Test files: 1 new — `tests/RoslynMcp.Tests/Services/SymbolLocatorValidatorTests.cs` covering valid file/line, valid metadataName, malformed metadataName (parenthesized — exact case from PR #467), unparseable symbolHandle, fully empty locator, locator with multiple modes set. |
| Tool policy | edit-only |
| Estimated context cost | 55000 |
| Risks | (1) Hotspot — `ServerSurfaceCatalog.Symbols.cs` partial. Schedule non-adjacent to initiative #3 (also catalog-touching). Current order has #3 at position 3 and #6 at position 6 — non-adjacent. (2) `metadataName` parsing logic must mirror exactly what `SymbolLocatorFactory.Create()` does — a divergence would mean `validate_locator` says "valid" but `find_references` rejects, defeating the purpose. Best implementation: reuse `Create()` in a try/catch wrapper. (3) Dependency drift: if initiative #1 ships and the `schemaHint` is good enough, agents self-correct without this tool. Re-measure the `InvalidArgument`-on-locator-tools rate after #1 ships before committing to ship #6. **If rate drops by ≥80%, mark obsolete.** |
| Validation | (a) Unit test fixture covering 6+ shape cases. (b) `mcp__roslyn__compile_check` per edit. (c) `./eng/verify-release.ps1 -Configuration Release` (gates README surface count). (d) Manual: pass each of the 6 case shapes through `validate_locator`, confirm `valid` and `mode` values match the factory's actual behavior. |
| Performance review | N/A — pure validation, no workspace I/O. |
| CHANGELOG category | Added |
| CHANGELOG entry (draft) | Added: `validate_locator` read-only tool — pre-flight validation for `SymbolLocator` shapes (`filePath+line+column`, `metadataName`, `symbolHandle`). Returns `{ valid, mode, normalized, hint }` so callers can detect malformed locators before the round-trip cost of `find_references` / `symbol_relationships`. Closes `validate-locator-preflight-tool`. |
| Backlog sync | Close rows: [`validate-locator-preflight-tool`]. Mark obsolete: []. Update related: []. |

## Skipped rows

| Row | Reason |
|---|---|
| `workspace-process-pool-or-daemon` | Explicitly Defer'ed pending worse-profile evidence (per backlog Defer section). |

## Self-vet checklist

- [x] Rule 1: 6 rows → 6 initiatives. No bundling.
- [x] Rule 3: max 5 production files in any initiative; #3 and #6 invoke the structural-unit exemption (4 units each, addenda-defined). #5 at the 4-prod-file cap. #1, #2, #4 well under.
- [x] Rule 3b: all initiatives are `edit-only` — no `*_apply` / `*_preview` work needed (#4's mechanical text replacement, #1/#2/#3/#5/#6 are new code or config).
- [x] Rule 4: max 2 new test files (#5); all others ≤1.
- [x] Rule 5: all initiatives ≤55K (ceiling 80K).
- [x] Hotspot distribution: #3 and #6 both touch `ServerSurfaceCatalog.*.cs` (new tool registration). Order positions are 3 and 6 — non-adjacent. #5 touches `ServerSurfaceCatalog.Analysis.cs` for description-only edit; classed minor-touch but flagged in Risks. #4 touches no hotspot.
- [x] Markdown link hrefs: all source citations use plain inline code; no markdown-link form into `src/`. Verified safe against `verify-ai-docs.ps1`.
