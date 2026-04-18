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

    /// <summary>
    /// Previews scaffolding test files for multiple target types in a single composite preview.
    /// Reuses one workspace snapshot across targets to avoid per-target compilation cost.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="request">The batch scaffolding parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<RefactoringPreviewDto> PreviewScaffoldTestBatchAsync(string workspaceId, ScaffoldTestBatchDto request, CancellationToken ct);

    /// <summary>
    /// Previews scaffolding the FIRST test file for a target service that has no existing
    /// fixture in the destination test project. Inspects the service's constructor and public
    /// methods, derives boilerplate shape from up to three most-recently-modified sibling
    /// fixtures, and emits one <c>&lt;Service&gt;Tests.cs</c> with ClassInitialize / service
    /// instantiation + one smoke-test per public method. Distinct from
    /// <see cref="PreviewScaffoldTestAsync"/> which adds a single method-focused test to an
    /// existing fixture.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="request">The first-test-file scaffolding parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<RefactoringPreviewDto> PreviewScaffoldFirstTestFileAsync(string workspaceId, ScaffoldFirstTestFileDto request, CancellationToken ct);
}
