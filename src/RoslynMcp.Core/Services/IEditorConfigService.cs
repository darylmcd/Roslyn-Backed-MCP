using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Provides read and write access to .editorconfig settings affecting the workspace.
/// </summary>
public interface IEditorConfigService
{
    /// <summary>
    /// Gets the effective .editorconfig options for the specified source file.
    /// </summary>
    Task<EditorConfigOptionsDto> GetOptionsAsync(string workspaceId, string filePath, CancellationToken ct);

    /// <summary>
    /// Sets or updates an .editorconfig key for files matching C# sources, creating a file next to the source tree if needed.
    /// </summary>
    Task<EditorConfigWriteResultDto> SetOptionAsync(
        string workspaceId, string sourceFilePath, string key, string value, CancellationToken ct);
}
