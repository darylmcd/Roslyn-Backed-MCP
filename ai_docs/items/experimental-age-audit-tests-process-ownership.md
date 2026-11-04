# experimental-age-audit-tests-process-ownership — Own audit script test processes

**row:** `experimental-age-audit-tests-process-ownership` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/Skills/ExperimentalAgeAuditScriptTests.cs:186`
- `tests/RoslynMcp.Tests/Helpers/PwshScriptRunner.cs`
- `tests/RoslynMcp.Tests/Support/GitFixtureRunner.cs`

## Acceptance

- [ ] Route the private Git and PowerShell helpers through shared bounded owned runners, preserving arguments, output assertions, and deadlines.
- [ ] Red-first pressure and timeout regressions prove stream drains and child exit before the helper returns.

## Evidence

- Cold review of PR #1968 found a Git helper at `ExperimentalAgeAuditScriptTests.cs:186-201` that ignores the timed wait and exit code, and a PowerShell helper at `:203-232` that reads redirected streams sequentially before its timed wait and throws immediately after kill.
