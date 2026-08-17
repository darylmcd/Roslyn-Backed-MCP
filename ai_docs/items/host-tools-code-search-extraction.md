# host-tools-code-search-extraction — Extract code-search tools

**row:** `host-tools-code-search-extraction` · **pri:** `Low` · **size:** `M` · **deps:** `host-tools-code-quality-extraction`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs` — `FindReflectionUsages`, `SemanticSearch`.
- New `src/RoslynMcp.Host.Stdio/Tools/CodeSearchTools.cs`.
- `tests/RoslynMcp.Tests/FindReflectionUsagesPaginationTests.cs`
- New `tests/RoslynMcp.Tests/CodeSearchToolsTests.cs`.

## Acceptance

- [ ] Move exactly `find_reflection_usages` and `semantic_search` with no forwarding wrappers or duplicate registrations.
- [ ] Preserve reflection pagination/summary, semantic-query, metadata, parameter/default, payload, and cancellation contracts.
- [ ] One table-driven output-equivalence/discovery matrix proves both names appear once and valid/invalid inputs behave unchanged.

## Evidence

- These two endpoints form the remaining code-search cluster after reflection completeness is explicit.
