# namespace-dependencies-multi-namespace-ownership — Attribute namespace graph entries to their owning scopes

**row:** `namespace-dependencies-multi-namespace-ownership` · **pri:** `Medium` · **size:** `S` · **verified:** `34a87d9c712c19c0af78caca46be5d50af6ab384`

## Anchors

- `src/RoslynMcp.Roslyn/Services/NamespaceDependencyService.cs:54-81`
- (new) `tests/RoslynMcp.Tests/NamespaceDependencyOwnershipTests.cs`

## Acceptance

- [ ] A file with sibling and nested namespace declarations reports each type under its own bound namespace and associates scoped import edges with the correct namespace. Preserve deterministic counts and edge ordering.

## Evidence

- Source review at `34a87d9c`. The service takes the first namespace in a file and counts every descendant type and import under it.
