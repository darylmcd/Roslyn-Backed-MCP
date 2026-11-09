---
category: Fixed
---

- **Fixed:** A failure after the workspace reload swap (preview-token invalidation) no longer disposes the newly live workspace and leaks the retired one; the retired workspace is disposed once, the failure still propagates, the reload event still fires so caches drop the old solution, and the workspace stays stale for the next reload.
