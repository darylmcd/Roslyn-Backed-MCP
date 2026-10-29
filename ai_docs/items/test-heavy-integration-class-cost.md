# test-heavy-integration-class-cost — Cut the per-test cost of InterfaceBaseTypeInsertionIntegrationTests

**row:** `test-heavy-integration-class-cost` · **pri:** `Medium` · **size:** `L`

## Anchors

- `tests/RoslynMcp.Tests/InterfaceBaseTypeInsertionIntegrationTests.cs`

## Acceptance

- [ ] TRX shows what each test in `InterfaceBaseTypeInsertionIntegrationTests` spends its seconds on (workspace load, restore, build, process start).
- [ ] The dominant cost is removed by shared fixture amortization, a cheaper fixture, or splitting the class so class-level parallelism applies, without weakening any assertion.
- [ ] Mean hosted case seconds per run for the class drop measurably on `windows-latest` (baseline 1013s), measured from fresh hosted TRX.

## Evidence

- `CI_POLICY.md` 2026-10-29 recheck: mean TRX case seconds per run on `windows-latest` for `InterfaceBaseTypeInsertionIntegrationTests` is 1013s; MSTest runs a class's cases serially, so the class floors its leg's wall time.

## Context

- regression_shape: the class's mean hosted case seconds above a stated ceiling is visible in the collector report.
- Siblings: `test-heavy-integration-class-cost` (InterfaceBaseTypeInsertionIntegrationTests, 1013s), `test-heavy-class-cost-fork-native-admission`, `test-heavy-class-cost-moved-declaration-ownership`, `test-heavy-class-cost-crossproject-receiving-namespace`; `ci-shard-duration-profile-hosted` consumes the result.
