# test-coverage-unexpected-error-not-iserror — Report test_coverage unexpected failures as isError through the shared filter

**row:** `test-coverage-unexpected-error-not-iserror` · **pri:** `Medium` · **size:** `M` · **deps:** `validation-tools-error-envelope-not-iserror`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/TestCoverageTools.cs:126`
- `src/RoslynMcp.Roslyn/Services/TestCoverageCoordinator.cs:174`
- `tests/RoslynMcp.Tests/TestCoverageFailureEnvelopeTests.cs`

## Acceptance

- [ ] The generic `catch (Exception ex)` in `test_coverage` no longer converts an unexpected exception into a normal (`isError: false`) result; the exception reaches `StructuredCallToolFilter`, so the wire result carries `isError: true`, `_meta`, and a correlated `InternalError` report — the same contract PR #1659 applies to ValidationTools.
- [ ] Caller cancellation still propagates as OperationCanceledException; decide and document whether the gate-timeout branch (`BuildTimeoutResult`) stays a structured success-shaped envelope or also becomes an error, consistent with `test_run` after #1659.
- [ ] `TestCoverageCoordinator.BuildUnexpectedErrorResult` is deleted if it has no remaining callers.
- [ ] A wire-level test asserts `isError: true` for an injected unexpected failure (confirmed failing against the base code).

## Evidence

- HEAD 6f31f065 `TestCoverageTools.cs:126-135`:
  `catch (Exception ex) { ... var details = UnexpectedExceptionReporting.Report(exceptionReporter, ex, UnexpectedExceptionCategory.TestCoverage); return SerializeWithDeprecation(TestCoverageCoordinator.BuildUnexpectedErrorResult(details.Public), deprecation); }` — returned as the tool's normal string payload.
- `TestCoverageCoordinator.cs:174-188`: `BuildUnexpectedErrorResult` builds `new TestCoverageResultDto(Success: false, Error: summary, ...)` — a failure encoded inside a success result.
- Same defect class as row `validation-tools-error-envelope-not-iserror` (PR #1659, open at filing time, which does not touch TestCoverageTools); depends on it so the shared-filter pattern lands first.

Source: backlog-remediate 20260926T234932Z follow-up (Directive #3 spin-off of PR #1659).
