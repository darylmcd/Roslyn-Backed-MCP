---
category: Fixed
---

- **Fixed:** made document-set persistence transactional, redacted workspace-fork restore errors, consolidated apply/verify behavior, improved prompt and path hot paths, and decomposed high-risk orchestration hotspots with deterministic shutdown and rollback behavior. Closes `client-root-path-validator-remaining-complexity`, `coupling-analysis-compute-hotspot-decomposition`, `document-set-persistence-transactional-rollback`, `editservice-apply-verify-workflow-consolidation`, `host-tools-prompt-reflection-and-path-io-perf`, `refactoringservice-remaining-complexity-decomposition`, `stdio-shutdown-flush-observability`, `validate-recent-git-changes-error-boundary`, `workspace-fork-apply-orchestration-decomposition`, and `workspace-fork-restore-error-redaction` (PR #1126).
