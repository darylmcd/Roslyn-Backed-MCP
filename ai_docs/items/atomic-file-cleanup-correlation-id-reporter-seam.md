## Anchors

- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs:242` — temp-cleanup warning path
- `src/RoslynMcp.Core/Services/IUnexpectedExceptionReporter.cs:39`
- `src/RoslynMcp.Host.Stdio/Diagnostics/ServerObservability.cs:109`
- `tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs:113`

## Acceptance

- Temp-file cleanup diagnostics carry the real ambient correlation id instead of the literal `unavailable`.
- The route crosses layers through the Core `IUnexpectedExceptionReporter` seam (not `RequestCorrelationContext`, which is internal to `RoslynMcp.Host.Stdio`).
- The existing test's `correlationId=unavailable` assertion is replaced by one pinning a real id.

## Evidence

Traced in code during PR #1283 review. `ProjectUnexpected` passes `correlationId` straight to `NormalizeCorrelationId` (`PublicExceptionDetailPolicy.cs:25,47-52`), which returns the literal `unavailable` for null — and the shipped test asserts exactly that. The sanitization landed correctly, but the correlation half of the contract is inert: a redacted diagnostic with no joinable id cannot be traced to a server-side record.

Source: code-quality review of PR #1283 (initiative `atomic-file-cleanup-error-detail-redaction`, sweep 20260819T180531Z).
