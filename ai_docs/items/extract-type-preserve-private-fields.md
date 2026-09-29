# extract-type-preserve-private-fields — Keep extracted field accessibility

**row:** `extract-type-preserve-private-fields` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:567-570`
- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:1024-1055`
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs`

## Acceptance

- [ ] An extracted private field stays private in the generated type; access changes occur only where composition requires them.
- [ ] A red-first preview/apply test asserts accessibility and compilation.

## Evidence

- Parent `extract-type-breaks-interfaces-and-publicizes-fields`: `EnsurePublicAccessibility` rewrites private fields to public.

## Context

- Separate regression mechanism from source interface obligations and untouched-region formatting.
