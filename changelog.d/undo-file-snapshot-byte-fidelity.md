---
category: Fixed
---

- **Fixed:** preserved byte-exact undo and project-file writes, used platform-correct path identity, restored static-import stdout analyzer coverage, decomposed apply and catalog hot paths, documented scripting sync-over-async constraints, and removed duplicated test scaffolding. Closes `undo-file-snapshot-byte-fidelity`, `project-file-mutation-byte-fidelity`, `cross-platform-path-key-comparer`, `document-set-project-reference-write-decomposition`, `refactoring-apply-orchestration-decomposition`, `apply-verification-state-machine-decomposition`, `stdoutwrite-analyzer-complexity-split`, `servercatalog-analyzer-complexity-split`, `scripting-sync-over-async-document`, `nuget-vuln-scan-redundant-start-progress`, `workspace-unresolved-analyzer-message-wording`, `listlogger-tuple-assert-duplication`, `dup-method-detector-test-setup-dedup`, `aggregate-scorecard-test-runner-dedup`, and `core-dto-symbollocator-validate-unit-tests` (PR #1132).
