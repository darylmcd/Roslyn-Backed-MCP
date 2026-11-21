# split-service-di-rewrite-first-registration-only — Rewrite every matching DI registration, not only the first

**row:** `split-service-di-rewrite-first-registration-only` · **pri:** `Medium` · **size:** `S` · **verified:** `e71aace5065e4d5a7edbefb3068e02c0a2579312`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs`
- `tests/RoslynMcp.Tests/CompositeSplitServiceDiPreviewTests.cs`

## Acceptance

- [ ] A file with two registrations of the source type has both rewritten (red-first test); the dropped-registration warning counts stay correct.

## Evidence

- Plan sketch of split-service-compose-same-file-registration-mutation (merged).
