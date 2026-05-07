---
category: Changed
---

- **Changed:** `WorkspaceEvicted` envelopes now carry the originally-loaded solution path and an exact `recovery=workspace_load(path: "...")` retry shape when the eviction was a same-process trim (path was retained on the in-memory eviction record). Cross-process recycle envelopes still omit both fields because the prior process's session metadata is unrecoverable. Typoed-`workspaceId` lookups remain `category=NotFound`. `WorkspaceEvictedException.LoadedPath` is the new typed surface; the `WorkspaceManager._evictedWorkspaces` value type is now an `EvictedSessionRecord(LoadedAtUtc, LoadedPath)` record struct. Closes `workspace-id-recovery-hints`.
