# cross-project-move-partial-type-ownership — Keep a partial type together across projects

**row:** `cross-project-move-partial-type-ownership` · **pri:** `Medium` · **size:** `S` · **verified:** `d9e2b3ffd8ab69e6bc20b0b06fe4fe7d57d63dd2`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs`
- `tests/RoslynMcp.Tests/MovedDeclarationFileOwnershipIntegrationTests.cs`

## Acceptance

- [ ] For a top-level partial type with mutually dependent parts in separate files, move all parts with verified binding or refuse before creating a preview. Observe a red-first regression through the experimental tool and preserve ordinary single-declaration moves.

## Evidence

- The cross-project move resolves one declaration and removes it from one source file. Other declarations of the same symbol can remain and compile in the original assembly. Namespace relocation has an independent multi-declaration refusal.
