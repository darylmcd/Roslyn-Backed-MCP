# relocation-selected-enum-delegate-reported-not-found — Name the unsupported kind for enum or delegate selection

**row:** `relocation-selected-enum-delegate-reported-not-found` · **pri:** `Medium` · **size:** `S` · **deps:** `relocation-source-delete-preserves-other-members`

## Anchors

- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs:213`
- `tests/RoslynMcp.Tests/NamespaceRelocationTests.cs`

## Acceptance

- [ ] Selecting an enum or delegate yields a kind-naming refusal like `TypeMoveService.cs:56-68`, or is supported; operator may prefer support over refusal.

## Evidence

- Live read 2026-11-05: `FindTypeInNamespaceAsync` requires `TypeDeclarationSyntax`.
