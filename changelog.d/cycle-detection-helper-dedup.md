### Changed
- Extracted duplicated `WouldCreateProjectReferenceCycle` into shared `ProjectGraphHelpers` helper used by both `CrossProjectRefactoringService` and `ProjectMutationService`. Closes `cycle-detection-helper-dedup` from the 2026-05-26 discovery-sweep refactor audit.
