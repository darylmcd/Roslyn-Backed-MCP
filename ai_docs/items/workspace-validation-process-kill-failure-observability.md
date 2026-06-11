# workspace-validation-process-kill-failure-observability — surface swallowed cleanup-kill failures

**row:** `workspace-validation-process-kill-failure-observability` · **pri:** `Low` · **size:** `S` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs`
- focused workspace validation tests

## Acceptance

- [ ] Bounded non-secret observability added for cleanup kill failures without changing validation envelopes
- [ ] Regression: controllable process/kill seam forces cleanup kill failure after timeout/failure and asserts warning/log observability while preserving the existing validation warning shape

## Evidence

- Observed while implementing `dotnet-command-runner-kill-failure-observability`; Standing Engineering Directive #3.

## Context

`WorkspaceValidationService` still has best-effort process cleanup paths that call `Process.Kill(entireProcessTree: true)` and swallow kill exceptions while returning a bounded validation warning. If the cleanup kill fails, callers can miss leaked `git status` process risk and file-lock context.
