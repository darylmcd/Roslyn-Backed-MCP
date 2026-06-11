# surface-test-shipped-prompt-local-skill-reference — remove maintainer-local .claude path from shipped prompt

**row:** `surface-test-shipped-prompt-local-skill-reference` · **pri:** `Low` · **size:** `M` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `skills/mcp-server-surface-test/prompts/phases/apply-and-test.md`
- `eng/verify-skills-are-generic.ps1`

## Acceptance

- [ ] Reference replaced with portable wording or a public/repo-shipped anchor
- [ ] Considered: `eng/verify-skills-are-generic.ps1` rejecting `.claude/skills/` references in shipped prompts
- [ ] Regression: static check proves shipped prompt files do not reference `.claude/skills/` unless an explicit allowlist entry documents why

## Evidence

- 2026-06-02 top-5 remediation code-quality review, Standing Directive #3.

## Context

Shipped surface-test prompt text points readers at maintainer-local `.claude/skills/reconcile-backlog-sweep-plan/SKILL.md`, which may be unavailable for installed plugin consumers and is not covered by the current shipped-skill genericity guard.
