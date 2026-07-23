---
category: Fixed
---
Impact sweep, coupling analysis, and symbol search now share one correct lazy type-enumeration helper (`RoslynSymbolTraversal`) that walks namespace/type trees to arbitrary nesting depth without pruning matching descendants beneath non-matching parents. Previously `ImpactSweepService` silently dropped matching nested types beneath a non-matching parent, and both `ImpactSweepService` and `SymbolSearchService` missed types nested more than one level deep.
