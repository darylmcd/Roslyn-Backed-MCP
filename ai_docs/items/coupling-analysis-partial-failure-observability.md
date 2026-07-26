# coupling-analysis-partial-failure-observability — Surface skipped coupling metrics

**row:** `coupling-analysis-partial-failure-observability` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CouplingAnalysisService.cs`
- `src/RoslynMcp.Core/Models/CouplingMetricsDto.cs`
- `tests/RoslynMcp.Tests/CouplingAnalysisTests.cs`

## Acceptance

- [ ] Per-type metric failures remain isolated but are surfaced in the returned result as an explicit partial-result signal, count, or bounded warning list.
- [ ] Cancellation still propagates and is never converted into a partial success.
- [ ] A deterministic regression forces one type computation to fail and proves callers can distinguish the partial result from complete success.

## Evidence

- Cold review on 2026-07-26 found per-type exceptions are logged and omitted, while the returned result provides no indication that metrics are incomplete.
