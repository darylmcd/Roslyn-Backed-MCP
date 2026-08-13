# complexity-metrics-pagination-validation-hoist — Validate pagination before workspace dispatch

**row:** `complexity-metrics-pagination-validation-hoist` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs`
- `tests/RoslynMcp.Tests/AdvancedAnalysisToolsTests.cs`

## Acceptance

- [ ] Validate complexity-metrics pagination before entering `RunReadAsync` or resolving a workspace.
- [ ] Invalid limits with an unknown workspace return the pagination `ArgumentException` and never invoke the gate or service.
- [ ] Preserve the valid request path in one focused regression.

## Evidence

- This endpoint validates inside the gate unlike adjacent endpoints, so unrelated workspace errors can mask malformed input.
