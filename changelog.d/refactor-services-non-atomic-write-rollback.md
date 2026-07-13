---
category: Fixed
---

- **Fixed:** multi-file `apply_composite_preview` writes (and the underlying source-file writes in `EditService`, `UndoService`, and `ProjectMutationService`) now go through a shared atomic temp+rename helper instead of a direct `File.WriteAllTextAsync`, preventing a truncated/corrupt file on a mid-write crash or disk-full condition. A failure partway through a composite apply now logs a warning naming applied-vs-total plus the failing file and clearly marks the returned result as a partial apply instead of silently swallowing the exception. Closes `refactor-services-non-atomic-write-rollback`.
