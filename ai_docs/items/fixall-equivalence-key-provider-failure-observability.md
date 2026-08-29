# fixall-equivalence-key-provider-failure-observability — Observe swallowed provider failures

**row:** `fixall-equivalence-key-provider-failure-observability` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FixAllService.cs` (`GetEquivalenceKeyAsync`)
- `tests/RoslynMcp.Tests/FixAllServiceTests.cs`

## Acceptance

- [ ] Replace the non-cancellation catch that silently skips provider registration failures with correlated, secret-safe reporting through `IUnexpectedExceptionReporter` and `ILogger` while still trying the next diagnostic occurrence.
- [ ] Propagate cancellation unchanged and never include raw provider messages, source paths, or user code in operator diagnostics.
- [ ] One regression proves a failing occurrence is reported, a later healthy occurrence can still supply the equivalence key, and no sensitive sentinel crosses the public or operator boundary.

## Evidence

Direct review of `FixAllService.GetEquivalenceKeyAsync` found `catch (Exception ex) when (ex is not OperationCanceledException) { continue; }`. The exception is discarded without observability even though provider execution is an external extension boundary and the service already owns secret-safe exception reporting.
