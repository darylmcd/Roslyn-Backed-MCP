---
category: Fixed
---

- **Fixed:** The `revert_last_apply` description and remarks now state that reverting deletes files the apply created, matching `UndoService` (a file snapshot with no original text is deleted) instead of claiming created files are kept (`undo-tools-description-created-files-stale`).
