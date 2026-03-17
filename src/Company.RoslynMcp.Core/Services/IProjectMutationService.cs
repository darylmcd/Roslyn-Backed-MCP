using Company.RoslynMcp.Core.Models;

namespace Company.RoslynMcp.Core.Services;

public interface IProjectMutationService
{
    Task<RefactoringPreviewDto> PreviewAddPackageReferenceAsync(string workspaceId, AddPackageReferenceDto request, CancellationToken ct);
    Task<RefactoringPreviewDto> PreviewRemovePackageReferenceAsync(string workspaceId, RemovePackageReferenceDto request, CancellationToken ct);
    Task<RefactoringPreviewDto> PreviewAddProjectReferenceAsync(string workspaceId, AddProjectReferenceDto request, CancellationToken ct);
    Task<RefactoringPreviewDto> PreviewRemoveProjectReferenceAsync(string workspaceId, RemoveProjectReferenceDto request, CancellationToken ct);
    Task<RefactoringPreviewDto> PreviewSetProjectPropertyAsync(string workspaceId, SetProjectPropertyDto request, CancellationToken ct);
    Task<RefactoringPreviewDto> PreviewAddTargetFrameworkAsync(string workspaceId, AddTargetFrameworkDto request, CancellationToken ct);
    Task<RefactoringPreviewDto> PreviewRemoveTargetFrameworkAsync(string workspaceId, RemoveTargetFrameworkDto request, CancellationToken ct);
    Task<RefactoringPreviewDto> PreviewSetConditionalPropertyAsync(string workspaceId, SetConditionalPropertyDto request, CancellationToken ct);
    Task<RefactoringPreviewDto> PreviewAddCentralPackageVersionAsync(string workspaceId, AddCentralPackageVersionDto request, CancellationToken ct);
    Task<RefactoringPreviewDto> PreviewRemoveCentralPackageVersionAsync(string workspaceId, RemoveCentralPackageVersionDto request, CancellationToken ct);
    Task<ApplyResultDto> ApplyProjectMutationAsync(string previewToken, CancellationToken ct);
}