# physical-path-resolver-volume-mount-junction — Resolve volume-GUID junction targets instead of joining them to the link parent

**row:** `physical-path-resolver-volume-mount-junction` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/PhysicalPathResolver.cs:30-46`
- `tests/RoslynMcp.Tests/ClientRootPathValidatorTests.cs`

## Acceptance

- [ ] `PhysicalPathResolver.GetLinkTargetPath` recognizes a volume-mount-point junction target (`FileInfo.LinkTarget` shape `Volume{<guid>}\`, and the `\??\Volume{<guid>}\` / `\\?\Volume{<guid>}\` spellings) as absolute and resolves it to an existing path. The resolved path must address the same file as the input: `File.Exists` must hold wherever it holds for the unresolved path. It must never produce `<link-parent>\Volume{<guid>}\…`.
- [ ] Red-first test: a directory junction whose target is a volume mount point resolves to a path where the file exists. If creating a mount-point junction is impossible in the test environment, test `GetLinkTargetPath(linkPath, "Volume{…}\\")` directly (it is `internal`) and assert that the result is not joined to the link parent.
- [ ] `workspace_load` on a solution under `C:\Code-Repo\<repo>` (where `C:\Code-Repo` is a volume-mount junction) reaches a ready session with the plugin launch env `ROSLYNMCP_SANCTIONED_ROOTS="."`. The boundary check still refuses a sibling repository outside the root.
- [ ] Audit every other `GetLinkTargetPath` / `LinkTarget` consumer (the sanctioned-root canonicalization in `ConfiguredRootBoundary` included) so that the root and the request path canonicalize the same way after the fix.

## Evidence

- Repro 2026-09-29 (Windows 11, roslyn-mcp 4.3.0 plugin, `dnx Darylmcd.RoslynMcp@4.3.0`, `ROSLYNMCP_SANCTIONED_ROOTS="."`, session cwd `C:\Code-Repo\PriceIndex`):
  - `C:\Code-Repo` is `Directory, ReparsePoint`, `LinkType=Junction`, target `Volume{847fcdf8-30e5-4bdd-b0f4-c5ad48cd5e71}\`. It is a volume mount point.
  - A file-based probe (`#:project src/RoslynMcp.Roslyn/RoslynMcp.Roslyn.csproj`) printed `new DirectoryInfo(@"C:\Code-Repo").LinkTarget = Volume{847fcdf8-…}\`, with `IsPathFullyQualified=False` and `IsPathRooted=False`.
  - It printed `PhysicalPathResolver.Resolve(@"C:\Code-Repo\PriceIndex\PriceIndex.slnx") = C:\Volume{847fcdf8-…}\PriceIndex\PriceIndex.slnx` with `File.Exists=False`.
  - Cause: `PhysicalPathResolver.cs:45` (`return Path.Join(Path.GetDirectoryName(linkPath), rawLinkTarget);`) treats the volume target as link-parent-relative.
- Effect: the configured root `.` canonicalizes through the same mangled path, so the boundary check passes. `WorkspaceManager.cs:1441-1443` then throws `FileNotFoundException("Workspace path was not found: …")` for every existing `.sln`/`.slnx`/`.csproj` under the junction. Observed: `workspace_load` returned `category: FileNotFound` for `PriceIndex.slnx` (forward and back slashes) and for `src/Platform.Core/Platform.Core.csproj`. A sibling repository (`C:\Code-Repo\Roslyn-Backed-MCP\SampleSolution.slnx`) correctly got `InvalidArgument: The requested path is outside the configured sanctioned-root boundary.`
- Blast radius: every repository under `C:/Code-Repo/` sits under this junction. The Roslyn MCP is unusable for all of them on this machine, and agents fall back to Grep + CLI. The BioFileTransfer and PriceIndex backlog-remediate runs on 2026-09-29 both hit it.

## Context

- Supersedes the uncommitted draft `workspace-load-existing-file-notfound` (Medium, investigate-first) left in `.worktrees/workspace-load-existing-file-notfound` by a BioFileTransfer session. It is the same symptom and says "the cause … is not yet proven"; this row carries the proven cause. Drop that draft rather than committing it.
- High because it disables the server's core entry point (`workspace_load`) for every repository on the maintainer's machine. [source: PriceIndex backlog-remediate 20260929T145557Z]
