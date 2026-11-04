---
category: Fixed
---

- **Fixed:** Failed document-set persistence now compensates only paths it provably left in a recorded post-state (including `.csproj` rewrites by `TryApplyChanges`). A path edited, replaced or recreated outside the operation is preserved and reported as an ownership-conflict `IOException` aggregated with the primary and independent cleanup failures; captured-but-untouched files are never restored or deleted. Ownership is best-effort: the compare-then-act window between the check and the restore/delete remains. Closes `physical-compensation-external-edit-ownership`.
