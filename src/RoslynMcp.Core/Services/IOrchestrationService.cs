using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Provides high-level orchestrated operations that coordinate multiple workspace mutations
/// in a single preview-then-apply workflow.
/// </summary>
public interface IOrchestrationService
{
    /// <summary>
    /// Previews migrating a NuGet package reference from one package identifier and version to another
    /// across all affected projects in the workspace.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="oldPackageId">The package identifier to replace.</param>
    /// <param name="newPackageId">The replacement package identifier.</param>
    /// <param name="newVersion">The version of the replacement package.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<RefactoringPreviewDto> PreviewMigratePackageAsync(
        string workspaceId,
        string oldPackageId,
        string newPackageId,
        string newVersion,
        CancellationToken ct);

    /// <summary>
    /// Previews splitting a class by moving selected members into a new partial class file.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="filePath">The absolute path to the file containing the class to split.</param>
    /// <param name="typeName">The simple name of the class to split.</param>
    /// <param name="memberNames">The names of members to move to the new file.</param>
    /// <param name="newFileName">The file name for the new partial class file (without path).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<RefactoringPreviewDto> PreviewSplitClassAsync(
        string workspaceId,
        string filePath,
        string typeName,
        IReadOnlyList<string> memberNames,
        string newFileName,
        CancellationToken ct);

    /// <summary>
    /// Previews extracting an interface from a concrete type and updating dependency injection registrations
    /// to wire the implementation to the new abstraction.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="filePath">The absolute path to the file containing the type.</param>
    /// <param name="typeName">The simple name of the type to extract from.</param>
    /// <param name="interfaceName">The name to give the extracted interface, or <see langword="null"/> to auto-derive.</param>
    /// <param name="targetProjectName">The project to place the interface in.</param>
    /// <param name="updateDiRegistrations">When <see langword="true"/>, DI registration call sites are updated to use the new interface type.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<RefactoringPreviewDto> PreviewExtractAndWireInterfaceAsync(
        string workspaceId,
        string filePath,
        string typeName,
        string? interfaceName,
        string targetProjectName,
        bool updateDiRegistrations,
        CancellationToken ct);

    /// <summary>
    /// Applies a previously previewed composite orchestration operation to disk.
    /// </summary>
    /// <param name="previewToken">The token returned by a prior preview call.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ApplyResultDto> ApplyCompositeAsync(string previewToken, CancellationToken ct);
}
