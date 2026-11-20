# cross-project-interface-generic-type-parameters — Preserve generic parameters in extracted interfaces

**row:** `cross-project-interface-generic-type-parameters` · **pri:** `Medium` · **size:** `M` · **verified:** `847d7f94a4f3565da05208010a1397da04f1047f`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceMemberProjector.cs:188` (`Strip`), `:245` (`InheritedConstraints`) — method type parameters and constraints already carry over (PR #1875); enclosing-type parameters do not.
- `tests/RoslynMcp.Tests/InterfaceMemberProjectorTests.cs`
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs`

## Acceptance

- [ ] Preserve required source/enclosing declaration type parameters and constraints plus generic method type parameters/constraints in emitted interfaces and bound uses.
- [ ] Cover used/unused/constrained parameters with actual compilation and original binding/trivia preservation; do not replace support with blanket refusal.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. CreateInterfaceCompilationUnit builds InterfaceDeclaration modifiers and members without TypeParameterList or constraint clauses.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.

## Notes

2026-10-28 PR #1875: method type parameters and their constraints (including override-inherited ones) carry over. Remaining delta: type parameters of the enclosing source type (class Repo<T>) are not declared on the interface, so members using T still do not bind.

2026-11-17 screen at c705e2e1. Decompose the experimental-only sweep across both arity-zero declaration emitters, constructed base and usage references, dependency inversion, and extract-and-wire. Keep exact constructed arguments and transitive constraints. Qualify introduced high-fanout review limits and current-major shared apply behavior before selecting a complete slice.
