using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Provides preview and apply operations for moving a type declaration
/// from a multi-type file into its own dedicated file within the same project.
/// </summary>
public interface ITypeMoveService
{
    /// <summary>
    /// Previews moving a type declaration to its own file. The type is removed from the
    /// source file and placed in a new file with appropriate using directives and namespace.
    /// </summary>
    Task<RefactoringPreviewDto> PreviewMoveTypeToFileAsync(
        string workspaceId, string sourceFilePath, string typeName, string? targetFilePath, CancellationToken ct);
}
