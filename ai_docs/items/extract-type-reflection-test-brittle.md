# extract-type-reflection-test-brittle — Extract-type tests reflect into private members

**row:** `extract-type-reflection-test-brittle` · **pri:** `Low` · **size:** `S` · **deps:** `test-private-field-reflection-helper`

## Anchors

- `tests/RoslynMcp.Tests/TypeExtractionTests.cs`

## Acceptance

- [ ] Tests that reach private TypeExtractionService members by reflection use the shared reflection helper (`test-private-field-reflection-helper`) or a public seam, so a rename fails at compile time.

## Evidence

- Found during the PR 1951 modifier-consolidation review: the consolidation required editing reflection-by-name lookups.
