---
category: Fixed
---

- **Fixed:** `get_di_registrations` default response now returns a bounded first page (`offset` / `limit`, default 100 registrations) with `totalCount` / `hasMore` metadata, preventing MCP inline transport cap overflow on large DI graphs (fixes gh #771).
