---
category: Maintenance
---

- **Maintenance:** Routed `BuildService`, `FixAllService`, `BulkRefactoringService`, and `CrossProjectRefactoringService`'s live-solution compilation reads through the shared `ICompilationCache`, closing the next bounded slice of the read-side compilation-cache adoption sweep (group-b core).
