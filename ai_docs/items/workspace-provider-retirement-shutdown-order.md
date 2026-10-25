# workspace-provider-retirement-shutdown-order — Verify provider retirement disposal order

**row:** `workspace-provider-retirement-shutdown-order` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs:31` — manager/gate resolution ownership through45.
- `src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs:190` — throttle release; gate semaphore disposal552–555.
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:1242` — retirement drain before manager teardown through1252.
- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkSourceLifecycleTests.cs`

## Acceptance

- [ ] Reproduce or disprove the risk with actual provider disposal while manager-owned root retirement is already inside the gate; direct manager-only tests are insufficient.
- [ ] If real, drain retirement before provider gate teardown while preserving external manager ownership, async failure visibility and ordinary host shutdown.
- [ ] Preserve cancellation, operation retention and semaphore ownership without synchronous joins or a new lifetime controller.
- [ ] Complete fresh review and appropriate qualification, or close the unsupported premise with evidence.

## Evidence

- Unproven source-derived ordering risk: manager is captured while gate resolution is lazy; reverse provider disposal may dispose gate semaphores before manager retirement drains.
- Current manager retirement already drains its own work. No actual provider-order failure has been reproduced or claimed.

## Context

- Independent nonblocking investigation; no restore closure dependency. Re-derive actual DI capture/disposal order before implementation.
