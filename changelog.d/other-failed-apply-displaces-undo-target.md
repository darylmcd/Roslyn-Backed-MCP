---
category: Fixed
---

- **Fixed:** Failed text edits, project mutations, and refactorings preserve the previous undo target when they leave no changes. Failed operations with surviving changes still offer recovery. Refactoring recovery preserves files changed by another writer, and reloads from disk when only the workspace needs recovery.
