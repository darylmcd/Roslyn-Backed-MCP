# split-service-di-staging-hygiene — Tidy split-service DI staging

**row:** `split-service-di-staging-hygiene` · **pri:** `Low` · **size:** `S` · **verified:** `e71aace5065e4d5a7edbefb3068e02c0a2579312`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs`
- `tests/RoslynMcp.Tests/CompositeSplitServiceDiPreviewTests.cs`

## Acceptance

- [ ] mutations.Single throws InvalidOperationErrors.Internal (consistent with StageEdit).
- [ ] Path comparisons use FileSystemPath.Comparer, including the test helper PathsEqual.
- [ ] Line endings follow the file (CRLF file stays CRLF), red-first test.

## Evidence

- Cold implementation review of the split-compose PR, follow-up 1 and 3; plan sketches.
