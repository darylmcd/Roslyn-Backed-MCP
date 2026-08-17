# test-assembly-cleanup-failure-observability — Surface shared test cleanup failures

**row:** `test-assembly-cleanup-failure-observability` · **pri:** `Low` · **size:** `S` · **deps:** `host-process-metadata-tests-async-await`

## Anchors

- `tests/RoslynMcp.Tests/TestBase.cs`
- `tests/RoslynMcp.Tests/AssemblyCleanup.cs`
- `tests/RoslynMcp.Tests/HostProcessMetadataTests.cs`

## Acceptance

- [ ] Capture workspace-manager and configured-server disposal failures instead of swallowing them.
- [ ] Always execute current temp-root cleanup in `finally`, even when one shared resource fails to dispose.
- [ ] Report one aggregate cleanup failure after every owned resource and exact temp tree has been attempted.
- [ ] Add an injected disposal-failure regression proving later cleanup still runs and the original failure is observable.
- [ ] Make the metadata fixture's temp-tree deletion failure observable while its environment reset still runs.

## Evidence

- `TestBase.DisposeAssemblyResourcesAsync` contains bare best-effort catches for both shared resource owners, so file-watcher, child-process, pipe, or host teardown failures can be silently lost at assembly cleanup.
- `HostProcessMetadataTests.Cleanup` also suppresses temp-tree deletion failures even though the touched fixture can leave process metadata artifacts behind.
