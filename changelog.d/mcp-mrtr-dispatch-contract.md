---
category: Changed
---

- **Changed:** `InputRequiredException` and `InputResponses` now travel through one request-scoped adapter (`RequestScopedInputAdapter`) shared across the supported MCP protocol eras, replacing the era-specific dispatch that had grown into `StructuredCallToolFilter` and `StructuredCallElicitationCoordinator`. Tool-call semantics are unchanged for clients on either era; the adapter is the single seam later elicitation work builds on. Closes `mcp-mrtr-dispatch-contract`.
