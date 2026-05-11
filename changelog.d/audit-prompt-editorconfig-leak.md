---
category: Fixed
---

- **Fixed:** `/mcp-server-surface-test` and `/mcp-server-stress` audit prompts no longer leak `.editorconfig` modifications into the audited repo's primary checkout. Phase 7 step 2 (`set_editorconfig_option`) and Phase 8b W2 (concurrency writer probe) now explicitly require the disposable worktree as the write target — the primary-checkout write path that produced IT-Chat-Bot's post-2026-05-11-audit `.editorconfig` drift is removed. Each phase also gains a mandatory `git checkout -- .editorconfig` revert step. New Phase 0 step 2a captures an *Isolation baseline* (the audited repo's pre-audit `git status --porcelain`), and Final surface closure step 3a is a hard gate that diffs the run-end primary-checkout state against the baseline and files any leak as a P1 finding with category `audit-prompt-leak`. The Phase 6 teardown's local git-status check already covered Phase 6; this is the catch-all for every other mutating phase.
