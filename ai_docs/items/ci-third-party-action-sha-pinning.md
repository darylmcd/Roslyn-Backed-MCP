# ci-third-party-action-sha-pinning — Pin CI actions to reviewed immutable revisions

**row:** `ci-third-party-action-sha-pinning` · **pri:** `Medium` · **size:** `M`

## Anchors

- `.github/workflows/ci.yml`
- `.github/workflows/publish-nuget.yml`
- `.github/dependabot.yml`
- `tests/RoslynMcp.Tests/CiRunnerParityContractTests.cs`

## Acceptance

- [ ] Replace mutable action major tags in the two release/validation workflows with reviewed full commit SHAs and retain readable version comments.
- [ ] Configure or confirm Dependabot updates for GitHub Actions so pinned revisions remain maintainable.
- [ ] Add one contract test that rejects non-SHA external `uses:` references while allowing repository-local actions.
- [ ] Run actionlint and both workflow contract suites after pinning.

## Evidence

The validation and publication workflows currently trust mutable major tags for checkout, SDK setup, cache, and artifact upload. Hosted runners limit persistence and workstation blast radius, but immutable revisions still reduce upstream tag-move and dependency supply-chain risk.
