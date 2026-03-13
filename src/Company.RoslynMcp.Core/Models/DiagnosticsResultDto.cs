namespace Company.RoslynMcp.Core.Models;

public sealed record DiagnosticsResultDto(
    IReadOnlyList<DiagnosticDto> WorkspaceDiagnostics,
    IReadOnlyList<DiagnosticDto> CompilerDiagnostics,
    IReadOnlyList<DiagnosticDto> AnalyzerDiagnostics,
    int TotalErrors,
    int TotalWarnings,
    int TotalInfo);
