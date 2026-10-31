# class-split-nested-type-identity — Preserve containing type during class split

**row:** `class-split-nested-type-identity` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ClassSplitOrchestrator.cs:39`
- `src/RoslynMcp.Roslyn/Services/ClassSplitOrchestrator.cs:124`
- `tests/RoslynMcp.Tests/ClassSplitReceivingNamespaceIntegrationTests.cs` (extend)

## Acceptance

- [ ] A public preview/apply split of a nested type keeps the new partial under the same containing type chain and binds moved members to the original symbol.
- [ ] Reparse serialized preview, compile and run it before apply; verify applied/reloaded compilation and original source preservation.

## Evidence

- Cold plan review on 2026-10-31: `PreviewSplitClassAsync` accepts nested `TypeDeclarationSyntax`, but `CreateCompilationUnit` emits the selected partial at top level and retains only namespace ancestry.
