# workspace-validation-zero-run-warning-accuracy

**row:** `workspace-validation-zero-run-warning-accuracy` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs`
- `tests/RoslynMcp.Tests/WorkspaceValidationVerdictTests.cs`

## Acceptance

- [ ] Emit the zero-test warning only for the same state that produces test-zero-run. Preserve genuine runner failure envelopes without an additional filter-race claim, and use observed facts rather than an unproven working-directory or change-tracker cause.

## Evidence

2026-09-15 direct validation-verdict review: AppendTestZeroRunWarning checks Total == 0 even when Failed > 0; synthetic invocation failures therefore append a misleading filter-resolution warning.
