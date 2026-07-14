---
category: Fixed
---

- **Fixed:** `NamespaceRelocationService` and `CrossProjectRefactoringService` no longer re-parse every document in the solution on each call — `FindTypeInNamespaceAsync`, `CountSiblingTypesInNamespaceAsync`, and `PreviewDependencyInversionAsync`'s post-extraction constructor rewrite now use Roslyn's indexed symbol lookups (`SymbolFinder` / compilation namespace-symbol traversal) scoped to the relevant projects, eliminating redundant full-solution AST walks on large solutions. Closes `refactor-services-full-solution-scan-perf`.
