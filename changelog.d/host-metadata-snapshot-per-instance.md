---
category: Maintenance
---

- **Maintenance:** The consume-once previous-process snapshot behind `server_info` / `server_heartbeat` `previousStdioPid` / `previousExitedAt` / `previousRecycleReason` is now per-instance state on the DI-injected `ServerProcessMetadata` instead of a process-wide static, and the static direct-call `ServerProcessMetadata` fallback in `ServerTools` is removed. This closes a cross-class test race in which a concurrently running test class that probed `server_info` could drain a snapshot another class had just published. Wire shape and production behavior are unchanged: the host still publishes once at startup and the first probe still carries the fields.
