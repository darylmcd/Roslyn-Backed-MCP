# promotion-scorecard-refresh-toplevel-run — Refresh the promotion scorecard from a top-level --full surface-test run

**row:** `promotion-scorecard-refresh-toplevel-run` · **pri:** `Medium` · **size:** `S`

## Anchors

- `audit-reports/_latest-promotion-scorecard.json`
- `.claude/skills/promote-tier/SKILL.md`
- `skills/mcp-server-surface-test/SKILL.md`

## Acceptance

- [ ] `audit-reports/_latest-promotion-scorecard.json` is regenerated against the CURRENT server surface and committed, replacing the `serverVersion: 1.38.1` / `generatedAt: 2026-05-16T06:25:47Z` snapshot.
- [ ] The refresh comes from a genuine `--full` run, not the degraded `--single-agent` path — a partial scorecard is worse than a stale one because it destroys the staleness signal while looking current.

## Evidence

Verified 2026-09-02: the canonical scorecard still reads `generatedAt: 2026-05-16T06:25:47Z` / `serverVersion: 1.38.1` against a v4.1.2 server. PR #1202 shipped only the durability prerequisite (the scorecard is now git-tracked, so a refresh produces a reviewable diff) — not the refresh itself.

## Context

Split from `promotion-tier-execution-batch` (2026-09-02) to separate the blocked ops run from the promotions it gates.

**BLOCKED — environment capability gap, not a work failure, and it has already failed once this way.** Initiative `promotion-tier-scorecard-refresh-execution` (sweep `20260825T151721Z`) was planned, scheduled `heroic-last`, executed, and deferred without shipping. Per `skills/mcp-server-surface-test/SKILL.md:55`, dispatching `audit-phase-runner` subagents is "the only way the `--full` tier achieves its 250+ tool-call coverage"; `:56` documents `--single-agent` as the opt-out for hosts that cannot spawn subagents. **A workflow-dispatched executor subagent cannot itself spawn subagents**, so only the degraded path was available.

**RE-SCOPE — do not re-plan this as a sweep initiative.** It must be run by a **top-level orchestrator session** that can spawn subagents: invoke `/mcp-server-surface-test --full` directly, then commit the refreshed scorecard. Nesting it inside `/backlog-remediate` will fail the same way every time.

**Stale anchor corrected here:** the parent cited `skills/promote-tier/`, which does not exist — the maintainer skill lives at `.claude/skills/promote-tier/`.

**No staleness alarm exists.** Nothing compares `generatedAt` / `serverVersion` against the current build; tracked by `surface-test-audit-artifact-gate-and-scorecard-staleness`.
