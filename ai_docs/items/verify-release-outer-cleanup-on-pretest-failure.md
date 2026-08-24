# verify-release-outer-cleanup-on-pretest-failure — Clean every verifier exit path

**row:** `verify-release-outer-cleanup-on-pretest-failure` · **pri:** `Medium` · **size:** `S`

## Anchors

- `eng/verify-release.ps1`
- `tests/RoslynMcp.Tests/VerifyReleaseChildScriptTests.cs`

## Acceptance

- [ ] Put build-server shutdown and exact run-owned temporary-directory cleanup under an outer `finally` covering restore, Release build, shard planning, test execution, TRX validation, and publish failures.
- [ ] Attempt every cleanup action even after an earlier cleanup fails; retain the primary failure and aggregate redacted cleanup diagnostics.
- [ ] Never delete a shared temp root or output owned by another process; resolve and validate each exact run-owned path before recursive cleanup.
- [ ] One parameterized child-process regression injects each pre-test and post-test failure and proves the owned temp root is absent and shutdown was attempted exactly once.

## Evidence

The current cleanup region begins after restore, Release build, and shard planning. Any failure before that region can leave compiler build servers or private run state behind on a reused developer agent, even though hosted CI workers are ephemeral.
