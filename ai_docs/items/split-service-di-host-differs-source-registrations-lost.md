# split-service-di-host-differs-source-registrations-lost — Warn when source-type registrations remain in the source file but the host registration file differs

**row:** `split-service-di-host-differs-source-registrations-lost` · **pri:** `Medium` · **size:** `S` · **verified:** `e71aace5065e4d5a7edbefb3068e02c0a2579312`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs`
- `tests/RoslynMcp.Tests/CompositeSplitServiceDiPreviewTests.cs`

## Acceptance

- [ ] With a host registration file different from the source file, registrations of the source type that stay in the source file are rewritten or warned about (red-first test).

## Evidence

- Cold implementation review of the split-compose PR, follow-up 2.
