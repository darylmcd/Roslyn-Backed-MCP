# symbol-search-pagination-integer-overflow

**row:** `symbol-search-pagination-integer-overflow` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`
- `tests/RoslynMcp.Tests/SymbolSearchPaginationTests.cs`

## Acceptance

- [ ] Cover offset=int.MaxValue with a positive allowed limit. Return a clear argument error or an overflow-safe terminal page; never request a wrapped scan size or report a negative continuation.

## Evidence

- 2026-09-15 direct source review: SearchSymbols computes Math.Max(1000, offset + limit) with unchecked int arithmetic despite accepting nonnegative int offsets; a large offset can wrap its requested scan size.
