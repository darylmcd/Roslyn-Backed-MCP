# host-tools-complexity-extraction — Extract complexity tools

**row:** `host-tools-complexity-extraction` · **pri:** `Low` · **size:** `M` · **deps:** `host-tools-code-search-extraction`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs` — remaining `GetComplexityMetrics` endpoint and obsolete type.
- New `src/RoslynMcp.Host.Stdio/Tools/ComplexityTools.cs`.
- `tests/RoslynMcp.Tests/ExpandedSurfaceIntegrationTests.cs`
- `tests/RoslynMcp.Tests/Top10V3RegressionTests.cs`

## Acceptance

- [ ] Move the final `get_complexity_metrics` endpoint, delete the empty `AdvancedAnalysisTools` type/file, and add no forwarding wrapper or duplicate registration.
- [ ] Preserve pagination validation, metadata, parameters/defaults, payload, and cancellation behavior.
- [ ] One focused valid/invalid boundary matrix proves exactly one registration and unchanged results/errors.

## Evidence

- The preceding slices account for 11 endpoints; complexity is the twelfth and final responsibility left in the original file.
