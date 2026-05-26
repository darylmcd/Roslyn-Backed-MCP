---
category: Maintenance
---

- **Maintenance:** Extracted duplicated `RunGit` + `StageFixtureBaseline` test helpers from `ValidateRecentGitChangesTests` and `ValidateWorkspaceChangeTrackerReconcileTests` into a shared `GitFixtureRunner` static under `tests/RoslynMcp.Tests/Support/`. Closes `test-git-fixture-helper-dedup` from the 2026-05-26 discovery-sweep refactor audit.
