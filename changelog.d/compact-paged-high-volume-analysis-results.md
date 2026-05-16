---
category: Fixed
---

- **Fixed:** `find_reflection_usages` now returns bounded, paginated results (`offset` / `limit` / `summary` / `hasMore` / `totalCount`) — previously returned all hits with no cap, producing 100+ KB responses on reflection-heavy solutions (gh #760).
