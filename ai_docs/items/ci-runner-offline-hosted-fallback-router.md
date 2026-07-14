# ci-runner-offline-hosted-fallback-router — Hosted-fallback router for the self-hosted runner

**row:** `ci-runner-offline-hosted-fallback-router` · **pri:** `Medium` · **size:** `S`

## Anchors

- `.github/workflows/ci.yml` (validate job `runs-on` conditional)
- `docs/self-hosted-runner.md` (§ Availability — documents the gap and the manual workaround)

## Acceptance

- [ ] A cheap ubuntu `route` job probes `GET /repos/{owner}/{repo}/actions/runners` and outputs the label set; `validate` consumes it via `needs.route.outputs`.
- [ ] Runner offline → PR runs land on `ubuntu-latest` instead of queueing indefinitely; runner online → behavior unchanged (self-hosted for maintainer PRs, hosted for fork/dependabot).
- [ ] Missing/invalid PAT secret degrades to current behavior (self-hosted routing), never blocks CI.

## Evidence

- PR #1055 runs queued 1h57m and ~14.5h behind an asleep/wedged runner (runs 29278594120, 29289006580) — 2026-07-14 CI-hang investigation.

## Context

`runs-on` is evaluated before any job executes, so the fallback needs a prior router job. The workflow `GITHUB_TOKEN` cannot read the self-hosted runners API (repo-admin scope); the operator must create a fine-grained PAT (Administration: read) and store it as a repo secret before this row is implementable. Dependabot routing + the powercfg/service-recovery guidance shipped 2026-07-14 already mitigate the common cases; this row closes the remaining box-offline window.
