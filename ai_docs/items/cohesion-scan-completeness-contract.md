# cohesion-scan-completeness-contract — Report cohesion scan completeness

**row:** `cohesion-scan-completeness-contract` · **pri:** `Medium` · **size:** `M` · **deps:** `public-exception-detail-policy,mcp-logging-stderr-otel-migration`

## Anchors

- `src/RoslynMcp.Core/Models/CohesionMetricsDto.cs`
- `src/RoslynMcp.Core/Services/ICohesionAnalysisService.cs`
- `src/RoslynMcp.Roslyn/Services/CohesionAnalysisService.cs`
- `src/RoslynMcp.Host.Stdio/Tools/CohesionAnalysisTools.cs`
- `tests/RoslynMcp.Tests/CohesionAnalysisTests.cs`

## Acceptance

- [ ] Add `CohesionMetricsScanResult(Metrics, IsComplete, FailedTypeCount)` and one detailed interface method that owns the real scan.
- [ ] `get_cohesion_metrics` uses the detailed result and emits `isComplete` plus `failedTypeCount` without dropping successful metrics.
- [ ] Keep the list-returning method as a compatibility projection over the detailed scan, but fail closed with stable safe guidance when incomplete so prompt/refactoring consumers cannot infer completeness.
- [ ] Per-type non-cancellation failures increment the count and emit secret-safe correlated diagnostics; replace silent cancellation breaks with `ThrowIfCancellationRequested` so cancellation propagates.
- [ ] One table-driven scanner-outcome regression covers mixed success/failure and cancellation, proving partial fields, retained metrics, legacy-projection refusal, and unchanged ordering/content in the preexisting complete `count`/`metrics` subtree.
- [ ] Classify the additive public fields under the SDK compatibility decision and changelog policy.

## Evidence

- `CohesionAnalysisService` catches per-type failures, logs, and skips the type.
- Its cancellation polls currently break and return a partial result instead of propagating cancellation.
- `get_cohesion_metrics` serializes the reduced list and `count` with no completeness signal, while prompt/refactoring consumers receive the same partial list as if complete.
