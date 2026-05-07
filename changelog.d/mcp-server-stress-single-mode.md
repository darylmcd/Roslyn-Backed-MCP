---
category: Changed — BREAKING
---

- **Changed — BREAKING:** Collapsed `/audit-deep` modes — `full`, `promotion-only`, `read-only` — into a single canonical run. Apply tools always exercised on a disposable worktree the skill creates and tears down post-run; promotion scorecard always emitted. The audited repo's working tree is never mutated. `--no-worktree` available for environments that can't create a worktree (degraded mode, recorded in report header). Closes `mcp-server-stress-single-mode`.
