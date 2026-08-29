---
category: Fixed
---

- **Fixed:** Removed ineffective stdio shutdown flushing, redacted process-kill diagnostics, hardened timing, cleanup, restore, and cooperative-cancellation test infrastructure, and made the skill-prefix guard exercise its real drift path. Closes `stdio-shutdown-flush-transport-ownership`, `timing-sensitive-test-assertions-flake-under-load`, `skill-prefix-guard-tests-assert-bcl-not-gate`, `addenda-ci-equivalent-self-hosted-runner-caveat`, `dotnet-command-runner-kill-log-path-redaction`, `test-temp-cleanup-helper-adoption-wave-2`, `isolated-workspace-restore-helper-deduplication`, and `mstest-cooperative-timeout-token-flow`.
