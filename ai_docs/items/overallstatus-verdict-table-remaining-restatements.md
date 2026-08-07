# overallstatus-verdict-table-remaining-restatements — convert the last two overallStatus restatements to canonical-table pointers

**row:** `overallstatus-verdict-table-remaining-restatements` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Prompts/RoslynPrompts.RefactoringWorkflows.cs:341-344`
- `skills/refactor-loop/SKILL.md:94-99`

## Acceptance

- [ ] `RoslynPrompts.RefactoringWorkflows.cs:341-344`'s hardcoded 4-value `overallStatus` list (clean/compile-error/analyzer-error/test-failure) is replaced with a pointer to the single-sourced canonical table — it is stale since PR #1169 added `test-zero-run`, `timeout`, and `git-status-unknown`.
- [ ] `skills/refactor-loop/SKILL.md:94-99`'s duplicated `test-zero-run`/`timeout` prose is replaced with the same pointer.

## Evidence

- Traced during code-quality review of PR #1184 (`single-source-overallstatus-verdict-table`): grep for `analyzer-error` across the worktree at HEAD found exactly these two non-test, non-plan restatement sites outside that PR's diff.

## Context

Spin-off from single-sourcing the `validate_workspace` `overallStatus` verdict table (PR #1184), which converted 4+ sites but did not reach these two.
