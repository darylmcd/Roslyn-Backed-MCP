# Backlog sweep plan — 2026-04-22T22:30:00Z (draft)

**Plan kind:** planning pass only (no product code or backlog edits).

**Preamble — anchor verification (Step 3):** *Skipped for planner token budget.* Cited `file:line` and symbol names are grounded in a source read of the listed files; the executor should run `symbol_search` / Grep for any `Validation` anchor before apply if the branch has moved.

**Preamble — P3 `workspace-close`:** Current `WorkspaceManager.Close` (`src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` ~244–257) only removes the session, unwatches, and disposes — it does not call `ValidateWorkspacePath` (the only `FileNotFoundException` in that file is ~1063, used on load paths). If the Jellyfin stress F21 `FileNotFound` does not reproduce on `main`, the executor should search subscribers (`WorkspaceClosed`), gate machinery, and `NotifyResourcesChangedAsync` in `CloseWorkspace` before adding defensive path handling.

**Final todo (all initiatives):** `backlog: sync ai_docs/backlog.md` per agent contract when closing or updating related rows.

---

## Initiative 1 — `validate-workspace-overallstatus-false-positive`

| Field | Content |
|-------|---------|
| **Status** | `merged` (PR [#343](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/343) + follow-up [#346](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/346) — DTO doc + extra tests, 2026-04-22) |
| **Backlog rows closed** | `validate-workspace-overallstatus-false-positive` |
| **Diagnosis** | `CompileCheckService.CheckAsync` sets `Success: acc.ErrorCount == 0 && !acc.Cancelled && acc.CompletedProjects > 0` (`src/RoslynMcp.Roslyn/Services/CompileCheckService.cs` ~71–76). If **zero projects** complete (empty filter, cancellation edge, or all compilations null), `Success` is false with **zero** `ErrorCount`. `WorkspaceValidationService.ComputeOverallStatus` then returns `"compile-error"` when `!compile.Success` (`src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs` ~254–266) even with no compiler/analyzer error rows. |
| **Approach** | Align semantics: (a) either treat "no project completed" as a distinct `OverallStatus` / `Success` path, or (b) have `ComputeOverallStatus` return `"clean"` when `compile.ErrorCount == 0` and the error diagnostic union is empty — **without** conflating genuine compile failures. Prefer reusing `compile.ErrorCount` and explicit `TotalProjects`/`CompletedProjects` from `CompileCheckDto` so gating use cases only see `compile-error` when there are real compiler-failure-class diagnostics. Add/adjust tests in existing workspace validation / compile check test coverage. |
| **Scope** | **Production ~2–3** (`WorkspaceValidationService.cs`, `CompileCheckService.cs` if `Success` contract changes, any DTO doc touch). **Tests ~1** existing file. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 32000 |
| **Risks** | Changing `Success` semantics may affect other `compile_check` consumers; run broad tests. |
| **Validation** | Unit/integration: workspace validation when `TotalProjects>0` but `CompletedProjects==0`; stress scenario with clean tree and 0 errors → `OverallStatus` not `compile-error`. |
| **Performance review** | N/A — correctness; no hot-path change intended. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | **Fixed** `validate_workspace` / `validate_recent_git_changes` sometimes reporting `overallStatus: compile-error` with zero errors (`validate-workspace-overallstatus-false-positive`: `WorkspaceValidationService.ComputeOverallStatus` / `CompileCheckService` success gating). |
| **Backlog sync** | Close: `validate-workspace-overallstatus-false-positive`. Mark obsolete: —. Update related: `mcp-audit-rollup-2026-04-13-22` (F13/E overlap when tranche done). |

---

## Initiative 2 — `preview-multi-file-edit-silent-syntax-acceptance`

| Field | Content |
|-------|---------|
| **Status** | `merged` (PR [#349](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/349), 2026-04-22) |
| **Backlog rows closed** | `preview-multi-file-edit-silent-syntax-acceptance` |
| **Diagnosis** | `PreviewMultiFileTextEditsAsync` uses `GetCSharpSyntaxErrors` which parses and collects **only** `GetDiagnostics()` at `Error` (`src/RoslynMcp.Roslyn/Services/EditService.cs` ~503–518, ~234–247). Trivia/namespace-boundary insertions can yield a tree Roslyn recovers from without surfacing the expected errors — or errors at a severity not handled — so invalid C# can slip through with a clean diff. |
| **Approach** | Tighten validation: e.g. require a full `GetDiagnostics()` pass for **all** severities that imply parse failure, or add an explicit parse of `CSharpSyntaxTree` + additional checks (root structure / `ContainsDiagnostics` for syntax-related ids). Reconcile with tool description in `MultiFileEditTools` and experimental catalog. Add regression from Jellyfin F17 shape (namespace + comment + `{`). |
| **Scope** | **Production ~1–2** (`EditService.cs` core; optional `MultiFileEditTools.cs` docstring). **Tests ~1** file. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 40000 |
| **Risks** | May flag intentional intermediate states — document `skipSyntaxCheck=true` or narrow the detector. |
| **Validation** | F17-style text; existing preview tests; ensure error response matches docstring. |
| **Performance review** | N/A. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | **Fixed** `preview_multi_file_edit` silent acceptance of invalid C# when default syntax check is on (`preview-multi-file-edit-silent-syntax-acceptance`: `EditService` post-edit parse validation). |
| **Backlog sync** | Close: `preview-multi-file-edit-silent-syntax-acceptance`. Update related: `mcp-audit-rollup-2026-04-13-22` (F17). |

---

## Initiative 3 — `workspace-close-missing-solution-on-disk`

| Field | Content |
|-------|---------|
| **Status** | `merged` (PR [#345](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/345), 2026-04-22) |
| **Backlog rows closed** | `workspace-close-missing-solution-on-disk` |
| **Diagnosis** | Per preamble, `Close` is lightweight; a `FileNotFound` likely comes from a **different** callsite in the `workspace_close` path (host notify, resource invalidation) or a race with reload. Reproduce and pin stack before or during implementation. |
| **Approach** | After repro: ensure close **always** evicts the in-memory session and returns success JSON when the session id exists, even if on-disk `LoadedPath` is gone; no pre-close validation that re-opens the sln. Add test simulating missing file if feasible (temp dir + delete). |
| **Scope** | **Production 2–4** (`WorkspaceTools.CloseWorkspace` path, `WorkspaceManager` / gate if needed). **Tests 1** file. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 38000 |
| **Risks** | Hiding I/O errors could mask real bugs — return structured partial success with warning if needed. |
| **Validation** | Close after deleting solution file; `workspace_list` no longer includes id. |
| **Performance review** | N/A. |
| **CHANGELOG category** | `Fixed` |
| **CHANGELOG entry (draft)** | **Fixed** `workspace_close` failure when the original solution file no longer exists on disk (`workspace-close-missing-solution-on-disk`: lifecycle + gate). |
| **Backlog sync** | Close: `workspace-close-missing-solution-on-disk`. Update related: `mcp-audit-rollup-2026-04-13-22` (F21). |

---

## Initiative 4a — `cc-18-19-s1-scripting-persist` (largest two methods, one session)

| Field | Content |
|-------|---------|
| **Status** | `pending` |
| **Backlog rows closed** | *none — row closes on 4g* |
| **Diagnosis** | `ScriptingService.EvaluateAsync` (cc=19, **182 LOC**) and `RefactoringService.PersistDocumentSetChangesAsync` (cc=18, **191 LOC**) per backlog; highest LOC / worst MI-per-cc among set. |
| **Approach** | Same as WS2: `extract_method_preview` where applicable, hand-`Edit` on CS0136/CS0841. `verify-release.ps1` for tranche. |
| **Scope** | **≤4 prod files** in one PR for this tranche; **≤3 test** files. |
| **Tool policy** | `preview-then-apply` |
| **Estimated context cost** | 55000 |
| **Risks** | Extraction can change behavior around async state — add targeted tests. |
| **Validation** | Filtered `test_run`; `compile_check` on touched projects. |
| **Performance review** | N/A unless hot path proven. |
| **CHANGELOG category** | `Maintenance` |
| **CHANGELOG entry (draft)** | **Maintenance** reduce cyclomatic complexity in `ScriptingService` / `RefactoringService` (`cc-18-to-19-residuals-post-top10-extraction` tranche 1). |
| **Backlog sync** | Contributes to `cc-18-to-19-residuals-post-top10-extraction`; do not close row. |

---

## Initiative 4b — `cc-18-19-s2-mutation-scaffold`

| Field | Content |
|-------|---------|
| **Status** | `pending` |
| **Backlog rows closed** | *none until row closure PR* |
| **Diagnosis** | `MutationAnalysisService.FindTypeMutationsAsync` (cc=18) + `ScaffoldingService.PreviewScaffoldTestBatchAsync` (cc=19) per `ai_docs/backlog.md`. |
| **Approach** | `preview-then-apply` extractions; `verify-release.ps1` per tranche. |
| **Scope** | ≤4 prod / ≤3 test per Rule 3–4. |
| **Tool policy** | `preview-then-apply` |
| **Estimated context cost** | 48000 |
| **CHANGELOG category** | `Maintenance` |
| **Backlog sync** | Contributes to `cc-18-to-19-residuals-post-top10-extraction`. |

## Initiative 4c — `cc-18-19-s3-codepattern-testdiscovery`

| Field | Content |
|-------|---------|
| **Diagnosis** | Pairs `CodePatternAnalyzer.ParseSemanticQuery` (cc=19) + `TestDiscoveryService.FindRelatedTestsAsync` (cc=18, maxNesting=5). |
| **Tool policy** | `preview-then-apply` |
| **Estimated context cost** | 48000 |
| **CHANGELOG** | `Maintenance` — *other fields as initiative 4b* |

## Initiative 4d — `cc-18-19-s4-security-restructure`

| Field | Content |
|-------|---------|
| **Diagnosis** | Pairs `SecurityDiagnosticService.GetAnalyzerStatusAsync` (cc=18) + `RestructureService.StructuralRewriter.TryMatch` (cc=19, maxNesting=5). |
| **Estimated context cost** | 50000 |
| **Tool policy** | `preview-then-apply` |

## Initiative 4e — `cc-18-19-s5-interface-type-strip`

| Field | Content |
|-------|---------|
| **Diagnosis** | Pairs `InterfaceExtractionService.BuildUsingDirectives` (cc=19) + `TypeExtractionService.StripInheritanceOnlyModifiers` (cc=18). |
| **Estimated context cost** | 48000 |
| **Tool policy** | `preview-then-apply` |

## Initiative 4f — `cc-18-19-s6-collapse-nuget`

| Field | Content |
|-------|---------|
| **Diagnosis** | Pairs `RefactoringService.CollapseBlankLineRunsInRange` (cc=19) + `NuGetVulnerabilityJsonParser.AddPackagesFromFramework` (cc=18, paramCount=7; may be disposition “dense switch not extractable”). If parser is not extractable, do only `CollapseBlankLineRunsInRange` in this tranche and add a new backlog line for the parser. |
| **Estimated context cost** | 50000 |
| **Tool policy** | `preview-then-apply` |

## Initiative 4g — `cc-18-19-s7-cohesion-solo` (closes row when 4a–4f are merged)

| Field | Content |
|-------|---------|
| **Status** | `pending` |
| **Backlog rows closed** | `cc-18-to-19-residuals-post-top10-extraction` — **only when** tranches 4a–4f are already in `main` (reconcile order in executor; if 4g lands first, leave row open). |
| **Diagnosis** | `CohesionAnalysisService.FindAccessedMembers` (cc=18) — last method in the backlog list after 4a–4f pairings. |
| **Tool policy** | `preview-then-apply` |
| **Estimated context cost** | 45000 |
| **Backlog sync** | Close `cc-18-to-19-residuals-post-top10-extraction` on the appropriate merge commit after full set ships. |

*If 4b–4f pairings are wrong on source read, the executor re-pairs; one extraction pair per tranche, ≤4 prod files.*

---

## Initiative 5 — `find-dead-fields-across-classes` (P4, new tool)

| Field | Content |
|-------|---------|
| **Status** | `pending` |
| **Backlog rows closed** | `find-dead-fields-across-classes` |
| **Diagnosis** | `find_dead_locals` / `find_unused_symbols` gap for fields (P4). |
| **Approach** | New MCP tool: Core contract + models, Roslyn analyzer service, `Host.Stdio` tool surface, catalog + DI. Rule 3 **structural-unit** exemption. Addenda: `tests/RoslynMcp.Tests/TestBase.cs`, `TestInfrastructure/TestServiceContainer.cs`, `README.md` tool counts. |
| **Tool policy** | `edit-only` (required for new-tool shape). |
| **productionFilesTouched** (reporting) | **7+** (see `state.json` `notes` for 4 units + 3 addenda) |
| **Estimated context cost** | 60000 |
| **CHANGELOG category** | `Added` |
| **Backlog sync** | Close: `find-dead-fields-across-classes`. |

---

## Initiative 6 — `find-duplicate-helpers-framework-wrapper-false-positive` (P4)

| Field | Content |
|-------|---------|
| **Status** | `pending` |
| **Backlog rows closed** | `find-duplicate-helpers-framework-wrapper-false-positive` |
| **Diagnosis** | `UnusedCodeAnalyzer.TryClassifyHelperAsDuplicate` and options (`src/RoslynMcp.Roslyn/Services/UnusedCodeAnalyzer.cs`); false positives for thin minimal-API / BCL wrappers (IT-Chat-Bot session). |
| **Approach** | Add `excludeFrameworkWrappers` or heuristics for single-expression BCL `System.*` / `Microsoft.*` targets; may extend `DuplicateHelperAnalysisOptions` + `AdvancedAnalysisTools` parameter surface. |
| **Scope** | 1–2 production files, 1 test file extension. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 35000 |
| **CHANGELOG category** | `Fixed` or `Changed` (behavior of experimental tool) |

---

## Initiative 7 — `host-parallel-mcp-reads` (P4, blocker: host)

| Field | Content |
|-------|---------|
| **Status** | `pending` |
| **Backlog rows closed** | *optional — ship server docs only; close when host+server story complete* |
| **Diagnosis** | Some MCP hosts serialize tool calls; P4 `blocker: host`. |
| **Approach** | Document `WorkspaceExecutionGate` + read path thread-safety in `ai_docs/runtime.md`; cross-refs. Consumer-side doc note if available. **≤2 doc files** if no code change. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 22000 |
| **CHANGELOG category** | `Maintenance` |

---

## Initiative 8 — `mcp-audit-rollup-2026-04-13-22` (P4, partial tranche)

| Field | Content |
|-------|---------|
| **Status** | `pending` |
| **Backlog rows closed** | *none* (rollup row stays open until all §A–F follow-ups are tracked elsewhere) |
| **Scope** | One shippable slice, e.g. F25 `heldMs` / gate accounting or another **≤4 file** item from the rollup; executor picks after reading `WorkspaceExecutionGate` / `workspace_reload` metadata. |
| **Tool policy** | `edit-only` |
| **scheduleHint** | `heroic-last` |
| **Estimated context cost** | 80000 |
| **notes** | Partial; parent rollup row is not closable in this tranche. |

---

## Initiative 9 — `workspace-process-pool-or-daemon` (P4)

| Field | Content |
|-------|---------|
| **Status** | `pending` |
| **Backlog rows closed** | *deferred if scope > one PR* |
| **Diagnosis** | Per-workspace reload cost across many subprocesses (P4). `deps: compilation-prewarm-on-load` — **verify** satisfied after PR #323 class of work. |
| **Approach** | If **>4 prod files**, split: doc-only tranche in `runtime.md` **or** spawn follow-up plan; daemon mode is a large initiative. This entry assumes **scoping to design note + 1–2 file hooks** for executor, else defer. |
| **Tool policy** | `edit-only` |
| **scheduleHint** | `heroic-last` |
| **Estimated context cost** | 70000 |

---

## Initiative 10 — `workspace-reload-auto-restore` (P4)

| Field | Content |
|-------|---------|
| **Status** | `pending` |
| **Backlog rows closed** | `workspace-reload-auto-restore` |
| **Diagnosis** | CS1705 / stale `MetadataReference` after csproj version edits without `dotnet restore` (IT-Chat-Bot F01; rollup §C). |
| **Approach** | Optional `autoRestore` on `workspace_reload` / `workspace_load`, and/or `restoreRequired` in status DTO when assets drift. |
| **Scope** | 2–4 production files, 1 test file. |
| **Tool policy** | `edit-only` |
| **Estimated context cost** | 50000 |
| **CHANGELOG category** | `Added` or `Changed` |

---

## Sort order (executed) — matches `state.json` `order` 1–16

1. `validate-workspace-overallstatus-false-positive` (P3)  
2. `preview-multi-file-edit-silent-syntax-acceptance` (P3)  
3. `workspace-close-missing-solution-on-disk` (P3)  
4. `find-duplicate-helpers-framework-wrapper-false-positive` (P4)  
5. `host-parallel-mcp-reads` (P4)  
6. `workspace-reload-auto-restore` (P4)  
7. `find-dead-fields-across-classes` (P4, new tool)  
8–14. `cc-18-19-s1` … `cc-18-19-s7` (P3 maintainability)  
15. `mcp-audit-rollup-2026-04-13-22` (P4 partial, `heroic-last`)  
16. `workspace-process-pool-or-daemon` (P4, `heroic-last`)

*Rationale: P3 correctness first (1–3), then smaller P4 wins and `workspace-reload` before the large new tool; cc tranches and heroics after.*

## Self-vet (Step 6)

- [x] No unlawful bundling without Rule-1 four-part cites (none bundled).
- [x] file budgets: cc tranches each ≤4 prod files; new tool uses structural exemption + addenda in notes.
- [x] test budget ≤3 per initiative.
- [x] `estimatedContextTokens` ≤ 80000 each (roll-up partial uses 80k with heroic hint).
- [x] `toolPolicy` set (new tool `edit-only`).
- [x] Anchor pass skipped — logged in preamble.
- [x] Top 5 by sort: three P3 correctness in positions 1–3.
- [x] Initiative count reflects backlog size + cc split + partial rollup.
- [x] `backlog.md` / `CHANGELOG.md` not modified in this pass.

---

## `backlog: sync ai_docs/backlog.md` — final todo

Execute with each shipped PR that closes a row, per contract.
