---
category: Added
---

- **Added:** With `ROSLYNMCP_OBSERVABILITY_SINK=file`, a background startup sweep deletes `roslyn-mcp-*.jsonl` files of dead processes older than `ROSLYNMCP_OBSERVABILITY_RETENTION_DAYS` (default 14), then the oldest until the directory fits `ROSLYNMCP_OBSERVABILITY_MAX_TOTAL_BYTES` (default 64 MiB), never touching the host's own files, a live pid's files, files written in the last 60 s or held-open files, and logs one `LogRetentionSweepCompleted` record (event id 1003). Closes `logging-retention-dead-pid-sweep`.
