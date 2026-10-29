---
category: Fixed
---

- **Fixed:** `workspace_load` no longer leaks a workspace capacity slot or leaves a disposed session registered when file-watcher registration (or status construction) fails after the session is added; the exact session is now removed through the normal close path (unwatch, preview invalidation, slot return) and the original failure is preserved. Closes `workspace-load-registration-compensation`.
