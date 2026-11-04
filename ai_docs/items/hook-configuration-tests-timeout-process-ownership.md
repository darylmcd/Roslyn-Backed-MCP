# hook-configuration-tests-timeout-process-ownership — Bound hook test cleanup

**row:** `hook-configuration-tests-timeout-process-ownership` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/HookConfigurationTests.cs:401`
- `tests/RoslynMcp.Tests/Helpers/PwshScriptRunner.cs`

## Acceptance

- [ ] On hook runner timeout, terminate the owned child, await exit and both redirected output reads under a bounded cleanup deadline, and retain timeout plus cleanup errors.
- [ ] Red-first blocked-child and held-pipe regressions prove the helper returns within its deadline with no owned child left live.

## Evidence

- Cold review of PR #1971 found `HookConfigurationTests.cs:401-420` waits after kill with `CancellationToken.None` and does not drain both pipes on timeout.
