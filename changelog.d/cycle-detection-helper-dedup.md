---
category: Maintenance
---

- **Maintenance:** Extracted duplicated `WouldCreateProjectReferenceCycle` from `CrossProjectRefactoringService` and `ProjectMutationService` into a shared `ProjectGraphHelpers` helper. Closes `cycle-detection-helper-dedup` from the 2026-05-26 discovery-sweep refactor audit.
