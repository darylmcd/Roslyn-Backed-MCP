# release-environment-precheck-contract-tests — Test the release-environment precheck

**row:** `release-environment-precheck-contract-tests` · **pri:** `Low` · **size:** `S` · **deps:** `—`

## Anchors

- `eng/verify-release-environment.ps1`
- New `tests/RoslynMcp.Tests/ReleaseEnvironmentPrecheckContractTests.cs`

## Acceptance

- [ ] Extract an injectable snapshot decision inside the existing script without weakening the fail-closed live probes.
- [ ] Table-test memory thresholds, each blocking process family, the bounded dotnet count, ready exit `0`, inspection failure exit `1`, and environment refusal exit `2`.
- [ ] Exercise Windows, Linux, and macOS memory-probe parsing from deterministic fixtures where host-independent parsing exists; keep unsupported live-host behavior explicit.
- [ ] Preserve path-free, secret-safe operator output and ownership-scoped cleanup guidance.

## Evidence

PR #1408 added a release-critical cross-platform environment guard and manually exercised the Windows ready/refuse paths, but no automated regression owns its pure decision, exit-code contract, or Linux/macOS parsing branches. Adjacent review requires a bounded deterministic test seam rather than a flaky assertion against the current machine.
