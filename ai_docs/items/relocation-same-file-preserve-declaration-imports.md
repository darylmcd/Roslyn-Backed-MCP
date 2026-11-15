# relocation-same-file-preserve-declaration-imports — Preserve scoped imports in same-file relocation

**row:** `relocation-same-file-preserve-declaration-imports` · **pri:** `Medium` · **size:** `S` · **verified:** `d9e2b3ffd8ab69e6bc20b0b06fe4fe7d57d63dd2`

## Anchors

- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs`
- `tests/RoslynMcp.Tests/NamespaceRelocationTests.cs`

## Acceptance

- [ ] In the sibling-preserving same-file rewrite, keep the selected declaration bound to its original namespace-scoped imports and preserve sibling bindings. Prove red first with a used alias and compile the preview and applied document.

## Evidence

- The same-file sibling branch moves only the selected declaration into a new namespace block. The namespace-scoped imports stay behind and no longer govern the moved declaration. Source-derived cold review.

## Context

- New-file moves use a separate projection. Consumer import selection is tracked by `namespace-relocation-consumer-scope-identity`.
