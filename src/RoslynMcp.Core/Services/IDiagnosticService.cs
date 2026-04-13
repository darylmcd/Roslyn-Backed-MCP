using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Provides access to compiler, analyzer, and workspace diagnostics for a loaded workspace.
/// </summary>
public interface IDiagnosticService
{
    /// <summary>
    /// Returns workspace, compiler, and analyzer diagnostics, optionally filtered by project, file, or severity.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="projectFilter">Restricts results to the named project, or <see langword="null"/> for all projects.</param>
    /// <param name="fileFilter">Restricts results to a specific file path, or <see langword="null"/> for all files.</param>
    /// <param name="severityFilter">Minimum severity for returned rows (e.g., <c>Error</c>, <c>Warning</c>, <c>Info</c>). <see langword="null"/> defaults to <c>Info</c> (Hidden is still excluded at collection time).</param>
    /// <param name="diagnosticIdFilter">Restricts results to a specific diagnostic ID (e.g., <c>CS0246</c>, <c>CA1000</c>), or <see langword="null"/> for all diagnostics.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<DiagnosticsResultDto> GetDiagnosticsAsync(
        string workspaceId, string? projectFilter, string? fileFilter, string? severityFilter, string? diagnosticIdFilter, CancellationToken ct);

    /// <summary>
    /// Returns detailed information for a specific diagnostic at a source location, including available code fixes.
    /// Returns <see langword="null"/> if no matching diagnostic is found.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="diagnosticId">The diagnostic identifier (e.g., <c>CS0101</c>).</param>
    /// <param name="filePath">The absolute path to the file where the diagnostic occurs.</param>
    /// <param name="line">The 1-based line number of the diagnostic.</param>
    /// <param name="column">The 1-based column number of the diagnostic.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<DiagnosticDetailsDto?> GetDiagnosticDetailsAsync(
        string workspaceId,
        string diagnosticId,
        string filePath,
        int line,
        int column,
        CancellationToken ct);
}
