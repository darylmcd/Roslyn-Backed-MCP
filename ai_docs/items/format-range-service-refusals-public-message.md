# format-range-service-refusals-public-message — Publish safe formatter range refusals

**row:** `format-range-service-refusals-public-message` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs`
- `tests/RoslynMcp.Tests/FormatRangeServiceTests.cs`

## Acceptance

- [ ] All eight coordinate/order/bounds argument refusals name the actual parameter and the applicable bounds or file line count through PublicArgumentException.
- [ ] The unprojectable formatter-change refusal gives safe PublicInvalidOperationException recovery text; retain the true internal formatter invariant.
- [ ] Preserve published category and BCL exceptionType; never expose raw request text, paths or lower-layer messages.
- [ ] Observe envelope regressions fail on old source; retain valid preview/apply and internal-invariant coverage.

## Evidence

- Source checked at 89ccceb3c58f8dbfc02f856358f287535253c7d1: range validation at RefactoringService.cs:574-600 has eight plain ArgumentException sites; projection refusal survives at :1206, while :1378 is an internal formatter invariant.
- The historical unrelated-line-count refusal was replaced; current projection behavior must be re-derived rather than copying the old acceptance text.

## Context

- Split from change-signature-refusals-public-message; independent of change-signature permutations. Current Core public/refusal contracts replace the missing historical Family design pointer.
