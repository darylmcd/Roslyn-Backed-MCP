# testbase-workspace-cap-comment-drift — Correct fixture capacity ownership comment

**row:** `testbase-workspace-cap-comment-drift` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/TestBase.cs:171` — stale production-default and fixture-lifetime comment.
- `tests/RoslynMcp.Tests/WorkspaceCapLruEvictionTests.cs:66` — actual default capacity contract.

## Acceptance

- [ ] Replace stale numeric and fixture ownership claims with a current bounded policy pointer; verify production option and TestServiceContainer/assembly lifetime.
- [ ] Keep executable behavior and test assertions unchanged; use source/docs checks and cold review rather than a mirrored runtime test for prose.

## Evidence

- Main58ba4207 comment says production default8; WorkspaceManagerOptions actual default16 and DefaultMaxConcurrentWorkspaces_Is_16 test corroborate it.
- Fixture capacity ownership moved to TestServiceContainer. Current comment also describes older lifetime assumptions.

## Context

- Independent nonblocking comment-only follow-up; no restore closure dependency.
