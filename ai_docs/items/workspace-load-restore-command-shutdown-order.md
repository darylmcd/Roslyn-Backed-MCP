# workspace-load-restore-command-shutdown-order — Qualify command-executor ownership during an admitted workspace load

**row:** `workspace-load-restore-command-shutdown-order` · **pri:** `Medium` · **size:** `M` · **verified:** `e6c61174610c8d045d559f0621b7b02d31cecbfa`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs` — optional auto-restore within an admitted workspace load.
- `src/RoslynMcp.Roslyn/Services/GatedCommandExecutor.cs` — admission closes when disposal starts.
- `src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs` — provider lifetime and dependency capture.

## Acceptance

- [ ] Establish actual stdio host stop and service provider capture order. With real services, pause a workspace load before optional restore and observe which admitted work is awaited during shutdown.
- [ ] If an admitted load can outlive command-executor admission, preserve correct ownership or classified cancellation through restore and reload. Keep the normal stable load contract.

## Evidence

- 5.x shutdown implementation review. The workspace operation gate and the command executor independently own their drains. Source inspection identified a possible cross-dependency race; no transport-level reproduction yet.
