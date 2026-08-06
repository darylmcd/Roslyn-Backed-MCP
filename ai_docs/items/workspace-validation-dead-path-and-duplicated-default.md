# workspace-validation-dead-path-and-duplicated-default — consolidated low-severity cleanup in the git-status-timeout paths

**row:** `workspace-validation-dead-path-and-duplicated-default` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs:171` (`DegradeStatusWhenGitStatusUnknown(CreateTimeoutResult(...), gitTimedOut)` — dead wrapper)
- `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs:22` (`DefaultGitStatusTimeout = TimeSpan.FromSeconds(10)`)
- `src/RoslynMcp.Roslyn/Services/ValidationServiceOptions.cs:56` (`GitStatusTimeout = TimeSpan.FromSeconds(10)` — duplicated default)

## Acceptance

- [ ] The dead `DegradeStatusWhenGitStatusUnknown` wrapper on the catch branch is dropped (return `CreateTimeoutResult(...)` directly) or a one-line comment marks it as intentional defence-in-depth.
- [ ] The 10-second default lives in one place — the public ctor falls back to `new ValidationServiceOptions().GitStatusTimeout` (matching the pattern `BuildService`/`TestRunnerService` already use) instead of a second hardcoded literal.

## Evidence

- Code-quality review of PR #1152 (`validate-recent-git-changes-status-timeout-false-clean`): (1) `CreateTimeoutResult` hardcodes `OverallStatus: "timeout"` and the degrade only fires on an exact `clean` match, so the wrapper can never change the result. (2) The null-options/internal-ctor path and the DI path each read an independent 10-second literal.

## Context

Spin-off from the `validate-recent-git-changes-status-timeout-false-clean` initiative (backlog-sweep plan `20260805T222513Z_backlog-sweep`, PR #1152). Both findings are hygiene, not functional bugs; consolidated per the sweep's filing gate.
