---
category: Fixed
---

- **Fixed:** A failure while disposing a retired or half-loaded MSBuild workspace or analyzer lease during `workspace_load`/`workspace_reload` no longer leaves the session's load lock held forever or masks the original load failure or cancellation; each cleanup step now runs independently, failures are logged, and the lock is always released. Closes `workspace-native-load-cleanup-gate-release`.
