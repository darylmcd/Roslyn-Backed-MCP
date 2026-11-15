# test-heavy-class-cost-fork-native-admission — Cut the per-test cost of ForkNativeAdmissionTests

**row:** `test-heavy-class-cost-fork-native-admission` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/Workspace/ForkNativeAdmissionTests.cs`

## Acceptance

- [ ] TRX shows what each test in `ForkNativeAdmissionTests` spends its seconds on (workspace load, restore, build, process start).
- [ ] The dominant cost is removed by shared fixture amortization, a cheaper fixture, or splitting the class so class-level parallelism applies, without weakening any assertion.
- [ ] Mean hosted case seconds per run for the class drop measurably on `windows-latest` (baseline 759s), measured from fresh hosted TRX.

## Evidence

- `CI_POLICY.md` 2026-10-29 recheck: mean TRX case seconds per run on `windows-latest` for `ForkNativeAdmissionTests` is 759s; MSTest runs a class's cases serially, so the class floors its leg's wall time.

## Context

- regression_shape: the class's mean hosted case seconds above a stated ceiling is visible in the collector report.
- Siblings: `test-heavy-integration-class-cost` (InterfaceBaseTypeInsertionIntegrationTests, 1013s), `test-heavy-class-cost-fork-native-admission`, `test-heavy-class-cost-moved-declaration-ownership`, `test-heavy-class-cost-crossproject-receiving-namespace`; `ci-shard-duration-profile-hosted` consumes the result.

## Notes

Ubuntu 26.04 evidence from the first refreshed profile PR 2097 / run 43617927737: ForkNativeAdmissionTests consumed 871.21 cumulative TRX case seconds in Linux 2. The leg took 29:39 despite near-equal projected weights. Isolate restore, build and native-probe costs using repeatable first-attempt evidence before choosing an optimization. See ci-ubuntu-duration-profile-refresh-2604.
