# complexity-metrics-nesting-self-node-classification — Count nested control-flow self nodes

**row:** `complexity-metrics-nesting-self-node-classification` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CodeMetricsService.cs`
- `tests/RoslynMcp.Tests/CodeMetricsNestingTests.cs`

## Acceptance

- [ ] Count each nested control-flow construct's own node when deriving maximum nesting depth.
- [ ] Preserve the established depth contract for blocks, expressions, and sibling branches.
- [ ] An unbraced nested-`if` regression expects the mathematically correct depth instead of locking the current undercount.

## Evidence

- The existing deep-nesting test comment derives depth 6 while its assertion and current visitor result are 5, exposing a self-node classification omission.
