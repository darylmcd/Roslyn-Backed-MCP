# diagnostic-query-orchestration-collaborator-extraction — Extract diagnostic query orchestration

**row:** `diagnostic-query-orchestration-collaborator-extraction` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/DiagnosticService.cs` (`GetDiagnosticsAsync`)
- `src/RoslynMcp.Roslyn/Services/DiagnosticQueryService.cs` (new internal collaborator)
- `tests/RoslynMcp.Tests/DiagnosticServiceFilterTotalsTests.cs`

## Acceptance

- [ ] Move project selection, cache routing, diagnostic aggregation, totals, and pagination orchestration behind one internal collaborator; keep `DiagnosticService` responsible for the stable public service contract.
- [ ] Preserve workspace-version invalidation, cancellation, project/file/id/severity filters, aggregate totals, and page metadata.
- [ ] One table-driven regression covers cached and uncached queries across the filter dimensions and asserts identical result contracts.

## Evidence

Semantic metrics on 2026-08-30 report `DiagnosticService.GetDiagnosticsAsync` at 100 LOC, cyclomatic complexity 13, nesting depth 2, and maintainability index 36.55 after the supported-fix collaborator extraction.
