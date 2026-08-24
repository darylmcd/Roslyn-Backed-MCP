# test-shard-adapter-discovery-parity — Compare metadata and adapter discovery

**row:** `test-shard-adapter-discovery-parity` · **pri:** `Medium` · **size:** `S`

## Anchors

- `eng/get-test-shard-plan.ps1`
- `tests/RoslynMcp.Tests/TestShardPlanContractTests.cs`

## Acceptance

- [ ] Obtain the runnable class catalog from the installed MSTest/VSTest adapter without parsing human-formatted console prose.
- [ ] Fail when the planner omits an adapter-discovered class or includes a class the adapter cannot select.
- [ ] Cover inherited/custom test-class discovery and parameterized methods in a small fixture assembly.
- [ ] Keep the existing exact-filter, deterministic, disjoint-union, and empty-shard checks.

## Evidence

The current completeness regression compares a two-shard plan with a one-shard plan produced by the same metadata algorithm. If that algorithm misses a future MSTest discovery shape, both plans agree and CI can remain green while never executing the omitted class.
2026-08-24 planner/TRX cross-check: metadata discovery cataloged 317 classes while the combined Windows TRX covered 316. The missing WorkspaceReadConcurrencyBenchmark class is intentionally removed later by TestCategory!=Benchmark, proving the current planner union is not yet adapter/filter execution parity.
