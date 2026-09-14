# workspace-validation-git-process-lifetime — Own Git collection cleanup

**row:** `workspace-validation-git-process-lifetime` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs` — CollectGitChangedFilesAsync cancellation and stream tasks
- `tests/RoslynMcp.Tests/ValidateRecentGitChangesTests.cs` — Git collection regressions

## Acceptance

- [ ] Extract Git process collection into a focused internal collaborator, keeping the public validation service contract unchanged and the slice within two production files and one test file.
- [ ] On caller cancellation, terminate and await the owned Git process before propagating cancellation; observe both output-reader tasks on every exit path.
- [ ] Preserve timeout versus caller cancellation classification and sanitized warnings.
- [ ] Use one controlled blocked-process regression to prove process exit and stream completion before the operation returns.

## Evidence

- CollectGitChangedFilesAsync starts output readers inside its try block, then timeout/failure paths kill without awaiting process exit or observing both reader tasks.
- The caller-cancellation catch only rethrows; disposing Process does not terminate a still-running child. Git process lifecycle is embedded in the 1,074-line validation coordinator.
- Found while fixing the independent 1 ms timeout-test race for Dependabot PR #1521. This lifecycle redesign is a separate production behavior change from controlling the existing regression trigger.
