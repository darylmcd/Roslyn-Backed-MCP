# type-extraction-base-ctor-blockers-decomposition — CollectBaseConstructorDispatchBlockers is one ~680-line method

**row:** `type-extraction-base-ctor-blockers-decomposition` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs`

## Acceptance

- [ ] The method is split into named collaborators with characterization tests green before the move; no behavior change.

## Evidence

- TypeExtractionService.cs ~184.

## Notes

Surface audit 2026-11-20 (server 5.1.1): get_complexity_metrics ranks CollectBaseConstructorDispatchBlockers first in the solution: cyclomatic complexity 284, 678 lines (215-892), maintainability index 0.
