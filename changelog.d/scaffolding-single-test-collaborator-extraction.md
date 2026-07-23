---
category: Maintenance
---

- **Maintenance:** Extracted single-test scaffolding orchestration into a new `SingleTestScaffolder` collaborator and established a shared `TestScaffoldRenderer` (target resolution, constructor-argument synthesis, framework rendering) consumed directly by both the single-test and batch/first-test-file scaffolding flows — `ScaffoldingService` is now a thin `IScaffoldingService` facade for test scaffolding, matching the sibling `TypeScaffolder` extraction. No behavior change: preview content, warnings, tokens, logger categories, and the public facade contract are unchanged.
