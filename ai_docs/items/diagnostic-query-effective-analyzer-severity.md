# diagnostic-query-effective-analyzer-severity — Honor effective analyzer severity

**row:** `diagnostic-query-effective-analyzer-severity` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/DiagnosticQueryService.cs` (`ProjectHasErrorDefaultAnalyzer` and the Error-only analyzer short-circuit)
- `tests/RoslynMcp.Tests/DiagnosticServiceFilterTotalsTests.cs`

## Acceptance

- [ ] An Error-only query skips analyzer execution only when effective analyzer configuration proves that no diagnostic can surface as Error; descriptor `DefaultSeverity` alone is not sufficient.
- [ ] A regression project with a Warning-default analyzer escalated to Error through analyzer config returns the diagnostic and truthful analyzer/error totals.
- [ ] Preserve the large-solution fast path when effective configuration proves the analyzer pass cannot contribute an Error.

## Evidence

The 2026-08-30 diagnostic collaborator extraction confirmed the current optimization explicitly ignores `.editorconfig` escalation. An Error-only query can therefore omit an analyzer diagnostic whose effective severity is Error.
