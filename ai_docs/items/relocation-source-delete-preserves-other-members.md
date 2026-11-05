# relocation-source-delete-preserves-other-members — Source deletion honors every remaining declaration

**row:** `relocation-source-delete-preserves-other-members` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs:333-344`
- (new) src/RoslynMcp.Roslyn/Helpers/NamespaceMemberClassifier.cs
- `src/RoslynMcp.Roslyn/Services/TypeMoveService.cs:34-35,75-77`
- `tests/RoslynMcp.Tests/NamespaceRelocationTests.cs`
- (new) tests/RoslynMcp.Tests/NamespaceMemberClassifierTests.cs

## Acceptance

- [ ] Relocating `Selected` out of `namespace Old { class Selected {} enum Keep { A } }` (also delegate, block and file-scoped, enum in a sibling namespace block, enum in a nested namespace, `#if`-disabled declaration) keeps the source document with `Keep`; red regression observed first.
- [ ] Sole-type source removal still deletes the file; the relocation ownership matrix stays green.
- [ ] `TypeMoveService` uses the shared classifier instead of its inline copy.

## Evidence

- Cold review 2026-11-01: the delete predicate counts only TypeDeclarationSyntax, so enum/delegate-only siblings are deleted with the source file. Split from `relocation-source-delete-drops-enum-delegate`; plan verified at f15e28e7.
