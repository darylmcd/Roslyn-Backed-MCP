# ci-merge-group-validate-support — Report the required validate check on merge_group events

**row:** `ci-merge-group-validate-support` · **pri:** `High` · **size:** `M`

## Anchors

- `.github/workflows/ci.yml`
- `eng/resolve-ci-topology.ps1`
- `CI_POLICY.md`
- `tests/RoslynMcp.Tests/CiTopologyDecisionContractTests.cs`
- `tests/RoslynMcp.Tests/CiRunnerParityContractTests.cs`

## Acceptance

- [ ] `ci.yml` triggers on `merge_group` and the `validate-gate` job reports the `validate` context for `pull_request` AND `merge_group` (never for dispatch/schedule).
- [ ] `resolve-ci-topology.ps1` accepts `merge_group` and routes it to the full code-PR topology (fail-closed: no docs-only/evidence-only shortcut without a trusted changed-file set).
- [ ] merge_group legs run the PR-shaped release verification and changed-file format check with `-BaseRef` from `github.event.merge_group.base_sha`; `cancel-in-progress` stays false for merge_group.
- [ ] CI_POLICY.md documents the merge-queue event; contract tests cover the new event and the gate-name expression.
- [ ] After merge, the operator-approved (2026-09-27) ruleset `merge_queue` rule (squash, group size 1) is enabled and one PR lands through the queue end to end.

## Evidence

- Ruleset 14515474 requires context `validate` with `strict_required_status_checks_policy: false`; `ci.yml` names the gate job `validate` only when `github.event_name == 'pull_request'` (else `validate-informational`), so a merge-queue run would never report the required check and queued PRs would hang.
- Operator decision 2026-09-27: land through GitHub (no local CI duplicating `validate`), enable merge queue for current-main integration testing.

## Context

Merge queue replaces the rebase-all + post-merge `ci.yml` dispatch backstop used for backlog-remediate 20260926T234932Z gen-1 landing. Global landing tooling (`/ship --land`, `bsweep-state land`/`land-queue`) must learn to enqueue (`gh pr merge --auto`) and wait for MERGED — tracked in the global `~/.claude` backlog, not here.
