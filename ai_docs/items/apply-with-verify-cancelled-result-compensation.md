# apply-with-verify-cancelled-result-compensation — Compensate returned compile-check cancellation

**row:** `apply-with-verify-cancelled-result-compensation` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ApplyWithVerifyTool.cs:67-192`
- `tests/RoslynMcp.Tests/ApplyWithVerifyCancellationAndScopeTests.cs`

## Acceptance

- [ ] A cancelled pre-apply baseline result throws `OperationCanceledException` carrying the caller token and performs zero apply and zero revert calls.
- [ ] A cancelled post-apply result attempts exactly one best-effort revert with a fresh token, then surfaces cancellation.
- [ ] Existing thrown-cancellation behavior remains unchanged; revert false/throw paths log at Error and preserve the original cancellation.
- [ ] No cancelled compile-check result can serialize `applied`, `applied_with_errors`, or `rolled_back`.
- [ ] Tests pin exact compile, apply, and revert call counts.

## Evidence

- Read-only context: `src/RoslynMcp.Roslyn/Services/CompileCheckService.cs:47-85` converts `OperationCanceledException` into `CompileCheckDto.Cancelled=true` (`src/RoslynMcp.Core/Models/CompileCheckDto.cs:5-31`), but `ApplyWithVerifyTool` currently compensates only when an exception escapes.

## Dependencies

- None. Land before workflow extraction so the refactor preserves the corrected behavior.
