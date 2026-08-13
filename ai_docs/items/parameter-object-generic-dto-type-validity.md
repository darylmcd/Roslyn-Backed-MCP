# parameter-object-generic-dto-type-validity — Validate generated DTO parameter types

**row:** `parameter-object-generic-dto-type-validity` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs` (`ResolveDtoProject`, `BuildDtoSource`)
- `tests/RoslynMcp.Tests/ParameterObjectPreviewTests.cs`

## Acceptance

- [ ] Analyze every grouped parameter type in the generated top-level DTO context before building a preview.
- [ ] Propagate required method/containing-type generic parameters and constraints, or refuse with the dependent parameter/type named.
- [ ] Reject inaccessible parameter types and cross-project type references that make the chosen DTO visibility or project invalid.
- [ ] Add one table-driven compile regression for a method type parameter and a less-accessible nested type; no malformed preview token is stored.

## Evidence

- `BuildDtoSource` emits `ITypeSymbol.ToDisplayString()` into a non-generic top-level record without validating generic scope, accessibility, or availability from a cross-project DTO location.
