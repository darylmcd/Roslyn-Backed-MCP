# workspace-load-registration-test-timeout-ownership — Settle load registration timeouts

**row:** `workspace-load-registration-test-timeout-ownership` · **pri:** `Medium` · **size:** `S` · **deps:** `timing-test-guard-operation-ownership`

## Anchors

- `tests/RoslynMcp.Tests/WorkspaceLoadRegistrationCompensationTests.cs:245`
- `tests/RoslynMcp.Tests/WorkspaceLoadRegistrationCompensationTests.cs:247`
- `tests/RoslynMcp.Tests/WorkspaceLoadRegistrationCompensationTests.cs:250`
- `tests/RoslynMcp.Tests/WorkspaceLoadRegistrationCompensationTests.cs:252`

## Acceptance

- [ ] Timeout of any gated load or signal waiter cancels and settles the owned load before fixture cleanup.
- [ ] Red-first regressions cover both signal waits and both load waits, asserting that no load remains active after cleanup.

## Evidence

- Same-mechanism sweep during the PR #1969 timeout-ownership repair found four unowned `WaitAsync` sites at `WorkspaceLoadRegistrationCompensationTests.cs:245-252` that can strand a load after a signal waiter times out.
