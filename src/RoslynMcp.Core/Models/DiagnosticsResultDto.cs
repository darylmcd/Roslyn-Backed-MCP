namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents grouped diagnostic results for a workspace analysis run.
/// </summary>
public sealed record DiagnosticsResultDto(
    IReadOnlyList<DiagnosticDto> WorkspaceDiagnostics,
    IReadOnlyList<DiagnosticDto> CompilerDiagnostics,
    IReadOnlyList<DiagnosticDto> AnalyzerDiagnostics,
    int TotalErrors,
    int TotalWarnings,
    int TotalInfo);
