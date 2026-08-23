# locationdto-stage1-diagnostic-producers-a — populate service and build diagnostic locations

**row:** `locationdto-stage1-diagnostic-producers-a` · **pri:** `Medium` · **size:** `M` · **deps:** `locationdto-stage1-contracts`

## Anchors

- `src/RoslynMcp.Roslyn/Services/DiagnosticService.cs`
- `src/RoslynMcp.Roslyn/Services/CompileCheckService.cs`
- `src/RoslynMcp.Roslyn/Helpers/DotnetOutputParser.cs`
- `src/RoslynMcp.Roslyn/Services/ScriptingService.cs`
- `tests/RoslynMcp.Tests/DiagnosticServiceTests.cs`
- `tests/RoslynMcp.Tests/CompileCheckServiceTests.cs`
- `tests/RoslynMcp.Tests/DotnetOutputParserTests.cs`

## Acceptance

- [ ] Populate `DiagnosticDto.Location` at all diagnostic construction sites in the four anchored producers.
- [ ] Keep nested and legacy flat coordinates exactly equal, including null/no-location results.

## Regression shape

One focused producer case per test file asserts nested/flat equality and the no-location shape.
