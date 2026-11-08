# linux-pwsh-startup-cache-functional-cohort-failure — Identify failing native cache child

**row:** `linux-pwsh-startup-cache-functional-cohort-failure` · **pri:** `High` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/LinuxPowerShellStartupCacheOwnershipTests.cs:47`
- `tests/RoslynMcp.Tests/Fixtures/pwsh-startup-cache-ownership.sh`
- `tests/RoslynMcp.Tests/Fixtures/pwsh-startup-cache-functional.ps1`

## Acceptance

- [ ] Identify the failing parent and its exact error from retained per-parent output; distinguish module-cache serialization timeout from other functional failures.
- [ ] Reproduce the failure on the relevant base commit or obtain a bounded causal playback witness before attributing it to an environment or prior code.
- [ ] Repair the proven owner without weakening the functional assertions, 30-second module-cache budget, test selection, or hosted deadline; validate the affected Linux child and descendant cases.

## Evidence

- PR #1971 run `41911201587`, Linux shard 3, failed `NativeCache_DeniesOrdinaryProfileWritesAndPreservesFunctionalConsumersAsync` after 33 seconds: `Functional cohort failed`, exit 1. The failing parent log was not emitted by that head. PR #1970 Linux shard 3 passed; the base failure and cause remain unproven. PR #1971 follow-up adds failure-only per-parent log output without changing the assertion or deadline.

## Context

- A 30-second module-analysis cache poll exists in launch 1, so its timeout is a hypothesis from elapsed time, not an established cause.
- The separate `linux-pwsh-startup-profile-invalid-assembly-identity` row tracks a malformed native assembly identity; no evidence yet connects it to this cohort failure.

## Notes

2026-11-08: PR #2023 fixed a demonstrated module-cache file-sharing/readiness race in the native startup verifier and added bounded writer playback tests. The PR #1971 functional cohort's failing child was not logged, so that occurrence remains unattributed; retain this row until its exact parent/error and causal witness are established.
