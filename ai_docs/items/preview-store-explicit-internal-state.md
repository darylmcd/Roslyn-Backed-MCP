# preview-store-explicit-internal-state — Preserve preview safety state at internal store producers

**row:** `preview-store-explicit-internal-state` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/PreviewStore.cs:68` default-false Store; line 88 assigns Unspecified provenance.
- `src/RoslynMcp.Roslyn/Contracts/IPreviewStore.cs:23` legacy contract; lines 56/89 drop explicit kind through interface fallback.
- `src/RoslynMcp.Roslyn/Services/ChangeSignatureService.cs:524` signature preview producer discards computed diff safety state.
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

