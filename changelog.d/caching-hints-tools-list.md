---
category: Changed
---

- **Changed:** Static tool, prompt, resource, and resource-template lists are now ordinally deterministic and carry private five-minute MCP caching hints. Resource bodies carry private, immediately-stale hints so live workspace data is never reused across users or server processes. Closes `caching-hints-tools-list`.
