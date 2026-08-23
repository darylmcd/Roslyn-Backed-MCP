# locationdto-stage1-symbol-type-producers — populate symbol and type-usage locations

**row:** `locationdto-stage1-symbol-type-producers` · **pri:** `Medium` · **size:** `M` · **deps:** `locationdto-stage1-contracts`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/SymbolMapper.cs`
- `src/RoslynMcp.Roslyn/Services/MutationAnalysisService.cs`
- `tests/RoslynMcp.Tests/SymbolMapperTests.cs`
- `tests/RoslynMcp.Tests/MutationAnalysisServiceTests.cs`

## Acceptance

- [ ] Populate `SymbolDto.Location` and the `DiagnosticDto.Location` instances emitted by `SymbolMapper`.
- [ ] Populate `TypeUsageDto.Location` in `MutationAnalysisService`.
- [ ] Keep nested and legacy flat coordinates exactly equal at each producer boundary.

## Regression shape

Producer tests assert nested/flat equality for symbol, mapped diagnostic, and type-usage results.
