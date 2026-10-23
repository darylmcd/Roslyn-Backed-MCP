# physical-operation-canonical-path-ownership — physical-operation-canonical-path-ownership

**row:** `physical-operation-canonical-path-ownership` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.Recovery.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`
- `src/RoslynMcp.Roslyn/Helpers/PhysicalPathResolver.cs`
- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkGraphReplayTests.cs`

## Acceptance

- [ ] Issue and compare exact physical snapshot ownership using canonical validated absolute paths at capture, write intent and completion; reject duplicate physical ownership including lexical aliases before mutation.
- [ ] Accept equivalent Windows slash spellings and lexical normalization without treating them as filesystem links; continue rejecting actual symlink or junction components and relative paths.
- [ ] Observe native mixed-spelling and duplicate-ownership regressions fail before correction; retain independent Linux case-distinct identities and guarded recovery controls.

## Evidence

- Current-session restore-authority-contexts5: valid Windows forward-spelled capture fails EnsurePhysicalPath raw-versus-canonical comparison; intent/completion aliases fail ownership lookups. New same-file composite positive controls expose the shared boundary.

## Context

- Required complete-fix companion of active restore physical-operation authority; preserve strict current path authorization.
