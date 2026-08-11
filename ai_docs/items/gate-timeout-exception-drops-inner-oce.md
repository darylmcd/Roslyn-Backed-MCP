# gate-timeout-exception-drops-inner-oce — TimeoutException reclassification sites drop the original OperationCanceledException

**row:** `gate-timeout-exception-drops-inner-oce` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs` (`RunPerWorkspaceAsync`, `RunLoadGateAsync` — the `catch (OperationCanceledException) when (...) throw new TimeoutException(...)` blocks added/extended by `test-run-unfiltered-bare-error-rootcause`)
- `src/RoslynMcp.Roslyn/Services/GatedCommandExecutor.cs:95` (pre-existing `catch (OperationCanceledException) when (...) throw new TimeoutException(...)`)

## Acceptance

- [ ] Both `WorkspaceExecutionGate`'s two gate-timeout reclassification catches and `GatedCommandExecutor.ExecuteAsync`'s existing one pass the caught `OperationCanceledException` as the `innerException` argument to the `TimeoutException` constructor, instead of constructing a message-only `TimeoutException` that discards it
- [ ] Confirm `ToolErrorHandler.ClassifyAndFormat` (or wherever the resulting `TimeoutException` is logged/formatted) doesn't need a matching change to actually surface the inner exception — if it does, make the minimal change needed so the provenance isn't silently dropped by the next layer either
- [ ] Regression: an existing or new test asserts `TimeoutException.InnerException` is the original `OperationCanceledException` for at least one of the three call sites

## Evidence

Found during the spec-compliance re-review of `test-run-unfiltered-bare-error-rootcause`'s gate-timeout fix (2026-08-11): all three `throw new TimeoutException(...)` sites construct the exception from a message string only, dropping the caught `OperationCanceledException` — discarding cancellation provenance (e.g. which token fired, the original stack trace) from logs and any downstream diagnostics that inspect `InnerException`. Small, mechanical, but touches a pre-existing file (`GatedCommandExecutor.cs`) that `test-run-unfiltered-bare-error-rootcause` doesn't otherwise own, so it's scoped as its own unit rather than fixed inline there.

## Context

`TimeoutException` has a `TimeoutException(string? message, Exception? innerException)` constructor — the fix is a one-line change at each of the three sites (add `, ex` or equivalent to the constructor call, which requires naming the caught exception in the `catch` clause since two of the three currently use a parameterless `catch (OperationCanceledException)` with only a `when` filter).
