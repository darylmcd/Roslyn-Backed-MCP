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
}
