# workspace-execution-gate-test-timeout-ownership — Settle gate test timeouts

**row:** `workspace-execution-gate-test-timeout-ownership` · **pri:** `Medium` · **size:** `S` · **deps:** `timing-test-guard-operation-ownership`

## Anchors

- `tests/RoslynMcp.Tests/WorkspaceExecutionGateTests.cs:1172`

## Acceptance

- [ ] A timed-out gate test cancels and observes its underlying gate operation before releasing owned fixture state.
- [ ] A red-first blocked-operation regression proves no gate operation remains active after the test helper returns or throws.

## Evidence

- Same-mechanism sweep during the PR #1969 timeout-ownership repair found an unowned `WaitAsync` at `WorkspaceExecutionGateTests.cs:1172` that can leave gate work live after a waiter timeout.
