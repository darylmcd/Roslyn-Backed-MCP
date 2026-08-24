# dependabot-contract-sensitive-package-routing — Isolate MCP SDK upgrades from servicing groups

**row:** `dependabot-contract-sensitive-package-routing` · **pri:** `Medium` · **size:** `S`

## Anchors

- `.github/dependabot.yml`
- `docs/upgrade-matrix.md`
- `tests/RoslynMcp.Tests/CiRunnerParityContractTests.cs`

## Acceptance

- [ ] Exclude `ModelContextProtocol` from the generic NuGet minor/patch group so each SDK upgrade receives a dedicated PR.
- [ ] Keep routine servicing packages grouped and preserve the separate version-paired MSTest policy.
- [ ] Add one contract regression that proves every dependency marked protocol/contract-sensitive in the upgrade matrix is excluded from generic Dependabot groups.
- [ ] Document that an MCP SDK PR must update or supersede ADR 0003, refresh notices/upgrade-matrix evidence, and exercise every supported raw-wire protocol era before merge.

## Evidence

Dependabot PR #1326 combined ModelContextProtocol 2.2.0 with nine routine analyzer, framework, test, SourceLink, and security-package updates. The repository upgrade matrix requires release-note review, ADR 0003 disposition, and raw-wire-era verification for every MCP SDK bump; the generic grouped PR cannot express or enforce that review boundary.
