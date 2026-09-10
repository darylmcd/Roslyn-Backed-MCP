# workspace-manager-file-watcher-disposal-ownership — Establish one file-watcher disposal owner

**row:** `workspace-manager-file-watcher-disposal-ownership` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:1053`
- `src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs:108`
- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs`
- `tests/RoslynMcp.Tests/Services/WorkspaceManagerTests.cs`

## Acceptance

- [ ] Production registrations and `WorkspaceManager` have one explicit owner for the singleton `IFileWatcherService` lifetime.
- [ ] Provider shutdown and direct workspace-manager disposal cannot dispose the watcher twice.
- [ ] The chosen ownership contract is explicit in the registration and disposal paths.

## Regression

Build the production service provider, resolve `WorkspaceManager`, dispose the manager and then the provider, and prove the watcher shutdown path executes once without an exception.

## Evidence

`IFileWatcherService` is registered as a singleton, while `WorkspaceManager.Dispose` unconditionally disposes its injected watcher. Provider disposal then owns the same singleton again; `FileWatcherService` has no explicit idempotent disposal guard.
Bundled under test-service-container-production-di-lifetime in plan 20260910T192234Z_backlog-remediate because the provider-backed test composition is the root-cause reproduction and exactly-once watcher-disposal regression.
