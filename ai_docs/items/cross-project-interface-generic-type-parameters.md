# cross-project-interface-generic-type-parameters — Preserve generic parameters in extracted interfaces

**row:** `cross-project-interface-generic-type-parameters` · **pri:** `Medium` · **size:** `M` · **deps:** `interface-member-projection-shared-emitter`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:565`
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:603`
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs`
- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs:209-215` — the in-project emitter also emits method type parameters without constraints (CS0425 for constrained generic methods).

## Acceptance

- [ ] Preserve required source/enclosing declaration type parameters and constraints plus generic method type parameters/constraints in emitted interfaces and bound uses.
- [ ] Cover used/unused/constrained parameters with actual compilation and original binding/trivia preservation; do not replace support with blanket refusal.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. CreateInterfaceCompilationUnit builds InterfaceDeclaration modifiers and members without TypeParameterList or constraint clauses.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.
