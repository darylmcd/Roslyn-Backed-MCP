# Post-2026-04-20-audit follow-up plan

**created_at:** 2026-04-21T12:36:58Z
**author:** Comprehensive refactor audit, 2026-04-20 session (PR #301 + #302)
**closes (when fully shipped):** backlog rows `tools-dispatch-shim-boilerplate-duplication` (P3) and `top10-complexity-hotspots-not-yet-extracted` (P3)

This plan sequences two independent workstreams surfaced by the 2026-04-20 audit. Each workstream is gated by per-session caps from `ai_docs/prompts/deep-review-and-refactor.md` (5 findings, ≤2 high-risk).

---

## Workstream 1 — `tools-dispatch-shim-boilerplate-duplication` via source generator

### Problem

Every `*_apply` and `*_preview` MCP tool in `src/RoslynMcp.Host.Stdio/Tools/*Tools.cs` is a 7-line dispatcher that does:

1. Resolve `workspaceId` from preview token via `IPreviewStore.PeekWorkspaceId(token)`
2. Throw `KeyNotFoundException` if missing
3. Call `gate.RunWriteAsync(wsId, async c => JsonSerializer.Serialize(await service.MethodAsync(token, c), JsonDefaults.Indented), ct)`

`find_duplicated_methods` clusters: 12 `Apply*` (hash `ece53a0446dc8122`), 10 `Preview*` (hash `5145812bbea83a68`), and several smaller siblings — ~22 byte-identical bodies, ~200 LOC of pure boilerplate.

### Why a generator (not a hand-extracted helper)

Hand-extracted `ToolDispatch.ApplyAsync` would save ~200 LOC and is the simpler win, but it does **not** address the structural conflict observed in `server-surface-catalog-append-conflict-hotspot` (P4): every new tool still requires hand-edits to `*Tools.cs` AND `ServerSurfaceCatalog.cs`. The generator eliminates BOTH hand-maintenance points by treating the service interfaces as the source of truth.

### Architecture

Two new MSBuild artifacts:

```
analyzers/
├── ServerSurfaceCatalogAnalyzer/         (existing — RMCP001/RMCP002 parity)
└── McpToolShimGenerator/                 (NEW)
    ├── McpToolShimGenerator.csproj       (netstandard2.0, IIncrementalGenerator)
    ├── ToolMethodModel.cs                (record: name, ServiceInterface, MethodName, IsApply, attribs)
    ├── ServiceInterfaceCollector.cs      (ForAttributeWithMetadataName syntax provider)
    ├── ToolShimEmitter.cs                (StringBuilder template for one tool method)
    └── McpToolShimGenerator.cs           (RegisterSourceOutput pipeline glue)
```

The generator is referenced by `RoslynMcp.Host.Stdio.csproj` as a `<ProjectReference OutputItemType="Analyzer" ReferenceOutputAssembly="false" />` (same shape as `ServerSurfaceCatalogAnalyzer` already uses).

### Generator input contract

Annotate each service interface method that should produce a tool with the existing `[McpServerTool]` attribute. Today the attribute lives on the *Tools.cs* method; we **lift it** to the service interface method instead. Example before:

```csharp
// Tools/CodeActionTools.cs (hand-written)
[McpServerTool(Name = "apply_code_action", ReadOnly = false, Destructive = true, ...), Description("…")]
[McpToolMetadata("code-actions", "stable", false, true, "…")]
public static Task<string> ApplyCodeAction(IWorkspaceExecutionGate gate, IRefactoringService refactoringService, IPreviewStore previewStore, string previewToken, CancellationToken ct = default)
{ /* 7 lines */ }
```

After:

```csharp
// Core/Services/IRefactoringService.cs (single source of truth)
public interface IRefactoringService
{
    [McpServerTool(Name = "apply_code_action", ReadOnly = false, Destructive = true, ...)]
    [McpToolMetadata("code-actions", "stable", false, true, "Apply a previously previewed Roslyn code action.")]
    [Description("Apply a previously previewed code action using its preview token")]
    [GeneratedDispatch(Kind = DispatchKind.ApplyByToken)]   // ← NEW marker
    Task<RefactoringResultDto> ApplyRefactoringAsync(string previewToken, CancellationToken ct);
    // …
}
```

Generator emits to `obj/Generated/RoslynMcp.Host.Stdio/McpToolShimGenerator/CodeActionTools.g.cs`:

```csharp
internal static partial class CodeActionTools
{
    [McpServerTool(Name = "apply_code_action", ReadOnly = false, Destructive = true, ...), Description("…")]
    [McpToolMetadata("code-actions", "stable", false, true, "…")]
    public static Task<string> ApplyCodeAction(
        IWorkspaceExecutionGate gate, IRefactoringService service, IPreviewStore previewStore,
        [Description("The preview token returned by preview_code_action")] string previewToken,
        CancellationToken ct = default)
        => ToolDispatch.ApplyByTokenAsync(gate, service, previewStore, previewToken, service.ApplyRefactoringAsync, ct);
}
```

`ToolDispatch.ApplyByTokenAsync` is a hand-written runtime helper (~10 LOC) that the generator delegates into — kept hand-written so reviewers can audit the shared lock/serialization path in one place.

`DispatchKind` enum has at minimum:
- `ApplyByToken` — the 12-method `Apply*` cluster (token → workspaceId → write-gate)
- `PreviewWithWorkspaceId` — the 10-method `Preview*` cluster (workspaceId arg → write-gate → preview)
- `ReadByWorkspaceId` — covers a few more clusters that emerged in this audit (e.g. `GetEditorConfigOptions`, `GetSecurityDiagnostics`)

### Migration phases (each phase = 1 PR)

| Phase | Scope | Risk | Validation gate |
|-------|-------|------|---|
| **1.1** | Create `McpToolShimGenerator` project + `ToolDispatch` runtime helper + `[GeneratedDispatch]` attribute. Wire into `RoslynMcp.Host.Stdio.csproj`. **No tools migrated yet** — generator is a no-op until interfaces are annotated. | low | `dotnet build` clean; new analyzer project visible in `analyzers/`; `roslyn-mcp:nuget-preflight` passes |
| **1.2 (shipped 2026-04-21, partial)** | **Revised scope:** generator emission deferred — `ModelContextProtocol.Analyzers.XmlToDescriptionGenerator` (MCP SDK) emits `public static partial` declarations for every `[McpServerTool]` method, and CS0756 fires when our generator also tries to emit a partial definition. Instead: migrated all 3 `CodeActionTools` methods (`GetCodeActions` + `PreviewCodeAction` + `ApplyCodeAction`) to use `ToolDispatch` inline (7→~5 LOC each; 3 bodies total); lifted the FLAG-6B empty-result hint from the Tool shim into `ICodeActionService.GetCodeActionsAsync` via new `CodeActionListDto`. Serialized JSON wire format preserved byte-identical. `McpToolShimGenerator` stays the phase-1.1 no-op; phase 1.3+ must either coordinate a partial-method opt-out with the MCP SDK maintainers, or emit to a non-`partial` sibling helper class that the Tools method calls through. | medium | full `verify-release.ps1` clean; 779/779 tests pass; catalog unchanged |
| **1.3** | Migrate next 3 groups: `BulkRefactoringTools`, `ExtractMethodTools`, `FixAllTools`. | medium | per-PR full validation |
| **1.4** | Migrate next 4 groups: `InterfaceExtractionTools`, `RefactoringTools`, `TypeExtractionTools`, `TypeMoveTools`. | medium | per-PR full validation |
| **1.5** | Migrate the remaining `Preview*` cluster (10 methods across `EditorConfigTools`, `ExceptionFlowTools`, `FlowAnalysisTools`, `MSBuildTools`, `SuppressionTools`). | medium | per-PR full validation |
| **1.6** | Migrate the smaller clusters (`MutationAnalysisTools`, etc.) and any leftover `*Tools.cs` methods that fit the dispatch shape. | low | per-PR full validation |
| **1.7** | Subsume `ServerSurfaceCatalogAnalyzer`'s parity check into the generator (the analyzer was validating that hand-maintained catalog entries match `[McpServerTool]` attributes; with the generator owning emission, the catalog can also be generated). Either remove the analyzer or simplify it to a one-rule "every interface method with `[GeneratedDispatch]` must also have `[McpServerTool]`" check. | medium | full validation; verify `RMCP001`/`RMCP002` analyzer-rule tests still pass or are migrated |

7 PRs total. Phases 1.2–1.6 can ship in any order after 1.1; 1.7 must be last.

### Counterargument & risk

- **"This couples Core to MCP-framework attributes."** Today `Core` services already use `Description` from `System.ComponentModel`; lifting `McpServerTool` from `ModelContextProtocol.Server` adds one MCP package reference to `RoslynMcp.Core`. **Mitigation:** put the marker `[GeneratedDispatch]` in `RoslynMcp.Core` (we own it); keep `[McpServerTool]` on the interface but accept the dependency — the alternative (a side-table mapping interface methods to tool names) introduces a third source-of-truth and re-creates the parity problem.
- **Source generators can be debugger-hostile.** Roslyn's `EmitCompilerGeneratedFiles` MSBuild property is enabled in `Directory.Build.props` already (used by `ServerSurfaceCatalogAnalyzer` for inspection); same approach works here.
- **Breaks if the MCP SDK changes attribute shape.** Low probability — `McpServerTool` is part of the public stable surface. If it does change, the generator's emitter is one file to update.

### Exit criteria

- `find_duplicated_methods(minLines=12)` returns 0 hits in `src/RoslynMcp.Host.Stdio/Tools/*.cs` for the `Apply*` and `Preview*` clusters.
- `obj/Generated/RoslynMcp.Host.Stdio/McpToolShimGenerator/` contains one `.g.cs` per migrated tool group.
- `server-surface-catalog-append-conflict-hotspot` (P4) closable as a side-effect — adding a new tool now means adding one method to a service interface, no Tools/*.cs touch, no ServerSurfaceCatalog.cs touch.

### Estimated effort

- Phase 1.1: 1 session (~2 hours) — generator + tests.
- Phases 1.2–1.6: 5 sessions, ~30 min each.
- Phase 1.7: 1 session, ~1 hour.
- **Total: ~7 sessions / ~1 working day** if pursued sequentially.

---

## Workstream 2 — `top10-complexity-hotspots-not-yet-extracted` (17 methods)

### Sequencing

2 methods per session per the audit's cap discipline, with two structural exceptions:

- **Session 2.1 is special**: ClassifySite's 12-param signature must become a `RecordFieldClassificationContext` record FIRST; only then is extract-method coherent. Bundle + extract count as one finding (genuinely-coupled per the audit prompt rule 3).
- **Session 2.10 is disposition-only**: two dense-switch methods (`SideEffectClassifier.ClassifyMethod` cc=22 MI=50, `MutationAnalysisService.ClassifyTypeUsageAfterWalk` cc=21 MI=50) get `considered-and-rejected` rationale documented in the backlog, no code change.

### Method-by-method plan

Ranked by `cc / (MI / 100)` (worst first — high cc with low maintainability):

| Session | Method | cc | MI | LOC | Approach | Risk |
|---|---|---|---|---|---|---|
| **2.1** | `RecordFieldAdditionService.ClassifySite` (`Services/RecordFieldAdditionService.cs:218`) | 25 | 32 | 117 | (a) Bundle 12 params into `RecordFieldClassificationContext` record; (b) extract per-construct switch arms (`ObjectCreationExpressionSyntax`, `ImplicitObjectCreationExpressionSyntax`, `WithExpressionSyntax`, `RecursivePatternSyntax`, `DeclarationExpressionSyntax`) into `ClassifyConstructionSite`/`ClassifyDeconstructionSite`/`ClassifyPropertyPatternSite`/`ClassifyWithExpressionSite` helpers. Target: outer cc<10, no helper >cc 8. | medium |
| **2.2** | `TestReferenceMapService.BuildAsync` (`Services/TestReferenceMapService.cs:23`) | 30 | 28 | 161 | Look for phase boundaries (likely "discover test classes → resolve references → build map" stages). Extract 3 helpers. Target: outer cc<15. | medium |
| **2.2** | `ChangeSignatureService.ApplyAddRemoveAsync` (`Services/ChangeSignatureService.cs:235`) | 28 | 28 | 160 | Add vs remove paths likely separable; param-position math likely extractable. Target: outer cc<15. **Test scope:** `ChangeSignature*Tests.cs` must pass unchanged. | medium |
| **2.3** | `CompileCheckService.CheckAsync` (`Services/CompileCheckService.cs:22`) | 28 | 30 | 135 | **Hot-path**: measure timing pre/post via existing performance tests. Diagnostic-collection loop is likely extractable. Target: outer cc<15. **Counterargument check:** if the extracted helpers each carry `Solution`/`Project` snapshots, allocation cost may rise — measure. | medium |
| **2.3** | `UnusedCodeAnalyzer.FindUnusedSymbolsAsync` (`Services/UnusedCodeAnalyzer.cs:53`) | 24 | 33 | 109 | Per-symbol-kind classification logic likely extractable. Target: outer cc<15. | medium |
| **2.4** | `ScaffoldingService.ResolveInterfaceMembersAsync` (`Services/ScaffoldingService.cs:1009`) | 23 | 38 | 79 | Member-shape resolution per kind (method vs property vs event) likely extractable. Target: outer cc<12. | medium |
| **2.4** | `RecordFieldAdditionService.PreviewAdditionAsync` (`Services/RecordFieldAdditionService.cs:39`) | 22 | 30 | 152 | Sister to ClassifySite; may benefit from the same `RecordFieldClassificationContext` from Session 2.1. Sequencing dependency: must land after 2.1. | medium |
| **2.5** | `SymbolRefactorService.PreviewSplitServiceWithDiAsync` (`Services/SymbolRefactorService.cs:162`) | 22 | 32 | 128 | DI-registration discovery + class-split orchestration are likely separable. Target: outer cc<12. | medium |
| **2.5** | `SymbolSearchService.SearchSymbolsAsync` (`Services/SymbolSearchService.cs:37`) | 22 | 38 | 77 | Nesting=5 — kind/namespace/project filter chain extractable. Target: outer cc<12. | medium |
| **2.6** | `CohesionAnalysisService.AnalyzeTypeCohesion` (`Services/CohesionAnalysisService.cs:74`) | 21 | 40 | 66 | LCOM4 cluster computation is a textbook extract candidate. Target: outer cc<10. | low |
| **2.6** | `CohesionAnalysisService.FindSharedMembersAsync` (`Services/CohesionAnalysisService.cs:250`) | 21 | 40 | 68 | Sister to `AnalyzeTypeCohesion`; both can land in the same PR (related; same file; same test scope). | low |
| **2.7** | `DuplicateMethodDetectorService.FindDuplicatedMethodsAsync` (`Services/DuplicateMethodDetectorService.cs:37`) | 21 | 35 | 104 | AST-normalize → bucket-by-hash phases extractable. Target: outer cc<12. | low |
| **2.7** | `ScaffoldingService.BuildMethodTargetInvocationBlock` (`Services/ScaffoldingService.cs:1627`) | 21 | 40 | 66 | Per-parameter-kind invocation-block synthesis extractable. Target: outer cc<12. | low |
| **2.8** | `TestReferenceMapService.DetectMockDriftAsync` (`Services/TestReferenceMapService.cs:198`) | 21 | 38 | 79 | Nesting=6 — mock-vs-real comparison loop extractable. Target: outer cc<12. | medium |
| **2.8** | `UndoService.RevertFromSolutionSnapshotAsync` (`Services/UndoService.cs:165`) | 21 | 32 | 130 | Per-document-revert loop extractable. **Risk:** this is the undo path; characterization tests must run before AND after. | medium |
| **2.9** | `CodeMetricsService.VisitChildForNesting` (`Services/CodeMetricsService.cs:203`) | 21 | 40 | 68 | **Solo session** — recursion-heavy; visitor-pattern split needs careful argument-passing. Estimate 1 PR alone (no second hotspot), but use the saved capacity to add characterization tests for the visitor first. | medium |
| **2.10** | **Disposition-only**: `SideEffectClassifier.ClassifyMethod` (cc=22 MI=50) + `MutationAnalysisService.ClassifyTypeUsageAfterWalk` (cc=21 MI=50). Both are dense switch blocks where MI=50 indicates the high cc is OFFSET by clear structure. **Action:** add inline comments explaining each is intentionally "wide switch over closed kind set"; document the rejection in the backlog row's session log; close the row when this session ships. | — | — | — | low |

10 sessions. 9 active + 1 disposition.

### Per-session protocol

Each non-disposition session follows the audit-prompt's required workflow but scoped to 2 hotspots:

1. `workspace_load` worktree's `RoslynMcp.slnx`.
2. For each hotspot:
   - Read the method top-to-bottom; identify phase boundaries (look for `// 1)` `// 2)` style comments OR natural blank-line separations).
   - `get_complexity_metrics(filePath=<file>)` to record baseline.
   - `extract_method_preview` for each phase; review the diff; `extract_method_apply`. **Fallback:** if `extract_method_apply` hits the known `extract-method-apply-var-redeclaration` (CS0136/CS0841) bug on async multi-local loops, hand-`Edit` with explicit `private static` helpers.
   - `workspace_reload` + `compile_check` clean.
   - `test_related_files(filePaths=[<file>])` → `test_run --filter "<derived>"` clean.
   - `get_complexity_metrics` confirms target cc met.
3. Full `eng/verify-release.ps1` clean.
4. Commit with itemized body (one bullet per hotspot, before/after metrics).
5. Open PR; `/ship` after CI green.

### Counterarguments to recheck per-session

Before extracting each method, ask:

- **"Is the high cc actually defensible?"** If MI ≥ 45, the `cc / MI` rationale weakens. Spot-check by reading the method — if it's a clean wide-switch with no nested logic, document `considered-and-rejected` instead of forcing extraction.
- **"Will extraction obscure data flow?"** If the method has many local-`out` accumulators that need to thread through helpers, the helper signatures may become as bad as ClassifySite's 12 params. In that case, do a parameter-bundle FIRST (the ClassifySite pattern) — count it as the second of the session's 2 hotspots.
- **"Does the test suite cover the touched method?"** `test_related_files` is the canonical check. If coverage is weak, **add characterization tests first** — burn a session on test-only PRs if necessary. The audit prompt explicitly permits this (Rule 9).

### Exit criteria

- `get_complexity_metrics(minComplexity=18)` returns ≤2 results (the two `considered-and-rejected` dense switches).
- All other 15 methods are at cc<20.
- Backlog row `top10-complexity-hotspots-not-yet-extracted` removed (with PR-description trace per the open-work-only contract).

### Estimated effort

- **9 active sessions** × ~1 hour each (the 2026-04-20 audit took ~2 hours and shipped 5 findings; 2-finding focused sessions should be faster).
- **1 disposition session** × ~30 min.
- **Total: ~10 sessions / ~1.5 working days** if pursued sequentially. Can be parallelized if multiple agents run on disjoint files (per the `backlog-sweep-execute.md` Step 2a disjoint-files rule) — for example sessions 2.6 + 2.7 + 2.8 could run as 3 parallel subagents since the touched files don't overlap.

---

## Cross-workstream dependencies

- **Sessions 2.4 (PreviewAdditionAsync) waits on 2.1 (ClassifySite param bundle).** Same file, shared `RecordFieldClassificationContext` record.
- **Workstream 1 phase 1.7 SHOULD wait on Workstream 2 completion** if any of the touched `*Tools.cs` files need shim regeneration after the Core service interfaces are stable. In practice the generator only reads service interface attributes, not the tool method bodies, so this is a soft dependency — Workstream 1 can run in parallel with Workstream 2.
- **No code-touch overlap** between the two workstreams: WS1 touches `analyzers/`, `src/RoslynMcp.Core/Services/I*.cs`, and `src/RoslynMcp.Host.Stdio/Tools/*.cs`; WS2 touches `src/RoslynMcp.Roslyn/Services/*.cs` exclusively. They can be interleaved freely.

## Suggested kickoff order

1. **Workstream 1 phase 1.1** (generator scaffolding) — unblocks all of WS1 and is a one-shot.
2. **Workstream 2 session 2.1** (ClassifySite bundle + extract) — unblocks WS2 session 2.4.
3. After (1) and (2): sessions can run in any order, optionally in parallel via `initiative-executor` subagents.

## Backlog rows after this plan ships

When the last WS1 + WS2 session lands:

- **Remove**: `tools-dispatch-shim-boilerplate-duplication` (P3), `top10-complexity-hotspots-not-yet-extracted` (P3).
- **Possibly close as side-effect**: `server-surface-catalog-append-conflict-hotspot` (P4) — verify by re-running `find_duplicated_methods` and confirming the catalog file is no longer hand-maintained.
- **Bump `updated_at`** in `ai_docs/backlog.md` per the open-work-only contract.

---

## Open questions for the user before kickoff

1. **Workstream 1 phase 1.1 — accept the new `Microsoft.CodeAnalysis.CSharp` analyzer dependency in `analyzers/McpToolShimGenerator/`?** It's already in the repo via `ServerSurfaceCatalogAnalyzer` so this is consistent, but adds another netstandard2.0 project to maintain.
2. **Workstream 1 phase 1.2 — pilot tool group?** I picked `CodeActionTools` (smallest, 2 methods). Acceptable, or pick a different one as the canary?
3. **Workstream 2 — pursue sequentially or parallelize via subagent dispatch?** Sequential is safer (each session's `get_complexity_metrics` informs the next); parallel saves wall-clock at the cost of higher risk if two extractions touch overlapping helpers in the same file (sessions 2.6 + 2.7 both touch `CohesionAnalysisService.cs` and `ScaffoldingService.cs` respectively — fine; sessions 2.2 touches `TestReferenceMapService.cs` while 2.8 touches it again — must be sequenced).
