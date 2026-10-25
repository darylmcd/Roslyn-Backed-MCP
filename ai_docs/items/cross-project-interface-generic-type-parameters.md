# cross-project-interface-generic-type-parameters — Preserve generic parameters in extracted interfaces

**row:** `cross-project-interface-generic-type-parameters` · **pri:** `Medium` · **size:** `M` · **deps:** —

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:565`
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:603`
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs`

## Acceptance

- [ ] Preserve required source/enclosing declaration type parameters and constraints plus generic method type parameters/constraints in emitted interfaces and bound uses.
- [ ] Cover used/unused/constrained parameters with actual compilation and original binding/trivia preservation; do not replace support with blanket refusal.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. CreateInterfaceCompilationUnit builds InterfaceDeclaration modifiers and members without TypeParameterList or constraint clauses.
- Retained immutable source: `d333c2e96a1e23a31286cda53b920dbee5d6b7be`; preserved tag: `archive/pr1806-restore-reviewed-graph-20261025`. No build/test ran in this content audit; historical receipts are not current runtime qualification.
