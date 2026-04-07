using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Provides in-memory compilation checking using the Roslyn Compilation API,
/// without shelling out to dotnet build.
/// </summary>
public interface ICompileCheckService
{
    /// <summary>
    /// Performs an in-memory compilation check for the specified project or all projects in the workspace.
    /// Returns compiler diagnostics (errors/warnings) without producing binaries on disk.
    /// </summary>
    /// <param name="workspaceId">The workspace session id.</param>
    /// <param name="projectFilter">Optional: limit to a single project by name.</param>
    /// <param name="emitValidation">When true, performs full emit validation instead of GetDiagnostics-only.</param>
    /// <param name="severityFilter">Optional: minimum severity (Error, Warning, Info, Hidden).</param>
    /// <param name="fileFilter">Optional: only return diagnostics whose file path matches.</param>
    /// <param name="offset">Number of diagnostics to skip before returning results (default 0).</param>
    /// <param name="limit">Maximum number of diagnostics to return (default 50).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<CompileCheckDto> CheckAsync(
        string workspaceId,
        string? projectFilter,
        bool emitValidation,
        string? severityFilter,
        string? fileFilter,
        int offset,
        int limit,
        CancellationToken ct);
}
