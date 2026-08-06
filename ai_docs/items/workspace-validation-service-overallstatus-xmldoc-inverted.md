# workspace-validation-service-overallstatus-xmldoc-inverted — IWorkspaceValidationService OverallStatus XML doc has skipped/timeout inverted

**row:** `workspace-validation-service-overallstatus-xmldoc-inverted` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Core/Services/IWorkspaceValidationService.cs:53` (`OverallStatus` XML doc enumeration)
- `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs:968` (`CreateTimeoutResult` sets `OverallStatus: "timeout"`)

## Acceptance

- [ ] The `<param name="OverallStatus">` enumeration matches the values the service can actually return: `clean`, `compile-error`, `analyzer-error`, `test-failure`, `test-zero-run`, `git-status-unknown`, `timeout`.
- [ ] `skipped` is either removed from the doc or its producing path is identified and documented.

## Evidence

- Code-quality review of PR #1169 (`document-git-status-unknown-verdict`): `rg '"skipped"' src tests` returns zero hits in production code (only an unrelated scorecard-script test), while `CreateTimeoutResult` sets `OverallStatus: "timeout"` — the doc has the set exactly inverted on those two values (`skipped` listed but never emitted; `timeout` emitted but not listed).

## Context

Spin-off from the `document-git-status-unknown-verdict` initiative (backlog-sweep plan `20260806T023001Z_backlog-sweep`, PR #1169). Pre-existing drift, not introduced by that PR, but surfaced while reviewing the same enumeration surface.
