# moved-declaration-preserve-extern-aliases — Preserve referenced extern aliases in moved declarations

**row:** `moved-declaration-preserve-extern-aliases` · **pri:** `Medium` · **size:** `M` · **verified:** `d9e2b3ffd8ab69e6bc20b0b06fe4fe7d57d63dd2`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/MovedDeclarationFileWriter.cs:24-81`
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs`
- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs`
- `tests/RoslynMcp.Tests/MovedDeclarationFileOwnershipIntegrationTests.cs`

## Acceptance

- [ ] A bound extern alias retains its binding in new-file relocation. Cross-project moves preserve equivalent resolution or refuse before minting a token when the receiver cannot resolve the alias. Prove red first with aliased reference binding and compilation.

## Evidence

- The shared new-file writer copies using directives and namespace-local usings but omits both sets of extern alias directives. Source-derived cold review.
