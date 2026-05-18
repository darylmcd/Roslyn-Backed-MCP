# Backlog sweep plan — 20260517T235058Z

**Generated:** 2026-05-17T23:50:58Z
**Backlog snapshot:** 2026-05-17T15:49:13Z
**Mode:** `/backlog-sweep:prepare count=20`
**Initiative count:** 20 selected (8 High + 12 Medium)
**Phase:** skeleton (deepener stanzas pending)

## Plan summary

Twenty initiatives selected from the 34-row actionable backlog (post PR #808 intake). Selection covers all 8 High-priority (P1) audit findings plus 12 of 19 Medium (P2) findings, prioritized by source-repo coverage (3× self-audit + 9× sibling-repo cross-section).

## Bundle considerations (Rule 1)

Two pairs flagged as bundle candidates — the deepener will verify or split:

- **Initiatives 10 + 11** — `member-hierarchy-overrides-mislabels-sibling-interface-impls` (gh #736) and `find-overrides-vs-member-hierarchy-cross-tool-inconsistency` (gh #737) both touch `MemberHierarchyService.cs` / `OverridesService.cs` and describe the same cross-tool semantic question (what counts as "override" vs "sibling-interface implementation"). Strong bundle signal.
- **Initiatives 19 + 20** — `analyze-dependencies-prompt-payload-overflow` (gh #755) and `review-test-coverage-prompt-payload-overflow` (gh #756) are payload-cap overflows on different prompts; both likely need the same `PromptMessageBuilder.SerializeTruncatedList` pattern (precedent: PR #790 for guided_extract_interface). Likely bundle.

## Initiatives

### 1. extract-method-preview-same-block-scope-false-negative

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `extract-method-preview-same-block-scope-false-negative` |
| Source | gh #744 (P1 — `networkdocumentation` audit) |
| Diagnosis | Root cause is in `FindStatementsInSelection` at `src/RoslynMcp.Roslyn/Services/ExtractMethodService.cs:415-425`. The filter predicate `selectionSpan.Contains(s.Span)` requires that the entire statement's `TextSpan` (exclusive end, pointing one past the closing `}`) falls within the selection span computed by `BuildSelectionSpan` (line 106-113). When the caller supplies `endColumn` pointing at or adjacent to the closing brace of a multi-line `if`-block, `BuildSelectionSpan` sets `endPosition = text.Lines[endLine-1].Start + (endColumn - 1)` — a 0-based character index at or before the `}`. The if-statement's `Span.End` is `pos_of_} + 1`, which exceeds `endPosition`, so `Contains` returns false and the if-statement is silently dropped from the collected set. The statements inside the if-body's nested block ARE found (their `SpanStart` is contained and their parent is a `BlockSyntax`), but they share a different parent block from any outer-block statements. The caller `FindEnclosingMethodAndStatements` then fires the "All selected statements must be in the same block scope" guard — a false-negative rejection. Confirmed live at line 415; single reference from line 124. The backlog-row anchor cited a sibling-repo path (`NetworkDocumentation.Parsers`) — the relevant in-repo anchor is `src/RoslynMcp.Roslyn/Services/ExtractMethodService.cs:415-425`. |
| Approach | (1) In `src/RoslynMcp.Roslyn/Services/ExtractMethodService.cs`, change `FindStatementsInSelection` (line 419-422) to filter statements by start-anchor: replace `selectionSpan.Contains(s.Span)` with `s.SpanStart >= selectionSpan.Start && s.SpanStart < selectionSpan.End`. The existing `s.Parent is BlockSyntax` guard is correct and must be preserved. (2) Add a test method to `tests/RoslynMcp.Tests/ExtractMethodTests.cs` covering the gh #744 repro: add a single-statement if-block fixture in `samples/SampleSolution/SampleLib/RefactoringProbe.cs`, then assert that `PreviewExtractMethodAsync` succeeds (non-null token + diff containing the new method name) when the selection spans that if-block with `endColumn` pointing to the closing brace. Mirror existing tests using `workspace.GetPath("SampleLib", "RefactoringProbe.cs")`. |
| Scope | Production files: 2 — `src/RoslynMcp.Roslyn/Services/ExtractMethodService.cs`, `samples/SampleSolution/SampleLib/RefactoringProbe.cs`. Test files: 1 — `tests/RoslynMcp.Tests/ExtractMethodTests.cs` (extend). All within Rules 3 and 4. |
| Tool policy | edit-only |
| Estimated context cost | 30000 |
| Risks | (1) Start-anchor filter broadens collection — verify multi-statement outer-block selections still work and selections ending mid-statement don't spuriously include statements that start after selection end (`SpanStart < selectionSpan.End` prevents this). (2) Confirm nested-block selections still collect from the nested block and pass the same-parent check. (3) New fixture in `RefactoringProbe.cs` may shift line numbers — append after class closing brace (line 43) rather than mid-file. (4) Fanout probe: `FindStatementsInSelection` has 1 reference (same file). No cross-file impact. |
| Validation | (1) `mcp__roslyn__compile_check` after edit — zero new errors. (2) `dotnet test --filter "ClassName=ExtractMethodTests"` — all existing tests pass; new if-block test passes. (3) Manual repro from gh #744 via `extract_method_preview`. (4) `./eng/verify-release.ps1 -Configuration Release` CI gate. |
| Performance review | N/A — correctness fix, no hot-path changes. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed `extract_method_preview` rejecting valid single-statement if-block selections with "All selected statements must be in the same block scope" when `endColumn` landed on or adjacent to the closing brace. The statement collector now anchors on statement start position rather than requiring the full span to fall within selection bounds (gh #744). |
| Backlog sync | Close rows: [`extract-method-preview-same-block-scope-false-negative`]. |

### 2. surface-test-teardown-directory-survives-windows-lock

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `surface-test-teardown-directory-survives-windows-lock` |
| Source | gh #745 (P1 — `networkdocumentation` audit; operational risk) |
| Diagnosis | Phase 6z of `skills/mcp-server-surface-test/prompts/phases/apply-and-test.md` (lines 99–109) runs `dotnet build-server shutdown` as Step 1 of teardown — this releases `VBCSCompiler.exe` / `testhost.exe` locks on `bin/{Debug,Release}/net*/` but does NOT release locks held by the running `roslynmcp.exe` host process on analyzer DLLs (`RoslynMcp.Analyzers.dll`). The disposable worktree directory then survives `git worktree remove --force` because Windows retains the file handle. The fix mechanism is already shipped: `workspace_close(workspaceId, drainProcesses=true)` at `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs:100-149` closes the MCP workspace session AND runs `dotnet build-server shutdown` as one operation. Phase 6z does not call it. Stale summary in `skills/mcp-server-surface-test/prompts/full.md:27` also omits `workspace_close`. Direct evidence in `ai_docs/audit-reports/20260508T154415Z_roslyn-backed-mcp_mcp-server-audit.md:171`. |
| Approach | (1) In `skills/mcp-server-surface-test/prompts/phases/apply-and-test.md`, replace Phase 6z step 1 with: (a) call `workspace_close(workspaceId: <disposable-worktree-workspace-id>, drainProcesses: true)` first to release the MCP host's analyzer DLL lock + build-server locks atomically; (b) run `dotnet build-server shutdown` as belt-and-braces for out-of-band processes. Mirror the wording from `reconcile-backlog-sweep-plan/SKILL.md`'s *Worktree teardown discipline (Windows)* callout. Guard the new step on the `--no-worktree` mode. (2) In `skills/mcp-server-surface-test/prompts/full.md` line 27, update the summary to mention `workspace_close(drainProcesses=true)` before `dotnet build-server shutdown` and `git worktree remove --force`. No C# changes. |
| Scope | Production files touched: 2 (both shipped skill prompt files: `skills/mcp-server-surface-test/prompts/phases/apply-and-test.md`, `skills/mcp-server-surface-test/prompts/full.md`). Test files: 0. Within Rule 3 (≤4 files). The `verify-skills-are-generic.ps1` checker scans `SKILL.md` only, not phase prompts. |
| Tool policy | edit-only |
| Estimated context cost | 20000 |
| Risks | (1) `--no-worktree` mode: no workspace was loaded from a worktree, so `workspace_close` with a worktree workspaceId would return NotFound. Extend the existing `--no-worktree` gate to skip the new step. (2) If Phase 17e already closed the disposable workspace, the workspace_close call should be conditional: "if disposable workspace still open, drain; otherwise step (b) standalone shutdown still applies." (3) `verify-ai-docs.ps1` checker passes (no banned patterns in phase prompts, only SKILL.md). |
| Validation | (1) Read updated Phase 6z + full.md line 27 — confirm both reference `workspace_close(drainProcesses=true)`. (2) `pwsh -NoProfile -File eng/verify-ai-docs.ps1` passes. (3) Manual repro: reproduce gh #745 on Windows 11 — run `/mcp-server-surface-test --full`, execute updated Phase 6z, verify `git worktree list` no longer shows the disposable worktree. (4) No `dotnet test` required (doc-only change). |
| Performance review | N/A — correctness fix, no hot-path changes. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed `mcp-server-surface-test` Phase 6z teardown leaving the disposable worktree directory undeletable on Windows 11. The teardown sequence now calls `workspace_close(drainProcesses=true)` before `git worktree remove --force`, releasing the MCP host's analyzer DLL lock that `dotnet build-server shutdown` alone does not cover. Closes gh #745. |
| Backlog sync | Close rows: [`surface-test-teardown-directory-survives-windows-lock`]. |

### 3. symbol-relationships-builtin-type-unbounded-enumeration

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `symbol-relationships-builtin-type-unbounded-enumeration` |
| Source | gh #757 (P1 — `tradewise` audit) |
| Diagnosis | In `src/RoslynMcp.Roslyn/Services/SymbolRelationshipService.cs:132-168`, `GetSymbolRelationshipsAsync` calls `PromoteToDeclaringMemberIfRequestedAsync` (line 143) which short-circuits when `preferDeclaringMember=false` (guard at line 326). When the cursor lands on a builtin-type token like `void`, Roslyn resolves it to `System.Void` — an `INamedTypeSymbol` with `SpecialType != SpecialType.None` and no source locations. The code falls through to `_referenceService.FindReferencesAsync` (line 156) on `System.Void`, which enumerates every void-returning method reference solution-wide (57.7 KB measured on TradeWise's 11-project / 759-document solution). The `preferDeclaringMember=true` branch auto-promotes to the enclosing method and returns only 9 refs. Fix point: after line 143 promotion, if `preferDeclaringMember=false` AND the post-promotion symbol is a builtin (`SpecialType != None`), return early sentinel with empty buckets + hint. Note: backlog-row anchor `src/TradeWise.Infrastructure/Persistence/AlertRuleRepository.cs:269` is in sibling TradeWise repo, not this repo — fix is entirely in Roslyn-Backed-MCP. |
| Approach | (1) Add a nullable `string? Hint` property to `src/RoslynMcp.Core/Models/SymbolRelationshipsDto.cs`. (2) In `src/RoslynMcp.Roslyn/Services/SymbolRelationshipService.cs:GetSymbolRelationshipsAsync`, after line 143 add: `if (!preferDeclaringMember && symbol is INamedTypeSymbol namedSym && namedSym.SpecialType != SpecialType.None) return new SymbolRelationshipsDto(Symbol: SymbolMapper.ToDto(symbol, solution), Definitions: [], References: [], Implementations: [], BaseMembers: [], Overrides: [], Hint: "Resolved to builtin type — references list suppressed. Set preferDeclaringMember=true or relocate cursor to a non-builtin token.");`. (3) In `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs:GetSymbolRelationships` wrapper (~line 495), add `hint = result.Hint` to the anonymous serialization object (wrapper enumerates fields individually; new field won't auto-serialize). (4) New test file `tests/RoslynMcp.Tests/SymbolRelationshipsBuiltinTypeSuppressionTests.cs` with 2 fixtures: builtin-suppression and regression-guard for `preferDeclaringMember=true`. |
| Scope | Production files touched: 3 — `src/RoslynMcp.Core/Models/SymbolRelationshipsDto.cs`, `src/RoslynMcp.Roslyn/Services/SymbolRelationshipService.cs`, `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`. Test files added: 1. Within Rule 3 (≤4) and Rule 4 (≤3). |
| Tool policy | edit-only |
| Estimated context cost | 32000 |
| Risks | (1) `SymbolRelationshipsDto` is a positional record — adding a field changes the constructor signature. Use named-argument construction to avoid breaking existing call sites (currently only line 162-168). (2) `SpecialType != None` covers C# builtin keywords but not non-special BCL types like `System.Console` — those would still enumerate. The broader `ContainingAssembly == System.Private.CoreLib` guard is a potential follow-on. (3) Existing `IntegrationTests.SymbolNavigation.cs` exercises `GetSymbolRelationshipsAsync` indirectly — verify it still passes. (4) Anchor in backlog row is sibling-repo path; synthesize an inline void-return method in test fixture instead. |
| Validation | (1) `mcp__roslyn__compile_check` passes on all 3 edited files. (2) New `SymbolRelationshipsBuiltinTypeSuppressionTests` passes: `preferDeclaringMember=false` on `void` → empty refs + non-null Hint; `preferDeclaringMember=true` → expected non-empty refs. (3) `IntegrationTests.SymbolNavigation.cs` tests still pass. (4) `dotnet build RoslynMcp.slnx -c Release -p:TreatWarningsAsErrors=true` passes. |
| Performance review | N/A — correctness fix. The guard adds one `SpecialType` property read before async work; negligible. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed `symbol_relationships` returning a 57+ KB payload overflow when `preferDeclaringMember=false` and the cursor lands on a builtin-type token (e.g. `void`, `int`). The tool now detects builtin-type resolution (`SpecialType != None`) and returns an empty relationship envelope with a `hint` field explaining the suppression, rather than enumerating all solution-wide references to the builtin. The `preferDeclaringMember=true` auto-promotion path is unaffected. Closes gh #757. |
| Backlog sync | Close rows: [`symbol-relationships-builtin-type-unbounded-enumeration`]. |

### 4. get-coupling-metrics-no-summary-mode

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `get-coupling-metrics-no-summary-mode` |
| Source | gh #763 (P1 — `firewallanalyzer` audit) |
| Diagnosis | `get_coupling_metrics` at `src/RoslynMcp.Host.Stdio/Tools/CouplingAnalysisTools.cs:16-33` returns every type's full `CouplingMetricsDto` record unconditionally. On an 11-project solution the payload hit 62 KB, exceeding the MCP token cap (gh #763). No `summary` parameter exists. Service layer (`src/RoslynMcp.Roslyn/Services/CouplingAnalysisService.cs:29-81`) and interface (`src/RoslynMcp.Core/Services/ICouplingAnalysisService.cs:21`) return `IReadOnlyList<CouplingMetricsDto>` and are unchanged — aggregation lives entirely in the wrapper. Exact precedent: `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs:77-102` implements `project_diagnostics` summary branch as a GroupBy+select inside the tool method without touching the service. |
| Approach | Modify `GetCouplingMetrics` in `src/RoslynMcp.Host.Stdio/Tools/CouplingAnalysisTools.cs` only. Add `bool summary = false` parameter with description "When true, return per-project rollup counts (typeCount, avgInstability, stableCount, balancedCount, unstableCount, isolatedCount) without per-type detail rows. 10-100x smaller payload on multi-project solutions." When `summary=true`, group `results` by `ProjectName`, project each group to a rollup, serialize `{ summary = true, projectCount, totalTypes, projects }`. When `summary=false`, preserve current `{ count, metrics }` shape verbatim. Mirror branch structure from `AnalysisTools.cs:77-102`. No service-layer changes. |
| Scope | Production files: 1 — `src/RoslynMcp.Host.Stdio/Tools/CouplingAnalysisTools.cs`. Test files: 1 extended — `tests/RoslynMcp.Tests/CouplingAnalysisTests.cs` (add `GetCouplingMetrics_SummaryMode_ReturnsPerProjectRollup`). Rule 3 exemption: tool-surface-only, 1 file (within 2-file cap). |
| Tool policy | edit-only |
| Estimated context cost | 22000 |
| Risks | New summary shape advertised in description attribute. Verify `summary=false` (existing response) is bit-for-bit identical to current output. `projectName` filter combined with `summary=true` should still group (degenerate: single project). |
| Validation | (1) `mcp__roslyn__compile_check` on `RoslynMcp.Host.Stdio`. (2) Extend `CouplingAnalysisTests` with summary-mode test using `SharedWorkspaceTestBase` — assert `summary=true` in response JSON, projectCount matches per-project entry count, no `metrics` array. (3) Existing four `GetCouplingMetrics_*` tests remain green. (4) Manual: call `get_coupling_metrics(workspaceId, excludeTestProjects=true, limit=100, summary=true)` on Roslyn-Backed-MCP solution; payload under 10 KB. |
| Performance review | N/A — correctness fix, no hot-path changes. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | `get_coupling_metrics` — add `summary=true` mode returning per-project rollup counts (typeCount, avgInstability, classification buckets) without per-type detail rows. Resolves MCP token-cap overflow on 10+ project solutions (gh #763). |
| Backlog sync | Close rows: [`get-coupling-metrics-no-summary-mode`]. |

### 5. validate-workspace-runtests-total-zero

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `validate-workspace-runtests-total-zero` |
| Source | gh #764 (P1 — `firewallanalyzer` audit) |
| Diagnosis | Root cause in `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs:320-334` (`ComputeOverallStatus`). When `runTests=true` with non-empty filter, `ValidateInternalAsync` calls `TestRunnerService.RunTestsAsync` (line 188-192). If `dotnet test` fails to match any tests (working-directory or IChangeTracker-timing issue), subprocess returns exit-0 with `total=0, failed=0`. `ComputeOverallStatus` then returns `"clean"` because no branch fires: `ErrorCount == 0`, no analyzer errors, `testRunResult.Failed == 0` (line 331). The `total=0` signal is completely ignored; no guard for zero-run against non-empty filter. Confirmed in gh #764 repro: auto-derived 26-FQDN filter returned total=0; standalone `test_run` with same filter returned 36 passes. |
| Approach | (1) In `ComputeOverallStatus` (`src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs:320-334`): add guard — when `runTests && testRunResult is not null && testRunResult.Total == 0` — return new status `"test-zero-run"` instead of `"clean"`. (2) In `ValidateInternalAsync` (same file, line 182-216): when `runTests=true` AND `testRunResult.Total == 0` AND `related.DotnetTestFilter` non-empty, append warning to response: `"validate_workspace: runTests=true produced testRunResult.total=0 with filter '<filter>'; this likely indicates filter resolution failure (working-directory or IChangeTracker timing). Run test_run with the same filter to confirm."` Append to `warnings` before constructing `WorkspaceValidationDto`. (3) Extend existing `tests/RoslynMcp.Tests/WorkspaceValidationOverallStatusTests.cs` with `ComputeOverallStatus_RunTestsTrue_TotalZero_NonEmptyFilter_YieldsTestZeroRun` covering the new status. |
| Scope | Production files touched: 1 (`src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs`). Test files modified: 1 (`tests/RoslynMcp.Tests/WorkspaceValidationOverallStatusTests.cs` extended). |
| Tool policy | edit-only |
| Estimated context cost | 28000 |
| Risks | (1) `"test-zero-run"` is a new status string — callers exact-matching `"clean"` will see different value. Intentional breaking behavior (old value was incorrect); flag in CHANGELOG. (2) gh #764 notes the root cause is "likely a race between IChangeTracker file-list refresh and dotnet test child process working dir" — this fix surfaces the symptom but doesn't eliminate the race. May need a follow-on row. (3) Empty `DotnetTestFilter` already skips test-run (existing guard line 183) — new guard only fires when filter genuinely produced but `dotnet test` returned zero. |
| Validation | (1) Unit test added to `WorkspaceValidationOverallStatusTests.cs`: `TestRunResultDto(Total=0, Failed=0, Passed=0)` + `runTests=true` → status `"test-zero-run"`. (2) Existing `ComputeOverallStatus` tests still pass (no regressions on `total>0` paths). (3) `mcp__roslyn__compile_check` — zero CS errors. (4) `dotnet test --filter "WorkspaceValidationOverallStatus"` passes. (5) Manual: call `validate_workspace(runTests=true, changedFilePaths=[<file>])` against live workspace; verify no longer `overallStatus=clean` when total=0. |
| Performance review | N/A — correctness fix, no hot-path changes. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed `validate_workspace(runTests=true)` falsely reporting `overallStatus=clean` when `testRunResult.total=0` despite a non-empty discovered test filter. Status now returns `test-zero-run` and the response includes a diagnostic warning identifying the likely filter-resolution failure. Breaking: callers exact-matching `overallStatus="clean"` need to handle `"test-zero-run"` as a non-passing verdict. (Fixes gh #764) |
| Backlog sync | Close rows: [`validate-workspace-runtests-total-zero`]. |

### 6. extract-interface-cross-project-uncompilable

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `extract-interface-cross-project-uncompilable` |
| Source | gh #765 (P1 — `firewallanalyzer` audit) |
| Diagnosis | _Pending deepener._ |
| Approach | _Pending deepener._ |
| Scope | _Pending deepener._ |
| Tool policy | _Pending deepener._ |
| Estimated context cost | _Pending deepener._ |
| Risks | _Pending deepener._ |
| Validation | _Pending deepener._ |
| Performance review | _Pending deepener._ |
| CHANGELOG category | _Pending deepener._ |
| CHANGELOG entry (draft) | _Pending deepener._ |
| Backlog sync | Close rows: [`extract-interface-cross-project-uncompilable`]. |

### 7. split-service-with-di-broken-output

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `split-service-with-di-broken-output` |
| Source | gh #766 (P1 — `firewallanalyzer` audit; refactor tool emits non-functional code) |
| Diagnosis | _Pending deepener._ |
| Approach | _Pending deepener._ |
| Scope | _Pending deepener._ |
| Tool policy | _Pending deepener._ |
| Estimated context cost | _Pending deepener._ |
| Risks | _Pending deepener._ |
| Validation | _Pending deepener._ |
| Performance review | _Pending deepener._ |
| CHANGELOG category | _Pending deepener._ |
| CHANGELOG entry (draft) | _Pending deepener._ |
| Backlog sync | Close rows: [`split-service-with-di-broken-output`]. |

### 8. preview-token-stale-across-auto-reload

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `preview-token-stale-across-auto-reload` |
| Source | gh #767 (P1 — `firewallanalyzer` audit) |
| Diagnosis | _Pending deepener._ |
| Approach | _Pending deepener._ |
| Scope | _Pending deepener._ |
| Tool policy | _Pending deepener._ |
| Estimated context cost | _Pending deepener._ |
| Risks | _Pending deepener._ |
| Validation | _Pending deepener._ |
| Performance review | _Pending deepener._ |
| CHANGELOG category | _Pending deepener._ |
| CHANGELOG entry (draft) | _Pending deepener._ |
| Backlog sync | Close rows: [`preview-token-stale-across-auto-reload`]. |

### 9. set-editorconfig-option-duplicate-key-append

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `set-editorconfig-option-duplicate-key-append` |
| Source | gh #735 (P2 — `roslyn-backed-mcp` self-audit) |
| Diagnosis | _Pending deepener._ |
| Approach | _Pending deepener._ |
| Scope | _Pending deepener._ |
| Tool policy | _Pending deepener._ |
| Estimated context cost | _Pending deepener._ |
| Risks | _Pending deepener._ |
| Validation | _Pending deepener._ |
| Performance review | _Pending deepener._ |
| CHANGELOG category | _Pending deepener._ |
| CHANGELOG entry (draft) | _Pending deepener._ |
| Backlog sync | Close rows: [`set-editorconfig-option-duplicate-key-append`]. |

### 10. member-hierarchy-overrides-mislabels-sibling-interface-impls

**Bundle candidate with initiative 11.** Deepener should verify Rule 1 four-conditions test.

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `member-hierarchy-overrides-mislabels-sibling-interface-impls` |
| Source | gh #736 (P2 — `roslyn-backed-mcp` self-audit) |
| Diagnosis | _Pending deepener._ |
| Approach | _Pending deepener._ |
| Scope | _Pending deepener._ |
| Tool policy | _Pending deepener._ |
| Estimated context cost | _Pending deepener._ |
| Risks | _Pending deepener._ |
| Validation | _Pending deepener._ |
| Performance review | _Pending deepener._ |
| CHANGELOG category | _Pending deepener._ |
| CHANGELOG entry (draft) | _Pending deepener._ |
| Backlog sync | Close rows: [`member-hierarchy-overrides-mislabels-sibling-interface-impls`]. |

### 11. find-overrides-vs-member-hierarchy-cross-tool-inconsistency

**Bundle candidate with initiative 10.** Deepener should verify Rule 1 four-conditions test.

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `find-overrides-vs-member-hierarchy-cross-tool-inconsistency` |
| Source | gh #737 (P2 — `roslyn-backed-mcp` self-audit) |
| Diagnosis | _Pending deepener._ |
| Approach | _Pending deepener._ |
| Scope | _Pending deepener._ |
| Tool policy | _Pending deepener._ |
| Estimated context cost | _Pending deepener._ |
| Risks | _Pending deepener._ |
| Validation | _Pending deepener._ |
| Performance review | _Pending deepener._ |
| CHANGELOG category | _Pending deepener._ |
| CHANGELOG entry (draft) | _Pending deepener._ |
| Backlog sync | Close rows: [`find-overrides-vs-member-hierarchy-cross-tool-inconsistency`]. |

### 12. project-diagnostics-totaldiagnostics-collapses-under-severity-filter

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `project-diagnostics-totaldiagnostics-collapses-under-severity-filter` |
| Source | gh #746 (P2 — `networkdocumentation` audit) |
| Diagnosis | _Pending deepener._ |
| Approach | _Pending deepener._ |
| Scope | _Pending deepener._ |
| Tool policy | _Pending deepener._ |
| Estimated context cost | _Pending deepener._ |
| Risks | _Pending deepener._ |
| Validation | _Pending deepener._ |
| Performance review | _Pending deepener._ |
| CHANGELOG category | _Pending deepener._ |
| CHANGELOG entry (draft) | _Pending deepener._ |
| Backlog sync | Close rows: [`project-diagnostics-totaldiagnostics-collapses-under-severity-filter`]. |

### 13. symbol-signature-help-returns-bare-null-for-resolvable-method-metadata

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `symbol-signature-help-returns-bare-null-for-resolvable-method-metadata` |
| Source | gh #747 (P2 — `networkdocumentation` audit) |
| Diagnosis | _Pending deepener._ |
| Approach | _Pending deepener._ |
| Scope | _Pending deepener._ |
| Tool policy | _Pending deepener._ |
| Estimated context cost | _Pending deepener._ |
| Risks | _Pending deepener._ |
| Validation | _Pending deepener._ |
| Performance review | _Pending deepener._ |
| CHANGELOG category | _Pending deepener._ |
| CHANGELOG entry (draft) | _Pending deepener._ |
| Backlog sync | Close rows: [`symbol-signature-help-returns-bare-null-for-resolvable-method-metadata`]. |

### 14. extract-interface-preview-duplicate-interface-when-already-implements

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `extract-interface-preview-duplicate-interface-when-already-implements` |
| Source | gh #748 (P2 — `networkdocumentation` audit) |
| Diagnosis | _Pending deepener._ |
| Approach | _Pending deepener._ |
| Scope | _Pending deepener._ |
| Tool policy | _Pending deepener._ |
| Estimated context cost | _Pending deepener._ |
| Risks | _Pending deepener._ |
| Validation | _Pending deepener._ |
| Performance review | _Pending deepener._ |
| CHANGELOG category | _Pending deepener._ |
| CHANGELOG entry (draft) | _Pending deepener._ |
| Backlog sync | Close rows: [`extract-interface-preview-duplicate-interface-when-already-implements`]. |

### 15. change-type-namespace-preview-omits-consumer-using-additions

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `change-type-namespace-preview-omits-consumer-using-additions` |
| Source | gh #749 (P2 — `networkdocumentation` audit) |
| Diagnosis | _Pending deepener._ |
| Approach | _Pending deepener._ |
| Scope | _Pending deepener._ |
| Tool policy | _Pending deepener._ |
| Estimated context cost | _Pending deepener._ |
| Risks | _Pending deepener._ |
| Validation | _Pending deepener._ |
| Performance review | _Pending deepener._ |
| CHANGELOG category | _Pending deepener._ |
| CHANGELOG entry (draft) | _Pending deepener._ |
| Backlog sync | Close rows: [`change-type-namespace-preview-omits-consumer-using-additions`]. |

### 16. symbol-refactor-preview-empty-appliedfiles-on-success

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `symbol-refactor-preview-empty-appliedfiles-on-success` |
| Source | gh #750 (P2 — `networkdocumentation` audit) |
| Diagnosis | _Pending deepener._ |
| Approach | _Pending deepener._ |
| Scope | _Pending deepener._ |
| Tool policy | _Pending deepener._ |
| Estimated context cost | _Pending deepener._ |
| Risks | _Pending deepener._ |
| Validation | _Pending deepener._ |
| Performance review | _Pending deepener._ |
| CHANGELOG category | _Pending deepener._ |
| CHANGELOG entry (draft) | _Pending deepener._ |
| Backlog sync | Close rows: [`symbol-refactor-preview-empty-appliedfiles-on-success`]. |

### 17. test-run-fqdn-drift-vs-test-discover

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `test-run-fqdn-drift-vs-test-discover` |
| Source | gh #752 (P2 — `networkdocumentation` audit) |
| Diagnosis | _Pending deepener._ |
| Approach | _Pending deepener._ |
| Scope | _Pending deepener._ |
| Tool policy | _Pending deepener._ |
| Estimated context cost | _Pending deepener._ |
| Risks | _Pending deepener._ |
| Validation | _Pending deepener._ |
| Performance review | _Pending deepener._ |
| CHANGELOG category | _Pending deepener._ |
| CHANGELOG entry (draft) | _Pending deepener._ |
| Backlog sync | Close rows: [`test-run-fqdn-drift-vs-test-discover`]. |

### 18. find-overrides-payload-overflow-on-corlib-virtual

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `find-overrides-payload-overflow-on-corlib-virtual` |
| Source | gh #754 (P2 — `networkdocumentation` audit) |
| Diagnosis | _Pending deepener._ |
| Approach | _Pending deepener._ |
| Scope | _Pending deepener._ |
| Tool policy | _Pending deepener._ |
| Estimated context cost | _Pending deepener._ |
| Risks | _Pending deepener._ |
| Validation | _Pending deepener._ |
| Performance review | _Pending deepener._ |
| CHANGELOG category | _Pending deepener._ |
| CHANGELOG entry (draft) | _Pending deepener._ |
| Backlog sync | Close rows: [`find-overrides-payload-overflow-on-corlib-virtual`]. |

### 19. analyze-dependencies-prompt-payload-overflow

**Bundle candidate with initiative 20.** Deepener should verify Rule 1 four-conditions test.

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `analyze-dependencies-prompt-payload-overflow` |
| Source | gh #755 (P2 — `networkdocumentation` audit) |
| Diagnosis | _Pending deepener._ |
| Approach | _Pending deepener._ |
| Scope | _Pending deepener._ |
| Tool policy | _Pending deepener._ |
| Estimated context cost | _Pending deepener._ |
| Risks | _Pending deepener._ |
| Validation | _Pending deepener._ |
| Performance review | _Pending deepener._ |
| CHANGELOG category | _Pending deepener._ |
| CHANGELOG entry (draft) | _Pending deepener._ |
| Backlog sync | Close rows: [`analyze-dependencies-prompt-payload-overflow`]. |

### 20. review-test-coverage-prompt-payload-overflow

**Bundle candidate with initiative 19.** Deepener should verify Rule 1 four-conditions test.

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `review-test-coverage-prompt-payload-overflow` |
| Source | gh #756 (P2 — `networkdocumentation` audit) |
| Diagnosis | _Pending deepener._ |
| Approach | _Pending deepener._ |
| Scope | _Pending deepener._ |
| Tool policy | _Pending deepener._ |
| Estimated context cost | _Pending deepener._ |
| Risks | _Pending deepener._ |
| Validation | _Pending deepener._ |
| Performance review | _Pending deepener._ |
| CHANGELOG category | _Pending deepener._ |
| CHANGELOG entry (draft) | _Pending deepener._ |
| Backlog sync | Close rows: [`review-test-coverage-prompt-payload-overflow`]. |
