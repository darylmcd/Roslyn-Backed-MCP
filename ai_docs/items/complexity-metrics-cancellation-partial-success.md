# complexity-metrics-cancellation-partial-success — Propagate cancellation from metrics scans

**row:** `complexity-metrics-cancellation-partial-success` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CodeMetricsService.cs`
- `tests/RoslynMcp.Tests/CodeMetricsServiceTests.cs`

## Acceptance

- [ ] Use `ThrowIfCancellationRequested` at document and member boundaries instead of breaking enumeration.
- [ ] Pre-cancelled and deterministically mid-scan tokens propagate `OperationCanceledException` through service/tool layers.
- [ ] Cancellation never serializes a partial-success metrics list.

## Evidence

- Both cancellation polls currently `break`, returning incomplete results as successful analysis.
