using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;

namespace RoslynMcp.Roslyn.Contracts;

/// <summary>
/// Registry for post-apply symbol-rotation hints, keyed by preview token.
/// </summary>
/// <remarks>
/// When a refactor that changes a symbol's identity (e.g. rename) is previewed, the preview
/// service registers a hint so the generic apply path can rebuild a fresh
/// <see cref="SymbolDto"/> for the mutated symbol and return it on
/// <see cref="ApplyResultDto.MutatedSymbol"/>. Hints are consumed on the first successful
/// <see cref="ConsumeAsync"/> and can be invalidated explicitly when an apply fails.
/// </remarks>
public interface IPostApplySymbolResolver
{
    /// <summary>
    /// Registers a rename hint against a preview token. Overwrites any prior hint for the token.
    /// </summary>
    void RegisterRename(string previewToken, PostApplyRenameHint hint);

    /// <summary>
    /// Removes any hint registered for <paramref name="previewToken"/> without attempting resolution.
    /// Call on failed applies or token expiry to avoid leaking entries.
    /// </summary>
    void Invalidate(string previewToken);

    /// <summary>
    /// Consumes the hint registered for <paramref name="previewToken"/> and returns a fresh
    /// <see cref="SymbolDto"/> for the post-apply symbol, or null if no hint was registered or the
    /// symbol could not be resolved in <paramref name="appliedSolution"/>.
    /// </summary>
    Task<SymbolDto?> ConsumeAsync(string previewToken, Solution appliedSolution, CancellationToken ct);
}

/// <summary>
/// Hint for rotating a handle after a rename apply — stores the declaration position (stable across
/// renames) plus the expected new name so the resolver can re-resolve the renamed symbol and
/// verify it landed where expected.
/// </summary>
public sealed record PostApplyRenameHint(
    string DeclarationFilePath,
    int DeclarationLine,
    int DeclarationColumn,
    string ExpectedNewName);
