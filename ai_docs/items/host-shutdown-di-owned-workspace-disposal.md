# host-shutdown-di-owned-workspace-disposal — Give DI sole workspace-disposal ownership

**row:** `host-shutdown-di-owned-workspace-disposal` · **pri:** `Medium` · **size:** `S` · **deps:** `mcp-logging-stderr-otel-migration`

## Anchors

- `src/RoslynMcp.Host.Stdio/Program.cs`
- New `tests/RoslynMcp.Tests/HostShutdownLifecycleTests.cs`.

## Acceptance

- [ ] Remove manual disposal of the DI-owned `IWorkspaceManager` from `ApplicationStopping`; the host/container remains the sole owner.
- [ ] Prove an in-flight request completes before the container tears the workspace singleton down; stdout transport draining remains owned by `stdio-shutdown-flush-transport-ownership`.
- [ ] A disposal-count sentinel observes exactly one teardown through normal exit and cancellation-driven exit.

## Evidence

- `RunAsync` already disposes the host and singleton container.
- `Program.cs` also manually disposes the singleton during `ApplicationStopping`, earlier than container teardown.
- The earlier claim that the host itself leaks is false; duplicate and potentially premature workspace ownership is the actual defect.
