# workspace-load-existing-file-notfound — Investigate false missing-path response

**row:** `workspace-load-existing-file-notfound` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`
- `tests/RoslynMcp.Tests/WorkspaceSessionLoaderFailureTests.cs`

## Acceptance

- [ ] Reproduce why the installed `workspace_load` reports `FileNotFound` for a solution that the same Windows session can read inside a configured sanctioned root.
- [ ] Correct the path or host boundary that causes the false missing-path result; retain refusal for truly absent or outside-root paths.
- [ ] Add an integration regression using the discovered solution/project path shape and confirm `workspace_load` reaches a ready session.

## Evidence

- On 2026-09-29, `server_info` reported roslyn-mcp 4.3.0 with `pathBoundary.configuredRootCount=1`; Codex configuration has `ROSLYNMCP_SANCTIONED_ROOTS='C:/Code-Repo'`. `rg --files` found `C:/Code-Repo/BioFileTransfer/BioFileTransfer.sln` and `BioFileTransfer.csproj`; both `workspace_load` calls returned `category: FileNotFound` within 31 ms and 7 ms. `WorkspaceTools.cs:70-73` validates the path then passes the original string to `workspace.LoadAsync`; `WorkspaceManager.cs:1438-1443` throws when its resolved path fails `File.Exists`. The cause between these steps is not yet proven.

## Context

- BioFileTransfer backlog-remediate used source reads and CLI validation after the live server failed. This row is investigate-first because the current evidence does not distinguish path canonicalization, process visibility, and root-validation behavior.
2026-09-29 base proof: on clean origin/main 85b6e3d, dotnet test RoslynMcp.slnx --no-restore --filter FullyQualifiedName~ApplyTextEdit_VerifyFalse_OmitsVerificationField --nologo failed 1/1 at WorkspaceManager.EnsureWorkspacePathsArePhysical:1476 with a filesystem-link-component refusal. The backlog-only worktree just ci also failed many workspace tests with the same refusal and resolved sample paths rendered as C:\Volume{...}; stopped that aggregate after the systemic failure. Inspect PhysicalPathResolver.Resolve and path comparison in WorkspaceManager before deciding whether this is the same cause as live BioFileTransfer FileNotFound.
