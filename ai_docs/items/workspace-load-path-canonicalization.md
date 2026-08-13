# workspace-load-path-canonicalization — Document.FilePath carries a swappable link component

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`
- `src/RoslynMcp.Roslyn/Helpers/SymbolResolver.cs`
- `src/RoslynMcp.Roslyn/Services/EditService.cs`

## Acceptance

- [ ] Document paths are link-resolved at workspace-load time so `Document.FilePath` cannot contain a symlink/junction component that is re-resolved at write time.
- [ ] A deterministic link-swap regression proves `MSBuildWorkspace.TryApplyChanges` cannot write outside the configured boundary after validation passed.
- [ ] Workspaces legitimately loaded through a symlinked project directory still resolve documents correctly (no false misses) — the risk that made the naive fix unsafe.

## Evidence

`EditService.ApplyTextEditsCoreAsync` calls `_workspace.TryApplyChanges` BEFORE `PersistDocumentTextToDiskAsync`. MSBuildWorkspace flushes changed documents to disk itself using the un-canonicalized `Document.FilePath` — confirmed by the in-repo comment in `WorkspaceManager.cs` ("MSBuildWorkspace.TryApplyChanges writes .cs/.csproj files to disk on its way out").

Consequence: pinning the canonical target at the later `AtomicFileWriter` write does NOT close the boundary escape, because Roslyn already wrote through the swappable path. This is why `path-boundary-link-swap-toctou` acceptance items 3-4 cannot be satisfied from that initiative's seam.

## Context

`WorkspaceManager.cs` is an addenda-declared hotspot, so this needs its own initiative and must not be bundled. The naive approach (reuse the boundary-canonical form for document lookup) was explicitly rejected during planning: MSBuildWorkspace does not canonicalize `Document.FilePath` through links, so canonicalizing the lookup side risks false misses for workspaces loaded via a symlinked project dir.
