# diagnostic-query-result-cache-concurrent-cap — Bound concurrent result-cache growth

**row:** `diagnostic-query-result-cache-concurrent-cap` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/DiagnosticQueryService.cs` (`StoreResult`)
- `tests/RoslynMcp.Tests/DiagnosticQueryServiceRegressionTests.cs`

## Acceptance

- [ ] Make same-version eviction and insertion one atomic per-workspace operation so concurrent distinct filters cannot exceed `MaxResultCacheEntriesPerWorkspace`.
- [ ] Preserve same-key replacement and newer-version replacement without letting an older writer mutate the newer entry.
- [ ] Add a synchronized concurrent-filter regression that proves the cache stays bounded and completed entries remain reusable.

## Evidence

The 2026-08-30 direct remediation review found `Results.Count`, arbitrary eviction, and insertion are separate operations on a concurrent dictionary, so simultaneous writers can all observe spare capacity and exceed the intended eight-entry bound.
