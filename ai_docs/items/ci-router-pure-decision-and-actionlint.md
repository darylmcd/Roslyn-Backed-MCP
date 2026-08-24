# ci-router-pure-decision-and-actionlint — Execute and lint the routing contract

**row:** `ci-router-pure-decision-and-actionlint` · **pri:** `Medium` · **size:** `M`

## Anchors

- `.github/workflows/ci.yml`
- `eng/resolve-ci-topology.ps1` (new)
- `eng/verify-actionlint.ps1` (new)
- `justfile`
- `tests/RoslynMcp.Tests/CiRunnerParityContractTests.cs`

## Acceptance

- [ ] Extract a pure topology decision that accepts event kind, changed-file records, and the reported file count; keep GitHub API access at the workflow boundary.
- [ ] Table-test code, policy docs, current/original rename paths, behavior-bearing Markdown, count mismatch/cap, API failure, pull request, dispatch, and schedule inputs.
- [ ] Assert the exact leg set, complete shard union, sole artifact owner, and event-specific aggregate check name for every route.
- [ ] Install a checksum-pinned repository-owned `actionlint` and run it from `just ci`; no developer-global executable is required.
- [ ] Keep only workflow integration sentinels in `CiRunnerParityContractTests`; comments and unrelated substrings cannot satisfy routing behavior.

## Evidence

The current workflow is schema-valid and its scoped string contracts cover the immediate topology, but inline route-array construction cannot be executed directly. A pure decision boundary will make fail-closed file routing and exact matrix ownership independently testable while repository-pinned actionlint catches YAML and expression regressions before push.
