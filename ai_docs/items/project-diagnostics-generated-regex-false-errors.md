# project-diagnostics-generated-regex-false-errors — Preserve source-generator diagnostics parity

**row:** `project-diagnostics-generated-regex-false-errors` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`
- `src/RoslynMcp.Roslyn/Services/CompilationCache.cs`
- `src/RoslynMcp.Roslyn/Services/DiagnosticService.cs`
- `src/RoslynMcp.Roslyn/Services/CompileCheckService.cs`
- `tests/RoslynMcp.Tests/CompileCheckServiceTests.cs`
- `tests/RoslynMcp.Tests/DiagnosticServiceFilterTotalsTests.cs`
- `tests/RoslynMcp.Tests/WorkspaceToolsIntegrationTests.cs`

## Acceptance

- [ ] Load and execute source generators in the same project snapshot used by `compile_check` and `project_diagnostics`; do not suppress diagnostics by ID or special-case `GeneratedRegexAttribute`.
- [ ] A fixture containing a valid `[GeneratedRegex]` partial method reports no CS8795 through either diagnostic surface.
- [ ] A neighboring partial method that genuinely lacks an implementation still reports CS8795 through both surfaces.
- [ ] Preserve analyzer isolation, compilation-cache versioning, cancellation, pagination, and project/file filters.
- [ ] A process-level `dotnet build` parity assertion prevents the MCP surfaces from reporting generator-only compiler errors that the authoritative build does not report.

## Evidence

Live verification on 2026-08-29 loaded this repository through the configured Roslyn MCP 4.1.0 server. Both `compile_check(projectName=RoslynMcp.Roslyn)` and `project_diagnostics(projectName=RoslynMcp.Roslyn)` reported 12 CS8795 errors for valid `[GeneratedRegex]` methods across `DotnetOutputParser`, `CodePatternAnalyzer`, `TreeNodeFilterTranslator`, `TestRunnerService`, and `SymbolRefactorService`, while the same tree compiled successfully through `dotnet test`/MSBuild. This is a diagnostics-integrity defect, not a repository compile failure.
