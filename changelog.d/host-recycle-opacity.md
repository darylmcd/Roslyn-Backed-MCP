---
category: Added
---

- **Added:** `server_info.connection` and `server_heartbeat` now carry `previousStdioPid`, `previousExitedAt`, and `previousRecycleReason` on the first probe after a host restart, then clear after emission — driven by a new `HostProcessMetadataStore` (disk-persisted, TTL-guarded) and `HostProcessMetadataSnapshotProvider` (`ServerTools` + `ConnectionStateDto` + `Program.cs` DI). Closes `host-recycle-opacity`.
