# ci-process-tests-runner-adoption — Share CI script process fixtures

**row:** `ci-process-tests-runner-adoption` · **pri:** `Low` · **size:** `S` · **deps:** `powershell-script-test-runner-foundation`

## Anchors

- `tests/RoslynMcp.Tests/TestShardPlanContractTests.cs`
- `tests/RoslynMcp.Tests/TestResultsSummaryContractTests.cs`

## Acceptance

- [ ] Replace both private PowerShell process launchers and result records with the shared `PwshScriptRunner`.
- [ ] Preserve argument boundaries, concurrent stdout/stderr draining, timeout tree-kill, environment injection, and full failure diagnostics.
- [ ] Keep each suite's planner/summary-specific assertions unchanged.
- [ ] One source scan proves neither anchored file retains a private pwsh resolver, `ProcessStartInfo`, or duplicate result record.

## Evidence

Both new CI contract suites need process isolation but repeat the exact infrastructure already tracked by `powershell-script-test-runner-foundation`. Folding them into the foundation implementation would exceed its three-test-file bound, so this is a dependent adoption slice.
