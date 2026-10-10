# Cold adversarial plan review

| Field | Value |
|---|---|
| Plan | D:/Roslyn-Backed-MCP/.worktrees/plan-20261007T211247Z_backlog-remediate/ai_docs/plans/20261007T211247Z_backlog-remediate/plan.md |
| Cycle | 4; targeted restore terminal-disposition and whole-plan graph review |
| Outcome | passed-with-warnings |
| Findings | 0 block / 11 warn / 18 info |
| Anchor verification | performed |
| Stanza reads | All 30 exact stanzas: CreateFileW OPEN_REPARSE_POINT directory/file handles; FileAttributeTagInfo/type checks; local-drive final-path normalization; ordinal immediate-parent proof before reading; one same-handle sequence capped at 5121 bytes; every body <=5120 |

Restore is terminal deferred with its complete 12-production/9-test/2-doc scope and truthful 120000 estimate; native Rule 5 admission still refuses the installed 80000 cap. Terminal skipping under the rubric is disposition review only and grants no execution or publication approval. The original ea969292d61a8c633cb77581a587841d36742741 checkpoint is partial source-only work (5 production/4 tests/1 fragment), no PR or full producer/hosted gate; supplied 72 scoped passes were not rerun. The unchanged primary-constructor initiative remains pending at 70000, without new implementation evidence. The complete 30-stanza graph has 65 edges, versus 3 cached edges. Dependencies are acyclic with no unknown IDs. No build, test, restore, source edit, or owned-state mutation occurred; the reviewer-owned ready Roslyn workspace was closed with drainProcesses=false.

## Terminal scope and publication disposition

| Requirement | Current evidence | Disposition |
|---|---|---|
| Restore scope | Exact safely read Scope equals 12 production, 9 tests, 2 docs; own ai_docs/items/restore-callers-missing-packages-path.md is production; fanoutEstimate=12, heroic-last, fanoutOversize=false | Complete proposed scope recorded; implementation incomplete |
| Native admission | restore/complete-graph-admission-5f8.log says: exec-args Rule 5 context cap 80000 exceeded by restore-callers-missing-packages-path=120000 - split or re-plan before execution; state records native exit4 | No exception admission. Retain deferred; live operator re-plan/fresh cold review and lawful complete-scope admission remain required |
| Partial checkpoint | Live retained worktree HEAD ea969292d61a8c633cb77581a587841d36742741; diff from immutable base337fc2cde10eca3f591cc016c146dee7b893e26a lists exactly 10 original paths. prUrl=null; sourceCheckpoints owns same head | Preserve branch/worktree/checkpoint; do not close row or publish partial fork behavior |
| Original restore requirements | Approach explicitly retains platform/null-root identity, uniform vs mixed dependency restores, one total timeout/cancel budget, alternate asset layouts and every JSON-kind access | Must remain in full implementation; no graph-only replacement or compatibility default |
| Physical graph mechanism | Live base WorkspaceForkApplyService128-149 copies/replays documents then restores before loading. CrossProjectRefactoringService195/200 stores reference-changing preview; EnsureProjectReference returns AddProjectReference775. DocumentSetPersistenceService151 calls reference persistence375-485; SolutionDiffHelper67-115 and PreviewStore211-252 omit reference file effects. RefactoringService830-868 rebases changed projects but not added/removed membership | Exposed fork graph loss, omitted reviewed csproj diff/write paths and incomplete shared replay must be repaired together in this row or an independently correct dependency-chained decomposition |
| Shared extraction proof | Live Roslyn PersistAsync refs: RefactoringService356, UndoFileOperations174/245, ClientRootPathValidator558. RebaseModifiedSolutionOntoCurrentAsync ref: RefactoringService402 | Retain source rollback/application and token authority; reusable disk renderer/replay is coherent, not source mutation during fork validation |
| Public contract | ADR0010 presently documents copy/replay under source gate then restore/load outside. Scope amends that existing ADR for preflight/capture/current rebase/physical validation; no DTO/tool/tier change proposed | Preserve public semantics and broader three-family/safety-migration ownership. Any stable breaking redesign still requires ADR/migration controls |

Terminal initiatives are skipped by per-initiative Rule1/3/3b/4/5/5b/classifier checks exactly as the canonical rubric requires. This does not adjudicate their estimates as executable. The terminal restore's former five-file forcing-shape and non-heroic degree findings are no longer applicable and are removed; unchanged initiative findings below are carried verbatim from cycle3. No new block and no oscillation.

## Findings

| Initiative | Severity | Rule | Evidence |
|---|---|---|---|
| preview-token-lifecycle-evidence | warn | C2-wave-conflict | Consecutive orders 7.02 and 8.01 share docs/decisions/README.md; keep the pair serialized. Deferred predecessors retain their disposition; this is whole-graph scheduling evidence. |
| preview-token-reason-projection | warn | C2-wave-conflict | Consecutive orders 8.01 and 8.02 share docs/decisions/0024-preview-token-terminal-lifecycle.md, tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs, tests/RoslynMcp.Tests/ToolDispatchTests.cs; keep the pair serialized. Deferred predecessors retain their disposition; this is whole-graph scheduling evidence. |
| preview-token-solution-confirmation | warn | C2-wave-conflict | Consecutive orders 8.02 and 8.03 share tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs; keep the pair serialized. Deferred predecessors retain their disposition; this is whole-graph scheduling evidence. |
| preview-token-composite-confirmation | warn | C2-wave-conflict | Consecutive orders 8.03 and 8.04 share tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs; keep the pair serialized. Deferred predecessors retain their disposition; this is whole-graph scheduling evidence. |
| preview-token-project-confirmation | warn | C2-wave-conflict | Consecutive orders 8.04 and 8.05 share tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs; keep the pair serialized. Deferred predecessors retain their disposition; this is whole-graph scheduling evidence. |
| preview-diff-whitespace-omission | warn | C2-wave-conflict | Consecutive orders 25 and 26 share src/RoslynMcp.Roslyn/Helpers/DiffGenerator.cs, src/RoslynMcp.Roslyn/Helpers/SolutionDiffHelper.cs, src/RoslynMcp.Roslyn/Services/EditService.cs, src/RoslynMcp.Roslyn/Services/RefactoringService.cs, tests/RoslynMcp.Tests/DiffGeneratorTests.cs, tests/RoslynMcp.Tests/SolutionDiffHelperTests.cs; keep the pair serialized. Deferred predecessors retain their disposition; this is whole-graph scheduling evidence. |
| workspace-lifecycle-argument-refusals-public-message | info | C2 | Whole-plan conflict degree 2; scheduleHint is not heroic-last. Rebuild includes terminal stanzas and excludes canonical shared paths. |
| tool-refusal-public-message-guard | info | C2 | Whole-plan conflict degree 3; scheduleHint is not heroic-last. Rebuild includes terminal stanzas and excludes canonical shared paths. |
| scaffold-batch-preview-apply-route | info | C2 | Whole-plan conflict degree 6; scheduleHint is not heroic-last. Rebuild includes terminal stanzas and excludes canonical shared paths. |
| workspace-project-alias-lookup | info | C2 | Whole-plan conflict degree 2; scheduleHint is not heroic-last. Rebuild includes terminal stanzas and excludes canonical shared paths. |
| workspace-close-global-build-server-shutdown | info | C2 | Whole-plan conflict degree 7; scheduleHint is not heroic-last. Rebuild includes terminal stanzas and excludes canonical shared paths. |
| change-signature-service-refusals-public-message | info | C2 | Whole-plan conflict degree 5; scheduleHint is not heroic-last. Rebuild includes terminal stanzas and excludes canonical shared paths. |
| cross-project-public-refusals-echo-input | info | C2 | Whole-plan conflict degree 4; scheduleHint is not heroic-last. Rebuild includes terminal stanzas and excludes canonical shared paths. |
| preview-diff-whitespace-omission | info | C2 | Whole-plan conflict degree 3; scheduleHint is not heroic-last. Rebuild includes terminal stanzas and excludes canonical shared paths. |
| preview-token-reason-projection | info | C2 | Whole-plan conflict degree 9; scheduleHint is not heroic-last. Rebuild includes terminal stanzas and excludes canonical shared paths. |
| preview-token-solution-confirmation | info | C2 | Whole-plan conflict degree 6; scheduleHint is not heroic-last. Rebuild includes terminal stanzas and excludes canonical shared paths. |
| preview-token-composite-confirmation | info | C2 | Whole-plan conflict degree 6; scheduleHint is not heroic-last. Rebuild includes terminal stanzas and excludes canonical shared paths. |
| preview-token-project-confirmation | info | C2 | Whole-plan conflict degree 5; scheduleHint is not heroic-last. Rebuild includes terminal stanzas and excludes canonical shared paths. |
| fork-composite-nonconsuming-snapshot | info | C2 | Whole-plan conflict degree 3; scheduleHint is not heroic-last. Rebuild includes terminal stanzas and excludes canonical shared paths. |
| fork-preview-project-composite-replay | info | C2 | Whole-plan conflict degree 7; scheduleHint is not heroic-last. Rebuild includes terminal stanzas and excludes canonical shared paths. |
| fork-composite-nonconsuming-snapshot | warn | hotspot | Consecutive orders 6/7.01 touch addenda hotspots: src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs, src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Orchestration.cs, README.md, src/RoslynMcp.Host.Stdio/README.md / src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs. Both are deferred; keep one hotspot-touching initiative per future wave. |
| fork-preview-project-composite-replay | warn | hotspot | Consecutive orders 7.01/7.02 touch addenda hotspots: src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs / README.md, src/RoslynMcp.Host.Stdio/README.md. Both are deferred; keep one hotspot-touching initiative per future wave. |
| preview-token-lifecycle-evidence | warn | hotspot | Consecutive orders 7.02/8.01 touch addenda hotspots: README.md, src/RoslynMcp.Host.Stdio/README.md / src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs. Both are deferred; keep one hotspot-touching initiative per future wave. |
| fix-all-equivalence-key-unregistered-fallback | warn | C2-graph-disagreement | Companion test adds Scope edges 6-12 and 8.01-12 on tests/RoslynMcp.Tests/PreviewRouteBindingFileOpsTests.cs, and adds that shared file to edge 12-25. Stored graph has 56 edges; reviewer rebuild has 58. Regenerate graph through sanctioned writer before execution scheduling. |
| fix-all-equivalence-key-unregistered-fallback | info | C2 | Whole-plan conflict degree is now 3 after adding PreviewRouteBindingFileOpsTests.cs; scheduleHint is not heroic-last. Conflicts are with deferred orders 6, 8.01 and 25; preserve their holds. |
| change-signature-class-struct-primary-ctor-add-remove | info | 3 | Five-file defect-forced-companion holds on current immutable base 1cd4a90: ChangeSignatureService.cs:85-92 refuses primary constructors; builder:72,129-155 omits type declarations/constructions; service:195-217,235-246,364-405 loses omitted/expanded parameter mappings. Core:5-26, Tools:11-44 and Catalog.Refactoring:18 advertise method-only behavior; SurfaceCatalogTests.cs:331-350,391 pins the changed-description set/count. The stanza repairs every shared adapter, semantic/trivia validation and public-description companion in one feature; README counts stay unchanged. |
| change-signature-class-struct-primary-ctor-add-remove | info | C2 | Whole-plan conflict degree 2; scheduleHint is not heroic-last. Rebuilt from every safely loaded Scope, including terminal stanzas and all production/test/doc companions. |
| split-service-refuse-cross-partition-references | info | C2 | Whole-plan conflict degree 5; scheduleHint is not heroic-last. Rebuilt from every safely loaded Scope, including terminal stanzas and all production/test/doc companions. |
| change-signature-class-struct-primary-ctor-add-remove | warn | C2-graph-disagreement | Stored graph contains only 16 edges; required all-stanza Scope rebuild has 62 edges, with 46 missing and zero extraneous: {"onlyStored":[],"onlyRebuilt":["[12,25,[\"src/RoslynMcp.Roslyn/Services/FixAllService.cs\",\"tests/RoslynMcp.Tests/PreviewRouteBindingFileOpsTests.cs\"]]","[15,25,[\"tests/RoslynMcp.Tests/Workspace/WorkspaceForkApplyTests.cs\"]]","[16,19,[\"docs/decisions/README.md\"]]","[16,22,[\"docs/decisions/README.md\"]]","[16,25,[\"docs/decisions/README.md\",\"src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs\"]]","[17,25,[\"src/RoslynMcp.Roslyn/Services/ChangeSignatureAddRemovePreviewBuilder.cs\",\"src/RoslynMcp.Roslyn/Services/ChangeSignatureService.cs\",\"tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs\"]]","[19,25,[\"docs/decisions/README.md\"]]","[2,25,[\"src/RoslynMcp.Roslyn/Services/CodeActionService.cs\"]]","[21,25,[\"src/RoslynMcp.Roslyn/Services/ChangeSignatureService.cs\",\"tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs\"]]","[22,25,[\"docs/decisions/README.md\",\"src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs\"]]","[25,26,[\"src/RoslynMcp.Roslyn/Helpers/DiffGenerator.cs\",\"src/RoslynMcp.Roslyn/Helpers/SolutionDiffHelper.cs\",\"src/RoslynMcp.Roslyn/Services/EditService.cs\",\"src/RoslynMcp.Roslyn/Services/RefactoringService.cs\",\"tests/RoslynMcp.Tests/DiffGeneratorTests.cs\",\"tests/RoslynMcp.Tests/SolutionDiffHelperTests.cs\"]]","[3,11,[\"src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs\"]]","[3,8.01,[\"src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs\"]]","[4,21,[\"tests/RoslynMcp.Tests/TestData/invalid-operation-construction-baseline.json\"]]","[4,6,[\"tests/RoslynMcp.Tests/TestData/invalid-operation-construction-baseline.json\"]]","[4,8.01,[\"tests/RoslynMcp.Tests/TestData/invalid-operation-construction-baseline.json\"]]","[5,25,[\"src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs\"]]","[6,12,[\"tests/RoslynMcp.Tests/PreviewRouteBindingFileOpsTests.cs\"]]","[6,21,[\"tests/RoslynMcp.Tests/TestData/invalid-operation-construction-baseline.json\"]]","[6,25,[\"src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs\",\"src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs\",\"src/RoslynMcp.Roslyn/Services/BatchTestScaffolder.cs\",\"tests/RoslynMcp.Tests/ExtractionApplyRouteBindingTests.cs\",\"tests/RoslynMcp.Tests/PreviewRouteBindingEditingTests.cs\",\"tests/RoslynMcp.Tests/PreviewRouteBindingFileOpsTests.cs\",\"tests/RoslynMcp.Tests/ScaffoldingIntegrationTests.cs\",\"tests/RoslynMcp.Tests/ToolDispatchTests.cs\"]]","[6,7.02,[\"README.md\",\"src/RoslynMcp.Host.Stdio/README.md\"]]","[6,8.01,[\"tests/RoslynMcp.Tests/ExtractionApplyRouteBindingTests.cs\",\"tests/RoslynMcp.Tests/PreviewRouteBindingEditingTests.cs\",\"tests/RoslynMcp.Tests/PreviewRouteBindingFileOpsTests.cs\",\"tests/RoslynMcp.Tests/TestData/invalid-operation-construction-baseline.json\",\"tests/RoslynMcp.Tests/ToolDispatchTests.cs\"]]","[6,8.02,[\"src/RoslynMcp.Host.Stdio/Tools/OrchestrationTools.cs\",\"src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs\",\"tests/RoslynMcp.Tests/ToolDispatchTests.cs\"]]","[7.01,25,[\"src/RoslynMcp.Roslyn/Services/CompositePreviewStore.cs\",\"src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs\",\"tests/RoslynMcp.Tests/Services/PersistentCompositeStorageTests.cs\"]]","[7.01,8.01,[\"src/RoslynMcp.Roslyn/Services/CompositePreviewStore.cs\",\"src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs\",\"tests/RoslynMcp.Tests/Services/PersistentCompositeStorageTests.cs\"]]","[7.01,8.04,[\"src/RoslynMcp.Roslyn/Services/CompositePreviewStore.cs\",\"src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs\",\"tests/RoslynMcp.Tests/Services/PersistentCompositeStorageTests.cs\"]]","[7.02,15,[\"src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs\",\"tests/RoslynMcp.Tests/Workspace/WorkspaceForkApplyTests.cs\"]]","[7.02,16,[\"docs/decisions/README.md\"]]","[7.02,19,[\"docs/decisions/README.md\"]]","[7.02,22,[\"docs/decisions/README.md\"]]","[7.02,25,[\"docs/decisions/README.md\",\"tests/RoslynMcp.Tests/Workspace/WorkspaceForkApplyTests.cs\"]]","[7.02,8.01,[\"docs/decisions/README.md\"]]","[7.02,8.02,[\"src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs\"]]","[8.01,11,[\"src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs\"]]","[8.01,12,[\"tests/RoslynMcp.Tests/PreviewRouteBindingFileOpsTests.cs\"]]","[8.01,16,[\"docs/decisions/README.md\"]]","[8.01,19,[\"docs/decisions/README.md\"]]","[8.01,21,[\"tests/RoslynMcp.Tests/TestData/invalid-operation-construction-baseline.json\"]]","[8.01,22,[\"docs/decisions/README.md\"]]","[8.01,25,[\"docs/decisions/README.md\",\"src/RoslynMcp.Core/Services/ICompositePreviewStore.cs\",\"src/RoslynMcp.Core/Services/IProjectMutationPreviewStore.cs\",\"src/RoslynMcp.Roslyn/Contracts/IPreviewStore.cs\",\"src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs\",\"src/RoslynMcp.Roslyn/Services/CompositePreviewStore.cs\",\"src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs\",\"src/RoslynMcp.Roslyn/Services/PreviewStore.cs\",\"src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs\",\"src/RoslynMcp.Roslyn/Services/RefactoringService.cs\",\"tests/RoslynMcp.Tests/ApplyUndoWorkflowServiceTests.cs\",\"tests/RoslynMcp.Tests/ApplyWithVerifyCancellationAndScopeTests.cs\",\"tests/RoslynMcp.Tests/BoundedStoreEvictionTests.cs\",\"tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs\",\"tests/RoslynMcp.Tests/ExtractionApplyRouteBindingTests.cs\",\"tests/RoslynMcp.Tests/ParameterObjectPreviewTests.cs\",\"tests/RoslynMcp.Tests/PreviewRouteBindingEditingTests.cs\",\"tests/RoslynMcp.Tests/PreviewRouteBindingFileOpsTests.cs\",\"tests/RoslynMcp.Tests/PreviewStoreTests.cs\",\"tests/RoslynMcp.Tests/ProjectMutationIntegrationTests.cs\",\"tests/RoslynMcp.Tests/Services/PersistentCompositeStorageTests.cs\",\"tests/RoslynMcp.Tests/ToolDispatchTests.cs\"]]","[8.01,26,[\"src/RoslynMcp.Roslyn/Services/RefactoringService.cs\"]]","[8.01,8.02,[\"docs/decisions/0024-preview-token-terminal-lifecycle.md\",\"tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs\",\"tests/RoslynMcp.Tests/ToolDispatchTests.cs\"]]","[8.01,8.03,[\"src/RoslynMcp.Roslyn/Services/RefactoringService.cs\",\"tests/RoslynMcp.Tests/ApplyUndoWorkflowServiceTests.cs\",\"tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs\",\"tests/RoslynMcp.Tests/RefactoringToolsIntegrationTests.cs\"]]","[8.01,8.04,[\"src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs\",\"src/RoslynMcp.Roslyn/Services/CompositePreviewStore.cs\",\"src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs\",\"tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs\",\"tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs\",\"tests/RoslynMcp.Tests/Services/PersistentCompositeStorageTests.cs\"]]","[8.01,8.05,[\"src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs\",\"tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs\",\"tests/RoslynMcp.Tests/ProjectMutationIntegrationTests.cs\"]]","[8.02,15,[\"src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs\"]]","[8.02,19,[\"docs/product-contract.md\"]]","[8.02,25,[\"src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs\",\"tests/RoslynMcp.Tests/PreviewTokenStaleAcrossAutoReloadTests.cs\",\"tests/RoslynMcp.Tests/ToolDispatchTests.cs\"]]","[8.02,8.03,[\"tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs\"]]","[8.02,8.04,[\"tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs\"]]","[8.02,8.05,[\"tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs\"]]","[8.03,25,[\"src/RoslynMcp.Roslyn/Services/RefactoringService.cs\",\"tests/RoslynMcp.Tests/ApplyUndoWorkflowServiceTests.cs\"]]","[8.03,26,[\"src/RoslynMcp.Roslyn/Services/RefactoringService.cs\"]]","[8.03,8.04,[\"tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs\"]]","[8.03,8.05,[\"tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs\"]]","[8.04,25,[\"src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs\",\"src/RoslynMcp.Roslyn/Services/CompositePreviewStore.cs\",\"src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs\",\"tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs\",\"tests/RoslynMcp.Tests/Services/PersistentCompositeStorageTests.cs\"]]","[8.04,8.05,[\"tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs\"]]","[8.05,25,[\"src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs\",\"tests/RoslynMcp.Tests/ProjectMutationIntegrationTests.cs\"]]"]} |

## Whole-plan conflict graph

conflictGraphSeenOK: false

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
      "b": 8.01,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs"
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
      "a": 4,
      "b": 6,
      "sharedFiles": [
        "tests/RoslynMcp.Tests/TestData/invalid-operation-construction-baseline.json"
      ]
    },
    {
      "a": 4,
      "b": 8.01,
      "sharedFiles": [
        "tests/RoslynMcp.Tests/TestData/invalid-operation-construction-baseline.json"
      ]
    },
    {
      "a": 4,
      "b": 21,
      "sharedFiles": [
        "tests/RoslynMcp.Tests/TestData/invalid-operation-construction-baseline.json"
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
      "b": 7.02,
      "sharedFiles": [
        "README.md",
        "src/RoslynMcp.Host.Stdio/README.md"
      ]
    },
    {
      "a": 6,
      "b": 8.01,
      "sharedFiles": [
        "tests/RoslynMcp.Tests/ExtractionApplyRouteBindingTests.cs",
        "tests/RoslynMcp.Tests/PreviewRouteBindingEditingTests.cs",
        "tests/RoslynMcp.Tests/PreviewRouteBindingFileOpsTests.cs",
        "tests/RoslynMcp.Tests/TestData/invalid-operation-construction-baseline.json",
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
      "b": 12,
      "sharedFiles": [
        "tests/RoslynMcp.Tests/PreviewRouteBindingFileOpsTests.cs"
      ]
    },
    {
      "a": 6,
      "b": 21,
      "sharedFiles": [
        "tests/RoslynMcp.Tests/TestData/invalid-operation-construction-baseline.json"
      ]
    },
    {
      "a": 6,
      "b": 25,
      "sharedFiles": [
        "src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs",
        "src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs",
        "src/RoslynMcp.Roslyn/Services/BatchTestScaffolder.cs",
        "tests/RoslynMcp.Tests/ExtractionApplyRouteBindingTests.cs",
        "tests/RoslynMcp.Tests/PreviewRouteBindingEditingTests.cs",
        "tests/RoslynMcp.Tests/PreviewRouteBindingFileOpsTests.cs",
        "tests/RoslynMcp.Tests/ScaffoldingIntegrationTests.cs",
        "tests/RoslynMcp.Tests/ToolDispatchTests.cs"
      ]
    },
    {
      "a": 7.01,
      "b": 8.01,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/CompositePreviewStore.cs",
        "src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs",
        "tests/RoslynMcp.Tests/Services/PersistentCompositeStorageTests.cs"
      ]
    },
    {
      "a": 7.01,
      "b": 8.04,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/CompositePreviewStore.cs",
        "src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs",
        "tests/RoslynMcp.Tests/Services/PersistentCompositeStorageTests.cs"
      ]
    },
    {
      "a": 7.01,
      "b": 25,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/CompositePreviewStore.cs",
        "src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs",
        "tests/RoslynMcp.Tests/Services/PersistentCompositeStorageTests.cs"
      ]
    },
    {
      "a": 7.02,
      "b": 8.01,
      "sharedFiles": [
        "docs/decisions/README.md"
      ]
    },
    {
      "a": 7.02,
      "b": 8.02,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs"
      ]
    },
    {
      "a": 7.02,
      "b": 15,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs",
        "tests/RoslynMcp.Tests/Workspace/WorkspaceForkApplyTests.cs"
      ]
    },
    {
      "a": 7.02,
      "b": 16,
      "sharedFiles": [
        "docs/decisions/README.md"
      ]
    },
    {
      "a": 7.02,
      "b": 19,
      "sharedFiles": [
        "docs/decisions/README.md"
      ]
    },
    {
      "a": 7.02,
      "b": 22,
      "sharedFiles": [
        "docs/decisions/README.md"
      ]
    },
    {
      "a": 7.02,
      "b": 25,
      "sharedFiles": [
        "docs/decisions/README.md",
        "tests/RoslynMcp.Tests/Workspace/WorkspaceForkApplyTests.cs"
      ]
    },
    {
      "a": 8.01,
      "b": 8.02,
      "sharedFiles": [
        "docs/decisions/0024-preview-token-terminal-lifecycle.md",
        "tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs",
        "tests/RoslynMcp.Tests/ToolDispatchTests.cs"
      ]
    },
    {
      "a": 8.01,
      "b": 8.03,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/RefactoringService.cs",
        "tests/RoslynMcp.Tests/ApplyUndoWorkflowServiceTests.cs",
        "tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs",
        "tests/RoslynMcp.Tests/RefactoringToolsIntegrationTests.cs"
      ]
    },
    {
      "a": 8.01,
      "b": 8.04,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs",
        "src/RoslynMcp.Roslyn/Services/CompositePreviewStore.cs",
        "src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs",
        "tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs",
        "tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs",
        "tests/RoslynMcp.Tests/Services/PersistentCompositeStorageTests.cs"
      ]
    },
    {
      "a": 8.01,
      "b": 8.05,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs",
        "tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs",
        "tests/RoslynMcp.Tests/ProjectMutationIntegrationTests.cs"
      ]
    },
    {
      "a": 8.01,
      "b": 11,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs"
      ]
    },
    {
      "a": 8.01,
      "b": 12,
      "sharedFiles": [
        "tests/RoslynMcp.Tests/PreviewRouteBindingFileOpsTests.cs"
      ]
    },
    {
      "a": 8.01,
      "b": 15,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/PreviewStore.cs",
        "src/RoslynMcp.Roslyn/Services/RefactoringService.cs",
        "tests/RoslynMcp.Tests/PreviewStoreTests.cs"
      ]
    },
    {
      "a": 8.01,
      "b": 16,
      "sharedFiles": [
        "docs/decisions/README.md"
      ]
    },
    {
      "a": 8.01,
      "b": 19,
      "sharedFiles": [
        "docs/decisions/README.md"
      ]
    },
    {
      "a": 8.01,
      "b": 21,
      "sharedFiles": [
        "tests/RoslynMcp.Tests/TestData/invalid-operation-construction-baseline.json"
      ]
    },
    {
      "a": 8.01,
      "b": 22,
      "sharedFiles": [
        "docs/decisions/README.md"
      ]
    },
    {
      "a": 8.01,
      "b": 25,
      "sharedFiles": [
        "docs/decisions/README.md",
        "src/RoslynMcp.Core/Services/ICompositePreviewStore.cs",
        "src/RoslynMcp.Core/Services/IProjectMutationPreviewStore.cs",
        "src/RoslynMcp.Roslyn/Contracts/IPreviewStore.cs",
        "src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs",
        "src/RoslynMcp.Roslyn/Services/CompositePreviewStore.cs",
        "src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs",
        "src/RoslynMcp.Roslyn/Services/PreviewStore.cs",
        "src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs",
        "src/RoslynMcp.Roslyn/Services/RefactoringService.cs",
        "tests/RoslynMcp.Tests/ApplyUndoWorkflowServiceTests.cs",
        "tests/RoslynMcp.Tests/ApplyWithVerifyCancellationAndScopeTests.cs",
        "tests/RoslynMcp.Tests/BoundedStoreEvictionTests.cs",
        "tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs",
        "tests/RoslynMcp.Tests/ExtractionApplyRouteBindingTests.cs",
        "tests/RoslynMcp.Tests/ParameterObjectPreviewTests.cs",
        "tests/RoslynMcp.Tests/PreviewRouteBindingEditingTests.cs",
        "tests/RoslynMcp.Tests/PreviewRouteBindingFileOpsTests.cs",
        "tests/RoslynMcp.Tests/PreviewStoreTests.cs",
        "tests/RoslynMcp.Tests/ProjectMutationIntegrationTests.cs",
        "tests/RoslynMcp.Tests/Services/PersistentCompositeStorageTests.cs",
        "tests/RoslynMcp.Tests/ToolDispatchTests.cs"
      ]
    },
    {
      "a": 8.01,
      "b": 26,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/RefactoringService.cs"
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
      "b": 15,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs"
      ]
    },
    {
      "a": 8.02,
      "b": 19,
      "sharedFiles": [
        "docs/product-contract.md"
      ]
    },
    {
      "a": 8.02,
      "b": 25,
      "sharedFiles": [
        "src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs",
        "tests/RoslynMcp.Tests/PreviewTokenStaleAcrossAutoReloadTests.cs",
        "tests/RoslynMcp.Tests/ToolDispatchTests.cs"
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
      "a": 8.03,
      "b": 15,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/RefactoringService.cs"
      ]
    },
    {
      "a": 8.03,
      "b": 25,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/RefactoringService.cs",
        "tests/RoslynMcp.Tests/ApplyUndoWorkflowServiceTests.cs"
      ]
    },
    {
      "a": 8.03,
      "b": 26,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/RefactoringService.cs"
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
      "b": 25,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs",
        "src/RoslynMcp.Roslyn/Services/CompositePreviewStore.cs",
        "src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs",
        "tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs",
        "tests/RoslynMcp.Tests/Services/PersistentCompositeStorageTests.cs"
      ]
    },
    {
      "a": 8.05,
      "b": 25,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs",
        "tests/RoslynMcp.Tests/ProjectMutationIntegrationTests.cs"
      ]
    },
    {
      "a": 12,
      "b": 25,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/FixAllService.cs",
        "tests/RoslynMcp.Tests/PreviewRouteBindingFileOpsTests.cs"
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
        "src/RoslynMcp.Roslyn/Helpers/SolutionDiffHelper.cs",
        "src/RoslynMcp.Roslyn/Services/PreviewStore.cs",
        "src/RoslynMcp.Roslyn/Services/RefactoringService.cs",
        "tests/RoslynMcp.Tests/PreviewStoreTests.cs",
        "tests/RoslynMcp.Tests/SolutionDiffHelperTests.cs",
        "tests/RoslynMcp.Tests/Workspace/WorkspaceForkApplyTests.cs"
      ]
    },
    {
      "a": 15,
      "b": 26,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Helpers/SolutionDiffHelper.cs",
        "src/RoslynMcp.Roslyn/Services/RefactoringService.cs",
        "tests/RoslynMcp.Tests/SolutionDiffHelperTests.cs"
      ]
    },
    {
      "a": 16,
      "b": 19,
      "sharedFiles": [
        "docs/decisions/README.md"
      ]
    },
    {
      "a": 16,
      "b": 22,
      "sharedFiles": [
        "docs/decisions/README.md"
      ]
    },
    {
      "a": 16,
      "b": 25,
      "sharedFiles": [
        "docs/decisions/README.md",
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
    }
  ],
  "degrees": {
    "2": 1,
    "3": 2,
    "4": 3,
    "5": 1,
    "6": 7,
    "9": 0,
    "10": 0,
    "11": 2,
    "12": 3,
    "13": 0,
    "14": 0,
    "15": 7,
    "16": 5,
    "17": 2,
    "18": 0,
    "19": 8,
    "20": 0,
    "21": 5,
    "22": 5,
    "23": 0,
    "24": 1,
    "25": 18,
    "26": 4,
    "7.01": 3,
    "7.02": 8,
    "8.01": 18,
    "8.02": 9,
    "8.03": 7,
    "8.04": 6,
    "8.05": 5
  },
  "zeroDegreeInitiatives": [
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

Current cache comparison: 3 stored versus 65 rebuilt edges; 62 missing and 0 extraneous. Exact differences:

```json
{
  "onlyStored": [],
  "onlyRebuilt": [
    "[12,25,[\"src/RoslynMcp.Roslyn/Services/FixAllService.cs\",\"tests/RoslynMcp.Tests/PreviewRouteBindingFileOpsTests.cs\"]]",
    "[15,19,[\"src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs\"]]",
    "[15,25,[\"src/RoslynMcp.Roslyn/Helpers/SolutionDiffHelper.cs\",\"src/RoslynMcp.Roslyn/Services/PreviewStore.cs\",\"src/RoslynMcp.Roslyn/Services/RefactoringService.cs\",\"tests/RoslynMcp.Tests/PreviewStoreTests.cs\",\"tests/RoslynMcp.Tests/SolutionDiffHelperTests.cs\",\"tests/RoslynMcp.Tests/Workspace/WorkspaceForkApplyTests.cs\"]]",
    "[15,26,[\"src/RoslynMcp.Roslyn/Helpers/SolutionDiffHelper.cs\",\"src/RoslynMcp.Roslyn/Services/RefactoringService.cs\",\"tests/RoslynMcp.Tests/SolutionDiffHelperTests.cs\"]]",
    "[16,19,[\"docs/decisions/README.md\"]]",
    "[16,22,[\"docs/decisions/README.md\"]]",
    "[16,25,[\"docs/decisions/README.md\",\"src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs\"]]",
    "[17,25,[\"src/RoslynMcp.Roslyn/Services/ChangeSignatureAddRemovePreviewBuilder.cs\",\"src/RoslynMcp.Roslyn/Services/ChangeSignatureService.cs\",\"tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs\"]]",
    "[19,25,[\"docs/decisions/README.md\"]]",
    "[2,25,[\"src/RoslynMcp.Roslyn/Services/CodeActionService.cs\"]]",
    "[21,25,[\"src/RoslynMcp.Roslyn/Services/ChangeSignatureService.cs\",\"tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs\"]]",
    "[22,25,[\"docs/decisions/README.md\",\"src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs\"]]",
    "[25,26,[\"src/RoslynMcp.Roslyn/Helpers/DiffGenerator.cs\",\"src/RoslynMcp.Roslyn/Helpers/SolutionDiffHelper.cs\",\"src/RoslynMcp.Roslyn/Services/EditService.cs\",\"src/RoslynMcp.Roslyn/Services/RefactoringService.cs\",\"tests/RoslynMcp.Tests/DiffGeneratorTests.cs\",\"tests/RoslynMcp.Tests/SolutionDiffHelperTests.cs\"]]",
    "[3,11,[\"src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs\"]]",
    "[3,8.01,[\"src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs\"]]",
    "[4,21,[\"tests/RoslynMcp.Tests/TestData/invalid-operation-construction-baseline.json\"]]",
    "[4,6,[\"tests/RoslynMcp.Tests/TestData/invalid-operation-construction-baseline.json\"]]",
    "[4,8.01,[\"tests/RoslynMcp.Tests/TestData/invalid-operation-construction-baseline.json\"]]",
    "[5,25,[\"src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs\"]]",
    "[6,12,[\"tests/RoslynMcp.Tests/PreviewRouteBindingFileOpsTests.cs\"]]",
    "[6,21,[\"tests/RoslynMcp.Tests/TestData/invalid-operation-construction-baseline.json\"]]",
    "[6,25,[\"src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs\",\"src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs\",\"src/RoslynMcp.Roslyn/Services/BatchTestScaffolder.cs\",\"tests/RoslynMcp.Tests/ExtractionApplyRouteBindingTests.cs\",\"tests/RoslynMcp.Tests/PreviewRouteBindingEditingTests.cs\",\"tests/RoslynMcp.Tests/PreviewRouteBindingFileOpsTests.cs\",\"tests/RoslynMcp.Tests/ScaffoldingIntegrationTests.cs\",\"tests/RoslynMcp.Tests/ToolDispatchTests.cs\"]]",
    "[6,7.02,[\"README.md\",\"src/RoslynMcp.Host.Stdio/README.md\"]]",
    "[6,8.01,[\"tests/RoslynMcp.Tests/ExtractionApplyRouteBindingTests.cs\",\"tests/RoslynMcp.Tests/PreviewRouteBindingEditingTests.cs\",\"tests/RoslynMcp.Tests/PreviewRouteBindingFileOpsTests.cs\",\"tests/RoslynMcp.Tests/TestData/invalid-operation-construction-baseline.json\",\"tests/RoslynMcp.Tests/ToolDispatchTests.cs\"]]",
    "[6,8.02,[\"src/RoslynMcp.Host.Stdio/Tools/OrchestrationTools.cs\",\"src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs\",\"tests/RoslynMcp.Tests/ToolDispatchTests.cs\"]]",
    "[7.01,25,[\"src/RoslynMcp.Roslyn/Services/CompositePreviewStore.cs\",\"src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs\",\"tests/RoslynMcp.Tests/Services/PersistentCompositeStorageTests.cs\"]]",
    "[7.01,8.01,[\"src/RoslynMcp.Roslyn/Services/CompositePreviewStore.cs\",\"src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs\",\"tests/RoslynMcp.Tests/Services/PersistentCompositeStorageTests.cs\"]]",
    "[7.01,8.04,[\"src/RoslynMcp.Roslyn/Services/CompositePreviewStore.cs\",\"src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs\",\"tests/RoslynMcp.Tests/Services/PersistentCompositeStorageTests.cs\"]]",
    "[7.02,15,[\"src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs\",\"tests/RoslynMcp.Tests/Workspace/WorkspaceForkApplyTests.cs\"]]",
    "[7.02,16,[\"docs/decisions/README.md\"]]",
    "[7.02,19,[\"docs/decisions/README.md\"]]",
    "[7.02,22,[\"docs/decisions/README.md\"]]",
    "[7.02,25,[\"docs/decisions/README.md\",\"tests/RoslynMcp.Tests/Workspace/WorkspaceForkApplyTests.cs\"]]",
    "[7.02,8.01,[\"docs/decisions/README.md\"]]",
    "[7.02,8.02,[\"src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs\"]]",
    "[8.01,11,[\"src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs\"]]",
    "[8.01,12,[\"tests/RoslynMcp.Tests/PreviewRouteBindingFileOpsTests.cs\"]]",
    "[8.01,15,[\"src/RoslynMcp.Roslyn/Services/PreviewStore.cs\",\"src/RoslynMcp.Roslyn/Services/RefactoringService.cs\",\"tests/RoslynMcp.Tests/PreviewStoreTests.cs\"]]",
    "[8.01,16,[\"docs/decisions/README.md\"]]",
    "[8.01,19,[\"docs/decisions/README.md\"]]",
    "[8.01,21,[\"tests/RoslynMcp.Tests/TestData/invalid-operation-construction-baseline.json\"]]",
    "[8.01,22,[\"docs/decisions/README.md\"]]",
    "[8.01,25,[\"docs/decisions/README.md\",\"src/RoslynMcp.Core/Services/ICompositePreviewStore.cs\",\"src/RoslynMcp.Core/Services/IProjectMutationPreviewStore.cs\",\"src/RoslynMcp.Roslyn/Contracts/IPreviewStore.cs\",\"src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs\",\"src/RoslynMcp.Roslyn/Services/CompositePreviewStore.cs\",\"src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs\",\"src/RoslynMcp.Roslyn/Services/PreviewStore.cs\",\"src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs\",\"src/RoslynMcp.Roslyn/Services/RefactoringService.cs\",\"tests/RoslynMcp.Tests/ApplyUndoWorkflowServiceTests.cs\",\"tests/RoslynMcp.Tests/ApplyWithVerifyCancellationAndScopeTests.cs\",\"tests/RoslynMcp.Tests/BoundedStoreEvictionTests.cs\",\"tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs\",\"tests/RoslynMcp.Tests/ExtractionApplyRouteBindingTests.cs\",\"tests/RoslynMcp.Tests/ParameterObjectPreviewTests.cs\",\"tests/RoslynMcp.Tests/PreviewRouteBindingEditingTests.cs\",\"tests/RoslynMcp.Tests/PreviewRouteBindingFileOpsTests.cs\",\"tests/RoslynMcp.Tests/PreviewStoreTests.cs\",\"tests/RoslynMcp.Tests/ProjectMutationIntegrationTests.cs\",\"tests/RoslynMcp.Tests/Services/PersistentCompositeStorageTests.cs\",\"tests/RoslynMcp.Tests/ToolDispatchTests.cs\"]]",
    "[8.01,26,[\"src/RoslynMcp.Roslyn/Services/RefactoringService.cs\"]]",
    "[8.01,8.02,[\"docs/decisions/0024-preview-token-terminal-lifecycle.md\",\"tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs\",\"tests/RoslynMcp.Tests/ToolDispatchTests.cs\"]]",
    "[8.01,8.03,[\"src/RoslynMcp.Roslyn/Services/RefactoringService.cs\",\"tests/RoslynMcp.Tests/ApplyUndoWorkflowServiceTests.cs\",\"tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs\",\"tests/RoslynMcp.Tests/RefactoringToolsIntegrationTests.cs\"]]",
    "[8.01,8.04,[\"src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs\",\"src/RoslynMcp.Roslyn/Services/CompositePreviewStore.cs\",\"src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs\",\"tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs\",\"tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs\",\"tests/RoslynMcp.Tests/Services/PersistentCompositeStorageTests.cs\"]]",
    "[8.01,8.05,[\"src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs\",\"tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs\",\"tests/RoslynMcp.Tests/ProjectMutationIntegrationTests.cs\"]]",
    "[8.02,15,[\"src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs\"]]",
    "[8.02,19,[\"docs/product-contract.md\"]]",
    "[8.02,25,[\"src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs\",\"tests/RoslynMcp.Tests/PreviewTokenStaleAcrossAutoReloadTests.cs\",\"tests/RoslynMcp.Tests/ToolDispatchTests.cs\"]]",
    "[8.02,8.03,[\"tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs\"]]",
    "[8.02,8.04,[\"tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs\"]]",
    "[8.02,8.05,[\"tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs\"]]",
    "[8.03,15,[\"src/RoslynMcp.Roslyn/Services/RefactoringService.cs\"]]",
    "[8.03,25,[\"src/RoslynMcp.Roslyn/Services/RefactoringService.cs\",\"tests/RoslynMcp.Tests/ApplyUndoWorkflowServiceTests.cs\"]]",
    "[8.03,26,[\"src/RoslynMcp.Roslyn/Services/RefactoringService.cs\"]]",
    "[8.03,8.04,[\"tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs\"]]",
    "[8.03,8.05,[\"tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs\"]]",
    "[8.04,25,[\"src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs\",\"src/RoslynMcp.Roslyn/Services/CompositePreviewStore.cs\",\"src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs\",\"tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs\",\"tests/RoslynMcp.Tests/Services/PersistentCompositeStorageTests.cs\"]]",
    "[8.04,8.05,[\"tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs\"]]",
    "[8.05,25,[\"src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs\",\"tests/RoslynMcp.Tests/ProjectMutationIntegrationTests.cs\"]]"
  ]
}
```

All production/test/doc Scope lists participate, including every terminal initiative and own row-detail production paths. Rooted compact lists in scaffold/split-service and the 38-file safety stanza resolve to the recorded complete arrays; shared backlog, CHANGELOG and changelog.d paths are excluded. Addenda virtually-shared entries contain only those same backlog/CHANGELOG paths. The previous two unchanged C2-graph-disagreement findings remain verbatim historical evidence by the input carry-forward contract; this current diff supersedes their numerical snapshot. Do not count a third structured finding for the same cache defect. Regenerate the cache through the sanctioned writer; retain current full graph for review, never infer independence from active-only cache. The widened restore has degree 7 and heroic-last; its new reference/diff/store companion overlap remains visible even though it is deferred. Consecutive adjacency is sorted by order including fractional children; the same six wave warnings still hold.

## Hotspots

| Order | Initiative | Files |
|---|---|---|
| 3 | workspace-lifecycle-argument-refusals-public-message | src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs |
| 6 | scaffold-batch-preview-apply-route | README.md, src/RoslynMcp.Host.Stdio/README.md, src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Orchestration.cs, src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs |
| 7.01 | fork-composite-nonconsuming-snapshot | src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs |
| 7.02 | fork-preview-project-composite-replay | README.md, src/RoslynMcp.Host.Stdio/README.md |
| 8.01 | preview-token-lifecycle-evidence | src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs |
| 11 | workspace-project-alias-lookup | src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs |
| 17 | change-signature-class-struct-primary-ctor-add-remove | src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Refactoring.cs |
| 25 | preview-store-explicit-internal-state | src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs, src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs |

Three consecutive hotspot pairs remain 6/7.01, 7.01/7.02, 7.02/8.01; unchanged warnings are retained.

## Stale rows

| Current active initiative | Closure row | Current plan backlog |
|---|---|---|
| change-signature-class-struct-primary-ctor-add-remove | change-signature-class-struct-primary-ctor-add-remove | present |
| load-profile-full-suite | load-profile-full-suite | present |
| workspace-close-global-build-server-shutdown | workspace-close-global-build-server-shutdown | present |
| test-temp-reaper-live-owner | test-temp-reaper-live-owner | present |
| change-signature-service-refusals-public-message | change-signature-service-refusals-public-message | present |
| cross-project-public-refusals-echo-input | cross-project-public-refusals-echo-input | present |
| top10-good-preview-result-contract | top10-good-preview-result-contract | present |
| workspace-close-isolated-nuget-handles | workspace-close-isolated-nuget-handles | present |
| preview-diff-whitespace-omission | preview-diff-whitespace-omission | present |

## Anchor freshness

First three current pending initiatives: primary constructors17 (ChangeSignatureService85-92 primary refusal resolves), load-profile18 (AssemblyInfo9 Workers=0, verify-release521 test invocation and ci.yml194-203 timing/TRX artifact behavior resolve), workspace-close19 (WorkspaceTools244-248 machine-wide build-server shutdown; DetachedTestHostDrain229-245 case-insensitive query/kill; resolver92-105 transient query handle). Anchors resolve on live immutable base337fc2cde10eca3f591cc016c146dee7b893e26a. Old retained cycle3 evidence is historical; unchanged per-initiative adjudication was not rerun. Observed defects are already covered by the corresponding existing rows; recommend keeping their complete repairs tracked rather than duplicating rows.

Recommended next step: retain restore deferred and its original source checkpoint, keep row open, and obtain truthful complete-scope admission or independently correct decomposition before another restore implementation dispatch. Unrelated unchanged primary70000 remains eligible for serial native admission under the existing workflow; this result imposes no all-execution hold. Correct graph cache via sanctioned owner. Preserve primary70000 pending scope and all other unchanged dispositions/findings. A passing-with-warnings disposition review neither ships restore nor substitutes for red-first complete tests, exact-head implementation cold review, serialized full local producer and complete hosted Windows/Linux/SDK-floor validation.

