# diagnostic-details-project-diagnostic-location-contract — diagnostic-details-project-diagnostic-location-contract

**row:** `diagnostic-details-project-diagnostic-location-contract` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/DiagnosticService.cs`
- `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs`
- `tests/RoslynMcp.Tests/AnalysisToolsTests.cs`
- `tests/RoslynMcp.Tests/DiagnosticFixIntegrationTests.cs`

## Acceptance

- [ ] A location tuple returned by `project_diagnostics` can be passed unchanged to `diagnostic_details` and returns the matching detail record.
- [ ] Preserve clear not-found behavior for genuinely absent diagnostics.
- [ ] Add one MCP002 fixture regression using the exact file, line, and column emitted by the list tool.

## Evidence

- `project_diagnostics(diagnosticId=MCP002)` returned ToolCallErrorWireContractTests.cs:350:30, while `diagnostic_details` with that exact tuple returned `found=false`.
