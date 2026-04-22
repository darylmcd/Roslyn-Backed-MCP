using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <inheritdoc />
public sealed class PostApplySymbolResolver : IPostApplySymbolResolver
{
    private readonly ConcurrentDictionary<string, PostApplyRenameHint> _hints = new(StringComparer.Ordinal);

    public void RegisterRename(string previewToken, PostApplyRenameHint hint)
        => _hints[previewToken] = hint;

    public void Invalidate(string previewToken)
        => _hints.TryRemove(previewToken, out _);

    public async Task<SymbolDto?> ConsumeAsync(string previewToken, Solution appliedSolution, CancellationToken ct)
    {
        if (!_hints.TryRemove(previewToken, out var hint))
            return null;

        var symbol = await SymbolResolver.ResolveAtPositionAsync(
            appliedSolution,
            hint.DeclarationFilePath,
            hint.DeclarationLine,
            hint.DeclarationColumn,
            ct).ConfigureAwait(false);

        if (symbol is null)
            return null;

        // The declaration position is stable across renames, so the symbol at the stashed
        // position should now carry the new name. If it doesn't, something else mutated the
        // position — return null rather than emit a misleading rotated handle.
        if (!string.Equals(symbol.Name, hint.ExpectedNewName, StringComparison.Ordinal))
            return null;

        return SymbolMapper.ToDto(symbol, appliedSolution);
    }
}
