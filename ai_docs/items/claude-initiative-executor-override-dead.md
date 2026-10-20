# claude-initiative-executor-override-dead — `.claude/agents/initiative-executor.md` overrides an agent that no longer exists

**row:** `claude-initiative-executor-override-dead` · **pri:** `Low` · **size:** `S`

## Anchors

- `.claude/agents/initiative-executor.md` — repo-local override of the global `initiative-executor` agent, deleted from `~/.claude` on 2026-10-19 (darylmcd/claude-config#818)

## Acceptance

- [ ] The override file is deleted, or rewritten as guidance for the repo's remediation subagents if any of its content (Roslyn workspace bootstrap, release-cut constraints) is still needed — in that case it is renamed so it does not pose as an agent override.
- [ ] `rg initiative-executor` over the repo returns nothing outside `ai_docs/archive`.

## Evidence

- The global engine (`/backlog-remediate`, its agents, the bsweep state machine) was removed from `~/.claude` in PRs #818/#819; nothing dispatches `initiative-executor` any more — `~/.claude/scratchpad/remediation-simplification/RESULT.md`.

## Context

Dead override files mislead the next agent into thinking the engine still runs here (Directive #3: stale config is bad code).
