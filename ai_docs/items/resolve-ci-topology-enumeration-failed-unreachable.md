# resolve-ci-topology-enumeration-failed-unreachable — `resolve-ci-topology.ps1`'s `-EnumerationFailed` fail-closed path is unreachable from the real `ci.yml` caller

**row:** `resolve-ci-topology-enumeration-failed-unreachable` · **pri:** `Low` · **size:** `M`

## Anchors

- `eng/resolve-ci-topology.ps1:47-51`
- `eng/resolve-ci-topology.ps1:162-168`
- `.github/workflows/ci.yml:60-61`

## Acceptance

- [ ] Either `ci.yml`'s "Decide validation topology" step calls `resolve-ci-topology.ps1` with `-EnumerationFailed` on a `gh api` failure (instead of throwing directly), so the fail-closed reason text has one source of truth, or the switch/branch is documented as test-only scaffolding with no production caller.
- [ ] No behavior change required — the current throw-before-invoke already fails closed; this is a design-consistency cleanup, not a correctness bug.

## Evidence

Cold `implementation-reviewer` on `ci-router-pure-decision` (PR #1441, merged): `ci.yml:60-61` throws immediately when `gh api ... $LASTEXITCODE -ne 0`, before `eng/resolve-ci-topology.ps1` is ever invoked. The script's own `-EnumerationFailed` branch (`:47-51`, `:162-168`), which returns a graceful `docs_only=false` decision with reason "Pull-request file listing could not be verified (API failure); routing full validation.", is exercised only by `CiTopologyDecisionContractTests.EnumerationFailed_FailsClosedWithADistinctReasonAsync` — never by the production workflow.
