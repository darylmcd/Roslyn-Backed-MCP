# type-extraction-composition-constructor-coverage — Initialize extracted composition on every construction path

**row:** `type-extraction-composition-constructor-coverage` · **pri:** `High` · **size:** `S` · **deps:** `type-extraction-member-shape-validation`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs`
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs`

## Acceptance

- [ ] Every constructible source-type path initializes the injected readonly extracted-type field exactly once.
- [ ] Handle implicit/no-constructor types, overloaded constructors with `this(...)` chains, and expression-bodied constructors, or refuse an unsupported topology before preview generation.
- [ ] Generated previews compile-check before being returned.
- [ ] Add one regression group covering those three constructor shapes.

## Evidence

- Composition injection updates only the first constructor, creates none for an implicit constructor, and does not add an assignment to expression-bodied constructors.
