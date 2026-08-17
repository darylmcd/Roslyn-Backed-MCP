# ci-router-runner-label-duplication — single-source the roslynmcp-dev runner label in the ci.yml route job

**row:** `ci-router-runner-label-duplication` · **pri:** `Low` · **size:** `S` · **deps:** `local-vulnerability-audit-fail-closed`

## Anchors

- `.github/workflows/ci.yml` (route job — runs_on JSON payload + online-runner filter)

## Acceptance

- [ ] The `roslynmcp-dev` literal appears exactly once in the `route` step; both the `runs_on` JSON payload and the online-runner `-contains` filter derive from that single variable.
- [ ] `docs/self-hosted-runner.md` § Troubleshooting quotes the single-source variable rather than a raw JSON literal, if applicable.

## Evidence

- Found during code-quality review of PR #1211 (`ci-runner-offline-hosted-fallback-router`): the router job embeds `roslynmcp-dev` independently in the `runs_on` JSON payload and in the online-runner filter predicate. Renaming the runner's label updates neither in lockstep — the probe then matches zero runners and routes hosted permanently, announced only by an `::notice` on the fallback branch (a silent, self-consistent-looking degrade).

## Context

Spin-off from the code-quality review of PR #1211. Deliberately left unfixed in that PR — not required by its acceptance criteria, and low urgency (only fires on a runner rename, which is rare and already produces a working, if under-optimal, hosted-fallback outcome rather than an outage).
