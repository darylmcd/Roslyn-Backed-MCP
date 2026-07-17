# analysis-type-traversal-enumeration-helper — Share complete Roslyn type enumeration

**row:** `analysis-type-traversal-enumeration-helper` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ImpactSweepService.cs:397-422`
- `src/RoslynMcp.Roslyn/Services/CouplingAnalysisService.cs:121-148`
- `src/RoslynMcp.Roslyn/Services/SymbolSearchService.cs:211-227`
- `src/RoslynMcp.Roslyn/Helpers/RoslynSymbolTraversal.cs` (new)
- `tests/RoslynMcp.Tests/SymbolTraversalTests.cs` (new)

## Acceptance

- [ ] One lazy shared helper enumerates namespace and named-type trees at arbitrary nesting depth.
- [ ] The implementation uses a stack of enumerator frames, preserves deterministic depth-first member order, and has O(tree depth) auxiliary storage.
- [ ] `allowedKinds` filters yielded symbols without pruning matching descendants beneath nonmatching parents.
- [ ] Impact sweep, coupling analysis, and symbol search call the shared helper; their three local enumeration bodies are removed.
- [ ] Tests cover deep classes beneath non-class parents, sibling order, filtered descendants, and relevant service behavior.

## Evidence

- The current impact helper enumerates only one nested level and prunes descendants when a parent type does not match the filter.
- Execute-time cold review rejected placing the general helper inside a service file.

## Dependencies

- None.
