using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Provides security-focused diagnostic analysis by filtering and enriching the existing
/// diagnostic pipeline with OWASP categorization and fix hints.
/// </summary>
public interface ISecurityDiagnosticService
{
    /// <summary>
    /// Returns only security-relevant diagnostics from the workspace, enriched with
    /// OWASP categories, severity classifications, and fix hints.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="projectFilter">Restricts results to the named project, or <see langword="null"/> for all projects.</param>
    /// <param name="fileFilter">Restricts results to a specific file path, or <see langword="null"/> for all files.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<SecurityDiagnosticsResultDto> GetSecurityDiagnosticsAsync(
        string workspaceId, string? projectFilter, string? fileFilter, CancellationToken ct);

    /// <summary>
    /// Checks whether security analyzer packages are present in the workspace without running diagnostics.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<SecurityAnalyzerStatusDto> GetAnalyzerStatusAsync(string workspaceId, CancellationToken ct);
}
