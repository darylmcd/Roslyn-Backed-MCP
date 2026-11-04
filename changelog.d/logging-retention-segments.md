---
category: Changed
---

- **Changed:** The JSON-lines file sink (`ROSLYNMCP_OBSERVABILITY_SINK=file`) now keeps numbered rotated segments (default 3, `ROSLYNMCP_OBSERVABILITY_MAX_SEGMENTS`) instead of one, deleting only the oldest on rotation, with a configurable segment threshold (`ROSLYNMCP_OBSERVABILITY_MAX_SEGMENT_BYTES`, default 5 MiB, minimum 8192). A record larger than the threshold is replaced by a bounded valid-JSON truncation marker instead of being appended whole. Per-process disk use is now up to (segments + 1) x threshold; invalid values fail startup. Partially addresses `logging-retention-destroys-repro-evidence` (dead-pid sweep: `logging-retention-dead-pid-sweep`).
