# namespace-relocation-consumer-scope-identity — Resolve relocation imports per consumer scope

**row:** `namespace-relocation-consumer-scope-identity` · **pri:** `Medium` · **size:** `S` · **verified:** `34a87d9c712c19c0af78caca46be5d50af6ab384`

## Anchors

- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs:550-594`
- `tests/RoslynMcp.Tests/NamespaceRelocationTests.cs`

## Acceptance

- [ ] Update imports for actual references to the moved type across multiple namespace scopes in one consumer file. Keep unrelated same-named bindings and prove the applied preview compiles.

## Evidence

- Source review at `34a87d9c`. The consumer pass selects the first namespace in each document as the ambient namespace for every reference and gates the pass by unbound identifier spelling.
