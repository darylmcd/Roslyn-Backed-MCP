# testquality-elapsed-bound-in-correctness-tests — Replace wall-clock pass/fail bounds in two correctness tests with structural assertions

**row:** `testquality-elapsed-bound-in-correctness-tests` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/WorkspaceToolsIntegrationTests.cs:468`
- `tests/RoslynMcp.Tests/ValidationToolsIntegrationTests.cs:423`

## Acceptance

- [ ] The 1000 ms assertion at `WorkspaceToolsIntegrationTests.cs:468` is removed; the test still fails against a status path that waits on `LoadLock` (a hang guard of 60 s or more may remain).
- [ ] The held `LoadLock` is still held when the call returns, asserted structurally.
- [ ] `ValidationToolsIntegrationTests.cs:423` asserts a structural scan bound (scanned-document counts from the diagnostics envelope) or keeps only a 60 s or longer hang guard.
- [ ] Each change is shown red against a simulated slow host or the pre-fix lock behaviour, not a retry.

## Evidence

- `WorkspaceToolsIntegrationTests.cs:432-470`: the header says pre-fix failure was a thrown `TimeoutException` after a 5 s lock wait, so the stopwatch adds only load sensitivity.
- `ValidationToolsIntegrationTests.cs:405-424`: correctness test carrying a 30 s elapsed budget.
- `rg -l WorkspaceToolsIntegrationTests ai_docs/items` → neither site is an anchor of `timing-test-reaudit-wave` or `gate-timing-tests-fail-under-machine-load`.
- refactor-audit 20261028-1500 at f8734a2f, finding `testquality-elapsed-bound-in-correctness-tests` (fingerprint 0b76756fb7); tier 0, tests not run for these two.

## Context

- regression_shape: no `Stopwatch` pass/fail bound under 60 s in either method.
- Counterargument weighed: bounds are generous against measured sub-ms and 1-3 s runs, and `Performance`-category classes (`PerformanceBaselineTests`) keep intentional budgets. Cost is a small rewrite; the gain is one fewer shared-runner flake source.
- Neighbours: `timing-test-reaudit-wave`, `gate-timing-tests-fail-under-machine-load`, `test-private-field-reflection-helper` (the test reaches `LoadLock` by reflection).
