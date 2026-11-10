# ci-contract-test-pins-exact-action-sha — Stop pinning exact third-party action SHAs in the CI parity contract test

**row:** `ci-contract-test-pins-exact-action-sha` · **pri:** `Low` · **size:** `S` · **verified:** `a2a51ec4de033bfd3aa36ba9d238b73e849b0f9a`

## Anchors

- `tests/RoslynMcp.Tests/CiRunnerParityContractTests.cs`
- `.github/workflows/ci.yml`

## Acceptance

- [ ] The test requires actions/setup-dotnet to be pinned to a 40-hex SHA with a version comment and matches ci.yml; a SHA-only dependabot bump needs no test edit; the fragment gate still requires a changelog fragment for workflow changes (document that for dependabot).

## Evidence

- PR 2043 (setup-dotnet v6.1.0) failed Quality_IndependentlyRestoresAndPreparesTheFormatterBeforeRunningIt and the changelog-fragment gate; the test hard-codes the old SHA at CiRunnerParityContractTests.cs ~:417.
