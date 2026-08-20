---
category: Fixed
---

- **Fixed:** the pre-merge CI gate now runs the full `eng/verify-release.ps1` suite on `ubuntu-latest` alongside the routed self-hosted Windows runner, so an OS-sensitive test can no longer pass on Windows, merge, and then fail the Linux publish gate at tag time (the v3.0.1 cut shipped no package for exactly this reason). `.github/workflows/ci.yml`'s `route` job now emits a runner matrix, `validate` fans out over it as `validate-leg (<os>)` legs with artifact uploads scoped to the primary leg, and the aggregator job keeps the `validate` check name that branch protection already requires — so no ruleset change is needed. `CI_POLICY.md` and `docs/self-hosted-runner.md` record the shared merge/publish OS coverage (`ci-merge-publish-runner-os-parity`).
