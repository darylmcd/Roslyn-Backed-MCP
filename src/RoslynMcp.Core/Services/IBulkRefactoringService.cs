using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Provides preview and apply operations for bulk refactoring operations
/// such as replacing all occurrences of a type reference across the solution.
/// </summary>
public interface IBulkRefactoringService
{
    /// <summary>
    /// Previews replacing all references to one type with another across the solution.
    /// Can be scoped to parameters, fields, or all reference sites.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="oldTypeName">Fully qualified or simple name of the type to replace.</param>
    /// <param name="newTypeName">Fully qualified or simple name of the replacement type.</param>
    /// <param name="scope">
    /// Scope filter: "parameters", "fields", or "all" (default).
    /// The "parameters" scope covers method parameter declarations and also generic
    /// arguments appearing in implemented-interface / base-class declarations (e.g. the
    /// <c>T</c> in <c>class Foo : IValidateOptions&lt;T&gt;</c>) so the interface contract
    /// stays in sync with the parameter rewrites.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<RefactoringPreviewDto> PreviewBulkReplaceTypeAsync(
        string workspaceId, string oldTypeName, string newTypeName, string? scope, CancellationToken ct);
}
