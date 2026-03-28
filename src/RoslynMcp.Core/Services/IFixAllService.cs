using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Provides batch code fix operations using Roslyn's FixAllProvider to apply a fix
/// to all instances of a diagnostic across a document, project, or solution.
/// </summary>
public interface IFixAllService
{
    /// <summary>
    /// Previews applying a code fix to all occurrences of the specified diagnostic across the given scope.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="diagnosticId">The diagnostic identifier to fix (e.g., CS8019).</param>
    /// <param name="scope">The scope: "document", "project", or "solution".</param>
    /// <param name="filePath">Required when scope is "document": the absolute path to the file.</param>
    /// <param name="projectName">Optional: filter to a specific project when scope is "project".</param>
    /// <param name="ct">Cancellation token.</param>
    Task<FixAllPreviewDto> PreviewFixAllAsync(
        string workspaceId, string diagnosticId, string scope,
        string? filePath, string? projectName, CancellationToken ct);
}
