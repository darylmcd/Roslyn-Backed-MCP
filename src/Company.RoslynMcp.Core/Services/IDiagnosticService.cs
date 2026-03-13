using Company.RoslynMcp.Core.Models;

namespace Company.RoslynMcp.Core.Services;

public interface IDiagnosticService
{
    Task<DiagnosticsResultDto> GetDiagnosticsAsync(
        string? projectFilter, string? fileFilter, string? severityFilter, CancellationToken ct);
}
