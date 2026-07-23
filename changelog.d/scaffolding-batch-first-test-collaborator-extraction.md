---
category: Maintenance
---

- **Maintenance:** Extracted batch and first-test-file scaffolding orchestration out of the `ScaffoldingService` facade into a dedicated `BatchTestScaffolder` collaborator (`scaffold_test_batch_preview` / `scaffold_first_test_file_preview`). The one-compilation-per-project batch cache and all generated test-file output are preserved byte-for-byte; the facade's logger-bound resolution helpers stay on the facade and are supplied to the collaborator as delegates, and `IScaffoldingService`'s public surface and DI lifetime are unchanged.
