---
category: Maintenance
---

- **Maintenance:** Single-sourced the `validate_workspace`/`validate_recent_git_changes` `overallStatus` verdict set into one canonical table in `ai_docs/domains/tool-usage-guide.md`, and fixed 3 surfaces (`skills/modernize/SKILL.md`, `skills/mcp-server-surface-test/prompts/phases/apply-and-test.md`, `ai_docs/prompts/stress-test-external-repo.md`) that still enumerated only the original 4-value set (`clean`/`compile-error`/`analyzer-error`/`test-failure`) after PR #1169 added `test-zero-run`/`timeout`/`git-status-unknown` — those surfaces now reference the canonical table instead of restating a stale list.
