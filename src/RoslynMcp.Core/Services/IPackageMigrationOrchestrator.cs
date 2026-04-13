using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Orchestrates NuGet package migration previews across project files and central package management.
/// </summary>
public interface IPackageMigrationOrchestrator
{
    Task<RefactoringPreviewDto> PreviewMigratePackageAsync(
        string workspaceId,
        string oldPackageId,
        string newPackageId,
        string newVersion,
        CancellationToken ct);
}
