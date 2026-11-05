# relocation-consumer-using-counts-enum-delegate-members — Consumer using decision counts all namespace members

**row:** `relocation-consumer-using-counts-enum-delegate-members` · **pri:** `High` · **size:** `S` · **deps:** `relocation-source-delete-preserves-other-members`

## Anchors

- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs:298-305,460-492,632-650`
- `tests/RoslynMcp.Tests/NamespaceRelocationTests.cs`

## Acceptance

- [ ] Enum or delegate left in `Old` (same file, another file, another project, a second `namespace Old` block of the source file) keeps `using Old;` in consumers and emits the kept-using warning; red regression first.
- [ ] The solution compiles after the preview; the cross-project sibling test stays green.

## Evidence

- `CountSiblingTypesInNamespaceAsync` filters declaring syntax with `is not TypeDeclarationSyntax` (:486) and excludes the source document (:485). Split from `relocation-source-delete-drops-enum-delegate`.
