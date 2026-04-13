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
    /// <param name="options">Compile check options (project filter, emit validation, severity, file, pagination).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<CompileCheckDto> CheckAsync(
        string workspaceId,
        CompileCheckOptions options,
        CancellationToken ct);
}
