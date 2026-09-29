# workspace-load-restore-next-call — Make load results actionable

**row:** `workspace-load-restore-next-call` · **pri:** `High` · **size:** `M` · **deps:** `compile-check-restore-next-call`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs:682-702`
- `src/RoslynMcp.Core/Models/WorkspaceStatusSummaryDto.cs:136-165`
- (new) `tests/RoslynMcp.Tests/WorkspaceLoadRestoreNextCallTests.cs`
- `docs/product-contract.md`

## Acceptance

- [ ] When `workspace_load` or `workspace_reload` leaves `restoreRequired:true`, both lean and verbose results expose the same top-level `nextCall` naming `workspace_reload` with `autoRestore:true`.
- [ ] Append the action in the load-result serializer without changing `workspace_status` DTOs or their advertised output schema.
- [ ] The missing-assets hint describes missing assets rather than falsely claiming restore inputs changed; red-first wire tests cover lean and verbose outputs.
- [ ] Document this additive output field in the product contract.

## Evidence

- Parent `compile-check-restore-required-handshake`: `WorkspaceTools.cs:682-702` serializes the load result and currently omits a structured recovery call.

## Context

- Reuses `NextCallDto` from the prerequisite slice. The restore behavior remains separately opt-in until the bounded default-restore slice lands.
