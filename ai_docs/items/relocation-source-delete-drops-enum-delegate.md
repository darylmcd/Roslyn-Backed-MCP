# relocation-source-delete-drops-enum-delegate — Preserve other namespace members during namespace changes

**row:** `relocation-source-delete-drops-enum-delegate` · **pri:** `High` · **size:** `L`

## Anchors

- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs:290-342`
- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs:460-492`
- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs:632-650`
- `tests/RoslynMcp.Tests/MovedDeclarationFileOwnershipIntegrationTests.cs`

## Acceptance

- [ ] Relocating `Selected` from `namespace Old { class Selected {} enum Keep { A } }` preserves `Keep` in the source file; observe the current loss in a red regression first.
- [ ] Changing `Selected` to another namespace in the same file keeps `Keep` in `Old` instead of rewriting its enclosing namespace.
- [ ] Preserve delegates and other direct namespace members under the same sibling and source-removal decisions, including nested namespaces; retain the existing sole-type source removal behavior.
- [ ] Preserve `using Old;` in consumers when another source member still occupies `Old`, whether in the source file or another document.
- [ ] The resulting solution compiles and the existing relocation ownership matrix remains green.

## Scope

- `L` reflects three regression paths in one production file: same-file namespace rewrite, relocation source deletion, and consumer using removal.
- Change the shared direct-namespace-member classification for all three decisions in one fix. Splitting by output path would leave the same enum/delegate exclusion active in the other branches.

## Evidence

- Cold review on 2026-11-01 found that `siblingTypesInSameNs`, `CountSiblingTypesInNamespaceAsync`, and the source-file removal predicate count only `TypeDeclarationSyntax`. Enums and delegates are excluded from the in-place rewrite decision, consumer using rewrite decision, and source-document deletion decision.
