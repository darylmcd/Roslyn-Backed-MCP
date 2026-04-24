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
    /// <param name="toolName">
    /// The originating MCP tool name (e.g. <c>set_editorconfig_option</c> or
    /// <c>set_diagnostic_severity</c>). Threaded through to
    /// <see cref="IChangeTracker.RecordChange"/> so <c>workspace_changes</c> reports the writer
    /// that actually ran (<c>workspace-changes-log-missing-editorconfig-writers</c>).
    /// </param>
    Task<EditorConfigWriteResultDto> SetOptionAsync(
        string workspaceId, string sourceFilePath, string key, string value, string toolName, CancellationToken ct);
}
