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

    /// <summary>
    /// Finds method-local variables whose only write is not followed by any read —
    /// the class of waste IDE0059 ("Unnecessary assignment of a value") covers when the
    /// diagnostic is at its default severity. Walks every method-like body (methods,
    /// constructors, accessors, local functions) and runs <c>SemanticModel.AnalyzeDataFlow</c>
    /// once per body, collecting <see cref="Microsoft.CodeAnalysis.ILocalSymbol"/>s that
    /// appear in <c>WrittenInside</c> but not in <c>ReadInside</c>. Conservative
    /// exclusions: discards (<c>_</c>), <c>foreach</c> iteration variables, <c>using</c> /
    /// <c>await using</c> resource locals, <c>catch (Exception ex)</c> exception locals,
    /// pattern-matching designations (<c>is T x</c>), and <c>out var</c> declarations
    /// at call sites are NOT flagged — those shapes routinely require a name even when
    /// the value is unused, and IDE0059 separately suggests the <c>out _</c> rewrite.
    /// </summary>
    Task<IReadOnlyList<DeadLocalDto>> FindDeadLocalsAsync(
        string workspaceId,
        DeadLocalsAnalysisOptions options,
        CancellationToken ct);
}
