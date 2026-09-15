# symbol-search-response-byte-budget

**row:** `symbol-search-response-byte-budget` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ReferenceResponsePager.cs`
- `tests/RoslynMcp.Tests/SymbolSearchPaginationTests.cs`

## Acceptance

- [ ] Exercise full and summary results containing long serialized fields; every successful page stays within the configured byte ceiling and continuation visits each result once. Preserve existing offset semantics and all locator fields.

## Evidence

- 2026-09-15 direct source review: SymbolTools.SearchSymbols caps rows and optionally strips fields, but documentation and locator strings remain unbounded; the summary comment incorrectly claimed a transport cap.
