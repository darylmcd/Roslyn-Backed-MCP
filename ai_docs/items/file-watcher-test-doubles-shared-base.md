# file-watcher-test-doubles-shared-base — Share one IFileWatcherService test double base and retire the test-only ClearStale

**row:** `file-watcher-test-doubles-shared-base` · **pri:** `Low` · **size:** `S` · **verified:** `a2a51ec4de033bfd3aa36ba9d238b73e849b0f9a`

## Anchors

- `tests/RoslynMcp.Tests/ExternalEditStalenessTests.cs`
- `tests/RoslynMcp.Tests/WorkspaceLoadRegistrationCompensationTests.cs`
- `tests/RoslynMcp.Tests/AnalyzerShadowLoaderLifecycleTests.cs`
- `tests/RoslynMcp.Tests/TestInfrastructure/TestServiceContainer.cs`

## Acceptance

- [ ] An interface change touches one test base instead of five doubles; FileWatcherService.ClearStale(string) (internal, one test caller) is removed with its test migrated.

## Evidence

- PR 2050 touched five doubles per interface change; ClearStale(string) kept internal only for FileWatcherClearStaleAwaiterTests.
