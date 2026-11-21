---
category: Fixed
---

- **Fixed:** A solution-snapshot revert (`revert_last_apply` / `revert_apply_by_sequence`) now restores additional-document and analyzer-config-document edits instead of skipping them (previously an apply that changed only such a file reported a no-op success or a failure, and one that also changed a `.cs` file reported success after restoring only the `.cs` file); analyzer-config files are restored by disk write plus reload because MSBuildWorkspace refuses to apply them. A revert whose apply added or removed a project owning only an additional or analyzer-config document now fails closed with the existing `revert-failed` result, the same refusal shipped for added and removed documents in #2082 (`undo-solution-snapshot-restore-incomplete`). Closes `undo-solution-diff-skips-additional-documents`.
