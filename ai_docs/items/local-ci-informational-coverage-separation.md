# local-ci-informational-coverage-separation — Align local CI with the PR gate

**row:** `local-ci-informational-coverage-separation` · **pri:** `Medium` · **size:** `S`

## Anchors

- `justfile`
- `CI_POLICY.md`
- `tests/RoslynMcp.Tests/JustfilePortabilityTests.cs`
- `tests/RoslynMcp.Tests/CiRunnerParityContractTests.cs`

## Acceptance

- [ ] `just ci` invokes the same no-coverage release-validation lane required for pull requests.
- [ ] Retain one explicit local recipe for full coverage, publish, manifest, and vulnerability validation.
- [ ] Documentation distinguishes required PR validation from informational scheduled/manual coverage.
- [ ] One source-contract regression pins both recipe mappings and the CI workflow arguments.

## Evidence

- The 2026-08-21 local gate treated a Coverlet session-end crash as fatal even though `CI_POLICY.md` classifies coverage as informational and pull-request CI invokes `verify-release.ps1 -NoCoverage`.
- The full coverage gate remains useful and passed after the contaminated analyzer fixture was corrected; the concern is command naming and required-lane parity, not removal of coverage verification.
