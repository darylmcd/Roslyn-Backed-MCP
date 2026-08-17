# release-policy-ci-trigger-drift — Align release policy with CI triggers

**row:** `release-policy-ci-trigger-drift` · **pri:** `Low` · **size:** `S`

## Anchors

- `docs/release-policy.md` — CI Gate trigger statement.
- `CI_POLICY.md` — canonical trigger contract.

## Acceptance

- [ ] State that CI runs on pull requests, manual dispatch, and the weekly schedule.
- [ ] Preserve the intentional no-push-to-main rationale by pointing to `CI_POLICY.md` instead of duplicating it.
- [ ] One documentation link/phrase check prevents the false main-push claim from returning.

## Evidence

- The release policy says CI runs on every PR and `main`, while `CI_POLICY.md` and `.github/workflows/ci.yml` intentionally omit push-to-main.
