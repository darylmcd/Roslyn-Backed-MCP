# cohesion-multifile-partial-semantic-model — cohesion-multifile-partial-semantic-model

**row:** `cohesion-multifile-partial-semantic-model` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CohesionAnalysisService.cs`
- `src/RoslynMcp.Roslyn/Services/RefactoringSuggestionService.cs`
- `tests/RoslynMcp.Tests/CohesionAnalysisTests.cs`
- `tests/RoslynMcp.Tests/RefactoringSuggestionTests.cs`

## Acceptance

- [ ] Resolve each partial member with a semantic model for its own syntax tree and emit one cohesion metric per logical partial type.
- [ ] Do not swallow a cross-tree semantic-model exception into an incomplete result; `suggest_refactorings` remains available when a project contains multi-file partial types.
- [ ] Add a two-file partial regression that proves complete cohesion output and a successful suggestion projection.

## Evidence

- A live Phase-2 audit reproduced `ScaffoldingService` partial-member analysis failing with “Syntax node is not within syntax tree”; the failure yields `failedTypeCount=3` and makes `suggest_refactorings` fail closed.
