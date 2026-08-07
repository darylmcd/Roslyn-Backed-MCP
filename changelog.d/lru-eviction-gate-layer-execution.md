---
category: Fixed
---

- **Fixed:** LRU workspace eviction now routes through `WorkspaceExecutionGate`'s per-workspace writer lock, so it can no longer dispose a workspace holding an in-flight gated reader or writer — eviction blocks until the reader/writer drains instead of evicting out from under it, closing the gap PR #1159 documented but did not fix. The eviction path also drops the evicted workspace's lock-registry entry (`RemoveGate`), fixing an adjacent per-eviction registry leak.
