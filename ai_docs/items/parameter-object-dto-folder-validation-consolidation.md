# parameter-object-dto-folder-validation-consolidation — one owner for dtoFolders entry validation

**row:** `parameter-object-dto-folder-validation-consolidation` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs:1199`
- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs:1253`
- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs:1267`
- `src/RoslynMcp.Roslyn/Helpers/ProjectRelativePathValidation.cs:33`

## Acceptance

- [ ] `SplitCallerSuppliedFolders` no longer duplicates the empty/whitespace and rooted-path refusals or the `['/','\']` separator literal — `ProjectRelativePathValidation` owns both (expose the separator set, and an entry-level validate/split overload if the entry-level messages matter).
- [ ] Existing hostile-`dtoFolders` tests still refuse with messages that name the offending entry.
- [ ] A null compilation in the existing-type collision guard (`:1267`) REFUSES rather than degrading to "destination is free" — today `compilation?.Assembly.GetTypeByMetadataName(...) is not null` silently skips the guard when `GetCompilationAsync` returns null.
- [ ] The document-path collision check (`StringComparison.OrdinalIgnoreCase`, `:1253`) and the sibling on-disk `File.Exists` guard use ONE OS-appropriate path-comparison policy — today they disagree about case on non-Windows filesystems.

## Evidence

Read both files in the PR #1273 diff: the rooted-entry and empty-entry checks in `SplitCallerSuppliedFolders` are re-checked verbatim by `ValidateFolderSegments` on the split output, and the separator array is declared twice. The two guard defects are visible at the cited lines in the same block.

## Context

Three code-quality findings from PR #1273 (`parameter-object-dto-output-boundary-validation`) — one medium duplication, two low anti-patterns. All advisory; none blocked that PR. The fail-open collision guard is the one with real teeth: a guard that degrades to "allowed" on a null compilation defeats the collision refusal the initiative added.
