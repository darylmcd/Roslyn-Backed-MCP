---
category: Fixed
---

- **Fixed:** deleting a loaded worktree root now retires its workspace under the writer gate, releases watchers and session resources exactly once, and raises the normal workspace-closed lifecycle event without making status reads destructive. Closes `missing-workspace-root-lifecycle-retirement`.
