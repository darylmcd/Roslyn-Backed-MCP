# test-assembly-cleanup-failure-observability — Surface shared test cleanup failures

**row:** `test-assembly-cleanup-failure-observability` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/TestBase.cs`
- `tests/RoslynMcp.Tests/AssemblyCleanup.cs`

## Acceptance

- [ ] Capture workspace-manager and configured-server disposal failures instead of swallowing them.
- [ ] Always execute current temp-root cleanup in `finally`, even when one shared resource fails to dispose.
- [ ] Report one aggregate cleanup failure after every owned resource and exact temp tree has been attempted.
- [ ] Add an injected disposal-failure regression proving later cleanup still runs and the original failure is observable.

## Evidence

- `TestBase.DisposeAssemblyResourcesAsync` contains bare best-effort catches for both shared resource owners, so file-watcher, child-process, pipe, or host teardown failures can be silently lost at assembly cleanup.
