# apply-with-verify-revertbudget-configurable — Make apply_with_verify's RevertBudget configurable

**row:** `apply-with-verify-revertbudget-configurable` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ApplyWithVerifyTool.cs:34`

## Acceptance

- [ ] Revert budget for the post-cancellation best-effort revert is sourced from config/options rather than a hardcoded 30s literal
- [ ] Default remains 30s so behavior is unchanged when unset

## Evidence

- `RevertBudget = TimeSpan.FromSeconds(30)` is a hardcoded revert timeout; well-documented named const but not operator-tunable — see code-quality review, PR #1038.
