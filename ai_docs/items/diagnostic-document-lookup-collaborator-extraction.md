# diagnostic-document-lookup-collaborator-extraction — Extract diagnostic document lookup

**row:** `diagnostic-document-lookup-collaborator-extraction` · **pri:** `Low` · **size:** `M` · **deps:** `diagnostic-query-orchestration-collaborator-extraction`

## Anchors

- `src/RoslynMcp.Roslyn/Services/DiagnosticService.cs` (`FindDiagnosticInDocumentAsync`)
- `src/RoslynMcp.Roslyn/Services/DiagnosticDocumentLookup.cs` (new internal collaborator)
- `tests/RoslynMcp.Tests/DiagnosticFixIntegrationTests.cs`

## Acceptance

- [ ] Move compiler/analyzer diagnostic lookup and matching behind one internal collaborator; keep DTO assembly and supported-fix enumeration outside it.
- [ ] Preserve source-span matching, analyzer isolation, cancellation, and the compiler-first selection contract.
- [ ] One table-driven regression covers compiler, analyzer, absent, and canceled lookup outcomes through the collaborator boundary.

## Evidence

Semantic metrics on 2026-08-30 report `DiagnosticService.FindDiagnosticInDocumentAsync` at 63 LOC, cyclomatic complexity 14, nesting depth 3, seven parameters, and maintainability index 42.12.
