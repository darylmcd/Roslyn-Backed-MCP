---
category: Maintenance
---

- **Maintenance:** Extract the post-open load finalization from `WorkspaceManager` into an internal `WorkspaceLoadFinalizer` collaborator: the physical-path refusal, per-project status, document-path index and restore/build readiness projection now live there, while the manager keeps session coordination, the cache write and the atomic old/new workspace swap. No tool, wire or load/reload behavior change. Adds regressions for the link refusal on initial load and reload and for a finalization failure during reload leaving the prior session unchanged. Closes `workspace-manager-load-finalization-collaborator-extraction`.
