# dangling-plan-path-comments-analyzers — Drop dangling plan-path comments (analyzers + tests)

**row:** `dangling-plan-path-comments-analyzers` · **pri:** `Low` · **size:** `M`

# dangling-plan-path-comments-analyzers — Drop dangling plan-path comments (analyzers + tests)

## Anchors

- `analyzers/ServerSurfaceCatalogAnalyzer/ServerSurfaceCatalogAnalyzer.cs`
- `analyzers/ServerSurfaceCatalogAnalyzer/StdoutWriteAnalyzer.cs`
- `tests/RoslynMcp.Tests/ServerSurfaceCatalogAnalyzerTests.cs`
- `tests/RoslynMcp.Tests/StdoutWriteAnalyzerTests.cs`

## Acceptance

- [ ] No comment in the listed files cites a deleted `ai_docs/plans/` path.

## Evidence

- Path scan; sweep trees are GC'd by /reconcile-plans after completion. (doc-audit 2026-09-23)
