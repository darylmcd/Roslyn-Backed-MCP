# parameter-object-declaration-metadata-preservation — Preserve parameter declaration metadata

**row:** `parameter-object-declaration-metadata-preservation` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs` (`RewriteMethodDeclaration`, `BuildDtoSource`)
- `tests/RoslynMcp.Tests/ParameterObjectPreviewTests.cs`

## Acceptance

- [ ] Define how attributes and declaration metadata on grouped parameters transfer to generated positional-record parameters/properties.
- [ ] Rewrite or preserve `nameof(groupedParameter)` references in attributes on retained parameters and method/return declarations.
- [ ] Refuse metadata that cannot be transferred without changing its declared target or observable reflection contract.
- [ ] Add one compile-and-reflection regression with a grouped parameter attribute and a retained-parameter attribute that names the grouped parameter.

## Evidence

- Grouped parameter syntax is dropped wholesale, losing its attributes, while attributes on retained declaration nodes can keep `nameof` references to parameters that no longer exist.

## Acceptance amendment (2026-08-13 adversarial review)

- Preserve or actionably refuse string-valued parameter links such as `CallerArgumentExpression` and `InterpolatedStringHandlerArgument`, not only `nameof` syntax.
