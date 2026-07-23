---
category: Maintenance
---

- **Maintenance:** extracted focused version, workspace-hint, surface-count, and update-block builders out of `ServerTools.GetServerInfo`, reducing its cyclomatic complexity from 11 to ≤ 8 with no change to the `server_info` response shape.
