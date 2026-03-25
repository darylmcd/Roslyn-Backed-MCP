using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Provides preview operations for modifying project file properties and references.
/// Each method produces a preview that must be applied with <see cref="ApplyProjectMutationAsync"/>.
/// </summary>
public interface IProjectMutationService
{
    /// <summary>Previews adding a <c>PackageReference</c> element to a project file.</summary>
    Task<RefactoringPreviewDto> PreviewAddPackageReferenceAsync(string workspaceId, AddPackageReferenceDto request, CancellationToken ct);

    /// <summary>Previews removing a <c>PackageReference</c> element from a project file.</summary>
    Task<RefactoringPreviewDto> PreviewRemovePackageReferenceAsync(string workspaceId, RemovePackageReferenceDto request, CancellationToken ct);

    /// <summary>Previews adding a <c>ProjectReference</c> element to a project file.</summary>
    Task<RefactoringPreviewDto> PreviewAddProjectReferenceAsync(string workspaceId, AddProjectReferenceDto request, CancellationToken ct);

    /// <summary>Previews removing a <c>ProjectReference</c> element from a project file.</summary>
    Task<RefactoringPreviewDto> PreviewRemoveProjectReferenceAsync(string workspaceId, RemoveProjectReferenceDto request, CancellationToken ct);

    /// <summary>Previews setting an allowlisted project property (e.g., <c>Nullable</c>, <c>LangVersion</c>).</summary>
    Task<RefactoringPreviewDto> PreviewSetProjectPropertyAsync(string workspaceId, SetProjectPropertyDto request, CancellationToken ct);

    /// <summary>Previews adding a target framework to a project's <c>TargetFrameworks</c> list.</summary>
    Task<RefactoringPreviewDto> PreviewAddTargetFrameworkAsync(string workspaceId, AddTargetFrameworkDto request, CancellationToken ct);

    /// <summary>Previews removing a target framework from a project's <c>TargetFrameworks</c> list.</summary>
    Task<RefactoringPreviewDto> PreviewRemoveTargetFrameworkAsync(string workspaceId, RemoveTargetFrameworkDto request, CancellationToken ct);

    /// <summary>Previews setting an allowlisted conditional project property with a specified MSBuild condition.</summary>
    Task<RefactoringPreviewDto> PreviewSetConditionalPropertyAsync(string workspaceId, SetConditionalPropertyDto request, CancellationToken ct);

    /// <summary>Previews adding a <c>PackageVersion</c> entry to <c>Directory.Packages.props</c>.</summary>
    Task<RefactoringPreviewDto> PreviewAddCentralPackageVersionAsync(string workspaceId, AddCentralPackageVersionDto request, CancellationToken ct);

    /// <summary>Previews removing a <c>PackageVersion</c> entry from <c>Directory.Packages.props</c>.</summary>
    Task<RefactoringPreviewDto> PreviewRemoveCentralPackageVersionAsync(string workspaceId, RemoveCentralPackageVersionDto request, CancellationToken ct);

    /// <summary>
    /// Applies a previously previewed project file mutation to disk and triggers a workspace reload.
    /// </summary>
    /// <param name="previewToken">The token returned by a prior preview call.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ApplyResultDto> ApplyProjectMutationAsync(string previewToken, CancellationToken ct);
}