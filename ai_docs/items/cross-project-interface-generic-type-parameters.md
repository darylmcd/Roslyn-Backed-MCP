# cross-project-interface-generic-type-parameters — Preserve generic declaration parameters during interface extraction

**row:** `cross-project-interface-generic-type-parameters` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:567`
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:778`
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs`

## Acceptance

- Preserve required source type parameters and constraints in generated interfaces and construct corresponding bound uses.
- Cover direct extraction and dependency inversion with used, unused and constrained type parameters using semantic compilation checks.
- Preserve unrelated formatting and bindings; do not replace generic support with a blanket rejection or compatibility shim.

## Evidence

- Static review2026-10-17: both interface extraction paths accept INamedTypeSymbol without an Arity guard; CreateInterfaceCompilationUnit builds accessibility and members but no TypeParameterList or constraint clauses.
- A member using the source declaration parameter can therefore reference an undeclared parameter in the generated interface. No runtime reproduction or fix is claimed.
- Separate declaration-generation mechanism from cross-project-interface-reference-binding; recorded during that prerequisite review, not selected for implementation.
