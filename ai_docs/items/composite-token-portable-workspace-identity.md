# composite-token-portable-workspace-identity — Redeem persisted composite tokens in an independent host

**row:** `composite-token-portable-workspace-identity` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompositePreviewStore.cs`
- `src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs`
- `src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs`
- `src/RoslynMcp.Core/Services/ICompositePreviewStore.cs`
- `tests/RoslynMcp.Tests/Services/PersistentCompositeStorageTests.cs`
- (new) tests/RoslynMcp.Tests/CompositeTokenCrossHostRedemptionTests.cs
- (new) docs/decisions/0032-persisted-composite-token-workspace-identity.md

## Acceptance

- [ ] Red first: host A (own DI, own session UUID) previews; host B (separate DI, shared persist dir, distinct UUID) redeems through `OrchestrationTools.ApplyComposite` and writes; a second redemption refuses.
- [ ] Receiving host not loaded, ambiguous (two same-path sessions), wrong solution, or a legacy v1 record with a non-live creator: refuses with zero writes and the payload file still present; redeems after the correct load in a restarted host.
- [ ] v1 records still read; v2 keeps WorkspaceId/WorkspaceVersion for diagnostics only; the version is never used as cross-host equivalence.
- [ ] ADR 0032 plus a changelog.d fragment with migration note.

## Evidence

- Verified at f15e28e7: `PeekWorkspaceId` is memory-only (`BoundedStore.cs:106-121`); `WorkspaceExecutionGate.cs:213-215`; orchestrator `CompositeApplyOrchestrator.cs:38/44/165`. Split from `persistent-composite-workspace-identity-not-portable`.
