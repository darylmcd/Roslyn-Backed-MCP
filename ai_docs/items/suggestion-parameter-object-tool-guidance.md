# suggestion-parameter-object-tool-guidance — Recommend parameter-object tools for high parameter counts

**row:** `suggestion-parameter-object-tool-guidance` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RefactoringSuggestionService.cs`
- `tests/RoslynMcp.Tests/RefactoringSuggestionTests.cs`

## Acceptance

- [ ] Recommend the registered introduce-parameter-object workflow for a seven-parameter method and align the sort comment with the actual stable category ordering.
- [ ] Prove the behavior with one focused regression shape and preserve the public DTO contract.

## Evidence

- 2026-09-14 direct cohesion remediation source review: The parameter-count suggestion recommends extract_type_preview/extract_type_apply even though its description asks for a parameter object; the sort comment also incorrectly claims numeric complexity ordering.
