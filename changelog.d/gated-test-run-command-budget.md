---
category: Fixed
---
- **Fixed:** `test_run` and `test_coverage` are bounded by `TestTimeout` (10 min) instead of the 2-minute gate request timeout and no longer hold the workspace lock or throttle slot while `dotnet test` runs; `test_run` reports `commandDurationMs` and `workspaceChangedDuringRun`, and `test_coverage` shares one `TestTimeout` budget across its projects.
