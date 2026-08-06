# git-status-timeout-docs-scope-correction — correct the GitStatusTimeout docs, the knob bounds two call sites

**row:** `git-status-timeout-docs-scope-correction` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ValidationServiceOptions.cs:48`
- `ai_docs/runtime.md:63`
- `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs:319`

## Acceptance

- [ ] `ValidationServiceOptions.GitStatusTimeout`'s XML doc and the `ai_docs/runtime.md` env-var row state that the timeout bounds every `git status --porcelain` subprocess in `WorkspaceValidationService` (`validate_recent_git_changes` scope collection AND the `validate_workspace` change-tracker reconcile), and that only the former degrades the verdict.
- [ ] No other `ROSLYNMCP_*_TIMEOUT_SECONDS` row in `runtime.md` is left with a similarly narrowed scope claim.

## Evidence

- Code-quality review of PR #1152 (`validate-recent-git-changes-status-timeout-false-clean`): `_gitStatusTimeout` is used at both `WorkspaceValidationService.cs:137` (`ValidateRecentGitChangesAsync`) and `:319` (`ReconcileChangeTrackerFilesAsync`), but the new docs name only the first.

## Context

Spin-off from the `validate-recent-git-changes-status-timeout-false-clean` initiative (backlog-sweep plan `20260805T222513Z_backlog-sweep`, PR #1152).
