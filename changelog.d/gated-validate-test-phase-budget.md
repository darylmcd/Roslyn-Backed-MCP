---
category: Fixed
---

- **Fixed:** Validation bundles run related tests under TestTimeout outside the validation phase cap, request deadline, and source read lock, preserve non-retryable timeout verdicts, and report workspace-changed when source mutation or closure invalidates a would-be clean verdict. workspace_fork_apply releases the source writer lock after copy and preview replay and closes loaded forks on cancellation (`gated-validate-test-phase-budget`).
