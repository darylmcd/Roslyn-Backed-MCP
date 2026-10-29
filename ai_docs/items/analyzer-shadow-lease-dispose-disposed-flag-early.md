# analyzer-shadow-lease-dispose-disposed-flag-early — Set AnalyzerShadowLoaderLease _disposed only after cleanup succeeds

**row:** `analyzer-shadow-lease-dispose-disposed-flag-early` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/AnalyzerReferenceIsolation.cs` (AnalyzerShadowLoaderLease.Dispose ~:524)
- `tests/RoslynMcp.Tests/WorkspaceLoadRegistrationCompensationTests.cs`

## Acceptance

- [ ] A failed Dispose can be retried and completes remaining cleanup; failure-injection seam added for the test.

## Evidence

- workspace-native-load-cleanup-gate-release cold review follow-up 3.
