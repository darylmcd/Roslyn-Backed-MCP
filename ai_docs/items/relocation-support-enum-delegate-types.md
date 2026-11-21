# relocation-support-enum-delegate-types — Support enum and delegate types in the type-move and relocation previews

**row:** `relocation-support-enum-delegate-types` · **pri:** `Medium` · **size:** `M` · **verified:** `d0b4076a7ca2a0464e76dc31324c3b24e1afc40e`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/UnsupportedTypeKindRefusal.cs`
- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs`
- `src/RoslynMcp.Roslyn/Services/TypeMoveService.cs`
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs`
- `tests/RoslynMcp.Tests/NamespaceRelocationTests.cs`

## Acceptance

- [ ] `change_type_namespace_preview`, `move_type_to_file_preview` and `move_type_to_project_preview` accept an enum or delegate type (`EnumDeclarationSyntax`, `DelegateDeclarationSyntax`) and produce a correct preview (declaration moved or re-namespaced, every consumer updated, compiles), instead of the unsupported-kind refusal added in #2180; red-first test per tool and kind.
- [ ] Refusal text and `AnyTypeDeclaration` supported-kind list updated; tools still refuse the kinds they cannot handle.
- [ ] Release classification recorded per tool (experimental vs stable surface, release-policy Rule 1/ADR 0043) with wire evidence for any stable tool.

## Evidence

- Operator decision 2026-11-21: support enum and delegate relocation rather than refuse them (answer to the question raised after #2180, which made the tools refuse these kinds instead of reporting a misleading not-found).
