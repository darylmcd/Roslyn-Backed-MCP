# diagnostic-query-cache-test-contract — Replace cache reflection in tests

**row:** `diagnostic-query-cache-test-contract` · **pri:** `Low` · **size:** `S` · **deps:** `—`

## Anchors

- `src/RoslynMcp.Roslyn/Services/DiagnosticQueryService.cs`
- `tests/RoslynMcp.Tests/DiagnosticQueryServiceRegressionTests.cs`

## Acceptance

- [ ] Replace reflection over the private `_resultCache` field and record members with a narrow internal observation contract or behavior-only assertions.
- [ ] Preserve the concurrent eight-entry cap proof and exact-filter reuse checks.
- [ ] Add one regression shape that fails when the cache exceeds its cap without binding to private field or property names.

## Evidence

The 2026-09-01 diagnostic policy review found `ReadCachedResults` reflecting over `_resultCache`, `TryGetValue`, and `Results`; routine private renames can break the test without changing cache behavior.
