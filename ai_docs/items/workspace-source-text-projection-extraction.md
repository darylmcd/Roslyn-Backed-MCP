# workspace-source-text-projection-extraction — Extract source-text request validation and projection

**row:** `workspace-source-text-projection-extraction` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs` — `GetSourceText`.
- `src/RoslynMcp.Roslyn/Helpers/SourceTextSlicer.cs`
- New focused internal request/projection helper under `src/RoslynMcp.Host.Stdio/Tools/`.
- `tests/RoslynMcp.Tests/WorkspaceToolsTests.cs`

## Acceptance

- [ ] Move line-range validation, clamping, truncation, and response projection behind one focused helper; keep the attributed endpoint as orchestration only.
- [ ] Preserve the stable MCP parameter schema, exception parameter names, JSON property casing, line counts, clamp behavior, and truncation marker.
- [ ] One table-driven regression covers invalid bounds, end clamping, empty/final-newline text, exact-cap text, and truncated text.
- [ ] Do not duplicate line slicing already owned by `SourceTextSlicer`.

## Evidence

- 2026-08-16 remediation review measured `WorkspaceTools.GetSourceText` at cyclomatic complexity 12 and 65 LOC; validation and wire projection are mixed into the endpoint.
