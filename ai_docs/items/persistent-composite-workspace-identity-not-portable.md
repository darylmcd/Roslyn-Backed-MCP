# persistent-composite-workspace-identity-not-portable — Resolve receiving session before redemption

**row:** `persistent-composite-workspace-identity-not-portable` · **pri:** `High` · **size:** `L`

## Regression shape

- Persisted composite tokens retain a creator-process workspace UUID and counter; public redemption in an independent receiving host cannot resolve the correct loaded session before claim or mutation.

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompositePreviewStore.cs:35-43`
- `src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs:76-87`
- `src/RoslynMcp.Host.Stdio/Tools/OrchestrationTools.cs:124-130`
- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs:43`
- `src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs`

## Acceptance

- [ ] Persist stable workspace identity; resolve one eligible receiving session before claim or writes. Deepen every affected entry construction/constructor and the persisted snapshot contract.
- [ ] Missing, ambiguous or wrong identity refuses without writes or claim loss. Do not infer cross-host snapshot equivalence from numeric process-local versions.
- [ ] Preserve one-time ownership, TTL, trusted-root safety and changed-source refusal. Bind the persisted preview to the correct source snapshot before mutation.
- [ ] Red-first public redemption uses two independently configured hosts and a restarted receiving host, with distinct session UUIDs. Storage instances sharing a fake UUID are insufficient.
- [ ] Record ADR and migration evidence for the published persisted contract. Re-vet the held lifecycle boundary against the landed prerequisite before execution.

## Evidence

- Source-only re-vet at `683dc0fcb503d02f49063e6909e327272ab4044c`: `PersistentCompositeStorage.cs:76-87,320-325` stores creator UUID/version without stable workspace identity.
- `WorkspaceManager.cs:368-369,1591,1613-1615` creates a fresh GUID and session-local counter; first load increments at :1269, apply at :1041, undo may rewind through :935. Equal counters do not establish equal source snapshots.
- Public dispatch `OrchestrationTools.cs:124-130` calls creator-memory `BoundedStore.PeekWorkspaceId:84-96`. A disk-aware peek returning the old UUID still fails receiving-session admission in `WorkspaceExecutionGate.cs:212-214` through `WorkspaceManager.cs:200-201`.
- `CompositeApplyOrchestrator.cs:43` discards retrieved version; direct orchestration can reach writes before reload rejects the creator UUID. No runtime proof or public cross-host success is claimed by this planning review.

## Context

- One portability regression shape, not an additional selected initiative. Full deepen scope is required; five production anchors are a starting boundary, not a correctness cap.
- Explicit prerequisite for the held `preview-token-lifecycle-evidence` boundary. A follow-up row alone does not discharge this prerequisite.
