# file-watcher-test-doubles-shared-base — Share one IFileWatcherService test double base and retire the test-only ClearStale

**row:** `file-watcher-test-doubles-shared-base` · **pri:** `Low` · **size:** `L`

## Anchors

- `tests/RoslynMcp.Tests/AnalyzerShadowLoaderLifecycleTests.cs:811`
- `tests/RoslynMcp.Tests/MissingWorkspaceRootRetirementTests.cs:214`
- `tests/RoslynMcp.Tests/TestInfrastructure/TestServiceContainer.cs:298`
- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkSourceLifecycleTests.cs:509`
- `tests/RoslynMcp.Tests/WorkspaceLoadRegistrationCompensationTests.cs:561`
- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs:292`
- `tests/RoslynMcp.Tests/ExternalEditStalenessTests.cs:781`

## Acceptance

- [ ] An interface change touches one test base instead of five doubles.
- [ ] The one test caller of `FileWatcherService.ClearStale(string)` uses `ClearStaleIfNoExternalEditSince`, and the test-only method is removed.

## Evidence

- PR 2050 touched five doubles per interface change; ClearStale(string) kept internal only for FileWatcherClearStaleAwaiterTests.

## Context

- The five doubles share a fixture-interface refactor across five files. Split retirement of ClearStale(string) into a production-and-caller row that can land independently.
