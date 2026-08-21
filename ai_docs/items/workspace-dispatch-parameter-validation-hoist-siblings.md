# workspace-dispatch-parameter-validation-hoist-siblings — Validate request parameters before workspace dispatch

**row:** `workspace-dispatch-parameter-validation-hoist-siblings` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/AnalyzerInfoTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ConsumerAnalysisTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`
- `tests/RoslynMcp.Tests/WorkspaceDispatchValidationPrecedenceTests.cs` — add one table-driven matrix.

## Acceptance

- [ ] Validate pagination, limits, and required request fields before `RunReadAsync` in every anchored endpoint.
- [ ] An invalid request paired with an unknown workspace returns the parameter error and invokes neither the execution gate nor the service.
- [ ] One table-driven regression covers every affected endpoint and preserves one valid request per validation shape.

## Evidence

- Adjacent review of complexity-metrics validation found ten sibling endpoints that still validate only after workspace dispatch begins.
