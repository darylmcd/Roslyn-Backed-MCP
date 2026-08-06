# document-git-status-unknown-verdict — document the git-status-unknown verdict on every agent-facing status surface

**row:** `document-git-status-unknown-verdict` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs:19`
- `src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs:40`
- `ai_docs/domains/tool-usage-guide.md:57`
- `skills/refactor-loop/SKILL.md:94`
- `skills/modernize/SKILL.md:72`

## Acceptance

- [ ] `validate_workspace` + `validate_recent_git_changes` MCP Descriptions enumerate the full status set including `test-zero-run` and `git-status-unknown`, each with its one-line caller action.
- [ ] `tool-usage-guide.md` and the `refactor-loop` / `modernize` skill decision tables list a rule for `git-status-unknown` (re-run; or raise `ROSLYNMCP_GIT_STATUS_TIMEOUT_SECONDS`) and for `test-zero-run`.

## Evidence

- Code-quality review of PR #1152 (`validate-recent-git-changes-status-timeout-false-clean`): `WorkspaceValidationService.cs:195-196` can now emit `git-status-unknown`, yet every agent-facing status surface (tool Description, tool-usage-guide.md, both skill decision tables) enumerates a closed set stopping at `test-failure`/`timeout`.

## Context

Spin-off from the `validate-recent-git-changes-status-timeout-false-clean` initiative (backlog-sweep plan `20260805T222513Z_backlog-sweep`, PR #1152). Also folds in the pre-existing (not introduced by this PR) `test-zero-run` documentation gap found during the same review pass.
