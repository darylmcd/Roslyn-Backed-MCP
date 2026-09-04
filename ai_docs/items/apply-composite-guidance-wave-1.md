# apply-composite-guidance-wave-1 — Migrate producer and DTO composite-apply guidance

**row:** `apply-composite-guidance-wave-1` · **pri:** `Low` · **size:** `M` · **deps:** `apply-composite-canonical-alias-surface`

## Anchors

- `src/RoslynMcp.Core/Models/RecordFieldAddSatelliteDto.cs`
- `src/RoslynMcp.Core/Models/ScaffoldingDtos.cs`
- `src/RoslynMcp.Roslyn/Services/BatchTestScaffolder.cs`
- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs`

## Acceptance

- [ ] Replace the deprecated route in the four named producer and DTO documentation surfaces.
- [ ] Preserve all token-store and redemption semantics.
- [ ] Require scoped stale-name search to return zero in these four files.

## Evidence

These producer and DTO descriptions teach first-party consumers to call the deprecated name.

## Context

Depends on `apply-composite-canonical-alias-surface`. Migration child split from `tool-consolidation-adr-and-alias-machinery`.
