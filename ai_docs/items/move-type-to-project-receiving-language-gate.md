# move-type-to-project-receiving-language-gate — move_type_to_project_preview stores tokens for types the receiving project's language version cannot compile

**row:** `move-type-to-project-receiving-language-gate` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs` (`PreviewMoveTypeToProjectAsync`, ~:30-115: no compile check before the token store)
- `src/RoslynMcp.Roslyn/Helpers/InterfaceNullableProjection.cs` (`EnsureReceivingLanguageAsync`, to be shared)
- (new) `tests/RoslynMcp.Tests/MoveTypeReceivingLanguageTests.cs`

## Acceptance

- [ ] Red first: moving a record or primary-constructor type into a project with a lower LangVersion stores a token today; it must refuse before the token store with the compiler ids (all of them, no compiler text).
- [ ] Reuse the receiving-vs-source parse-option differential from the interface-extraction gate via one shared helper; do not copy it.

## Evidence

- PR #2018 implementer probe: `TypeExtractionService.RefuseAddedCompilerErrorsAsync` is same-project and has no receiving language; the real sibling is `move_type_to_project_preview`, which has no compile check of any kind.
