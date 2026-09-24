---
category: Maintenance
---
- **Maintenance:** Tests: `MissingWorkspaceRootRetirementTests.TransientGateFailure_RetriesUntilMissingWorkspaceRetires` now awaits the fake gate's `RemoveGate` signal instead of reading its counter in the window between `RunWriteAsync` completing and the production `RemoveGate` call. That race began failing CI once the class ran in parallel.
