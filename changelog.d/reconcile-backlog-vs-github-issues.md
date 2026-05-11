### Added

- `/reconcile-backlog-vs-issues` maintainer skill — audits every `gh #NNN` reference in `ai_docs/backlog.md` against live GitHub Issue state and emits a 5-state triage report (issue-closed-row-open, issue-closed-row-reserved, reserved-stale, label-drift, issue-reopened-row-missing). Read-only — does not auto-edit the backlog. ([#643](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/643))
