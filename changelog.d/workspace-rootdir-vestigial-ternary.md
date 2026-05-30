---
category: Maintenance
---

- **Maintenance:** Collapsed a dead conditional in `WorkspaceManager.WaitForStableRestoreArtifactsAsync`. The `fullPath.EndsWith(".csproj") ? Path.GetDirectoryName(fullPath) : Path.GetDirectoryName(fullPath)` ternary had two identical branches, so the `.csproj` check never affected the result; reduced to a single `Path.GetDirectoryName(fullPath)`. Behavior unchanged. Closes `workspace-rootdir-vestigial-ternary`.
