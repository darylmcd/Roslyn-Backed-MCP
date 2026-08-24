# test-temp-cleanup-helper-adoption-wave-2 — Replace best-effort directory deletion copies

**row:** `test-temp-cleanup-helper-adoption-wave-2` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/TestInfrastructure/TestFixtureFileSystem.cs`
- `tests/RoslynMcp.Tests/ExtractInterfaceSemanticUsingsTests.cs`
- `tests/RoslynMcp.Tests/ExtractMethodThisExclusionTests.cs`

## Acceptance

- [ ] Replace each private untyped best-effort directory-delete catch with `TestFixtureFileSystem.DeleteDirectoryIfExists`.
- [ ] Preserve any primary assertion failure while retaining a cleanup failure through the repository cleanup collector where aggregation is required.
- [ ] Remove the now-dead local helpers and comments that claim the OS will reclaim leaked fixture state.
- [ ] One locked/read-only fixture regression proves bounded retry and visible terminal failure.

## Evidence

These fixtures copy the same recursive-delete helper and swallow every exception. The current session removed the first two copies from `BulkRefactoringTests` and `ExtractMethodFormatRegressionTests`; this bounded wave prevents the remaining pattern from staying silent.
2026-08-24 adjacent review: also include tests/RoslynMcp.Tests/ThirdPartyNoticeDriftTests.cs. Its raw Directory.Delete in finally can replace a primary assertion failure. Keep this wave within the three-test-file limit by swapping it for one listed fixture or split a later wave.
