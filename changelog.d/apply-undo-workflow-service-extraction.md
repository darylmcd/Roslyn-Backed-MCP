---
category: Maintenance
---

- **Maintenance:** extracted the apply/undo workflow decisions (project-scoped compile-check diffing, apply/rollback outcome selection, cancellation-safe best-effort revert, and sequence-revert outcome mapping) into a new `IApplyUndoWorkflowService` in the Roslyn layer. The `apply_with_verify` and `revert_apply_by_sequence` Host wrappers now only resolve the workspace, open the write gate, and map domain outcomes onto their existing JSON shapes — no wire-format change and no tool-surface change.
