# gate-tests-task-delay-writer-queue — Remove Task.Delay from gate writer-queue tests

**row:** `gate-tests-task-delay-writer-queue` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/WorkspaceExecutionGateTests.cs:255`
- `tests/RoslynMcp.Tests/WorkspaceExecutionGateTests.cs:264`

## Acceptance

- [ ] Tests assert queue state deterministically with no sleep.

## Evidence

- PR 1909 added tests proving the synchronous-registration technique; these older tests predate it.
- Sighted during the 2026-10-29 remediation of refactoring-rebase-source-kind-preservation, cross-project-interface-reference-binding and automatic-reload-analyzer-reader-lifetime (PRs 1908-1910). Not re-derived beyond the cold reviews.
