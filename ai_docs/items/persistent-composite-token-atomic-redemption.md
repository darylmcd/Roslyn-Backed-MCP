# persistent-composite-token-atomic-redemption — Claim one-time previews across hosts

**row:** `persistent-composite-token-atomic-redemption` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompositePreviewStore.cs`
- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs`
- `src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs`
- `tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs`
- `tests/RoslynMcp.Tests/Services/PersistentCompositeStorageTests.cs`

## Acceptance

- [ ] Redeem a persistent preview by atomically claiming its disk record before any mutation; exactly one process can acquire a token.
- [ ] Define fail-closed recovery for abandoned claims and mutation failure without making a partially applied operation replayable.
- [ ] Treat claim/delete I/O failure as a visible apply failure; remove comments that describe a still-valid payload as harmless.
- [ ] Preserve in-memory one-time semantics and public stale/unknown-token diagnostics.
- [ ] One two-reader regression races separate storage instances against the same token and proves one claimant, one rejection, and no second mutation.

## Evidence

Persistent lookup currently reads a valid payload without claiming it, while invalidation happens only after mutations and reload. Two hosts can therefore redeem the same one-time preview concurrently, and best-effort deletion can leave a valid payload replayable after the first apply.
