using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Provides preview and apply operations for extracting selected members from a type
/// into a new type, establishing composition and updating callers.
/// </summary>
public interface ITypeExtractionService
{
    /// <summary>
    /// Previews extracting the named members from a type into a new type.
    /// The source type gets a field and constructor parameter for the new type,
    /// and callers are updated to route through the new composition.
    /// </summary>
    Task<RefactoringPreviewDto> PreviewExtractTypeAsync(
        string workspaceId, string filePath, string sourceTypeName,
        IReadOnlyList<string> memberNames, string newTypeName, string? newFilePath,
        CancellationToken ct);
}
