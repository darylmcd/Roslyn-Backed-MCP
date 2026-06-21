---
category: Changed
---

- **Changed:** `compile_check`'s `workspaceId` parameter is now optional for single-workspace sessions — the read-path middleware auto-resolves it (`_meta.autoResolution: "single-workspace"`) and a guarded error directs callers to pass it explicitly when two or more workspaces are loaded. Explicit-`workspaceId` callers are unaffected. (Continues the workspaceId-optional read-only-surface sweep, one `*Tools.cs` file at a time; the remaining read-only tools stay open.)
