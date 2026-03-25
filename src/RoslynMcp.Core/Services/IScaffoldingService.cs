using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Provides preview operations for scaffolding new source file templates within a workspace.
/// </summary>
public interface IScaffoldingService
{
    /// <summary>
    /// Previews scaffolding a new type declaration file in the specified project.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="request">The scaffolding parameters, including type kind, name, and optional base type/interfaces.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<RefactoringPreviewDto> PreviewScaffoldTypeAsync(string workspaceId, ScaffoldTypeDto request, CancellationToken ct);

    /// <summary>
    /// Previews scaffolding a test file for a target type or method in a test project.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="request">The scaffolding parameters, including the test project name and target type.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<RefactoringPreviewDto> PreviewScaffoldTestAsync(string workspaceId, ScaffoldTestDto request, CancellationToken ct);
}