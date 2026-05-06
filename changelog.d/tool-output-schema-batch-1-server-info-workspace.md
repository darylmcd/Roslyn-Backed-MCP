---
category: Added
---

- **Added:** `outputSchema` + `structuredContent` channel for 6 high-traffic read tools (`server_info`, `server_heartbeat`, `workspace_status`, `workspace_list`, `workspace_health`, `workspace_drift_check`). Clients with MCP 2025-06-18 support get typed structured payloads; clients without continue to receive the existing text-channel JSON unchanged. `server_info`, `server_heartbeat`, and `workspace_list` were refactored from anonymous-object response bodies to typed DTOs (new `ServerToolDtos.cs`) so `typeof(...)` is reachable in the `[McpToolMetadata]` annotation; on-the-wire JSON shape is preserved bit-for-bit. Closes `tool-output-schema-batch-1-server-info-workspace`.
