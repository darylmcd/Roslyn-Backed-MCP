using Company.RoslynMcp.Core.Models;

namespace Company.RoslynMcp.Core.Services;

public interface IDiagnosticService
{
    Task<DiagnosticsResultDto> GetDiagnosticsAsync(
        string workspaceId, string? projectFilter, string? fileFilter, string? severityFilter, CancellationToken ct);

    Task<DiagnosticDetailsDto?> GetDiagnosticDetailsAsync(
        string workspaceId,
        string diagnosticId,
        string filePath,
        int line,
        int column,
        CancellationToken ct);
}
