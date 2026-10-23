# preview-store-explicit-internal-state — Preserve preview safety state at internal store producers

**row:** `preview-store-explicit-internal-state` · **pri:** `Medium` · **size:** `L`

## Anchors

- `src/RoslynMcp.Roslyn/Services/PreviewStore.cs:68` default-false Store; line 88 assigns Unspecified provenance.
- `src/RoslynMcp.Roslyn/Contracts/IPreviewStore.cs:23` legacy contract; lines 56/89 drop explicit kind through interface fallback.
- `src/RoslynMcp.Roslyn/Services/ChangeSignatureService.cs:429` signature preview producer discards computed diff safety state.
- `src/RoslynMcp.Roslyn/Services/BatchTestScaffolder.cs:232` batch preview computes bounded changes, then line 236 stores without them.
- `tests/RoslynMcp.Tests/PreviewStoreTests.cs` state preservation and contract tests.
- `tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs` signature preview/apply safety tests.
- `tests/RoslynMcp.Tests/ScaffoldingIntegrationTests.cs` batch preview/apply safety tests.

## Acceptance

- [ ] Probe Store overload implementations and all producers/consumers semantically before sealing implementation scope. Preserve actual per-file and per-solution truncation state and intended producer provenance at every in-tree producer; do not treat absence of a marker as proof of a complete preview without checking per-file markers.
- [ ] Observe regressions fail on old behavior: capped signature/batch preview is stored as truncated; no apply without the explicit documented override; incompatible specific routes reject tagged producer tokens. Keep valid untruncated previews and generic apply routes working.
- [ ] Migrate in-tree helpers/fakes with their callers; remove safety-state-dropping convenience paths used only to avoid migration. Preserve externally required compatibility only under the public release/deprecation contract; ADR plus migration note are required for breaking contracts.
- [ ] Cover interface dispatch as well as the concrete store: explicit provenance must not silently disappear through default interface delegation.

## Evidence

- Re-vet base: `c43e8fa6995151b22be6feb2c4a1020263b88ce5`. Original filing: PR #1745, immutable head `be8ad8b325f20ad18e775461def6686f29f4243a`.
- `PreviewStore.cs:64-69` calls the overload legacy and says it is kept so callers/tests need not adjust; exact forwarding construct: `=> Store(workspaceId, modifiedSolution, workspaceVersion, description, diffTruncated: false);`.
- `PreviewStore.cs:88`: `=> Store(workspaceId, modifiedSolution, workspaceVersion, description, diffTruncated, PreviewKind.Unspecified);`.
- `BatchTestScaffolder.cs:232-236` computes `SolutionDiffHelper.ComputeChangesAsync(state.OriginalSolution, state.Accumulator, ct)` then calls `var token = _previewStore.Store(workspaceId, state.Accumulator, _workspace.GetCurrentVersion(workspaceId), description);`. Its bounded changes are not supplied to Store, so truncation defaults false.
- `ChangeSignatureService.cs:524`: `var token = _previewStore.Store(workspaceId, accumulator, _workspace.GetCurrentVersion(workspaceId), description);`. `ChangeSignatureAddRemovePreviewBuilder.cs:206` populates changes using capped `DiffGenerator.GenerateUnifiedDiff(originalText, finalText, filePath)`.
- `IPreviewStore.cs:56` explicitly supplied kind is dropped: `=> Store(workspaceId, modifiedSolution, workspaceVersion, description, diffTruncated);`.
- `src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs:191-195`: `var actualKind = previewStore.PeekKind(previewToken);` and `if (actualKind == PreviewKind.Unspecified || actualKind == expectedKind) { return; }`. Untagged tokens bypass specific-route provenance checks by design.
- These source traces establish state loss; no runtime exploit, tests or gate was executed during re-vet. Medium bug classification is justified by bounded preview state being lost, beyond the original Low quality description.
- Live main contains no equivalent state-loss row; test-preview-store-cross-class-eviction concerns global capacity and different ownership semantics.

2026-10-07 source re-vet at6599be65: ExtractMethodService shared-expression preview tail also calls Store(workspaceId, accumulator, _workspace.GetCurrentVersion(workspaceId), description) after bounded fileChanges are computed; legacy overload defaults diffTruncated:false/PreviewKind.Unspecified. Include this producer in the existing all-producer safety-state/provenance migration, preserving generic apply and public contract-care requirements. No Store change belongs to the separate extraction argument-guard initiative.

2026-10-15 forced restore-graph prerequisite: native Workspace.CurrentSolution can differ from manager-authoritative evaluated metadata. All Store callers must supply explicit original snapshots as well as actual bounded diff state and intended producer kind; remove inference/default-state paths, migrate interface dispatch/fakes together. Direct plan20261015-restore-reviewed-graph.md and ADR0029 supersede the execution hold for this dependency. No completion claimed before original/fixed regressions and full delivery gates.

# Preview snapshot/version pairing review addendum proposal

| Field | Value |
|---|---|
| Existing row | preview-store-explicit-internal-state |
| Evidence | CrossProjectRefactoringService.cs124 captures original solution;131 and other semantic calls await;200/297 Store receives subsequently fetched current version. PreviewStore.cs67-82 stores supplied tuple without consulting manager. |
| Hosted protection | CrossProjectRefactoringTools.cs58/83 -> ToolDispatch.cs487 RunReadAsync; WorkspaceExecutionGate.cs281 reader lock spans callback. OrchestrationTools.cs78 similarly gated. |
| Classification | Pre-existing direct-service concurrency concern; new namespace semantic await does not create an unprotected hosted path. No runtime reproduction and no hosted exploit claimed. |
| Acceptance addition | Establish whether direct service callers must hold an execution lease. Enforce/document one complete authority boundary: capture original solution and version as one manager receipt and bind Store to it, or require validated caller lease. Deterministically suspend a producer, publish a competing update through supported service boundary, and prove no old-snapshot/new-version pairing can be issued. Preserve ordinary gated previews and sibling-edit rebase behavior. |
| Scope discipline | Inventory only; do not implement or advance the paused batch from this finding. |
