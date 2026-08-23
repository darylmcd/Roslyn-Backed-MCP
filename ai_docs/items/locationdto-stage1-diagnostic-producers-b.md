# locationdto-stage1-diagnostic-producers-b — populate remaining diagnostic locations

**row:** `locationdto-stage1-diagnostic-producers-b` · **pri:** `Medium` · **size:** `M` · **deps:** `locationdto-stage1-contracts`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SnippetAnalysisService.cs`
- `src/RoslynMcp.Roslyn/Services/UnresolvedAnalyzerReferenceStripper.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceDiagnosticsSink.cs`
- `tests/RoslynMcp.Tests/SnippetAnalysisServiceTests.cs`
- `tests/RoslynMcp.Tests/UnresolvedAnalyzerReferenceStripperTests.cs`
- `tests/RoslynMcp.Tests/WorkspaceDiagnosticsSinkTests.cs`

## Acceptance

- [ ] Populate `DiagnosticDto.Location` at all diagnostic construction sites in the three anchored producers.
- [ ] Keep nested and legacy flat coordinates exactly equal, including transformed/copied diagnostics.

## Regression shape

One focused producer case per test file asserts nested/flat equality through creation or transformation.
