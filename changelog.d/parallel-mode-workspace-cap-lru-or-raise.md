---
category: Fixed
---

- **Fixed:** Fixed parallel-mode workspace saturation: raised the default `MaxConcurrentWorkspaces` from 8 to 16 and added `evictPolicy="lru"` to `workspace_load` so callers can opt into silent LRU eviction of idle workspaces instead of receiving a hard error. Strict-mode errors now include `activeWorkspaces` and `lruCandidate` fields for one-round-trip self-recovery.
