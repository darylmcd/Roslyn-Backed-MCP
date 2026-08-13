# type-extraction-new-type-name-validation — Validate generated extraction type names

**row:** `type-extraction-new-type-name-validation` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs`
- `src/RoslynMcp.Roslyn/Helpers/IdentifierValidation.cs`
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs`

## Acceptance

- [ ] Reject null, empty, whitespace, and invalid C# identifiers before path or syntax construction.
- [ ] Throw an actionable `ArgumentException` naming `newTypeName` rather than indexing or emitting malformed syntax.
- [ ] Preserve valid Unicode identifiers.
- [ ] Add one input-validation regression table covering invalid and valid shapes.

## Evidence

- The service indexes `newTypeName[0]` and emits the value without the repository's identifier validation.
