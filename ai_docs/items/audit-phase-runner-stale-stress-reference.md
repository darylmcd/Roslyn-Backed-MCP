# audit-phase-runner-stale-stress-reference — Update the stale /mcp-server-stress reference in agents/audit-phase-runner.md

**row:** `audit-phase-runner-stale-stress-reference` · **pri:** `Low` · **size:** `S`

## Anchors

- `agents/audit-phase-runner.md`
- `tests/RoslynMcp.Tests/CiTopologyDecisionContractTests.cs:60`

## Acceptance

- [ ] `agents/audit-phase-runner.md` frontmatter and body no longer name the retired `/mcp-server-stress` command; they reference the current `/mcp-server-surface-test` skill.
- [ ] `CiTopologyDecisionContractTests` keeps passing (it forces full validation on any edit to this file; run the full gate).

## Evidence

- Deepener finding for `surface-test-skill-prompt-drift` (plan 20260930T213336Z): the agent's frontmatter still says `/mcp-server-stress`, which no longer exists. Left out of that row because editing the file forces full validation (`CiTopologyDecisionContractTests.cs:60`).

## Context

- Follow-on of `surface-test-skill-prompt-drift`, which ships this agent in the plugin package.
