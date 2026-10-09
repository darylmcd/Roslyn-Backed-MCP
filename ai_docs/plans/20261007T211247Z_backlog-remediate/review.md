# Cold plan review

| Field | Value |
|---|---|
| Plan | D:/Roslyn-Backed-MCP/.worktrees/plan-20261007T211247Z_backlog-remediate/ai_docs/plans/20261007T211247Z_backlog-remediate |
| Cycle | 0 |
| Outcome | passed-with-warnings |
| Findings | block=0; warn=11; info=15 |
| Anchor verification | performed |
| Reviewed population | 26 pending; 3 deferred and 1 obsolete loaded for whole graph; terminal per-initiative admission skipped |

The 26 pending initiatives meet current schema, single-row, token-estimate, production-fanout, route/classifier and dependency admission checks. No Rule 5/5b block remains in this operator-deferred subset. Warnings concern two Scope justification placements, serial overlap scheduling and the reviewer/producer graph-population mismatch. This is planning admission only; no implementation, test execution, or shipping claim is made. All 30 stanzas passed documented Win32 no-follow, reparse, local-namespace, ordinal containment and bounded same-handle UTF-8 reads.

## Findings

| Initiative | Severity | Rule | Evidence |
|---|---|---|---|
| preview-token-lifecycle-evidence | warn | 3 | Scope lists 6 production files but omits the required forcing-shape justification sentence; defect-forced-companion reasoning is in Risks. Move the traced mechanism and observable failure into Scope without shrinking the complete change. |
| preview-token-reason-projection | warn | 3 | Scope lists 8 production files but omits the required forcing-shape justification sentence; defect-forced-companion reasoning is in Risks. Move the traced mechanism and observable failure into Scope without shrinking the complete change. |
| navigation-and-locator-argument-refusals-public-message | info | 3 | Defect-forced-companion holds: SymbolResolver.cs:291, SymbolNavigationService.cs:153 and CompletionService.cs:52 feed unchecked coordinates to Roslyn consumers; shared checked conversion and local public corrections form one coherent change. |
| scaffold-batch-preview-apply-route | info | 3 | Defect-forced-companion holds: BatchTestScaffolder.cs:236 emits a solution token while OrchestrationTools.cs:124-128 peeks only composite ownership and ToolDispatch.cs:398 emits false stale guidance; producer, route and catalog companions are one repair. |
| restore-callers-missing-packages-path | info | 3 | Defect-forced-companion holds: WorkspaceForkApplyService.cs:531 omits packages cache, WorkspaceTools.cs:704-712 collapses mixed/default roots, and RestoreStalenessDetector.cs:405-406 dereferences unguarded JSON shape. Shared planning and root propagation address the traced restore mechanism. |
| plan | warn | C2-graph-disagreement | Whole-loaded-stanza graph contains 45 edges; stored graph has 20. Additional order pairs: 2/25, 5/25, 6/25, 6/7.02, 12/25, 15/25, 7.02/15, 16/25, 17/25, 19/25, 7.02/19, 21/25, 22/25, 7.02/22, 25/26, 8.01/25, 8.02/25, 8.03/25, 8.04/25, 8.05/25, 7.01/25, 7.02/25, 7.02/8.01, 7.02/8.02, 7.01/8.04. Every additional edge involves a deferred initiative. Restricting both graphs to pending initiatives yields identical 20 edges; stored active scheduling is accurate. Reviewer/producer population contract needs alignment. |
| preview-token-lifecycle-evidence | warn | C2-wave-conflict | Consecutive full-plan orders 7.02 (deferred) and 8.01 (pending) overlap: docs/decisions/README.md. Serialize executable conflicts; deferred neighbors remain parked. |
| preview-token-reason-projection | warn | C2-wave-conflict | Consecutive full-plan orders 8.01 (pending) and 8.02 (pending) overlap: docs/decisions/0024-preview-token-terminal-lifecycle.md, tests/RoslynMcp.Tests/ToolDispatchTests.cs. Serialize executable conflicts; deferred neighbors remain parked. |
| preview-token-solution-confirmation | warn | C2-wave-conflict | Consecutive full-plan orders 8.02 (pending) and 8.03 (pending) overlap: tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs. Serialize executable conflicts; deferred neighbors remain parked. |
| preview-token-composite-confirmation | warn | C2-wave-conflict | Consecutive full-plan orders 8.03 (pending) and 8.04 (pending) overlap: tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs. Serialize executable conflicts; deferred neighbors remain parked. |
| preview-token-project-confirmation | warn | C2-wave-conflict | Consecutive full-plan orders 8.04 (pending) and 8.05 (pending) overlap: tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs. Serialize executable conflicts; deferred neighbors remain parked. |
| preview-diff-whitespace-omission | warn | C2-wave-conflict | Consecutive full-plan orders 25 (deferred) and 26 (pending) overlap: src/RoslynMcp.Roslyn/Helpers/DiffGenerator.cs, src/RoslynMcp.Roslyn/Helpers/SolutionDiffHelper.cs, src/RoslynMcp.Roslyn/Services/EditService.cs, src/RoslynMcp.Roslyn/Services/RefactoringService.cs, tests/RoslynMcp.Tests/DiffGeneratorTests.cs, tests/RoslynMcp.Tests/SolutionDiffHelperTests.cs. Serialize executable conflicts; deferred neighbors remain parked. |
| fork-composite-nonconsuming-snapshot | warn | hotspot | Consecutive full-plan initiatives scaffold-batch-preview-apply-route and fork-composite-nonconsuming-snapshot both touch listed hotspots (src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs, src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Orchestration.cs, README.md, src/RoslynMcp.Host.Stdio/README.md; src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs). This pair includes deferred work; keep it parked and re-evaluate on operator re-plan. |
| fork-preview-project-composite-replay | warn | hotspot | Consecutive full-plan initiatives fork-composite-nonconsuming-snapshot and fork-preview-project-composite-replay both touch listed hotspots (src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs; README.md, src/RoslynMcp.Host.Stdio/README.md). This pair includes deferred work; keep it parked and re-evaluate on operator re-plan. |
| scaffold-batch-preview-apply-route | info | C2 | Whole-plan degree 4 without heroic-last; active cached degree 2. Use live generations and explicit dependsOn; parked edges do not authorize terminal execution. |
| restore-callers-missing-packages-path | info | C2 | Whole-plan degree 4 without heroic-last; active cached degree 2. Use live generations and explicit dependsOn; parked edges do not authorize terminal execution. |
| change-signature-class-struct-primary-ctor-add-remove | info | C2 | Whole-plan degree 2 without heroic-last; active cached degree 1. Use live generations and explicit dependsOn; parked edges do not authorize terminal execution. |
| workspace-close-global-build-server-shutdown | info | C2 | Whole-plan degree 7 without heroic-last; active cached degree 5. Use live generations and explicit dependsOn; parked edges do not authorize terminal execution. |
| change-signature-service-refusals-public-message | info | C2 | Whole-plan degree 2 without heroic-last; active cached degree 1. Use live generations and explicit dependsOn; parked edges do not authorize terminal execution. |
| cross-project-public-refusals-echo-input | info | C2 | Whole-plan degree 4 without heroic-last; active cached degree 2. Use live generations and explicit dependsOn; parked edges do not authorize terminal execution. |
| preview-diff-whitespace-omission | info | C2 | Whole-plan degree 2 without heroic-last; active cached degree 1. Use live generations and explicit dependsOn; parked edges do not authorize terminal execution. |
| preview-token-lifecycle-evidence | info | C2 | Whole-plan degree 7 without heroic-last; active cached degree 5. Use live generations and explicit dependsOn; parked edges do not authorize terminal execution. |
| preview-token-reason-projection | info | C2 | Whole-plan degree 9 without heroic-last; active cached degree 7. Use live generations and explicit dependsOn; parked edges do not authorize terminal execution. |
| preview-token-solution-confirmation | info | C2 | Whole-plan degree 6 without heroic-last; active cached degree 5. Use live generations and explicit dependsOn; parked edges do not authorize terminal execution. |
| preview-token-composite-confirmation | info | C2 | Whole-plan degree 5 without heroic-last; active cached degree 3. Use live generations and explicit dependsOn; parked edges do not authorize terminal execution. |
| preview-token-project-confirmation | info | C2 | Whole-plan degree 4 without heroic-last; active cached degree 3. Use live generations and explicit dependsOn; parked edges do not authorize terminal execution. |

## Reviewer conflict graph

Agreement: false. Complete stanza population includes deferred scopes; the active 26-initiative graph agrees exactly with the stored 20-edge graph. Restoring any parked work requires an explicit operator re-plan and fresh full graph/admission review.

```json
{
  "edges": [
    {
      "a": 2,
      "b": 25,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/CodeActionService.cs"
      ]
    },
    {
      "a": 3,
      "b": 11,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs"
      ]
    },
    {
      "a": 5,
      "b": 25,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs"
      ]
    },
    {
      "a": 6,
      "b": 25,
      "sharedFiles": [
        "src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs",
        "src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs",
        "src/RoslynMcp.Roslyn/Services/BatchTestScaffolder.cs",
        "tests/RoslynMcp.Tests/ScaffoldingIntegrationTests.cs",
        "tests/RoslynMcp.Tests/ToolDispatchTests.cs"
      ]
    },
    {
      "a": 6,
      "b": 8.01,
      "sharedFiles": [
        "tests/RoslynMcp.Tests/ToolDispatchTests.cs"
      ]
    },
    {
      "a": 6,
      "b": 8.02,
      "sharedFiles": [
        "src/RoslynMcp.Host.Stdio/Tools/OrchestrationTools.cs",
        "src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs",
        "tests/RoslynMcp.Tests/ToolDispatchTests.cs"
      ]
    },
    {
      "a": 6,
      "b": 7.02,
      "sharedFiles": [
        "README.md",
        "src/RoslynMcp.Host.Stdio/README.md"
      ]
    },
    {
      "a": 12,
      "b": 25,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/FixAllService.cs"
      ]
    },
    {
      "a": 15,
      "b": 19,
      "sharedFiles": [
        "src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs"
      ]
    },
    {
      "a": 15,
      "b": 25,
      "sharedFiles": [
        "tests/RoslynMcp.Tests/Workspace/WorkspaceForkApplyTests.cs"
      ]
    },
    {
      "a": 15,
      "b": 8.02,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs"
      ]
    },
    {
      "a": 15,
      "b": 7.02,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs",
        "tests/RoslynMcp.Tests/Workspace/WorkspaceForkApplyTests.cs"
      ]
    },
    {
      "a": 16,
      "b": 25,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs"
      ]
    },
    {
      "a": 17,
      "b": 21,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/ChangeSignatureService.cs",
        "tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs"
      ]
    },
    {
      "a": 17,
      "b": 25,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/ChangeSignatureAddRemovePreviewBuilder.cs",
        "src/RoslynMcp.Roslyn/Services/ChangeSignatureService.cs",
        "tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs"
      ]
    },
    {
      "a": 19,
      "b": 22,
      "sharedFiles": [
        "docs/decisions/README.md"
      ]
    },
    {
      "a": 19,
      "b": 24,
      "sharedFiles": [
        "tests/RoslynMcp.Tests/AnalyzerShadowLoaderLifecycleTests.cs"
      ]
    },
    {
      "a": 19,
      "b": 25,
      "sharedFiles": [
        "docs/decisions/README.md"
      ]
    },
    {
      "a": 19,
      "b": 8.01,
      "sharedFiles": [
        "docs/decisions/README.md"
      ]
    },
    {
      "a": 19,
      "b": 8.02,
      "sharedFiles": [
        "docs/product-contract.md"
      ]
    },
    {
      "a": 19,
      "b": 7.02,
      "sharedFiles": [
        "docs/decisions/README.md"
      ]
    },
    {
      "a": 21,
      "b": 25,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/ChangeSignatureService.cs",
        "tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs"
      ]
    },
    {
      "a": 22,
      "b": 25,
      "sharedFiles": [
        "docs/decisions/README.md",
        "src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs"
      ]
    },
    {
      "a": 22,
      "b": 8.01,
      "sharedFiles": [
        "docs/decisions/README.md"
      ]
    },
    {
      "a": 22,
      "b": 7.02,
      "sharedFiles": [
        "docs/decisions/README.md"
      ]
    },
    {
      "a": 25,
      "b": 26,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Helpers/DiffGenerator.cs",
        "src/RoslynMcp.Roslyn/Helpers/SolutionDiffHelper.cs",
        "src/RoslynMcp.Roslyn/Services/EditService.cs",
        "src/RoslynMcp.Roslyn/Services/RefactoringService.cs",
        "tests/RoslynMcp.Tests/DiffGeneratorTests.cs",
        "tests/RoslynMcp.Tests/SolutionDiffHelperTests.cs"
      ]
    },
    {
      "a": 25,
      "b": 8.01,
      "sharedFiles": [
        "docs/decisions/README.md",
        "src/RoslynMcp.Core/Services/ICompositePreviewStore.cs",
        "src/RoslynMcp.Core/Services/IProjectMutationPreviewStore.cs",
        "src/RoslynMcp.Roslyn/Contracts/IPreviewStore.cs",
        "src/RoslynMcp.Roslyn/Services/PreviewStore.cs",
        "tests/RoslynMcp.Tests/ApplyUndoWorkflowServiceTests.cs",
        "tests/RoslynMcp.Tests/ApplyWithVerifyCancellationAndScopeTests.cs",
        "tests/RoslynMcp.Tests/BoundedStoreEvictionTests.cs",
        "tests/RoslynMcp.Tests/ExtractionApplyRouteBindingTests.cs",
        "tests/RoslynMcp.Tests/ParameterObjectPreviewTests.cs",
        "tests/RoslynMcp.Tests/PreviewRouteBindingEditingTests.cs",
        "tests/RoslynMcp.Tests/PreviewRouteBindingFileOpsTests.cs",
        "tests/RoslynMcp.Tests/PreviewStoreTests.cs",
        "tests/RoslynMcp.Tests/ToolDispatchTests.cs"
      ]
    },
    {
      "a": 25,
      "b": 8.02,
      "sharedFiles": [
        "src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs",
        "tests/RoslynMcp.Tests/PreviewTokenStaleAcrossAutoReloadTests.cs",
        "tests/RoslynMcp.Tests/ToolDispatchTests.cs"
      ]
    },
    {
      "a": 25,
      "b": 8.03,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/RefactoringService.cs",
        "tests/RoslynMcp.Tests/ApplyUndoWorkflowServiceTests.cs"
      ]
    },
    {
      "a": 25,
      "b": 8.04,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs",
        "src/RoslynMcp.Roslyn/Services/CompositePreviewStore.cs",
        "src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs",
        "tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs",
        "tests/RoslynMcp.Tests/Services/PersistentCompositeStorageTests.cs"
      ]
    },
    {
      "a": 25,
      "b": 8.05,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs",
        "tests/RoslynMcp.Tests/ProjectMutationIntegrationTests.cs"
      ]
    },
    {
      "a": 25,
      "b": 7.01,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/CompositePreviewStore.cs",
        "src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs",
        "tests/RoslynMcp.Tests/Services/PersistentCompositeStorageTests.cs"
      ]
    },
    {
      "a": 25,
      "b": 7.02,
      "sharedFiles": [
        "docs/decisions/README.md",
        "tests/RoslynMcp.Tests/Workspace/WorkspaceForkApplyTests.cs"
      ]
    },
    {
      "a": 26,
      "b": 8.03,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/RefactoringService.cs"
      ]
    },
    {
      "a": 8.01,
      "b": 8.02,
      "sharedFiles": [
        "docs/decisions/0024-preview-token-terminal-lifecycle.md",
        "tests/RoslynMcp.Tests/ToolDispatchTests.cs"
      ]
    },
    {
      "a": 8.01,
      "b": 8.03,
      "sharedFiles": [
        "tests/RoslynMcp.Tests/ApplyUndoWorkflowServiceTests.cs"
      ]
    },
    {
      "a": 8.01,
      "b": 7.02,
      "sharedFiles": [
        "docs/decisions/README.md"
      ]
    },
    {
      "a": 8.02,
      "b": 8.03,
      "sharedFiles": [
        "tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs"
      ]
    },
    {
      "a": 8.02,
      "b": 8.04,
      "sharedFiles": [
        "tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs"
      ]
    },
    {
      "a": 8.02,
      "b": 8.05,
      "sharedFiles": [
        "tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs"
      ]
    },
    {
      "a": 8.02,
      "b": 7.02,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs"
      ]
    },
    {
      "a": 8.03,
      "b": 8.04,
      "sharedFiles": [
        "tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs"
      ]
    },
    {
      "a": 8.03,
      "b": 8.05,
      "sharedFiles": [
        "tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs"
      ]
    },
    {
      "a": 8.04,
      "b": 8.05,
      "sharedFiles": [
        "tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs"
      ]
    },
    {
      "a": 8.04,
      "b": 7.01,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/CompositePreviewStore.cs",
        "src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs",
        "tests/RoslynMcp.Tests/Services/PersistentCompositeStorageTests.cs"
      ]
    }
  ],
  "degrees": {
    "2": 1,
    "3": 1,
    "4": 0,
    "5": 1,
    "6": 4,
    "9": 0,
    "10": 0,
    "11": 1,
    "12": 1,
    "13": 0,
    "14": 0,
    "15": 4,
    "16": 1,
    "17": 2,
    "18": 0,
    "19": 7,
    "20": 0,
    "21": 2,
    "22": 4,
    "23": 0,
    "24": 1,
    "25": 18,
    "26": 2,
    "8.01": 7,
    "8.02": 9,
    "8.03": 6,
    "8.04": 5,
    "8.05": 4,
    "7.01": 2,
    "7.02": 7
  },
  "zeroDegreeInitiatives": [
    4,
    9,
    10,
    13,
    14,
    18,
    20,
    23
  ]
}
```

## Hotspots

| Initiative | Status | Paths |
|---|---|---|
| workspace-lifecycle-argument-refusals-public-message | pending | src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs |
| scaffold-batch-preview-apply-route | pending | src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs; src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Orchestration.cs; README.md; src/RoslynMcp.Host.Stdio/README.md |
| fork-composite-nonconsuming-snapshot | deferred | src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs |
| fork-preview-project-composite-replay | deferred | README.md; src/RoslynMcp.Host.Stdio/README.md |
| workspace-project-alias-lookup | pending | src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs |
| preview-store-explicit-internal-state | deferred | src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs; src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs |

## Stale rows

| Check | Result |
|---|---|
| Every pending backlogRowsClosed id | Present in supplied worktree backlog |
| First three pending anchor sets | SymbolLocator.cs:58; SymbolResolver.cs:286-291; SymbolNavigationService.cs:148-153; CompletionService.cs:29,47-52; WorkspaceManager.cs:1443,1456; WorkspaceSessionLoader.cs:78; WorkspaceExecutionGate.cs:209; PhysicalPathResolver.cs:20-25; ToolErrorHandler.cs:156-178; PublicInvalidOperationException.cs:26-28 resolve |
| Whole dependency graph | Reused assertAcyclicDependsOn directly from authoritative _bsweep-core.mjs; acyclic; no unknown target |
| Classifier guard | Deepen; inline red=6/green=6 corpus, recorded TP=6 FP=0 FN=0, static semantic inputs |
| Editorconfig matcher | Deepen; resolvable item Evidence red=13/green=12 with both baseline metrics and explicit static inputs |
| Lifecycle required implementations | Live Roslyn finds PreviewStore plus seven scoped fake files; composite/project concrete stores inherit BoundedStore operations |
| Token estimates | Pending max 65000 under freshly read 80000 marker; no narrower addenda override |
| Production fanout | Every pending estimate <= exact scoped production paths +2; no fanoutOversize; test-only ripple remains 0 |

## Recommended next step

Move the two complete forcing-shape explanations into their Scope cells through the sanctioned stanza writer, then refresh review coverage. Keep the three operator-deferred initiatives parked. Dispatch one executor at a time because serializeFullCi is true; use live conflict generations and dependency order. Preserve full required validation from CI_POLICY and addenda before landing. Flag the global reviewer/producer population mismatch and recommend one owning-tooling backlog row; align the shared population contract rather than silently narrowing this reviewer rebuild.

Observed source defects are already covered by selected or parked rows: unchecked source-position arithmetic, false preview-reload attribution, erased lifecycle cause and capacity overrun, and broken restore-root propagation/JSON shape guards. Preserve their complete root-cause scope and regression proof.

