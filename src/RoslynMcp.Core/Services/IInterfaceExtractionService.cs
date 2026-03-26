using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Provides preview and apply operations for extracting an interface from a concrete type
/// within the same project. The extracted interface is placed in a new file alongside the type.
/// </summary>
public interface IInterfaceExtractionService
{
    /// <summary>
    /// Previews extracting an interface from the given type. Creates a new interface file
    /// with selected member signatures, adds the interface to the type's base list,
    /// and optionally replaces concrete type references with the interface.
    /// </summary>
    Task<RefactoringPreviewDto> PreviewExtractInterfaceAsync(
        string workspaceId, string filePath, string typeName, string interfaceName,
        IReadOnlyList<string>? memberNames, bool replaceUsages, CancellationToken ct);
}
