---
category: Maintenance
---

- **Maintenance:** Decomposed `RefactoringService.PreviewCodeFixAsync` into named action-selection, changed-solution capture, preview-store and no-fix helpers with no behavior change, and corrected the stale rationale for the CS8019 `remove_unused_using` fallback (a live path because Roslyn ships no CS8019 `CodeFixProvider`, not a unit-test-only registry omission). Closes `refactoring-code-fix-preview-decomposition`.
