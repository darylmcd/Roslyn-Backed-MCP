---
category: Fixed
---

- **Fixed:** Workspace loads now keep lifecycle ownership through publication and cleanup, so shutdown waits for in-flight loads and concurrent same-path loads without global-property overrides publish one session. Closes `workspace-load-tryadd-outside-lifecycle-sync` and `workspace-load-shutdown-publication-race`.
