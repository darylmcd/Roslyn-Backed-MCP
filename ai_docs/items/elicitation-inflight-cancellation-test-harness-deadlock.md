# elicitation-inflight-cancellation-test-harness-deadlock — investigate whether cancelling a caller's token while an elicitation request is genuinely in flight hangs

**row:** `elicitation-inflight-cancellation-test-harness-deadlock` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Elicitation/ElicitationChoicePrompt.cs:120-124` (the `server.ElicitAsync` await)
- `src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs` (`RunReadAsync`/`RunPerWorkspaceAsync`, the linked-token plumbing a real call would go through)
- `tests/RoslynMcp.Tests/SymbolDisambiguationElicitationTests.cs` (the test class where this would be regression-tested)

## Acceptance

- [ ] Determine root cause: attempting to build a regression test that cancels the caller's own `CancellationToken` while an `elicitation/create` request is genuinely in flight (client handler never completes, cancellation triggered from inside the client's handler on receipt) deadlocks — the test hangs indefinitely (confirmed with an MSTest `[Timeout(15_000)]` backstop; the underlying test process must be killed, it does not time out cleanly on its own). This reproduced identically whether cancellation was dispatched inline from the handler OR decoupled via `Task.Run`, and independently of whether `ElicitationChoicePrompt.TryElicitChoiceAsync`'s `server.ElicitAsync` await was wrapped in `.AsTask().WaitAsync(cancellationToken)` — ruling out simple `Cancel()`-reentrancy and simple "outer await doesn't observe the token" as the sole cause. The actual blocking point (SDK request-tracking internals? the in-memory `Pipe`/`StreamServerTransport` duplex transport used only by this single-process test harness? something in `WorkspaceExecutionGate`'s semaphore/lock chain?) was not isolated within this investigation's budget.
- [ ] Determine whether this is a test-harness-only artifact (single-process client+server sharing an in-memory pipe and, likely, a synchronization context/thread that production's separate-process stdio transport never shares) or a genuine production risk: a real MCP client that stops responding to an outstanding `elicitation/create` request combined with the caller cancelling — does the server-side `find_references`/`go_to_definition`/`symbol_search` call actually unblock and return the `WorkspaceExecutionGate` slot it holds, or does it hang, silently consuming a gate slot until process restart?
- [ ] If genuinely a production risk: land a real fix (candidate starting point already explored and reverted — wrapping `server.ElicitAsync(...)` in `.AsTask().WaitAsync(cancellationToken)` did NOT resolve the reproduction, so the fix is not that simple) and land the regression test that currently cannot be written safely.
- [ ] If a test-harness-only artifact: document why (e.g. "the in-memory duplex-pipe harness deadlocks on X; production's stdio transport is immune because Y") in a comment on the harness helper, so a future attempt doesn't re-discover the same dead end.

## Evidence

Traced live during the `elicitation-trychoice-cancellation-swallow` row's spec-compliance re-review (top-n-remediation run 20260810T233007Z): the reviewer required acceptance bullet 2 ("a pre-cancelled CancellationToken surfaces cancellation ... through at least one SymbolTools disambiguation path") to be proven non-vacuously — i.e. cancellation genuinely in flight, not pre-cancelled before the call. Two independent implementation attempts (inline `cts.Cancel()` from the client's `ElicitationHandler`; the same dispatched via `Task.Run` to rule out synchronous reentrancy) both hung indefinitely and had to be killed via `taskkill`, burning multiple hours of wall clock before being reverted. A third attempt wrapping the awaited call in `.AsTask().WaitAsync(cancellationToken)` — normally a guaranteed fix for "outer await doesn't observe cancellation of an inner task that never completes" — also did not resolve it, meaning the block is upstream of that await entirely.

## Context

Spin-off from the `elicitation-trychoice-cancellation-swallow` row (top-n-remediation run 20260810T233007Z). That row shipped with acceptance bullet 2 satisfied via a pre-cancelled-token test that calls `ElicitationChoicePrompt.TryElicitChoiceAsync` directly (not through `SymbolTools`) — the spec-compliance reviewer's stricter "through SymbolTools, non-vacuously" bar was not met; this row exists to close that gap properly, with a dedicated investigation budget instead of a fix-cycle's.
2026-08-13 adjacent review: SymbolDisambiguationElicitationTests' pre-cancel regression still uses non-cooperative [Timeout(15_000)] (MSTEST0045). Fold replacement with a bounded cooperative timeout/cancellation shape into this row; do not file separate timeout debt.
