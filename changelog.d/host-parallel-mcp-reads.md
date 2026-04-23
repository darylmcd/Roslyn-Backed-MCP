---
category: Maintenance
---

- **Maintenance:** Documented the server-side parallel read contract in `ai_docs/runtime.md` and `ai_docs/domains/tool-usage-guide.md`: read-only MCP calls may overlap on a loaded workspace when the client supports concurrent requests, while write and lifecycle calls remain serialized by `WorkspaceExecutionGate`. Tracks `host-parallel-mcp-reads`. (PR #360)
