---
category: Fixed
---

- **Fixed:** `workspace_status(verbose=true)` timing out at 5 s on a ready workspace when a concurrent or recently-completed load held `session.LoadLock`. `GetStatus` and `GetStatusAsync` no longer acquire `LoadLock`; `BuildStatus` reads only thread-safe fields (`ImmutableArray`, `ConcurrentQueue`, scalar references) that do not require the lock, and the outer `gate.RunReadAsync` already serializes against concurrent writes. Fixes gh #761.
