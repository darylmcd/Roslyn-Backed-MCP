namespace Company.RoslynMcp.Core.Models;

public sealed record DiagnosticsResultDto(
    IReadOnlyList<DiagnosticDto> WorkspaceDiagnostics,
    IReadOnlyList<DiagnosticDto> CompilerDiagnostics,
    int TotalErrors,
    int TotalWarnings,
    int TotalInfo);
