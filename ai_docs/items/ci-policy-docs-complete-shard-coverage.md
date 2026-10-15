# ci-policy-docs-complete-shard-coverage — Restore complete policy-documentation test shards

**row:** `ci-policy-docs-complete-shard-coverage` · **pri:** `High` · **size:** `M` · **deps:** `—`

## Anchors

- `.github/workflows/ci.yml:171`
- `eng/verify-release.ps1:452`
- `eng/docs-only-test-classes.txt:1`
- `tests/RoslynMcp.Tests/CiTopologyDecisionContractTests.cs:376`
- `tests/RoslynMcp.Tests/VerifyReleaseChildScriptTests.cs:230`

## Acceptance

- [ ] Remove the docs-only test allowlist, selection switch and zero-test shard bypass; preserve both complete disjoint class shards required by CI_POLICY.md.
- [ ] Exercise the real workflow run block and verifier with controlled dotnet execution on both documentation shards; assert the exact selected filter and required nonempty TRX.
- [ ] Observe old behavior fail, then run focused contracts, unsharded local CI and the full hosted matrix on the corrected head.
- [ ] Retain evidence-only routing and documentation publish/audit exemptions; do not weaken the declared complete test partition.

## Evidence

- PR1787 headc36617c0 hosted run38809032189 reported green, but its artifacts contained only100tests on docs-linux-1-of-2 and no TRX on docs-linux-2-of-2.
- The release script replaces the selected shard filter with12allowlisted classes on shard0 and skips shard1; CI_POLICY.md requires the complete partition on both policy-documentation shards.
- Source and artifacts inspected2026-10-15; stop the current publication gate and repair this discovered prerequisite before shipping final metadata.
