# relocation-support-enum-delegate-types — Support enum and delegate types in the type-move and relocation previews

**row:** `relocation-support-enum-delegate-types` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/UnsupportedTypeKindRefusal.cs`
- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs`
- `src/RoslynMcp.Roslyn/Services/TypeMoveService.cs`
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs`
- `tests/RoslynMcp.Tests/NamespaceRelocationTests.cs`

## Acceptance

- [ ] `change_type_namespace_preview` (experimental) accepts an enum or delegate type and produces a correct preview (declaration relocated, consumers updated, 0 compile errors), instead of the #2180 unsupported-kind refusal; red-first tests per kind incl. destination-collision, nested (still refused with the nested-type message), generic delegate and file relocation.
- [ ] The shared helpers (MovedDeclarationFileWriter, MovedTypeDestinationIdentity incl. its silent non-TypeDeclarationSyntax return, NamespaceMemberClassifier) are widened; the other two tools still refuse through their own call sites.
- [ ] The unreleased fragment changelog.d/relocation-selected-enum-delegate-reported-not-found.md no longer claims change_type_namespace refuses.

## Evidence

- Operator decision 2026-11-21: support enum and delegate relocation rather than refuse them (answer to the question raised after #2180, which made the tools refuse these kinds instead of reporting a misleading not-found).
