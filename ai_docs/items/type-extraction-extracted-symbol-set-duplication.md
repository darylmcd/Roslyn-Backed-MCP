# type-extraction-extracted-symbol-set-duplication — Extracted-symbol discovery loop is copied four times

**row:** `type-extraction-extracted-symbol-set-duplication` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs`

## Acceptance

- [ ] One discovery helper with explicit filter/identity parameters replaces the copies at ~194, 1462, 1523, 1595; every caller's semantics preserved by characterization tests.

## Evidence

- PR 1990 left the copies in place; semantics differ per copy, so unification needs the characterization tests first.
