# claude-initiative-executor-override-dead - Retire stale active-agent comparisons

**row:** `claude-initiative-executor-override-dead` · **pri:** `Low` · **size:** `S`

## Anchors

- `.claude/agents/backlog-intake-extractor.md:123`

## Acceptance

- [ ] Active extractor instructions no longer describe retired initiative-executor or plan-vetter agents as current dispatch roles.
- [ ] Preserve read-only evidence extraction, parent-owned anchor verification and sanctioned backlog writing; retain historical audit evidence without presenting it as a live workflow.

## Evidence

- 2026-10-22: original executor-override premise is false in current files and origin/main; history records removal in ad576b9c (PR #1556).
- The remaining active extractor guidance compares its role with initiative-executor and plan-vetter at lines123-124. Reconcile those descriptions against the current thin remediation workflow.
- Global remediation engine and agents were removed in darylmcd/claude-config PRs #818/#819. No override or global profile-path anchor is invented here.
