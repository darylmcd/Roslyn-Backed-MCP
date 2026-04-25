# Backlog sweep plan — 20260424T041204Z

Produced by `ai_docs/prompts/backlog-sweep-plan.md` v4 against the 44-row backlog
(5 P2 + 29 P3 + 10 P4). Initiatives are ordered per Step 5 (correctness-class
desc, context-cost asc within band, catalog+WorkspaceManager distribution).

**Anchor verification:** done at intake time (commit 239eefb, 2026-04-24).
Every row cites both the core `src/RoslynMcp.Roslyn/Services/` file and the
`src/RoslynMcp.Host.Stdio/Tools/` tool registration. Not re-verified at
plan-draft time per prompt's cost-constrained-sweep escape hatch (§Step 3).

**Adjacent perf-smell pass:** skipped at plan-draft time (cost-constrained).
Executors should run `roslyn-mcp:review` / `complexity` on touched files during
Step 3 of the execute prompt and file any incidental perf findings as new
backlog rows rather than bundling.

**Batch-shape overview:**
- 5 P2 initiatives — all correctness / host-crash class; scheduled wave 1.
- 29 P3 initiatives — mix of tool-surface-only (11), fix/refactor 1-2 (15), fix/refactor 3-4 (3).
- 10 P4 initiatives — polish, cosmetic, or umbrella rows that may defer.

**WorkspaceManager-touching rows** (schedule across waves per Step 5 sort rule 4):
`autoreload-cascade-stdio-host-crash` (P2), `auto-reload-retry-inside-call` (P3),
`format-range-apply-preview-token-lifetime` (P3), `host-recycle-opacity` (P3).

**Catalog-touching rows** (schedule across waves): `get-prompt-text-publish-parameter-schema` (P3).

**Final todo (all initiatives):** `backlog: sync ai_docs/backlog.md` per agent contract.

---

### 1. `scaffold-test-preview-dotted-identifier` — Strip namespace prefix from scaffolded test class name

| Field | Content |
|-------|---------|
| **Status** | merged (PR #381, 2026-04-24) |
| **Backlog rows closed** | `scaffold-test-preview-dotted-identifier` |
| **Diagnosis** | `ScaffoldingService` uses the fully-qualified symbol name in the class-name template, emitting `public class RoslynMcp.Roslyn.Services.NamespaceRelocationServiceGeneratedTests` — dotted identifier is a CS syntax error. Row cites `src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs` + `src/RoslynMcp.Host.Stdio/Tools/ScaffoldingTools.cs:87`. |
| **Approach** | In `ScaffoldingService` class-name template, substitute `TypeSymbol.Name` (unqualified) for the FQN. Cross-check other scaffold templates (filename, fixture class) use the same naming source to avoid re-introducing the bug via a neighboring template. |
| **Scope** | Production 1: `src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs`. Tests 1: add to existing `ScaffoldingServiceTests` — one nested type + one top-level type must yield a single-identifier class name that `compile_check` accepts. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 30000 |
| **Risks** | Other scaffold paths (batch, first-test-file) may share the template — verify they also use unqualified name. |
| **Validation** | New unit tests assert the generated class identifier contains no `.`; integration pass through `compile_check` on scaffold output. |
| **Performance review** | N/A — correctness fix, no hot-path changes. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | - **Fixed:** `scaffold_test_preview` emitting a dotted class identifier that fails to compile (`ScaffoldingService.cs` class-name template now uses `TypeSymbol.Name`). (`scaffold-test-preview-dotted-identifier`) |
| **Backlog sync** | Close rows: `scaffold-test-preview-dotted-identifier`. Mark obsolete: none. Update related: none. |

---

### 2. `fix-all-preview-sequence-contains-no-elements` — Structured envelope on FixAllProvider crash

| Field | Content |
|-------|---------|
| **Status** | merged (PR #383, 2026-04-24) |
| **Backlog rows closed** | `fix-all-preview-sequence-contains-no-elements` |
| **Diagnosis** | `fix_all_preview(IDE0300)` returns `fixedCount=0` with `"Sequence contains no elements"` bubbling from the registered FixAll provider. Row cites `src/RoslynMcp.Roslyn/Services/FixAllService.cs` + `src/RoslynMcp.Host.Stdio/Tools/FixAllTools.cs`. |
| **Approach** | Wrap the provider invocation in try/catch for `InvalidOperationException`; return a structured `{error: true, category: "FixAllProviderCrash", perOccurrenceFallbackAvailable: true, message: ...}` envelope. Optionally fan out to per-occurrence `code_fix_preview` when the provider short-circuits (prefer envelope-first to keep scope minimal). |
| **Scope** | Production 2: `src/RoslynMcp.Roslyn/Services/FixAllService.cs`, `src/RoslynMcp.Host.Stdio/Tools/FixAllTools.cs`. Tests 1: extend `FixAllServiceTests` with IDE0300 repro and a synthetic throwing provider shim. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 38000 |
| **Risks** | Must not swallow unrelated exceptions — narrow catch to the provider call site. |
| **Validation** | Unit test with throwing provider asserts envelope shape; integration repro on IDE0300 returns category `FixAllProviderCrash` instead of raw error. |
| **Performance review** | N/A — correctness fix, no hot-path changes. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | - **Fixed:** `fix_all_preview` crash (`Sequence contains no elements`) now returns a structured `FixAllProviderCrash` envelope with per-occurrence fallback hint (`FixAllService` + `FixAllTools`). (`fix-all-preview-sequence-contains-no-elements`) |
| **Backlog sync** | Close rows: `fix-all-preview-sequence-contains-no-elements`. Mark obsolete: none. Update related: none. |

---

### 3. `organize-usings-preview-document-not-found-after-apply` — Unify post-auto-reload document lookup

| Field | Content |
|-------|---------|
| **Status** | merged (PR #382, 2026-04-24) |
| **Backlog rows closed** | `organize-usings-preview-document-not-found-after-apply` |
| **Diagnosis** | After an apply triggers auto-reload, `organize_usings_preview` fails with `"Document not found"` while `format_document_preview` succeeds on the same file/turn. The two tools use divergent document-resolver paths. Row cites `src/RoslynMcp.Host.Stdio/Tools/RefactoringTools.cs` and services `RefactoringService.cs` + `EditService.cs`. |
| **Approach** | Audit both resolver paths, extract a shared post-auto-reload document resolver helper, route both `organize_usings_preview` and `format_document_preview` through it. Ensure the resolver re-acquires the refreshed snapshot before the `GetDocument` call. |
| **Scope** | Production 3: `src/RoslynMcp.Roslyn/Services/RefactoringService.cs`, `src/RoslynMcp.Roslyn/Services/EditService.cs`, `src/RoslynMcp.Host.Stdio/Tools/RefactoringTools.cs`. Tests 1: `RefactoringServiceTests` — simulate auto-reload between preview call and its inner resolver. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 48000 |
| **Risks** | Document resolver is a chokepoint — verify no other callers depend on the old lookup semantics. |
| **Validation** | Regression test: apply → auto-reload → `organize_usings_preview` must succeed on same file/turn. |
| **Performance review** | N/A — correctness fix, no hot-path changes. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | - **Fixed:** `organize_usings_preview` returning `Document not found` after an auto-reload cascade; resolver now shares the post-reload snapshot with `format_document_preview` (`RefactoringService` / `EditService`). (`organize-usings-preview-document-not-found-after-apply`) |
| **Backlog sync** | Close rows: `organize-usings-preview-document-not-found-after-apply`. Mark obsolete: none. Update related: none. |

---

### 4. `find-dead-fields-remove-dead-code-contract-break` — Surface removal-blocked-by on dead-field results

| Field | Content |
|-------|---------|
| **Status** | merged (PR #380, 2026-04-24) |
| **Backlog rows closed** | `find-dead-fields-remove-dead-code-contract-break` |
| **Diagnosis** | `find_dead_fields(usageKind=never-read)` flags ctor-written fields as removable; `remove_dead_code_preview` then refuses with "still has references" — broken chained workflow. Row cites `src/RoslynMcp.Roslyn/Services/DeadCodeService.cs`, `src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs`, `src/RoslynMcp.Host.Stdio/Tools/DeadCodeTools.cs`. |
| **Approach** | Emit `deadFields[].removalBlockedBy: ["ConstructorWrite@..."]` and `safelyRemovable: bool` on the find-side response so callers don't chain a workflow that always refuses. (Prefer metadata-surface over cascading remove, keeps DeadCodeService scope scoped to the classifier.) |
| **Scope** | Production 3: `src/RoslynMcp.Roslyn/Services/DeadCodeService.cs`, `src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs`, `src/RoslynMcp.Host.Stdio/Tools/DeadCodeTools.cs` (+ DTO field addition in `src/RoslynMcp.Core/Models/` if needed, still ≤4). Tests 1: `DeadCodeServiceTests` — ctor-assignment case must populate `removalBlockedBy`. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 50000 |
| **Risks** | DTO field addition is breaking — documented per project's no-backcompat constraint. |
| **Validation** | Unit test covers ctor-assigned `_options` / `_factory` / `_instance` fields; envelope includes `removalBlockedBy` and `safelyRemovable=false`. |
| **Performance review** | N/A — correctness fix, no hot-path changes. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | - **Fixed:** `find_dead_fields` now surfaces `removalBlockedBy` + `safelyRemovable` so chained `remove_dead_code_preview` calls don't break the contract on ctor-written fields (`DeadCodeService` + tool wrappers). (`find-dead-fields-remove-dead-code-contract-break`) |
| **Backlog sync** | Close rows: `find-dead-fields-remove-dead-code-contract-break`. Mark obsolete: none. Update related: none. |

---

### 5. `autoreload-cascade-stdio-host-crash` — Structured envelope on stale-workspace transition in WorkspaceManager

| Field | Content |
|-------|---------|
| **Status** | merged (PR #389, 2026-04-24) |
| **Backlog rows closed** | `autoreload-cascade-stdio-host-crash` |
| **Diagnosis** | Two `staleAction=auto-reloaded` responses in the same turn (two writers + one reader) caused an unhandled exception in the post-auto-reload state-read path that terminated the stdio host. Every tool funnels through `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` for its snapshot — single choke point. |
| **Approach** | Guard the post-auto-reload state-read path in `WorkspaceManager` so a stale read returns a structured `category="StaleWorkspaceTransition"` envelope propagated to the tool layer instead of throwing. Preserve the original exception in `_meta` for diagnostics. |
| **Scope** | Production 2: `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`, + the shared envelope helper if modifications are needed. Tests 1: regression test — two writers (`preview_record_field_addition` + `extract_and_wire_interface_preview`) + one reader in the same test turn must not crash the host. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 55000 |
| **Risks** | WorkspaceManager is a 330-line hotspot — touch minimally. Verify no other callers depend on the exception-bearing signal. |
| **Validation** | New regression test simulates two-writer + one-reader turn; host survives; envelope carries `StaleWorkspaceTransition`. `verify-release.ps1` must pass. |
| **Performance review** | N/A — correctness fix on a choke point; single extra branch in the post-reload path. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | - **Fixed:** Two-writer + reader auto-reload cascade no longer terminates the stdio host; stale reads now return a `StaleWorkspaceTransition` envelope (`WorkspaceManager`). (`autoreload-cascade-stdio-host-crash`) |
| **Backlog sync** | Close rows: `autoreload-cascade-stdio-host-crash`. Mark obsolete: none. Update related: none. |

---

### 6. `symbol-search-empty-query-overflow` — Reject empty `symbol_search` queries with structured envelope

| Field | Content |
|-------|---------|
| **Status** | merged (PR #388, 2026-04-24) |
| **Backlog rows closed** | `symbol-search-empty-query-overflow` |
| **Diagnosis** | Behavior per backlog row; code path = `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs:15` handler delegating into `SymbolSearchService`. Empty/whitespace query dumps 70-80KB full workspace index. No cross-layer drift. |
| **Approach** | In the tool wrapper, guard-clause the handler: reject empty/whitespace queries with `{count:0, symbols:[], note:"query must be non-empty"}` (or `InvalidArgument` envelope) before delegating into the service. |
| **Scope** | Production 1: `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`. Rule 3 exemption: tool-surface-only, 1 file. Tests 1: extend `SymbolToolsTests` — empty-string + whitespace-only cases return the structured envelope. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 22000 |
| **Risks** | Confirm no internal caller passes empty strings deliberately (search for test usages first). |
| **Validation** | Unit test covers empty-string, whitespace-only, and a valid query — only the first two return the rejection envelope. |
| **Performance review** | N/A — guard clause, no hot-path changes. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | - **Fixed:** `symbol_search` empty/whitespace queries no longer dump the full workspace index; tool now returns a structured empty-query envelope (`SymbolTools`). (`symbol-search-empty-query-overflow`) |
| **Backlog sync** | Close rows: `symbol-search-empty-query-overflow`. Mark obsolete: none. Update related: none. |

---

### 7. `replace-string-literals-preview-throws-on-zero-match` — Return empty response on zero matches

| Field | Content |
|-------|---------|
| **Status** | merged (PR #386, 2026-04-24) |
| **Backlog rows closed** | `replace-string-literals-preview-throws-on-zero-match` |
| **Diagnosis** | Behavior per backlog row; code path = `src/RoslynMcp.Roslyn/Services/StringLiteralReplaceService.cs` + tool wrapper `src/RoslynMcp.Host.Stdio/Tools/RestructureTools.cs:40`. Throws `"Invalid operation: no matching literals found in scope"` instead of returning a structured empty response. No cross-layer drift. |
| **Approach** | Replace the throw-on-zero path with a structured `{count: 0, changes: []}` return, matching other preview tools. Exception becomes a genuine error case only for true error conditions. |
| **Scope** | Production 2: `src/RoslynMcp.Roslyn/Services/StringLiteralReplaceService.cs`, `src/RoslynMcp.Host.Stdio/Tools/RestructureTools.cs`. Rule 3 exemption: tool-surface-only (2 files — service + wrapper). Tests 1: extend existing StringLiteral tests — `literalValue="UNLIKELY_XYZ"` returns count=0. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 25000 |
| **Risks** | Confirm no test asserts on the old exception shape. |
| **Validation** | Unit test covers the zero-match path; other preview tools' shape parity verified. |
| **Performance review** | N/A — correctness fix, no hot-path changes. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | - **Fixed:** `replace_string_literals_preview` now returns `{count:0, changes:[]}` instead of throwing on zero matches (`StringLiteralReplaceService` + `RestructureTools`). (`replace-string-literals-preview-throws-on-zero-match`) |
| **Backlog sync** | Close rows: `replace-string-literals-preview-throws-on-zero-match`. Mark obsolete: none. Update related: none. |

---

### 8. `validate-recent-git-changes-bare-error-envelope` — Wrap handler in structured-envelope decorator

| Field | Content |
|-------|---------|
| **Status** | merged (PR #387, 2026-04-24) |
| **Backlog rows closed** | `validate-recent-git-changes-bare-error-envelope` |
| **Diagnosis** | Behavior per backlog row; code path = `src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs:37` handler. Returns bare `"An error occurred invoking 'validate_recent_git_changes'."` with no category/tool/exceptionType. No cross-layer drift. |
| **Approach** | Wrap the handler with the same structured-envelope decorator `validate_workspace` uses (locate it in the same file / neighboring tool module). Populate `category`, `tool`, `message`, `exceptionType`, and `_meta` fields. |
| **Scope** | Production 1: `src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs`. Rule 3 exemption: tool-surface-only, 1 file. Tests 1: `ValidationBundleToolsTests` — git-not-on-PATH + non-git-repo cases return structured envelope. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 25000 |
| **Risks** | Ensure envelope decorator is idempotent across handlers in this module. |
| **Validation** | Unit tests for both failure modes assert all five envelope fields. |
| **Performance review** | N/A — correctness fix, no hot-path changes. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | - **Fixed:** `validate_recent_git_changes` now returns the same structured error envelope as `validate_workspace` (`ValidationBundleTools`). (`validate-recent-git-changes-bare-error-envelope`) |
| **Backlog sync** | Close rows: `validate-recent-git-changes-bare-error-envelope`. Mark obsolete: none. Update related: none. |

---

### 9. `connection-state-ready-unsatisfiable-preload` — Rename pre-load `connection.state` to `idle`

| Field | Content |
|-------|---------|
| **Status** | merged (PR #393, 2026-04-24) |
| **Backlog rows closed** | `connection-state-ready-unsatisfiable-preload` |
| **Diagnosis** | Behavior per backlog row; code path = `src/RoslynMcp.Host.Stdio/Tools/ServerTools.cs` where `server_info` is registered and composed. Pre-load state reports `"initializing"` indefinitely, breaking hard-gate prompts. No cross-layer drift. |
| **Approach** | Rename the pre-load state value to `"idle"` (or `"ready-no-workspace"`) in the composition path; update the tool description to document the state machine. Prefer `"idle"` for brevity and symmetry with future states. |
| **Scope** | Production 1: `src/RoslynMcp.Host.Stdio/Tools/ServerTools.cs`. Rule 3 exemption: tool-surface-only, 1 file. Tests 1: `ServerToolsTests` — fresh host reports `state=idle`; post-workspace-load flips to `ready`. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 22000 |
| **Risks** | Downstream callers (deep-review prompts) key on `state=="ready"` — breaking change is intended per no-backcompat constraint; flag in CHANGELOG. |
| **Validation** | Unit test covers pre-load + post-load transitions. |
| **Performance review** | N/A — correctness fix, no hot-path changes. |
| **CHANGELOG category** | `Changed — BREAKING` |
| **CHANGELOG entry (draft)** | - **Changed — BREAKING:** `server_info.connection.state` now reports `idle` before any workspace loads (previously the unsatisfiable `initializing`). Prompts gating on `state==ready` should gate on `state in {idle, ready}` (`ServerTools`). (`connection-state-ready-unsatisfiable-preload`) |
| **Backlog sync** | Close rows: `connection-state-ready-unsatisfiable-preload`. Mark obsolete: none. Update related: none. |

---

### 10. `diagnostic-details-param-naming-drift` — Accept both `line`/`column` and `startLine`/`startColumn`

| Field | Content |
|-------|---------|
| **Status** | merged (PR #396, 2026-04-24) |
| **Backlog rows closed** | `diagnostic-details-param-naming-drift` |
| **Diagnosis** | Behavior per backlog row; code path = `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs:138` (`diagnostic_details` handler). Unique `line`/`column` where every other positional tool uses `startLine`/`startColumn`. No cross-layer drift. |
| **Approach** | Accept both naming conventions on input; map `startLine→line`, `startColumn→column` in the handler front matter. Update the tool description to list both aliases. |
| **Scope** | Production 1: `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs`. Rule 3 exemption: tool-surface-only, 1 file. Tests 1: `AnalysisToolsTests` — both parameter shapes resolve equivalently. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 22000 |
| **Risks** | Ensure defaulting logic handles the case when neither pair is supplied the same as before. |
| **Validation** | Unit test covers both shapes against the same symbol position. |
| **Performance review** | N/A — correctness fix, no hot-path changes. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | - **Fixed:** `diagnostic_details` now accepts `startLine`/`startColumn` in addition to `line`/`column` to match sibling positional tools (`AnalysisTools`). (`diagnostic-details-param-naming-drift`) |
| **Backlog sync** | Close rows: `diagnostic-details-param-naming-drift`. Mark obsolete: none. Update related: none. |

---

### 11. `impact-analysis-summary-mode` — Add `summary` + `declarationsLimit` to `impact_analysis`

| Field | Content |
|-------|---------|
| **Status** | merged (PR #399, 2026-04-24) |
| **Backlog rows closed** | `impact-analysis-summary-mode` |
| **Diagnosis** | Behavior per backlog row; code path = `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs:242` (`impact_analysis` handler, delegating into the shared impact helper also used by `symbol_impact_sweep`). `declarations` list has no paging cap; overflowed at 86KB. No cross-layer drift. |
| **Approach** | Add `summary: bool` + `declarationsLimit: int` parameters to the tool wrapper; mirror `find_references(summary=true)` contract. Summary mode returns counts only; non-summary mode honors `declarationsLimit` with `hasMore`. |
| **Scope** | Production 1: `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs`. Rule 3 exemption: tool-surface-only, 1 file. Tests 1: `AnalysisToolsTests` — summary + limit cases. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 25000 |
| **Risks** | `symbol_impact_sweep` shares the helper — ensure summary additions stay local to the wrapper. |
| **Validation** | Unit test asserts output size under summary vs full mode; `declarationsLimit` truncates with `hasMore=true`. |
| **Performance review** | N/A — correctness/UX fix. |
| **CHANGELOG category** | `Added` |
| **CHANGELOG entry (draft)** | - **Added:** `impact_analysis` now supports `summary` and `declarationsLimit` parameters, mirroring `find_references` paging (`AnalysisTools`). (`impact-analysis-summary-mode`) |
| **Backlog sync** | Close rows: `impact-analysis-summary-mode`. Mark obsolete: none. Update related: none. |

---

### 12. `find-references-bulk-summary-mode` — Add `summary` + `maxItemsPerSymbol` to `find_references_bulk`

| Field | Content |
|-------|---------|
| **Status** | merged (PR #395, 2026-04-24) |
| **Backlog rows closed** | `find-references-bulk-summary-mode` |
| **Diagnosis** | Behavior per backlog row; code path = `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs:302` (handler delegating into `ReferenceService`). Paging cap applies only to each symbol's inner list, not the outer batch; overflowed at 120KB. No cross-layer drift. |
| **Approach** | Add `summary: bool` + `maxItemsPerSymbol: int` to the tool wrapper; mirror existing `find_references(summary=true)` contract. |
| **Scope** | Production 1: `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`. Rule 3 exemption: tool-surface-only, 1 file. Tests 1: extend `SymbolToolsTests` — 2-symbol batch under summary + limit. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 25000 |
| **Risks** | Ensure per-symbol limit is applied before the envelope is assembled to truly cap output. |
| **Validation** | Unit test with 2-symbol batch asserts output size under summary vs full. |
| **Performance review** | N/A — correctness/UX fix. |
| **CHANGELOG category** | `Added` |
| **CHANGELOG entry (draft)** | - **Added:** `find_references_bulk` now supports `summary` and `maxItemsPerSymbol` parameters to prevent cross-batch overflow (`SymbolTools`). (`find-references-bulk-summary-mode`) |
| **Backlog sync** | Close rows: `find-references-bulk-summary-mode`. Mark obsolete: none. Update related: none. |

---

### 13. `move-type-to-file-preview-enum-support` — Structured error on unsupported type kind

| Field | Content |
|-------|---------|
| **Status** | merged (PR #398, 2026-04-24) |
| **Backlog rows closed** | `move-type-to-file-preview-enum-support` |
| **Diagnosis** | Behavior per backlog row; code path = `src/RoslynMcp.Roslyn/Services/TypeMoveService.cs` + tool wrapper `src/RoslynMcp.Host.Stdio/Tools/TypeMoveTools.cs`. Rejects enums with vague "Type 'X' not found" even though `symbol_search` resolves them. |
| **Approach** | Prefer (b): emit explicit `"type-kind Enum not supported for this refactor"` error citing the resolved `TypeKind`. Skip enum support implementation for this initiative (can be spun off). |
| **Scope** | Production 2: `src/RoslynMcp.Roslyn/Services/TypeMoveService.cs`, `src/RoslynMcp.Host.Stdio/Tools/TypeMoveTools.cs`. Rule 3 exemption: tool-surface-only (error-shape only, 2 files). Tests 1: `TypeMoveServiceTests` — `IngestionTerminalStatus` enum repro returns structured `type-kind Enum not supported`. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 28000 |
| **Risks** | Ensure the resolver distinguishes "symbol not found" from "symbol found but unsupported kind". |
| **Validation** | Unit test covers enum, delegate, and supported class — only class succeeds. |
| **Performance review** | N/A — correctness fix. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | - **Fixed:** `move_type_to_file_preview` now emits an explicit `type-kind <Kind> not supported` error for enums/delegates instead of a misleading not-found message (`TypeMoveService` + `TypeMoveTools`). (`move-type-to-file-preview-enum-support`) |
| **Backlog sync** | Close rows: `move-type-to-file-preview-enum-support`. Mark obsolete: none. Update related: none. |

---

### 14. `change-signature-preview-add-unhelpful-error` — Actionable error on `op=add` path

| Field | Content |
|-------|---------|
| **Status** | merged (PR #400, 2026-04-24) |
| **Backlog rows closed** | `change-signature-preview-add-unhelpful-error` |
| **Diagnosis** | Behavior per backlog row; code path = `src/RoslynMcp.Roslyn/Services/ChangeSignatureService.cs` + `ChangeSignatureAddRemovePreviewBuilder.cs` (tool: `src/RoslynMcp.Host.Stdio/Tools/ChangeSignatureTools.cs`). `op=add` with a valid name+type returns "Parameter 'index' has an out-of-range value" referencing an internal index. |
| **Approach** | Catch the out-of-range case in the add-path builder; either accept the add when the intent is unambiguous (default to end-append) or emit an actionable error citing caller-supplied `name`/`parameterType`. Prefer accept-at-end since the intent is clear. |
| **Scope** | Production 2: `src/RoslynMcp.Roslyn/Services/ChangeSignatureAddRemovePreviewBuilder.cs`, `src/RoslynMcp.Host.Stdio/Tools/ChangeSignatureTools.cs`. Rule 3 exemption: tool-surface-only (error-shape / default-index, 2 files). Tests 1: extend existing ChangeSignature tests — `op=add, name=cancellationToken` succeeds with end-append. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 28000 |
| **Risks** | Ensure end-append default doesn't collide with positional params — unit test both positions. |
| **Validation** | Regression test with `CancellationToken` add succeeds; explicit out-of-index case emits actionable error. |
| **Performance review** | N/A — correctness fix. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | - **Fixed:** `change_signature_preview(op=add)` now defaults to end-append instead of failing with an internal-index error (`ChangeSignatureAddRemovePreviewBuilder` + `ChangeSignatureTools`). (`change-signature-preview-add-unhelpful-error`) |
| **Backlog sync** | Close rows: `change-signature-preview-add-unhelpful-error`. Mark obsolete: none. Update related: none. |

---

### 15. `get-code-actions-caret-only-inverted-range` — Fix default-end computation for caret-only calls

| Field | Content |
|-------|---------|
| **Status** | merged (PR #394, 2026-04-24) |
| **Backlog rows closed** | `get-code-actions-caret-only-inverted-range` |
| **Diagnosis** | Behavior per backlog row; code path = `src/RoslynMcp.Roslyn/Services/CodeActionService.cs` + tool wrapper `src/RoslynMcp.Host.Stdio/Tools/CodeActionTools.cs`. Default-end computation produces a position 3 chars before start on caret-only calls. |
| **Approach** | Fix the default-range selector in `CodeActionService`: when `endLine`/`endColumn` are omitted, default to a zero-width selection at the caret (or the enclosing token's end) instead of computing a backwards offset. |
| **Scope** | Production 1: `src/RoslynMcp.Roslyn/Services/CodeActionService.cs`. Tests 1: `CodeActionServiceTests` — caret-only at various positions never produces `end < start`. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 30000 |
| **Risks** | Zero-width vs token-enclosing default may change which actions surface — verify with representative positions. |
| **Validation** | Regression test: caret-only at line=98, col=13 resolves successfully. |
| **Performance review** | N/A — correctness fix. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | - **Fixed:** `get_code_actions` no longer produces an inverted default range on caret-only calls (`CodeActionService`). (`get-code-actions-caret-only-inverted-range`) |
| **Backlog sync** | Close rows: `get-code-actions-caret-only-inverted-range`. Mark obsolete: none. Update related: none. |

---

### 16. `test-related-response-envelope-parity` — Wrap `test_related` in paged envelope

| Field | Content |
|-------|---------|
| **Status** | merged (PR #401, 2026-04-24) |
| **Backlog rows closed** | `test-related-response-envelope-parity` |
| **Diagnosis** | Behavior per backlog row; code path = `src/RoslynMcp.Roslyn/Services/TestDiscoveryService.cs` + tool-layer wrappers under `src/RoslynMcp.Host.Stdio/Tools/`. `test_related` returns bare array while `test_related_files` returns structured envelope with `dotnetTestFilter`. |
| **Approach** | Wrap `test_related` in the same envelope: `{tests: [...], dotnetTestFilter, pagination: {total, returned, hasMore}}`. Reuse the filter-synthesis helper from `test_related_files`. |
| **Scope** | Production 2: `src/RoslynMcp.Roslyn/Services/TestDiscoveryService.cs`, tool-layer wrapper file (test tools). Rule 3 exemption: tool-surface-only (shape-only, 2 files). Tests 1: extend `TestDiscoveryServiceTests` — assert envelope parity with `test_related_files`. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 30000 |
| **Risks** | Callers relying on the bare array are a breaking-change concern — acceptable per no-backcompat rule. Cross-ref P4 rows `test-related-files-empty-result-explainability` / `test-related-files-service-refactor-underreporting` — keep separate per Rule 1 (different concerns, different tests). |
| **Validation** | Regression test compares `test_related` envelope against `test_related_files` envelope for a matching symbol set. |
| **Performance review** | N/A — shape fix. |
| **CHANGELOG category** | `Changed — BREAKING` |
| **CHANGELOG entry (draft)** | - **Changed — BREAKING:** `test_related` now returns the same `{tests, dotnetTestFilter, pagination}` envelope as `test_related_files` instead of a bare array (`TestDiscoveryService`). (`test-related-response-envelope-parity`) |
| **Backlog sync** | Close rows: `test-related-response-envelope-parity`. Mark obsolete: none. Update related: `test-related-files-empty-result-explainability`, `test-related-files-service-refactor-underreporting` stay P4. |

---

### 17. `find-implementations-source-gen-partial-dedup` — Dedupe source-gen partial-class decls

| Field | Content |
|-------|---------|
| **Status** | merged (PR #406, 2026-04-24) |
| **Backlog rows closed** | `find-implementations-source-gen-partial-dedup` |
| **Diagnosis** | `find_implementations(ISnapshotStore)` returns `FileSnapshotStore` plus partials from `Logging.g.cs` / `RegexGenerator.g.cs` — same type, not distinct implementations. Row cites `src/RoslynMcp.Roslyn/Services/ReferenceService.cs` + `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`. |
| **Approach** | Deduplicate by containing `ISymbol` identity in `ReferenceService.FindImplementationsAsync`. Expose opt-in `includeGeneratedPartials: bool=false` for callers needing the raw list. |
| **Scope** | Production 2: `src/RoslynMcp.Roslyn/Services/ReferenceService.cs`, `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`. Tests 1: `ReferenceServiceTests` — interface with both generated + non-generated partials; default dedups, opt-in restores. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 32000 |
| **Risks** | `ISymbol` identity comparison must use the symbol's original definition — verify for generics. |
| **Validation** | Regression test on `ISnapshotStore` returns `FileSnapshotStore` once by default. |
| **Performance review** | N/A — correctness fix. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | - **Fixed:** `find_implementations` now dedupes source-gen partial decls by symbol identity; added `includeGeneratedPartials` opt-in (`ReferenceService` + `SymbolTools`). (`find-implementations-source-gen-partial-dedup`) |
| **Backlog sync** | Close rows: `find-implementations-source-gen-partial-dedup`. Mark obsolete: none. Update related: none. |

---

### 18. `find-property-writes-positional-record-silent-zero` — Add `PrimaryConstructorBind` bucket

| Field | Content |
|-------|---------|
| **Status** | merged (PR #411, 2026-04-24) |
| **Backlog rows closed** | `find-property-writes-positional-record-silent-zero` |
| **Diagnosis** | `find_property_writes` returns `count=0` for positional-record primary-ctor bindings. Row cites `src/RoslynMcp.Roslyn/Services/ReferenceService.cs` + `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs:323`. |
| **Approach** | In `ReferenceService`, detect positional-record primary-ctor bindings and emit a `PrimaryConstructorBind` write-kind bucket (or at minimum a warning on count=0 when the declaring type is a positional record). |
| **Scope** | Production 2: `src/RoslynMcp.Roslyn/Services/ReferenceService.cs`, `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`. Tests 1: `ReferenceServiceTests` — `WebSnapshotStoreContext.Store` repro yields positive write count. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 35000 |
| **Risks** | Distinguish primary-ctor bind from `init` setter; verify IOperation kind detection. |
| **Validation** | Regression test covers positional record with consumer `new T(value)`. |
| **Performance review** | N/A — correctness fix. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | - **Fixed:** `find_property_writes` now reports positional-record primary-constructor bindings via a `PrimaryConstructorBind` bucket (`ReferenceService`). (`find-property-writes-positional-record-silent-zero`) |
| **Backlog sync** | Close rows: `find-property-writes-positional-record-silent-zero`. Mark obsolete: none. Update related: none. |

---

### 19. `find-dead-fields-compound-assignment-writes` — Count compound-assignment + interlocked as writes

| Field | Content |
|-------|---------|
| **Status** | merged (PR #405, 2026-04-24) |
| **Backlog rows closed** | `find-dead-fields-compound-assignment-writes` |
| **Diagnosis** | `find_dead_fields` reports `writeReferenceCount=0` for `acc.ErrorCount++` and similar compound-assignment writes. Row cites `src/RoslynMcp.Roslyn/Services/DeadCodeService.cs` + `src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs` + a concrete callsite at `CompileCheckService.cs:155`. |
| **Approach** | Extend the write-reference sweep in `DeadCodeService` to recognize compound-assignment target-of operations (`++`, `--`, `+=`, `-=`, `*=`, `/=`, `<<=`, `>>=`, `\|=`, `&=`, `^=`) and `Interlocked.*` callsites where the first argument is a field ref. |
| **Scope** | Production 1: `src/RoslynMcp.Roslyn/Services/DeadCodeService.cs`. Tests 1: `DeadCodeServiceTests` — one test per compound operator + one `Interlocked.Increment` case. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 35000 |
| **Risks** | IOperation classification for compound assignments may differ from simple assignments — verify with Roslyn's `ICompoundAssignmentOperation`. |
| **Validation** | Unit tests assert write count > 0 for each operator variant. |
| **Performance review** | N/A — correctness fix. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | - **Fixed:** `find_dead_fields` now counts compound-assignment writes (`++`, `+=`, `Interlocked.*`) as write-references (`DeadCodeService`). (`find-dead-fields-compound-assignment-writes`) |
| **Backlog sync** | Close rows: `find-dead-fields-compound-assignment-writes`. Mark obsolete: none. Update related: none. |

---

### 20. `find-unused-symbols-convention-filter-gaps` — Extend convention list for MCP attrs + xUnit + ASP.NET

| Field | Content |
|-------|---------|
| **Status** | merged (PR #404, 2026-04-24) |
| **Backlog rows closed** | `find-unused-symbols-convention-filter-gaps` |
| **Diagnosis** | `excludeConventionInvoked=true` false-positives `[McpServerToolType]` / `[McpServerPromptType]` / `[McpServerResourceType]`, xUnit `[CollectionDefinition]` holders, and ASP.NET health-response writers. Row cites `src/RoslynMcp.Roslyn/Services/UnusedCodeAnalyzer.cs` + `src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs:13`. |
| **Approach** | Extend the default convention list in `UnusedCodeAnalyzer` to include the five listed attribute patterns + `HealthResponseWriter` signature pattern. |
| **Scope** | Production 1: `src/RoslynMcp.Roslyn/Services/UnusedCodeAnalyzer.cs`. Tests 1: `UnusedCodeAnalyzerTests` — fixtures for each pattern (at minimum one per attribute family + health writer). |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 32000 |
| **Risks** | Additions may over-suppress — unit-test the negative direction (similarly-named attributes that shouldn't be whitelisted). |
| **Validation** | Each fixture pattern no longer surfaces as unused. |
| **Performance review** | N/A — correctness fix. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | - **Fixed:** `find_unused_symbols(excludeConventionInvoked=true)` now recognizes `[McpServer*Type]` attributes, xUnit `[CollectionDefinition]` holders, and ASP.NET `HealthResponseWriter` signatures (`UnusedCodeAnalyzer`). (`find-unused-symbols-convention-filter-gaps`) |
| **Backlog sync** | Close rows: `find-unused-symbols-convention-filter-gaps`. Mark obsolete: none. Update related: none. |

---

### 21. `set-editorconfig-option-round-trip-asymmetry` — Align get-side cache refresh with set-side write

| Field | Content |
|-------|---------|
| **Status** | merged (PR #403, 2026-04-24) |
| **Backlog rows closed** | `set-editorconfig-option-round-trip-asymmetry` |
| **Diagnosis** | `set_editorconfig_option(CA9999.severity=none)` writes to disk but `get_editorconfig_options` immediately after returns without the new key when no loaded analyzer reports the id. Row cites `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs` + `src/RoslynMcp.Host.Stdio/Tools/EditorConfigTools.cs`. |
| **Approach** | In `EditorConfigService`, ensure the set path invalidates or updates the read-side cache; alternatively, the get path always reads the on-disk union rather than filtering by loaded analyzer ids. Prefer the latter for correctness. |
| **Scope** | Production 1: `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs`. Tests 1: `EditorConfigServiceTests` — set then get round-trip for an unloaded analyzer id. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 30000 |
| **Risks** | Widening the get-side output may include keys that are then filtered downstream — confirm no consumer filters on "loaded analyzer only". |
| **Validation** | Regression test: set CA9999.severity=none → get includes the key. |
| **Performance review** | N/A — correctness fix. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | - **Fixed:** `get_editorconfig_options` now returns keys written by `set_editorconfig_option` even when no loaded analyzer reports the id (`EditorConfigService`). (`set-editorconfig-option-round-trip-asymmetry`) |
| **Backlog sync** | Close rows: `set-editorconfig-option-round-trip-asymmetry`. Mark obsolete: none. Update related: none. |

---

### 22. `add-package-reference-preview-cpm-duplicate-detection` — Reject duplicate adds when CPM-transitive

| Field | Content |
|-------|---------|
| **Status** | merged (PR #409, 2026-04-24) |
| **Backlog rows closed** | `add-package-reference-preview-cpm-duplicate-detection` |
| **Diagnosis** | `add_package_reference_preview(Meziantou.Analyzer 3.0.50)` emits a duplicate `<PackageReference>` when the package is already present via CPM / `Directory.Build.props`. Row cites `src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs` + `src/RoslynMcp.Host.Stdio/Tools/ProjectMutationTools.cs`. |
| **Approach** | In `ProjectMutationService.AddPackageReferencePreviewAsync`, consult the evaluated PackageReference graph via `evaluate_msbuild_items` and return `status: "already-present"` (or structured refusal) before writing the diff. |
| **Scope** | Production 1: `src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs`. Tests 1: `ProjectMutationServiceTests` — fixture project with CPM + transitive reference repro. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 35000 |
| **Risks** | Evaluated-items probe may be slow — scope the probe to the target project only. |
| **Validation** | Regression test covers CPM-transitive case; non-transitive add still succeeds. |
| **Performance review** | N/A unless msbuild probe is measurably slow — record timing in test. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | - **Fixed:** `add_package_reference_preview` now detects CPM / transitive-present packages and returns `status: already-present` instead of emitting a duplicate (`ProjectMutationService`). (`add-package-reference-preview-cpm-duplicate-detection`) |
| **Backlog sync** | Close rows: `add-package-reference-preview-cpm-duplicate-detection`. Mark obsolete: none. Update related: none. |

---

### 23. `find-base-members-vs-member-hierarchy-metadata-drift` — Align metadata-boundary resolver

| Field | Content |
|-------|---------|
| **Status** | merged (PR #417, 2026-04-24) |
| **Backlog rows closed** | `find-base-members-vs-member-hierarchy-metadata-drift` |
| **Diagnosis** | `find_base_members`/`find_overrides` return count=0 for metadata-boundary interfaces (`IEquatable<T>.Equals`) while `member_hierarchy` resolves them on the same symbols. Row cites `src/RoslynMcp.Roslyn/Services/ReferenceService.cs` + `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`. |
| **Approach** | Align the metadata-boundary resolver in `ReferenceService`: reuse the same traversal `member_hierarchy` uses (or extract a shared helper) so `find_base_members` / `find_overrides` resolve through metadata the same way. Fallback: add `warning: "metadata-boundary"` field. Prefer the alignment fix. |
| **Scope** | Production 2: `src/RoslynMcp.Roslyn/Services/ReferenceService.cs`, `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs` (description update for new behavior). Tests 1: `ReferenceServiceTests` — `IEquatable<T>.Equals` repro yields non-zero count across all three tools. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 38000 |
| **Risks** | Traversal change may affect user-defined interface base discovery — verify with both source + metadata-only symbols. |
| **Validation** | Regression test across `find_base_members`, `find_overrides`, `member_hierarchy` on metadata-boundary symbols. |
| **Performance review** | N/A — correctness fix. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | - **Fixed:** `find_base_members` and `find_overrides` now resolve metadata-boundary interfaces to match `member_hierarchy` (`ReferenceService`). (`find-base-members-vs-member-hierarchy-metadata-drift`) |
| **Backlog sync** | Close rows: `find-base-members-vs-member-hierarchy-metadata-drift`. Mark obsolete: none. Update related: none. |

---

### 24. `find-type-mutations-undercounts-lifecycle-writes` — Cover collection-field writes in async lifecycle methods

| Field | Content |
|-------|---------|
| **Status** | merged (PR #410, 2026-04-24) |
| **Backlog rows closed** | `find-type-mutations-undercounts-lifecycle-writes` |
| **Diagnosis** | `find_type_mutations(WorkspaceManager)` surfaces only 2 mutators despite `LoadAsync`/`ReloadAsync`/`CloseAsync` mutating `_workspaces`. Row cites `src/RoslynMcp.Roslyn/Services/MutationAnalysisService.cs` + `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs:274`. |
| **Approach** | Extend the mutation-scope classifier in `MutationAnalysisService` to walk through async-state-machine boundaries and recognize collection-mutating ops (`Add`/`Remove`/`Clear`/`TryAdd`) on field targets inside async methods. |
| **Scope** | Production 1: `src/RoslynMcp.Roslyn/Services/MutationAnalysisService.cs`. Tests 1: `MutationAnalysisServiceTests` — `WorkspaceManager` and `SnapshotStore` fixtures must surface async lifecycle mutators. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 40000 |
| **Risks** | Async-state-machine traversal may double-count — unit-test with a known-mutator count. |
| **Validation** | Regression test asserts at least 5 mutators on `WorkspaceManager`. |
| **Performance review** | N/A — correctness fix. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | - **Fixed:** `find_type_mutations` now covers collection-field writes inside async lifecycle methods (`MutationAnalysisService`). (`find-type-mutations-undercounts-lifecycle-writes`) |
| **Backlog sync** | Close rows: `find-type-mutations-undercounts-lifecycle-writes`. Mark obsolete: none. Update related: none. |

---

### 25. `scaffold-test-preview-static-target-body` — Switch body template for static target types

| Field | Content |
|-------|---------|
| **Status** | merged (PR #412, 2026-04-24) |
| **Backlog rows closed** | `scaffold-test-preview-static-target-body` |
| **Diagnosis** | `scaffold_test_preview` emits `new T()` in Arrange even when target is static (`TenantConstants`, `SnapshotContentHasher`). Row cites `src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs` + `src/RoslynMcp.Host.Stdio/Tools/ScaffoldingTools.cs:87`. |
| **Approach** | In `ScaffoldingService`, detect `INamedTypeSymbol.IsStatic` and switch the body template to call static members directly (no `new <TargetType>`). |
| **Scope** | Production 1: `src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs`. Tests 1: `ScaffoldingServiceTests` — static utility class produces body without `new`. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 32000 |
| **Risks** | Ensure sibling-inference doesn't re-introduce instantiation via an imported pattern — overlap with P4 `scaffold-test-preview-sibling-inference-overbroad` is separate scope. |
| **Validation** | Regression test confirms no `new TenantConstants` / `new SnapshotContentHasher` in output. |
| **Performance review** | N/A — correctness fix. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | - **Fixed:** `scaffold_test_preview` now detects static target types and omits `new T()` from the Arrange body (`ScaffoldingService`). (`scaffold-test-preview-static-target-body`) |
| **Backlog sync** | Close rows: `scaffold-test-preview-static-target-body`. Mark obsolete: none. Update related: `scaffold-test-preview-sibling-inference-overbroad` (P4, separate). |

---

### 26. `scaffold-test-preview-ctor-arg-stubs` — Emit placeholder args for DI-registered target ctors

| Field | Content |
|-------|---------|
| **Status** | merged (PR #414, 2026-04-24) |
| **Backlog rows closed** | `scaffold-test-preview-ctor-arg-stubs` |
| **Diagnosis** | `scaffold_test_preview` emits `new T()` even when target's primary ctor requires args (DI-registered services e.g. `NamespaceRelocationService`). Row cites `src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs` + `src/RoslynMcp.Host.Stdio/Tools/ScaffoldingTools.cs:87`. |
| **Approach** | In `ScaffoldingService`, probe the target's primary ctor parameters; emit TODO placeholders or (when `NSubstitute` package is already referenced) `Substitute.For<T>()` stubs for each non-resolvable arg. |
| **Scope** | Production 1: `src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs`. Tests 1: `ScaffoldingServiceTests` — DI-registered service scaffold references each ctor param with a synthesized placeholder. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 35000 |
| **Risks** | NSubstitute detection must check both test project references and the target test project's NuGet graph. |
| **Validation** | Regression test: scaffold for a service with 2+ ctor params yields a compiling scaffold with placeholder args. |
| **Performance review** | N/A — correctness fix. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | - **Fixed:** `scaffold_test_preview` now emits `Substitute.For<T>()` or TODO placeholders for target ctor args instead of `new T()` (`ScaffoldingService`). (`scaffold-test-preview-ctor-arg-stubs`) |
| **Backlog sync** | Close rows: `scaffold-test-preview-ctor-arg-stubs`. Mark obsolete: none. Update related: none. |

---

### 27. `workspace-changes-log-missing-editorconfig-writers` — Wire editorconfig applies through ChangeTracker

| Field | Content |
|-------|---------|
| **Status** | merged (PR #415, 2026-04-24) |
| **Backlog rows closed** | `workspace-changes-log-missing-editorconfig-writers` |
| **Diagnosis** | `workspace_changes` omits `set_diagnostic_severity` / `set_editorconfig_option` applies and buckets distinct writers under generic names. Row cites `src/RoslynMcp.Roslyn/Services/ChangeTracker.cs` + `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs:312`. |
| **Approach** | In the editorconfig tool handlers, wire their applies through `ChangeTracker.Record` with a discriminated tool name. Also update the bucket logic so `rename_apply` / `code_fix_apply` / `format_document_apply` / `remove_dead_code_apply` retain their originating tool name rather than collapsing into `refactoring_apply` / `apply_text_edit`. |
| **Scope** | Production 3: `src/RoslynMcp.Roslyn/Services/ChangeTracker.cs`, `src/RoslynMcp.Host.Stdio/Tools/EditorConfigTools.cs`, `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs` (bucket logic if needed). Tests 1: `ChangeTrackerTests` — each writer surfaces with its own tool name. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 42000 |
| **Risks** | Bucket logic change may alter existing log consumers — acceptable per no-backcompat rule; document in CHANGELOG. |
| **Validation** | Regression test applies one of each writer and asserts distinct tool names in `workspace_changes` output. |
| **Performance review** | N/A — correctness fix. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | - **Fixed:** `workspace_changes` now logs `set_editorconfig_option` / `set_diagnostic_severity` applies and preserves distinct originating tool names (`ChangeTracker` + `EditorConfigTools`). (`workspace-changes-log-missing-editorconfig-writers`) |
| **Backlog sync** | Close rows: `workspace-changes-log-missing-editorconfig-writers`. Mark obsolete: none. Update related: none. |

---

### 28. `host-recycle-opacity` — Surface previous-process metadata on post-restart probes

| Field | Content |
|-------|---------|
| **Status** | merged (PR #422, 2026-04-25) |
| **Backlog rows closed** | `host-recycle-opacity` |
| **Diagnosis** | `server_info.connection` carries no history of host-process restarts. Row cites `src/RoslynMcp.Host.Stdio/Tools/ServerTools.cs:64` + the connection-state DTO under `src/RoslynMcp.Core/Models/`. |
| **Approach** | In the host's process-lifecycle module (near `ServerTools` composition), capture `previousStdioPid` / `previousExitedAt` / `previousRecycleReason` from disk-persisted process metadata on startup. Surface these on the first `server_info` / `server_heartbeat` probe after restart; clear after emission. |
| **Scope** | Production 3: `src/RoslynMcp.Host.Stdio/Tools/ServerTools.cs`, the connection-state DTO in `src/RoslynMcp.Core/Models/`, and the lifecycle module that persists/reads process metadata. Tests 1: host-lifecycle fixture — restart sequence surfaces previous-* fields exactly once. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 48000 |
| **Risks** | Disk-persisted metadata must survive crashes; scope write to shutdown hook, but guard against stale files (TTL or clear-on-read). |
| **Validation** | Integration test simulates a clean shutdown + restart; first probe carries previous-* fields, second probe does not. |
| **Performance review** | N/A — one-shot fields on startup. |
| **CHANGELOG category** | `Added` |
| **CHANGELOG entry (draft)** | - **Added:** `server_info.connection` now carries `previousStdioPid`, `previousExitedAt`, and `previousRecycleReason` on the first probe after a host restart (`ServerTools` + connection DTO). (`host-recycle-opacity`) |
| **Backlog sync** | Close rows: `host-recycle-opacity`. Mark obsolete: none. Update related: none. |

---

### 29. `revert-apply-by-sequence-number` — Add `revert_apply_by_sequence` MCP tool

| Field | Content |
|-------|---------|
| **Status** | merged (PR #431, 2026-04-25) |
| **Backlog rows closed** | `revert-apply-by-sequence-number` |
| **Diagnosis** | `revert_last_apply` is LIFO-only and gets cleared by auto-reload; late-session revert of an earlier apply requires hand edits. Row cites `src/RoslynMcp.Roslyn/Services/UndoService.cs` + `src/RoslynMcp.Host.Stdio/Tools/UndoTools.cs`. `workspace_changes` already tracks ordered applies with sequence numbers. |
| **Approach** | Add `revert_apply_by_sequence(workspaceId, sequenceNumber)` to `UndoService` returning `{reverted, revertedOperation, affectedFiles}`. Fail cleanly when later applies depend on the target. Register in `UndoTools` as a new `[McpServerTool]`; add a `ServerSurfaceCatalog` entry (forced by RMCP001/RMCP002). Update `README.md` surface-count. Register `I{Undo}Service` test-fixture DI if not already present. |
| **Scope** | Production 4 (new-MCP-tool structural-unit exemption): Core contract extension on `IUndoService`, Roslyn implementation in `UndoService`, Host.Stdio surface in `UndoTools`, Catalog entry in `ServerSurfaceCatalog.cs`. Addenda: `TestBase.cs` if DI wiring needed, `README.md` surface-count. Tests 1: `UndoServiceTests` — revert-by-sequence happy path + dependency-blocked path. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 55000 |
| **Risks** | Dependency detection (later applies depending on earlier target) is non-trivial — opt for conservative "fail if any later apply touches the same files". |
| **Validation** | Regression test: apply A, apply B, revert A by sequence — returns dependency-block if B touches A's files; success otherwise. |
| **Performance review** | N/A — new tool surface. |
| **CHANGELOG category** | `Added` |
| **CHANGELOG entry (draft)** | - **Added:** New MCP tool `revert_apply_by_sequence(workspaceId, sequenceNumber)` for non-LIFO session revert (`UndoService` + `UndoTools`). (`revert-apply-by-sequence-number`) |
| **Backlog sync** | Close rows: `revert-apply-by-sequence-number`. Mark obsolete: none. Update related: none. |

---

### 30. `diagnostic-details-perf-budget-overrun` — Short-circuit to single-document diagnostic scan

| Field | Content |
|-------|---------|
| **Status** | merged (PR #416, 2026-04-24) |
| **Backlog rows closed** | `diagnostic-details-perf-budget-overrun` |
| **Diagnosis** | `diagnostic_details` took 16045ms on single-symbol probes because the handler re-runs project-wide diagnostics even when `filePath` + position are pre-resolved. Row cites `src/RoslynMcp.Roslyn/Services/DiagnosticService.cs` + `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs:138`. |
| **Approach** | In `DiagnosticService`, short-circuit when `filePath` + line/column are supplied: fetch the single-document diagnostic set (`SemanticModel.GetDiagnostics(documentSpan)`) instead of the solution-wide pass. |
| **Scope** | Production 2: `src/RoslynMcp.Roslyn/Services/DiagnosticService.cs`, `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs`. Tests 1: `DiagnosticServiceTests` — perf assertion (< 2s) on the sample solution + correctness parity with the slow path. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 40000 |
| **Risks** | Single-document scan may miss cross-document diagnostics (e.g., project-level analyzer rules). Unit-test parity on a representative diagnostic id. |
| **Validation** | Perf assertion < 2s + parity test vs slow path on IT-Chat-Bot-like fixture. |
| **Performance review** | Intentional hot-path change. Record measured latency in the test to catch future regressions. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | - **Fixed:** `diagnostic_details` now short-circuits to a single-document diagnostic scan when `filePath` + position are supplied, cutting typical latency from ~16s to <2s (`DiagnosticService`). (`diagnostic-details-perf-budget-overrun`) |
| **Backlog sync** | Close rows: `diagnostic-details-perf-budget-overrun`. Mark obsolete: none. Update related: none. |

---

### 31. `format-range-apply-preview-token-lifetime` — Pin preview tokens to workspace-version ranges

| Field | Content |
|-------|---------|
| **Status** | merged (PR #425, 2026-04-25) |
| **Backlog rows closed** | `format-range-apply-preview-token-lifetime` |
| **Diagnosis** | `format_range_apply(previewToken)` fails with "Preview token not found or expired" within 5s of preview when auto-reload intervenes. Row cites `src/RoslynMcp.Roslyn/Services/PreviewStore.cs` + `src/RoslynMcp.Host.Stdio/Tools/RefactoringTools.cs:174`. |
| **Approach** | Pin preview tokens to a workspace-version RANGE (instead of a single version) in `PreviewStore` so a single auto-reload between preview and apply does not invalidate the token. Apply still verifies the resulting edit against the current version. |
| **Scope** | Production 1: `src/RoslynMcp.Roslyn/Services/PreviewStore.cs`. Tests 1: `PreviewStoreTests` — preview → auto-reload (one version bump) → apply succeeds; preview → two auto-reloads → apply still rejects (range policy bounded). |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 35000 |
| **Risks** | Range policy must cap span (e.g., max 1-2 version bumps) to avoid stale-preview apply. WorkspaceManager-family row — scheduled separate wave from `autoreload-cascade-stdio-host-crash` + `auto-reload-retry-inside-call` per Step 5 rule 4. |
| **Validation** | Regression test for the 5s-auto-reload window; both success + rejection paths. |
| **Performance review** | N/A — correctness fix. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | - **Fixed:** `format_range_apply` preview tokens now span a bounded workspace-version range so a single intervening auto-reload does not invalidate them (`PreviewStore`). (`format-range-apply-preview-token-lifetime`) |
| **Backlog sync** | Close rows: `format-range-apply-preview-token-lifetime`. Mark obsolete: none. Update related: none. |

---

### 32. `auto-reload-retry-inside-call` — Retry once against refreshed snapshot in WorkspaceManager

| Field | Content |
|-------|---------|
| **Status** | merged (PR #429, 2026-04-25) |
| **Backlog rows closed** | `auto-reload-retry-inside-call` |
| **Diagnosis** | `format_document_preview` / `get_completions` / `find_references_bulk` return "Document not found" or empty with `staleAction="auto-reloaded"` — server self-healed but caller saw an error. Row cites `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` + per-tool wrappers. |
| **Approach** | In the stale-snapshot handler shared across tools (WorkspaceManager's post-reload path), retry the operation once against the refreshed snapshot before surfacing the error; annotate retried calls with `_meta.retriedAfterReload=true`. Cancellation token caps total time. |
| **Scope** | Production 2: `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`, + the shared wrapper helper at `src/RoslynMcp.Host.Stdio/Tools/` (if the retry must thread through tool-side envelopes). Tests 1: WorkspaceManager-level fixture — single reload → retry succeeds; timeout path respected. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 50000 |
| **Risks** | Retry must not cascade: cap at 1. WorkspaceManager-family — scheduled separate wave from other WM-touching initiatives (order 5, 31, 28) per Step 5 rule 4. |
| **Validation** | Regression test: writer → reader in same turn, reader succeeds with `_meta.retriedAfterReload=true`. |
| **Performance review** | N/A — correctness fix; retry adds one additional snapshot read on the failure path only. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | - **Fixed:** WorkspaceManager now retries stale-snapshot reads once against the refreshed snapshot and annotates retried calls with `_meta.retriedAfterReload` (`WorkspaceManager`). (`auto-reload-retry-inside-call`) |
| **Backlog sync** | Close rows: `auto-reload-retry-inside-call`. Mark obsolete: none. Update related: none. |

---

### 33. `get-prompt-text-publish-parameter-schema` — Publish prompt `parameters[]` on catalog resource

| Field | Content |
|-------|---------|
| **Status** | merged (PR #421, 2026-04-25) |
| **Backlog rows closed** | `get-prompt-text-publish-parameter-schema` |
| **Diagnosis** | `roslyn://server/catalog/prompts/*` lists `name`/`summary`/`supportTier` only; callers must invoke `get_prompt_text` with empty `parametersJson` to discover required params. Row cites `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Resources.cs`, `ServerSurfaceCatalog.cs`, `src/RoslynMcp.Host.Stdio/Tools/PromptShimTools.cs`. |
| **Approach** | Extend the catalog resource DTO to include `parameters: [{name, type, required, defaultValue, description}]` per prompt, sourced from `[McpServerPrompt]` attributes at reflection time. Update `PromptShimTools` if it exposes a parallel introspection path. |
| **Scope** | Production 3: `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Resources.cs`, `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs`, `src/RoslynMcp.Host.Stdio/Tools/PromptShimTools.cs`. Tests 1: `ServerSurfaceCatalogTests` — catalog resource output includes parameters for a representative 3-param prompt. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 45000 |
| **Risks** | Catalog-touching — scheduled no adjacent-order with other catalog-touches (only one in this batch). Reflection cost runs at startup; ensure cached. |
| **Validation** | Regression test verifies parameter schema matches `[McpServerPrompt]` attribute set for representative prompts. |
| **Performance review** | N/A — one-time startup work. |
| **CHANGELOG category** | `Added` |
| **CHANGELOG entry (draft)** | - **Added:** Prompt catalog resource now publishes `parameters[]` (name, type, required, defaultValue, description) per prompt (`ServerSurfaceCatalog.Resources`). (`get-prompt-text-publish-parameter-schema`) |
| **Backlog sync** | Close rows: `get-prompt-text-publish-parameter-schema`. Mark obsolete: none. Update related: none. |

---

### 34. `ci-test-parallelization-audit` — Localize 3 pre-existing cleanup-race flakes

| Field | Content |
|-------|---------|
| **Status** | pending |
| **Backlog rows closed** | `ci-test-parallelization-audit` |
| **Diagnosis** | `eng/verify-release.ps1` has 3 pre-existing cleanup-race flakes after the `[DoNotParallelize]` sweep completed. Row cites candidate locations `tests/RoslynMcp.Tests/AssemblyCleanup.cs`, `tests/RoslynMcp.Tests/TestBase.cs`, and "another teardown path." Dedicated phased plan already exists at `ai_docs/plans/20260422T170500Z_test-parallelization-audit/plan.md`. |
| **Approach** | Phase 1: reproduce + name the failing tests (≥3 `verify-release.ps1` runs). Phase 2: localize the race to a single file. Phase 3: fix + add repeated-release stress gate (`eng/verify-release-stress.ps1` / CI). This initiative is the localization phase only — the fix ships as a follow-on row if the localization lands in a separate file budget. |
| **Scope** | Production 0 (investigation phase — no code edits yet) or 1-2 (teardown-path fix if localization lands in-phase). Tests: extend existing teardown tests if fix lands. Scope-cap guard: if the fix requires >2 files, split into follow-on row. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 50000 |
| **Risks** | May not reproduce in a single session — log evidence and re-queue if localization stalls. |
| **Validation** | `eng/verify-release-stress.ps1` (new or existing) passes 5× in a row after fix. |
| **Performance review** | N/A unless teardown timing is the root cause. |
| **CHANGELOG category** | `Maintenance` |
| **CHANGELOG entry (draft)** | - **Maintenance:** localized and fixed 3 cleanup-race flakes in `verify-release.ps1`; added repeated-release stress gate (`AssemblyCleanup` / `TestBase`). (`ci-test-parallelization-audit`) |
| **Backlog sync** | Close rows: `ci-test-parallelization-audit`. Mark obsolete: none. Update related: none. |

---

### 35. `diagnostic-details-supported-fixes-empty` — Populate `supportedFixes` or soften description

| Field | Content |
|-------|---------|
| **Status** | merged (PR #430, 2026-04-25) |
| **Backlog rows closed** | `diagnostic-details-supported-fixes-empty` |
| **Diagnosis** | Behavior per backlog row; code path = `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs:138`. `supportedFixes:[]` for CA1826 / ASP0015 / MA0001 / SYSLIB1045 despite description advertising "curated fix options." No cross-layer drift. |
| **Approach** | Prefer populating `supportedFixes` from the loaded `CodeFixProviderRegistry`. Fallback: soften the tool description to "fix options when available" if the registry doesn't expose the ids. |
| **Scope** | Production 1: `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs`. Rule 3 exemption: tool-surface-only, 1 file. Tests 1: `AnalysisToolsTests` — each of the four ids yields non-empty `supportedFixes` or docs-match-reality. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 28000 |
| **Risks** | Registry access pattern may not exist — fall back to description softening. |
| **Validation** | Regression test covers all four ids. |
| **Performance review** | N/A. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | - **Fixed:** `diagnostic_details.supportedFixes` now populates from the loaded `CodeFixProviderRegistry` (or the tool description matches the empty result) (`AnalysisTools`). (`diagnostic-details-supported-fixes-empty`) |
| **Backlog sync** | Close rows: `diagnostic-details-supported-fixes-empty`. Mark obsolete: none. Update related: none. |

---

### 36. `fix-all-preview-silent-on-missing-provider-info-severity` — Uniform guidance across severities

| Field | Content |
|-------|---------|
| **Status** | merged (PR #424, 2026-04-25) |
| **Backlog rows closed** | `fix-all-preview-silent-on-missing-provider-info-severity` |
| **Diagnosis** | On Info-severity ids `fix_all_preview` returns empty/null guidance (indistinguishable from "no occurrences"); on Warning-severity ids it returns helpful `list_analyzers` guidance. Row cites `src/RoslynMcp.Roslyn/Services/FixAllService.cs` + `src/RoslynMcp.Host.Stdio/Tools/FixAllTools.cs:18`. |
| **Approach** | In `FixAllService`, emit the same "no fix provider loaded" guidance regardless of severity; guidance should not key on severity. |
| **Scope** | Production 1: `src/RoslynMcp.Roslyn/Services/FixAllService.cs`. Tests 1: `FixAllServiceTests` — Info + Warning severity ids both yield the helpful guidance when no provider is loaded. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 28000 |
| **Risks** | Overlap with initiative 2 (`fix-all-preview-sequence-contains-no-elements`) — ensure the changes compose cleanly; schedule sequentially. |
| **Validation** | Regression test covers IDE0130 (Info) + CA1826 (Warning). |
| **Performance review** | N/A. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | - **Fixed:** `fix_all_preview` now returns uniform "no fix provider loaded" guidance regardless of diagnostic severity (`FixAllService`). (`fix-all-preview-silent-on-missing-provider-info-severity`) |
| **Backlog sync** | Close rows: `fix-all-preview-silent-on-missing-provider-info-severity`. Mark obsolete: none. Update related: `fix-all-preview-sequence-contains-no-elements` (separate, scheduled earlier). |

---

### 37. `split-class-preview-orphan-indent` — Trim trailing whitespace on post-split gaps

| Field | Content |
|-------|---------|
| **Status** | pending |
| **Backlog rows closed** | `split-class-preview-orphan-indent` |
| **Diagnosis** | `split_class_preview` leaves orphan-indentation blank lines where removed members lived. Row cites `src/RoslynMcp.Roslyn/Services/ClassSplitOrchestrator.cs` + `src/RoslynMcp.Host.Stdio/Tools/OrchestrationTools.cs:43`. |
| **Approach** | In `ClassSplitOrchestrator`, post-process the output: trim trailing whitespace on lines that become empty after member removal. |
| **Scope** | Production 1: `src/RoslynMcp.Roslyn/Services/ClassSplitOrchestrator.cs`. Tests 1: extend `ClassSplitOrchestratorTests` — post-split diff contains no `+    ` (trailing-space) lines. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 25000 |
| **Risks** | Ensure trim doesn't affect intentional indentation inside preserved members. |
| **Validation** | Regression test asserts no `^\s+$` lines in preview diff. |
| **Performance review** | N/A. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | - **Fixed:** `split_class_preview` no longer leaves orphan-indentation blank lines on post-split gaps (`ClassSplitOrchestrator`). (`split-class-preview-orphan-indent`) |
| **Backlog sync** | Close rows: `split-class-preview-orphan-indent`. Mark obsolete: none. Update related: none. |

---

### 38. `symbol-impact-sweep-suggested-tasks-count-drift` — Use pre-cap total in task-string builder

| Field | Content |
|-------|---------|
| **Status** | merged (PR #420, 2026-04-25) |
| **Backlog rows closed** | `symbol-impact-sweep-suggested-tasks-count-drift` |
| **Diagnosis** | `symbol_impact_sweep` reports `suggestedTasks: ["Review 10 reference(s)..."]` despite `totalCount=193`. Row cites `src/RoslynMcp.Roslyn/Services/ImpactSweepService.cs` + `src/RoslynMcp.Host.Stdio/Tools/ImpactSweepTools.cs:17`. |
| **Approach** | Use the pre-cap `totalCount` in the `suggestedTasks` string builder instead of the truncated list length. |
| **Scope** | Production 1: `src/RoslynMcp.Roslyn/Services/ImpactSweepService.cs`. Tests 1: extend `ImpactSweepServiceTests` — `totalCount=193, maxItemsPerCategory=10` yields task string citing 193. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 25000 |
| **Risks** | None material. |
| **Validation** | Regression test asserts task-string contains the pre-cap total. |
| **Performance review** | N/A. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | - **Fixed:** `symbol_impact_sweep.suggestedTasks` now reports the pre-cap reference total rather than the truncated list length (`ImpactSweepService`). (`symbol-impact-sweep-suggested-tasks-count-drift`) |
| **Backlog sync** | Close rows: `symbol-impact-sweep-suggested-tasks-count-drift`. Mark obsolete: none. Update related: none. |

---

### 39. `scaffold-test-preview-sibling-inference-overbroad` — Narrow sibling-inference to file-scoped namespace + base class

| Field | Content |
|-------|---------|
| **Status** | pending |
| **Backlog rows closed** | `scaffold-test-preview-sibling-inference-overbroad` |
| **Diagnosis** | `scaffold_test_preview` copies `[Trait("Category","Playwright")]`, Playwright usings, and 10+ unused imports from the MRU sibling fixture. Row cites `src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs` + `src/RoslynMcp.Host.Stdio/Tools/ScaffoldingTools.cs:87`. |
| **Approach** | Narrow sibling-inference to file-scoped namespace + base-class name only. Exclude `[Trait]` / `[Category]` / `[Collection]` attributes. Trim usings to those actually referenced in the generated body. |
| **Scope** | Production 1: `src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs`. Tests 1: `ScaffoldingServiceTests` — Playwright-sibling MRU does NOT leak into non-Playwright scaffold. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 32000 |
| **Risks** | Usings trim must keep any required via generic inference — test both static + instance scaffold cases. |
| **Validation** | Regression fixture with Playwright sibling + non-Playwright target. |
| **Performance review** | N/A. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | - **Fixed:** `scaffold_test_preview` sibling-inference now scopes to file-scoped namespace + base-class name and trims unused usings/attributes (`ScaffoldingService`). (`scaffold-test-preview-sibling-inference-overbroad`) |
| **Backlog sync** | Close rows: `scaffold-test-preview-sibling-inference-overbroad`. Mark obsolete: none. Update related: none. |

---

### 40. `project-mutation-preview-xml-formatting` — Format msbuild-preview diffs

| Field | Content |
|-------|---------|
| **Status** | pending |
| **Backlog rows closed** | `project-mutation-preview-xml-formatting` |
| **Diagnosis** | `add_package_reference_preview`, `set_project_property_preview`, `add_central_package_version_preview` emit collapsed single-line XML. Row cites `src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs` + `OrchestrationMsBuildXml.cs` + `src/RoslynMcp.Host.Stdio/Tools/ProjectMutationTools.cs`. |
| **Approach** | Post-process generated diffs through a format pass in `OrchestrationMsBuildXml.cs` before returning. Handle `</PropertyGroup><ItemGroup>`, trailing whitespace, and preserve intentional comment/trivia spacing. |
| **Scope** | Production 2: `src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs`, `src/RoslynMcp.Roslyn/Services/OrchestrationMsBuildXml.cs`. Tests 1: `ProjectMutationServiceTests` — each of the three preview tools yields formatted XML. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 35000 |
| **Risks** | XML format pass must not re-sort or alter semantic content. |
| **Validation** | Regression diff-equality on a fixture project for each preview tool. |
| **Performance review** | N/A — cosmetic. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | - **Fixed:** Project-mutation previews (`add_package_reference_preview`, `set_project_property_preview`, `add_central_package_version_preview`) now emit formatted XML with line breaks between groups (`ProjectMutationService` + `OrchestrationMsBuildXml`). (`project-mutation-preview-xml-formatting`) |
| **Backlog sync** | Close rows: `project-mutation-preview-xml-formatting`. Mark obsolete: none. Update related: none. |

---

### 41. `semantic-search-duplicate-results-and-fallback-signal` — Dedupe + surface Debug fallback fields

| Field | Content |
|-------|---------|
| **Status** | pending |
| **Backlog rows closed** | `semantic-search-duplicate-results-and-fallback-signal` |
| **Diagnosis** | `semantic_search("classes implementing IDisposable")` returns `JobProcessor` twice; advertised `fallbackStrategy` / `matchKind` fields missing. Row cites `src/RoslynMcp.Roslyn/Services/CodePatternAnalyzer.cs` + `src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs:293` + DTO `src/RoslynMcp.Core/Models/SemanticSearchResponseDto.cs`. |
| **Approach** | Dedupe by `symbolHandle` in `CodePatternAnalyzer`; populate `Debug.fallbackStrategy` + `matchKind` fields per result (already captured internally, just not projected to the DTO). |
| **Scope** | Production 2: `src/RoslynMcp.Roslyn/Services/CodePatternAnalyzer.cs`, `src/RoslynMcp.Core/Models/SemanticSearchResponseDto.cs`. Tests 1: `CodePatternAnalyzerTests` — duplicate removed + Debug fields populated. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 35000 |
| **Risks** | DTO field addition is breaking — acceptable per no-backcompat. Dedupe key `symbolHandle` must be canonical. |
| **Validation** | Regression test with intentionally-duplicated symbol set. |
| **Performance review** | N/A. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | - **Fixed:** `semantic_search` now dedupes results by `symbolHandle` and surfaces `Debug.fallbackStrategy` + `matchKind` per result (`CodePatternAnalyzer` + `SemanticSearchResponseDto`). (`semantic-search-duplicate-results-and-fallback-signal`) |
| **Backlog sync** | Close rows: `semantic-search-duplicate-results-and-fallback-signal`. Mark obsolete: none. Update related: none. |

---

### 42. `test-related-files-empty-result-explainability` — Add diagnostic fields to empty-result response

| Field | Content |
|-------|---------|
| **Status** | pending |
| **Backlog rows closed** | `test-related-files-empty-result-explainability` |
| **Diagnosis** | `test_related_files` empty result cannot distinguish "no tests exist" from "heuristic miss." Row cites `src/RoslynMcp.Core/Models/RelatedTestsForFilesDto.cs` + the service layer. |
| **Approach** | Extend `RelatedTestsForFilesDto` with `diagnostics: {scannedTestProjects, heuristicsAttempted, missReasons[]}`. Populate from `TestDiscoveryService`. |
| **Scope** | Production 2: `src/RoslynMcp.Core/Models/RelatedTestsForFilesDto.cs`, `src/RoslynMcp.Roslyn/Services/TestDiscoveryService.cs`. Tests 1: `TestDiscoveryServiceTests` — empty-result case populates diagnostics. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 35000 |
| **Risks** | Kept separate from P3 `test-related-response-envelope-parity` (order 16) per Rule 1 — different tests, different DTO. |
| **Validation** | Regression test: file with no matching tests yields non-empty `missReasons`. |
| **Performance review** | N/A. |
| **CHANGELOG category** | `Added` |
| **CHANGELOG entry (draft)** | - **Added:** `test_related_files` empty-result responses now include `diagnostics.scannedTestProjects`, `heuristicsAttempted`, and `missReasons[]` (`RelatedTestsForFilesDto` + `TestDiscoveryService`). (`test-related-files-empty-result-explainability`) |
| **Backlog sync** | Close rows: `test-related-files-empty-result-explainability`. Mark obsolete: none. Update related: `test-related-response-envelope-parity` (P3, order 16). |

---

### 43. `test-related-files-service-refactor-underreporting` — Broaden service-layer refactor heuristics

| Field | Content |
|-------|---------|
| **Status** | pending |
| **Backlog rows closed** | `test-related-files-service-refactor-underreporting` |
| **Diagnosis** | `TestDiscoveryService` fallback leans on type-name/filename affinity; multi-file service changes underreport. Row cites 2026-04-23 sweep misses (`MutationAnalysisService` + `ScaffoldingService`, `CodePatternAnalyzer` + `TestDiscoveryService`). |
| **Approach** | Broaden fallback heuristic to surface likely integration or neighboring service tests for multi-file changes: add namespace-neighbor and inbound-reference expansion when file-affinity match is empty. |
| **Scope** | Production 1: `src/RoslynMcp.Roslyn/Services/TestDiscoveryService.cs`. Tests 1: `TestDiscoveryServiceTests` — multi-file service-change fixture surfaces at least one neighbor-test before returning empty. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 42000 |
| **Risks** | Over-broadening can produce noise — cap neighbor-expansion to 1 hop. Scope touches `TestDiscoveryService`; keep cohesive with P4 order 42 but ship separately per Rule 1. |
| **Validation** | Regression validates against the two cited 2026-04-23 miss cases. |
| **Performance review** | Fallback runs only on empty primary results — amortized cost low; verify with timing test. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | - **Fixed:** `test_related_files` fallback now surfaces namespace-neighbor and inbound-reference tests on multi-file service refactors (`TestDiscoveryService`). (`test-related-files-service-refactor-underreporting`) |
| **Backlog sync** | Close rows: `test-related-files-service-refactor-underreporting`. Mark obsolete: none. Update related: `test-related-files-empty-result-explainability` (P4, order 42). |

---

### 44. `workspace-process-pool-or-daemon` — Gated investigation; defer until profiling evidence lands

| Field | Content |
|-------|---------|
| **Status** | pending |
| **Backlog rows closed** | `workspace-process-pool-or-daemon` |
| **Diagnosis** | Row explicitly depends on `large-solution profile` per `docs/large-solution-profiling-baseline.md`. No production code lands until profiling evidence (50+ project timings) shows `workspace_warm` is insufficient. Marked `heroic-last` — may `defer` at closeout per v4 guidance. |
| **Approach** | Phase 1: capture representative 50+ project timings per baseline doc. Phase 2 (conditional): produce bounded design note comparing daemon / process-pool / shared-workspace. NO implementation within this initiative. |
| **Scope** | Production 0 (investigation + design note only). Tests 0. Design note lands at `ai_docs/designs/` if phase 1 justifies phase 2. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 40000 |
| **Risks** | Likely `deferred` at closeout if profiling evidence does not materialize this batch. Consistent with v4 planner note on `heroic-last` often meaning "defer at closeout". |
| **Validation** | N/A — investigation phase. |
| **Performance review** | Output IS a performance review. |
| **CHANGELOG category** | `Maintenance` |
| **CHANGELOG entry (draft)** | - **Maintenance:** captured 50+ project `workspace_load` baseline timings; [design note link] (`workspace-process-pool-or-daemon`). |
| **Backlog sync** | Close rows: `workspace-process-pool-or-daemon` (only on shipping phase 1). Mark obsolete: none. Update related: none. |

---

- [ ] backlog: sync ai_docs/backlog.md
