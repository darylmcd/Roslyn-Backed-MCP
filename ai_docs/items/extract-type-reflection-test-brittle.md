# extract-type-reflection-test-brittle — Extract-type tests reflect into private members

**row:** `extract-type-reflection-test-brittle` · **pri:** `Low` · **size:** `S` · **deps:** `test-private-field-reflection-helper`

## Anchors

- `tests/RoslynMcp.Tests/TypeExtractionTests.cs`

## Acceptance

- [ ] Tests that reach private TypeExtractionService members by reflection use the shared reflection helper (`test-private-field-reflection-helper`) or a public seam, so a rename fails at compile time.

## Evidence

- Found during the PR 1951 modifier-consolidation review: the consolidation required editing reflection-by-name lookups.

## Notes

2026-11-11 (PR #2066 follow-up): the ModifierRewrites_* tests in tests/RoslynMcp.Tests/TypeExtractionTests.cs reach private static EnsurePublicAccessibility and StripInheritanceOnlyModifiers by name through reflection (:41-45, :68-71, :93-94, helper ApplyModifierRewrite). They are covered by this row's acceptance; a rename of either method fails at run time, not compile time. Prefer an internal seam (InternalsVisibleTo is already the usual route) over the field-reflection helper.
