namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents grouped diagnostic results for a workspace analysis run.
/// </summary>
/// <param name="WorkspaceDiagnostics">Workspace diagnostics matching the requested file/severity filter.</param>
/// <param name="CompilerDiagnostics">Compiler diagnostics matching the requested file/severity filter.</param>
/// <param name="AnalyzerDiagnostics">Analyzer diagnostics matching the requested file/severity filter.</param>
/// <param name="TotalErrors">Total error-severity diagnostics in the queried scope (project + file filters), ignoring <c>severityFilter</c>. Filter only narrows the returned arrays — totals are invariant under severity narrowing.</param>
/// <param name="TotalWarnings">Total warning-severity diagnostics in the queried scope, ignoring <c>severityFilter</c>.</param>
/// <param name="TotalInfo">Total info-severity diagnostics in the queried scope, ignoring <c>severityFilter</c>.</param>
/// <param name="CompilerErrors">Count of compiler diagnostics with Error severity in the queried scope (severity-filter invariant).</param>
/// <param name="AnalyzerErrors">Count of analyzer diagnostics with Error severity in the queried scope (severity-filter invariant).</param>
/// <param name="WorkspaceErrors">Count of workspace diagnostics with Error severity in the queried scope (severity-filter invariant).</param>
public sealed record DiagnosticsResultDto(
    IReadOnlyList<DiagnosticDto> WorkspaceDiagnostics,
    IReadOnlyList<DiagnosticDto> CompilerDiagnostics,
    IReadOnlyList<DiagnosticDto> AnalyzerDiagnostics,
    int TotalErrors,
    int TotalWarnings,
    int TotalInfo,
    int CompilerErrors = 0,
    int AnalyzerErrors = 0,
    int WorkspaceErrors = 0);
