# bulk-refactoring-test-workspace-leak-and-unguarded-cleanup — close the leaked workspace and guard the fixture delete

**row:** `bulk-refactoring-test-workspace-leak-and-unguarded-cleanup` · **pri:** `Medium` · **size:** `S` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `tests/RoslynMcp.Tests/BulkRefactoringTests.cs` (`ReplaceInvocation_Preview_TagsTokenWithTheSameSharedKind` — the workspace load and its bare `finally`)

## Acceptance

- [ ] The test closes the workspace it loads and routes both cleanup steps through `CleanupFailureCollector.RunAfterFailureAsync`, matching the sibling test in the same file.
- [ ] One regression shape: a cleanup failure surfaces alongside — not instead of — the primary assertion failure.

## Evidence

Traced by the cold code-quality review of PR #1384, not hypothesized. `TestBase.DisposeServices()` is an explicit no-op and `WorkspaceManager` is a single assembly-wide static torn down only in `AssemblyLifecycle.Cleanup`, so the workspace the test loads stays resident for the whole assembly run. `TestFixtureFileSystem.DeleteDirectoryIfExists` rethrows on its fifth attempt (its catch filter is `when (attempt < maxAttempts && ...)`), so a delete failure inside the bare `finally` REPLACES the test's real failure. The sibling test in the same file already uses `CleanupFailureCollector` + `WorkspaceManager.Close` precisely to avoid both.
