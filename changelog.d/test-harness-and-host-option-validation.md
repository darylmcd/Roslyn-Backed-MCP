---
category: Maintenance
---

- **Maintenance:** Consolidated duplicate in-memory MCP client/server fixtures behind one deterministic lifecycle harness with failed-initialization cleanup and explicit protocol negotiation. Invalid `ROSLYNMCP_ON_STALE` values now fail startup with the accepted vocabulary instead of silently enabling auto-reload. Closes `elicitation-inmemory-harness-consolidation`.
