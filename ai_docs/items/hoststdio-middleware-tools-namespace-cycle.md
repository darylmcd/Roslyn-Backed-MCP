# hoststdio-middleware-tools-namespace-cycle — break the Middleware ↔ Tools namespace cycle

**row:** `hoststdio-middleware-tools-namespace-cycle` · **pri:** `Low` · **size:** `M` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs`
- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`

## Acceptance

- [ ] One direction broken — shared contract extracted into a third namespace, or inverted via an interface
- [ ] Regression: an architecture/namespace-dependency test asserts no Middleware↔Tools cycle

## Evidence

- `audit-reports/20260531T192823Z_roslyn-backed-mcp_mcp-server-surface-test.md`; re-confirmed at HEAD 6acab28. Source: 2026-05-31 surface-test, Standing Directive #3.

## Context

[repo-code, not an MCP-surface defect — verified at HEAD 6acab28] `get_namespace_dependencies` reported a circular namespace dependency `RoslynMcp.Host.Stdio.Middleware ↔ RoslynMcp.Host.Stdio.Tools`; confirmed live — `Middleware/StructuredCallToolFilter.cs` references the Tools namespace and `Tools/SymbolTools.cs` references the Middleware namespace. A namespace cycle is a layering smell that complicates extraction/testing.
