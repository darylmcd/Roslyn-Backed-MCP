# type-extraction-external-warnings-skip-partial-parts — External consumer warnings skip private members used from other partial parts

**row:** `type-extraction-external-warnings-skip-partial-parts` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs`

## Acceptance

- [ ] A private member referenced from another partial part is reported; red regression first.

## Evidence

- TypeExtractionService.cs ~1470 (CollectExternalConsumerWarningsAsync).
