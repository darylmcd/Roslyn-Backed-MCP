namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents grouped diagnostic results for a workspace analysis run.
/// </summary>
/// <param name="TotalErrors">Sum of compiler, analyzer, and workspace error severities (backward-compatible aggregate).</param>
/// <param name="CompilerErrors">Count of compiler diagnostics with Error severity.</param>
/// <param name="AnalyzerErrors">Count of analyzer diagnostics with Error severity.</param>
/// <param name="WorkspaceErrors">Count of workspace diagnostics with Error severity.</param>
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
