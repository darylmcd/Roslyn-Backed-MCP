# workspace-close-drain-descendant-check-ignores-case-on-linux — workspace_close drain descendant check ignores case on Linux

**row:** `workspace-close-drain-descendant-check-ignores-case-on-linux` · **pri:** `Low` · **size:** `S` · **deps:** `—`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/DetachedTestHostDrain.cs`
- `tests/RoslynMcp.Tests/WorkspaceCloseDrainTests.cs`

## Acceptance

- [ ] `DetachedTestHostDrain` compares the candidate path with the working-directory prefix using `FileSystemPath.Comparison` (Ordinal off Windows), not `StringComparison.OrdinalIgnoreCase`.
- [ ] New test (Inconclusive on Windows): with the workspace at `<root>/ws`, a helper under `<root>/WS/host` is NOT killed and a helper under `<root>/ws/host` still is.
- [ ] Existing `WorkspaceCloseDrainTests` stay green on Windows (behavior unchanged there).

## Evidence

- `DetachedTestHostDrain.cs:190` uses `StringComparison.OrdinalIgnoreCase` on every OS. Pre-existing since #1013 (origin/main `WorkspaceTools.cs:352`). Listed as a follow-up in PR #1666; its 2026-09-28 review asked for a tracked row.

## Notes

- `FileSystemPath` (`src/RoslynMcp.Roslyn/Helpers/FileSystemPath.cs`) is internal; `RoslynMcp.Roslyn.csproj` grants `InternalsVisibleTo` to `RoslynMcp.Host.Stdio`.
- macOS: `FileSystemPath` treats it as case-sensitive although default APFS is not. Both drain paths are physical (`PhysicalPathResolver` at load, `proc_pidpath` for the candidate) but neither is case-normalized. Decide macOS explicitly; there is no macOS CI leg.
