# move-type-to-file-using-separator-blank-lines — Normalise the blank lines between kept usings and the moved declaration

**row:** `move-type-to-file-using-separator-blank-lines` · **pri:** `Low` · **size:** `M` · **verified:** `3a0482b82ab46549662bf934e05e63071594b2e4`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/TriviaNormalizationHelper.cs`
- `src/RoslynMcp.Roslyn/Services/TypeMoveService.cs`
- `tests/RoslynMcp.Tests/TriviaNormalizationHelperTests.cs`
- `tests/RoslynMcp.Tests/TypeMoveTests.cs`

## Acceptance

- [ ] NormalizeUsingToMemberSeparator leaves exactly one blank line between the kept usings and the moved declaration (class, enum and delegate), red-first test through move_type_to_file_preview; TriviaNormalizationHelperTests.cs:~69-85 asserts exactly one blank line, not at least one.
- [ ] The `as ITypeSymbol` cast at TypeMoveService.cs:~126 throws or refuses on null instead of silently skipping ContainsFileLocalType (unreachable today).

## Evidence

- Slice 3 worker (relocation-support-enum-delegate-move-to-file): two blank lines on the base commit with a class; cold review follow-ups 1 and 6.
