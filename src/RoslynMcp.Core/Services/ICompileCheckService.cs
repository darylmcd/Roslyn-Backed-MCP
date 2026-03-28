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
    Task<CompileCheckDto> CheckAsync(
        string workspaceId, string? projectFilter, bool emitValidation, CancellationToken ct);
}
