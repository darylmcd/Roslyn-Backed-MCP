---
category: Fixed
---

- **Fixed:** Validation forks exclude linked-worktree Git pointer files as well as Git directories; pipe-lifetime tests use owned descendants, and release validation disables reusable build servers instead of shutting down other agents' servers (`workspace-fork-copy-linked-worktree-gitfile`, `pipe-lifetime-test-global-build-server-shutdown`).
