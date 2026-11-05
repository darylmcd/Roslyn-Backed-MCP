# type-extraction-ctor-body-refusal-overbroad — Constructor-ancestor refusal is broader than the unsafe case

**row:** `type-extraction-ctor-body-refusal-overbroad` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionSameFileConsumerRewriter.cs`
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs`

## Acceptance

- [ ] EnsureInstanceReferenceCanBeRewritten refuses only initializer-argument positions; the characterization test that pins the current breadth is flipped deliberately with a reason.

## Evidence

- PR 1990: the refusal also covers constructor bodies, but InjectFieldAndCtorParameter inserts the assignment first (TypeExtractionService.cs ~1568-1572).
