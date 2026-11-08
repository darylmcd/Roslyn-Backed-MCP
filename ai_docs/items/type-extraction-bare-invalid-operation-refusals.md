# type-extraction-bare-invalid-operation-refusals — Unify TypeExtractionService refusals on a typed shape

**row:** `type-extraction-bare-invalid-operation-refusals` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs` (multi-part partial check, primary-ctor branch, bodyless / named `this(...)` / unresolved-target constructor topology branches)
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs`

## Acceptance

- [ ] Each listed refusal throws a typed exception (or the existing structured blocking-dependency shape) instead of a bare `InvalidOperationException`, with tests asserting the type.

## Evidence

- PR #2014 implementer sighting; the blocking-dependency family already carries structured refusals.
