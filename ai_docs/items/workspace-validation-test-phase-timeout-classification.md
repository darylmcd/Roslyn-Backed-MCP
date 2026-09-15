# workspace-validation-test-phase-timeout-classification

**row:** `workspace-validation-test-phase-timeout-classification` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs`
- `tests/RoslynMcp.Tests/WorkspaceValidationTimeoutTests.cs`

## Acceptance

- [ ] Let InternalValidationTimeoutException from the test_run phase reach the existing timeout-result boundary rather than the generic test invocation catch. Preserve genuine runner failures as test-failure and outer cancellation unchanged. Add one deterministic test-run timeout regression across direct and Git-scoped validation, proving timeout, retryability, and retained scope. Record an ADR and migration note for callers branching on OverallStatus.

## Evidence

2026-09-15 direct validation-scope review: DiscoverAndOptionallyRunTestsAsync catches Exception after RunValidationPhaseAsync converts a phase timeout to InternalValidationTimeoutException; the broad catch synthesizes Failed=1 and ComputeOverallStatus reports test-failure instead of timeout.
