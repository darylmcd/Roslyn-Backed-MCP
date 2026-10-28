# diagnostic-file-only-location-representation — decide how a diagnostic with a file but no position is reported

**row:** `diagnostic-file-only-location-representation` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Core/Models/LocationDto.cs`
- `src/RoslynMcp.Roslyn/Helpers/DotnetOutputParser.cs`
- `src/RoslynMcp.Roslyn/Services/UnresolvedAnalyzerReferenceStripper.cs`
- `src/RoslynMcp.Roslyn/Services/DiagnosticProjectAnalyzer.cs`
- `tests/RoslynMcp.Tests/DotnetOutputParserBuildDiagnosticsTests.cs`

## Acceptance

- [ ] An explicit decision says how a diagnostic that names a file but no position (`NU1201 project.csproj : error`, `MSBUILD : error MSB1009`, the unresolved-analyzer and `WORKSPACE001` project diagnostics) is reported now that the flat `filePath` is gone and `LocationDto` requires a path and a start.
- [ ] The chosen shape is implemented with a wire regression, and `ParseBuildDiagnostics_LocationlessNuGetError_IsReported` asserts the file again.

## Evidence

- At 5.0 (`locationdto-next-major-flat-field-removal`) these diagnostics became `location: null` because the operator decision keeps path and start required; before, the flat `filePath` carried the project file. Two `NU1101` findings for different projects are now distinguishable only by message.
- Options: make `LocationDto.StartLine`/`StartColumn` nullable (file-only span); add a diagnostic-level `file` field; or accept the loss and put the project name in the message.
