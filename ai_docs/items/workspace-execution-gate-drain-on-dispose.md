# workspace-execution-gate-drain-on-dispose — Drain admitted workspace-gate operations before disposal

**row:** `workspace-execution-gate-drain-on-dispose` · **pri:** `Medium` · **size:** `M` · **verified:** `ddf4cc6d0f4d1ee8a165571c099820583f52eeb9`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs:141`
- `src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs:189`
- `src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs:614`
- `src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs`

## Acceptance

- [ ] Reproduce teardown during direct active or queued gate operations. Preserve admitted execution and cancellation through release, reject later admission, and verify provider shutdown.

## Evidence

- Both semaphores are immediately disposed while entry points can own them. The provider-specific root retirement ordering proof remains covered by workspace-provider-retirement-shutdown-order.
