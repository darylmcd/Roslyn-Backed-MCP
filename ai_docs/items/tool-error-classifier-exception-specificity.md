# tool-error-classifier-exception-specificity

**row:** `tool-error-classifier-exception-specificity` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs`
- `tests/RoslynMcp.Tests/TestRunFailureEnvelopeTests.cs`

## Acceptance

- [ ] Resolve the nearest registered base type independently of registration order and retain every existing public category/message. Pin reordered base/derived registration behavior.

## Evidence

The assignability walk relies on Dictionary insertion order for WorkspaceEvicted and PreviewTokenStale to outrank their base types. Verified during the 2026-09-14 tool-contract review.
