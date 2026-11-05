# workspace-manager-project-index-extraction — Extract the project index and fork-retention code from WorkspaceManager

**row:** `workspace-manager-project-index-extraction` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`

## Acceptance

- [ ] The project index and fork-retention/retirement code move behind one internal collaborator without changing lifecycle semantics; characterization tests green before the move.

## Evidence

- PR 1993 cold review: ProjectIndexEntry/GetProject and fork retention remain in WorkspaceManager; the internal constructor takes 12 test-seam parameters.
