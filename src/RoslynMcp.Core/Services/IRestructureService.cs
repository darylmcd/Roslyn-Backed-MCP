using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Item 6: syntax-tree pattern-based find-and-replace. The service parses <c>pattern</c> and
/// <c>goal</c> as C# syntax fragments where <c>__name__</c> identifiers act as capture
/// placeholders: each occurrence matches any sub-expression / statement in the scoped
/// documents, then is substituted when the goal is re-rendered for the rewrite.
/// </summary>
public interface IRestructureService
{
    /// <summary>
    /// Previews a structural rewrite against the scoped documents. Returns a preview token and
    /// per-file unified diffs. No disk writes happen during preview.
    /// </summary>
    Task<RefactoringPreviewDto> PreviewRestructureAsync(
        string workspaceId,
        string pattern,
        string goal,
        RestructureScope scope,
        CancellationToken ct);
}

/// <summary>Scope filter for <see cref="IRestructureService.PreviewRestructureAsync"/>.</summary>
public sealed record RestructureScope(
    string? FilePath = null,
    string? ProjectName = null);
