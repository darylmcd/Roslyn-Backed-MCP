using RoslynMcp.Roslyn.Contracts;
using Microsoft.CodeAnalysis;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Shared helpers for resolving a Roslyn <see cref="Document"/> from a <c>(workspaceId, filePath)</c>
/// pair via the authoritative <see cref="IWorkspaceManager"/> snapshot.
/// </summary>
/// <remarks>
/// <para>
/// <c>organize-usings-preview-document-not-found-after-apply</c> — prior to this helper,
/// <c>RefactoringService.PreviewOrganizeUsingsAsync</c>, <c>PreviewFormatDocumentAsync</c>,
/// <c>PreviewFormatRangeAsync</c>, and <c>EditService.ApplyTextEdits*</c> each wrote their own
/// <c>_workspace.GetCurrentSolution(...) + SymbolResolver.FindDocument(...)</c> pair with subtly
/// different error messages (<c>"Document not found: ..."</c> vs.
/// <c>"Document not found in workspace: ..."</c>) and divergent exception types
/// (<see cref="InvalidOperationException"/> vs. <see cref="KeyNotFoundException"/>). On an
/// auto-reload cascade triggered by the execution gate, a caller that captured the solution
/// reference too early could see a <c>Document not found</c> for a file its sibling preview tool
/// had just resolved successfully on the same turn.
/// </para>
/// <para>
/// <see cref="GetDocumentOrThrow"/> and <see cref="GetDocumentFromFreshSolutionOrThrow"/>
/// centralize the resolver: both ask the <see cref="IWorkspaceManager"/> for <i>its current</i>
/// snapshot at the moment of the call — never a cached reference — and raise a single consistent
/// <see cref="InvalidOperationException"/> when the file is not in the workspace. Preview tools
/// that need the solution alongside the document use the second overload, which returns the same
/// snapshot used to resolve the document so subsequent diff / apply steps stay internally
/// consistent.
/// </para>
/// </remarks>
internal static class DocumentResolution
{
    /// <summary>
    /// Re-acquires the current <see cref="Solution"/> from <paramref name="workspace"/> and
    /// resolves <paramref name="filePath"/> to its matching <see cref="Document"/>. Throws a
    /// consistent <see cref="InvalidOperationException"/> when the file is not part of the
    /// workspace session.
    /// </summary>
    /// <remarks>
    /// Use this overload when the caller only needs the document. Callers that also need the
    /// owning solution (for later diffing or preview storage) should use
    /// <see cref="GetDocumentFromFreshSolutionOrThrow"/> to guarantee both references come from
    /// the same snapshot.
    /// </remarks>
    public static Document GetDocumentOrThrow(
        IWorkspaceManager workspace,
        string workspaceId,
        string filePath)
    {
        var (_, document) = GetDocumentFromFreshSolutionOrThrow(workspace, workspaceId, filePath);
        return document;
    }

    /// <summary>
    /// Re-acquires the current <see cref="Solution"/> from <paramref name="workspace"/> and
    /// resolves <paramref name="filePath"/> to its matching <see cref="Document"/>, returning
    /// both references from the same snapshot. Throws a consistent
    /// <see cref="InvalidOperationException"/> when the file is not part of the workspace
    /// session.
    /// </summary>
    public static (Solution Solution, Document Document) GetDocumentFromFreshSolutionOrThrow(
        IWorkspaceManager workspace,
        string workspaceId,
        string filePath)
    {
        var solution = workspace.GetCurrentSolution(workspaceId);
        return (solution, GetDocumentInSolutionOrThrow(solution, filePath));
    }

    /// <summary>
    /// Resolves <paramref name="filePath"/> against the supplied <paramref name="solution"/>
    /// (no re-acquisition). Use this overload when the caller is threading a progressively
    /// mutated solution across multiple resolve steps — e.g. <c>EditService</c>'s multi-file
    /// accumulator path — where re-reading from the workspace manager would drop the
    /// in-progress edits. Raises the same consistent <see cref="InvalidOperationException"/>
    /// as the workspace-manager overload so callers see one error shape regardless of source.
    /// </summary>
    public static Document GetDocumentInSolutionOrThrow(
        Solution solution,
        string filePath)
    {
        return SymbolResolver.FindDocument(solution, filePath)
            ?? throw new InvalidOperationException($"Document not found: {filePath}");
    }
}
