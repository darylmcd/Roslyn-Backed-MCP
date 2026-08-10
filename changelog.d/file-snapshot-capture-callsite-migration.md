---
category: Maintenance
---

- **Maintenance:** consolidated the last hand-rolled pre-apply snapshot ternaries (`RefactoringService.AddFileSnapshotAsync` and `RefactoringService.AddProjectFileSnapshotAsync`) onto the shared `FileSnapshotCapture.FromBytesOrFallback` helper, so `FileSnapshotDto.FromExistingBytes` is now named by exactly one production call site. Closes `file-snapshot-capture-helper-consolidation`.
