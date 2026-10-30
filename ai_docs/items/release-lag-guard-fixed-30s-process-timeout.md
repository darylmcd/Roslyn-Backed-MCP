# release-lag-guard-fixed-30s-process-timeout — ReleaseLagGuardTests fixed 30s process timeouts

**row:** `release-lag-guard-fixed-30s-process-timeout` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/ReleaseLagGuardTests.cs:177`
- `tests/RoslynMcp.Tests/ReleaseLagGuardTests.cs:197`

## Acceptance

- [ ] Root cause of the 30+ s verifier run on hosted Windows identified (pwsh startup under shard load vs verifier work) and fixed without loosening the guard; link or merge with timing-test-reaudit-wave.

## Evidence

- PR 1910 run 40927393890 failed windows-hosted-4-of-4 twice (ThresholdEnvVariable_OverridesDefaultAndInvalidValuesFallBack: "changelog verifier timed out after 30 seconds", elapsedMs 31769 then 32462) while the same class passes 5/5 in 9 s locally on that head and the same leg passed on PR 1909. Not proven a flake on a base commit.
- Sighted during the 2026-10-29 remediation of refactoring-rebase-source-kind-preservation, cross-project-interface-reference-binding and automatic-reload-analyzer-reader-lifetime (PRs 1908-1910). Not re-derived beyond the cold reviews.
