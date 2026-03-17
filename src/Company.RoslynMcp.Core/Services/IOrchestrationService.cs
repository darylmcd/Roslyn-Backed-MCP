using Company.RoslynMcp.Core.Models;

namespace Company.RoslynMcp.Core.Services;

public interface IOrchestrationService
{
    Task<RefactoringPreviewDto> PreviewMigratePackageAsync(
        string workspaceId,
        string oldPackageId,
        string newPackageId,
        string newVersion,
        CancellationToken ct);

    Task<RefactoringPreviewDto> PreviewSplitClassAsync(
        string workspaceId,
        string filePath,
        string typeName,
        IReadOnlyList<string> memberNames,
        string newFileName,
        CancellationToken ct);

    Task<RefactoringPreviewDto> PreviewExtractAndWireInterfaceAsync(
        string workspaceId,
        string filePath,
        string typeName,
        string? interfaceName,
        string targetProjectName,
        bool updateDiRegistrations,
        CancellationToken ct);

    Task<ApplyResultDto> ApplyCompositeAsync(string previewToken, CancellationToken ct);
}