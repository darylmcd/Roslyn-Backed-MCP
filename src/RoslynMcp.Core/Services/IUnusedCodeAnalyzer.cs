using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Finds symbols with zero references across a solution to identify dead code candidates.
/// Also surfaces private/internal helper methods whose body shape duplicates a reachable
/// canonical symbol from a referenced BCL/NuGet assembly (a category that
/// <see cref="FindUnusedSymbolsAsync"/> and classic dead-code scans miss because the
/// helper IS used locally).
/// </summary>
public interface IUnusedCodeAnalyzer
{
    Task<IReadOnlyList<UnusedSymbolDto>> FindUnusedSymbolsAsync(
        string workspaceId,
        UnusedSymbolsAnalysisOptions options,
        CancellationToken ct);

    /// <summary>
    /// Flags private/internal extension-method helpers whose body shape duplicates a
    /// reachable canonical symbol from a referenced package (BCL/NuGet). Conservative
    /// match: the helper body must be structurally ≤ 2 statements AND terminate in a
    /// single method-call invocation whose bound target lives in a non-source assembly
    /// (i.e. not declared in the current solution). Domain-specific wrappers whose body
    /// does more than a single delegation or null-guard are intentionally NOT flagged.
    /// </summary>
    Task<IReadOnlyList<DuplicateHelperDto>> FindDuplicateHelpersAsync(
        string workspaceId,
        DuplicateHelperAnalysisOptions options,
        CancellationToken ct);
}
