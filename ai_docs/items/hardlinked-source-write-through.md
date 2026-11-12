# hardlinked-source-write-through — Keep edits within the reviewed source targets

**row:** `hardlinked-source-write-through` · **pri:** `Medium` · **size:** `L` · **verified:** `a12412bbe4bd31a18cbdd8f481475587e1697c97`

## Anchors

- `src/RoslynMcp.Roslyn/Services/EditService.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`
- `src/RoslynMcp.Roslyn/Services/DocumentSetPersistenceService.cs`
- `src/RoslynMcp.Roslyn/Services/UndoService.cs`
- `src/RoslynMcp.Roslyn/Helpers/SymbolResolver.cs`

## Acceptance

- [ ] Use two hardlinked names for one source file to witness whether direct edits and preview-token writes affect an unlisted alias. Re-derive behavior on Windows and Linux.
- [ ] Qualify a preview that names both aliases and checks each reviewed write against disk. Establish the supported write boundary for distinct loaded documents sharing a file identity.
- [ ] Preserve recoverability for the exact affected files on refusal and undo. Screen any public behavior change for release compatibility before selection.

## Evidence

- Path resolution normalizes links and junctions, while separate hardlink paths can resolve to different DocumentIds. EditService and DocumentSetPersistenceService call MSBuildWorkspace.TryApplyChanges. The pinned Roslyn 5.9.0 MSBuildWorkspace implementation writes changed text in place, and DocumentSetPersistenceService also has a direct File.WriteAllTextAsync path. Source-derived. Runtime qualification pending.

## Context

- Separate from preview accumulator overwrite. Even an edit naming one path can write through to an unlisted hardlink.
