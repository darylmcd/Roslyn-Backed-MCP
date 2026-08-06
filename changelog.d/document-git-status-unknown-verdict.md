---
category: Maintenance
---

- **Maintenance:** Documented the `test-zero-run` and `git-status-unknown` `validate_workspace`/`validate_recent_git_changes` verdicts across the agent-facing status surfaces that previously stopped at `clean`/`compile-error`/`analyzer-error`/`test-failure`/`timeout` — both tool Descriptions and `ai_docs/domains/tool-usage-guide.md` now enumerate the full verdict set (reachability-scoped: `git-status-unknown` is `validate_recent_git_changes`-only), and the `refactor-loop`/`modernize` skill decision tables gained `test-zero-run`/`timeout` (the two verdicts reachable via the `validate_workspace` call they document).
