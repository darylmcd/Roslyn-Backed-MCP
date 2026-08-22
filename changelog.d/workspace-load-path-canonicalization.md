---
category: Fixed
---

- **Fixed:** `workspace_load` now opens symlinked or junction-backed solutions through their physical identity, verifies every loaded project and text-document path is physically pinned, and resolves caller file paths with the same component-ordered primitive. Re-pointing the original workspace link after load can no longer redirect `MSBuildWorkspace.TryApplyChanges` outside the loaded tree; linked-root document lookup remains valid before a swap and uses platform-correct path identity. Closes `workspace-load-path-canonicalization`.
