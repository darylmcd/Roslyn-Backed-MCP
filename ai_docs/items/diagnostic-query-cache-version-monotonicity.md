# diagnostic-query-cache-version-monotonicity — Keep cache versions monotonic

**row:** `diagnostic-query-cache-version-monotonicity` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/DiagnosticQueryService.cs` (`CacheDiagnostics` and `StoreResult`)
- `tests/RoslynMcp.Tests/DiagnosticServiceFilterTotalsTests.cs`

## Acceptance

- [ ] A late query or detail fallback captured at workspace version N cannot replace raw or result cache state already stored for version N+1.
- [ ] A deterministic concurrency regression completes version N+1 before version N and proves the newer entry remains reusable.
- [ ] Normal same-version cache hits and bounded per-workspace eviction remain unchanged.

## Evidence

The 2026-08-30 extraction review found both cache writes assign the caller's captured version unconditionally. Version checks prevent stale output, but a late older completion can evict valid newer cache state and force repeated analyzer work.
