using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Provides preview operations for refactorings that span multiple projects within a workspace.
/// </summary>
public interface ICrossProjectRefactoringService
{
    /// <summary>
    /// Previews moving a type declaration from its current project into a different project.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="sourceFilePath">The absolute path to the file that declares the type.</param>
    /// <param name="typeName">The simple name of the type to move.</param>
    /// <param name="targetProjectName">The name of the destination project.</param>
    /// <param name="targetNamespace">Explicit namespace for the moved type. When <see langword="null"/>, the namespace is inferred from <paramref name="preserveNamespace"/> (see below).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="preserveNamespace">When <see langword="true"/>, keep the type's current namespace if <paramref name="targetNamespace"/> is <see langword="null"/> (legacy behavior). When <see langword="false"/> (default), use <paramref name="targetNamespace"/> if set, otherwise the target project's default namespace.</param>
    Task<RefactoringPreviewDto> PreviewMoveTypeToProjectAsync(
        string workspaceId,
        string sourceFilePath,
        string typeName,
        string targetProjectName,
        string? targetNamespace,
        CancellationToken ct,
        bool preserveNamespace = false);

    /// <summary>
    /// Previews extracting an interface from a concrete type, optionally placing it in a different project.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="filePath">The absolute path to the file containing the type.</param>
    /// <param name="typeName">The simple name of the type to extract from.</param>
    /// <param name="interfaceName">The name to give the new interface, or <see langword="null"/> to auto-derive from the type name.</param>
    /// <param name="targetProjectName">The name of the project to place the interface in, or <see langword="null"/> for the same project.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<RefactoringPreviewDto> PreviewExtractInterfaceAsync(
        string workspaceId,
        string filePath,
        string typeName,
        string? interfaceName,
        string? targetProjectName,
        CancellationToken ct);

    /// <summary>
    /// Previews applying the dependency inversion principle by extracting an interface and updating
    /// constructor parameters to depend on the abstraction.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="filePath">The absolute path to the file containing the type.</param>
    /// <param name="typeName">The simple name of the type to invert.</param>
    /// <param name="interfaceName">The name to give the new interface, or <see langword="null"/> to auto-derive from the type name.</param>
    /// <param name="interfaceProjectName">The name of the project to place the extracted interface in.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<RefactoringPreviewDto> PreviewDependencyInversionAsync(
        string workspaceId,
        string filePath,
        string typeName,
        string? interfaceName,
        string interfaceProjectName,
        CancellationToken ct);
}
