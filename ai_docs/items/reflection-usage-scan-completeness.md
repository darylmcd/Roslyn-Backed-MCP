# reflection-usage-scan-completeness — Report reflection scan completeness

**row:** `reflection-usage-scan-completeness` · **pri:** `Medium` · **size:** `M` · **deps:** `public-exception-detail-policy,mcp-logging-stderr-otel-migration,complexity-metrics-pagination-validation-hoist,compilation-cache-wire-group-c-consumer,compilation-cache-cancellation-test-contract-drift`

## Anchors

- `src/RoslynMcp.Core/Models/ReflectionUsageDto.cs`
- `src/RoslynMcp.Core/Services/ICodePatternAnalyzer.cs`
- `src/RoslynMcp.Roslyn/Services/CodePatternAnalyzer.cs`
- `src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs`
- `tests/RoslynMcp.Tests/FindReflectionUsagesPaginationTests.cs`
- `tests/RoslynMcp.Tests/CompilationCacheAdoptionTests.cs`
- `tests/RoslynMcp.Tests/ExpandedSurfaceIntegrationTests.RepoSolutionAnalysis.cs`

## Acceptance

- [ ] Return `ReflectionUsageScanResult(Usages, IsComplete, FailedDocumentCount)` from the sole production method/consumer path.
- [ ] Detailed and summary `find_reflection_usages` responses emit `isComplete` and `failedDocumentCount`; when incomplete, document `totalCount` as an observed lower bound and scope `hasMore` to observed usages.
- [ ] Per-tree non-cancellation failures increment the count and emit secret-safe correlated diagnostics; replace silent cancellation breaks with `ThrowIfCancellationRequested` so cancellation propagates.
- [ ] One table-driven scanner-outcome regression covers mixed trees and cancellation, pinning partial metadata and pagination while complete ordering/content remain unchanged; direct service tests only adapt to the result wrapper.
- [ ] Classify the additive public fields under the SDK compatibility decision and changelog policy.

## Evidence

- `CodePatternAnalyzer` catches per-tree failures and continues after logging.
- Its cancellation polls currently break and return a partial result instead of propagating cancellation.
- Both public response modes call the reduced result count `totalCount`, so consumers cannot distinguish a complete scan from an observed lower bound.
