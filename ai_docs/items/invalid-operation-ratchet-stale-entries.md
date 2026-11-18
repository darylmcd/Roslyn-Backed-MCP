# invalid-operation-ratchet-stale-entries — Fail the IOE construction ratchet on stale baseline entries

**row:** `invalid-operation-ratchet-stale-entries` · **pri:** `Low` · **size:** `S` · **verified:** `12bc2c8b5395056e11d2772b593347500033e5b6`

## Anchors

- `tests/RoslynMcp.Tests/InvalidOperationConstructionRatchetTests.cs`:981-990
- `tests/RoslynMcp.Tests/TestData/invalid-operation-construction-baseline.json`

## Acceptance

- [ ] The ratchet fails when a baseline entry no longer matches a construction (or its count exceeds the actual), with a message naming the stale entries; the baseline is pruned in the same PR.

## Evidence

- reloc-b report (PR 2144), sketch 1: "Ratchet baseline carries 36 stale entries for constructions that no longer exist ... AssertSubset hides them" (measured on the slice-B branch, now merged).
- Verified at 12bc2c8b: `AssertSubset` (lines 981-990) computes only `additions`.
