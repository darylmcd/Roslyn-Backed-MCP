# relocation-same-file-rewrite-preserves-other-members — In-place namespace rewrite honors all siblings

**row:** `relocation-same-file-rewrite-preserves-other-members` · **pri:** `High` · **size:** `S` · **deps:** `relocation-consumer-using-counts-enum-delegate-members`

## Anchors

- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs:289-329`
- `tests/RoslynMcp.Tests/NamespaceRelocationTests.cs`

## Acceptance

- [ ] Changing `Selected` to another namespace in the same file (block and file-scoped) leaves enum, delegate and nested-namespace siblings in `Old`; red regression first; the result compiles.
- [ ] No-sibling in-place rename and class-sibling extraction behavior are unchanged.

## Evidence

- `siblingTypesInSameNs` uses `Members.OfType<TypeDeclarationSyntax>()` (:294), so non-type members are renamed with the namespace. Split from `relocation-source-delete-drops-enum-delegate`.

## Notes

2026-11-06 (PR 2004): consumer-using decision now counts enums/delegates and a second namespace block, but siblingTypesInSameNs (NamespaceRelocationService.cs:294) still uses OfType<TypeDeclarationSyntax>, so an in-place rewrite renames the whole block and moves a same-block enum to New. Add a case where a consumer references only that enum.
