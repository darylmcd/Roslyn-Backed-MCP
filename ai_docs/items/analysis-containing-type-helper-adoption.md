# analysis-containing-type-helper-adoption — Share containing-type resolution

**row:** `analysis-containing-type-helper-adoption` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/RoslynSymbolTraversal.cs`
- `src/RoslynMcp.Roslyn/Services/CouplingAnalysisService.cs:198-365`
- `src/RoslynMcp.Roslyn/Services/ConsumerAnalysisService.cs:20-147`
- `tests/RoslynMcp.Tests/SymbolTraversalTests.cs`

## Acceptance

- [ ] One shared helper walks syntax ancestors and returns the nearest enclosing `TypeDeclarationSyntax` as an `INamedTypeSymbol`.
- [ ] The helper returns null for nodes outside a type and passes the caller cancellation token to semantic lookup.
- [ ] Coupling and consumer analysis call the helper; `FindContainingTopLevelType` and service-local `FindContainingType` are removed.
- [ ] Nested-type, outside-type, coupling, and consumer behavior remain unchanged and the duplicate-method scan no longer reports the pair.

## Evidence

- Both services currently contain the same ancestor walk with different names.

## Dependencies

- `analysis-type-traversal-enumeration-helper`
