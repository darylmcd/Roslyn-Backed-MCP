# security-ci-base-ref-interpolated-into-run — Extend the run-body interpolation guard to ci.yml and bind its expressions through env

**row:** `security-ci-base-ref-interpolated-into-run` · **pri:** `Low` · **size:** `S`

## Anchors

- `.github/workflows/ci.yml:313`
- `.github/workflows/ci.yml:56`
- `tests/RoslynMcp.Tests/PublishVersionContextTests.cs:181`
- `tests/RoslynMcp.Tests/CiRunnerParityContractTests.cs:178`

## Acceptance

- [ ] The guard test (`PublishWorkflow_NeverInterpolatesContextIntoRunBodies`) covers every file under `.github/workflows`, not only `publish-nuget.yml`.
- [ ] `ci.yml` run bodies read context through `env:` and `$env:`, starting with `github.base_ref` at :313; `CiRunnerParityContractTests.cs:178` asserts the env-bound shape.
- [ ] The extended guard is red on the current `ci.yml` before the fix.

## Evidence

- `.github/workflows/ci.yml:313` expands `github.base_ref` into a pwsh command line; `rg -c '\$\{\{' .github/workflows/ci.yml` → 42 expression sites.
- `tests/RoslynMcp.Tests/PublishVersionContextTests.cs:181-247` is the existing guard and scopes to `publish-nuget.yml` only.
- `tests/RoslynMcp.Tests/CiRunnerParityContractTests.cs:178` locks the interpolated line verbatim.
- refactor-audit 20261028-1500 at f8734a2f, finding `security-ci-base-ref-interpolated-into-run` (fingerprint 9da181f519); tier 0.

## Context

- regression_shape: the guard fails on any `${{ }}` in a `run:` body of any workflow.
- Counterargument weighed: `base_ref` names a branch in this repo and darylmcd is the only collaborator with push; most ci.yml expressions are integers or enums. The row removes the per-site judgment and covers a future collaborator.
- Update `CiRunnerParityContractTests` in the same change; it asserts the old shape.
