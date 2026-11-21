# split-service-di-rewrite-regex-to-syntax — Replace the regex DI registration rewrite with a syntax-based one

**row:** `split-service-di-rewrite-regex-to-syntax` · **pri:** `Medium` · **size:** `M` · **verified:** `e71aace5065e4d5a7edbefb3068e02c0a2579312`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs`
- `tests/RoslynMcp.Tests/CompositeSplitServiceDiPreviewTests.cs`

## Acceptance

- [ ] The rewrite locates registrations by syntax (any receiver name, chained calls, generic and typeof forms) with red-first tests per form; no regex remains.

## Evidence

- Plan sketch of split-service-compose-same-file-registration-mutation (merged).
