namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents a method-local variable whose only write is not followed by any read —
/// the class of waste IDE0059 ("Unnecessary assignment of a value") covers when the
/// diagnostic is at its default severity. See
/// <see cref="IUnusedCodeAnalyzer.FindDeadLocalsAsync"/> for the heuristic that
/// produces these hits.
/// </summary>
/// <param name="SymbolName">The local variable name (e.g. <c>paramText</c>).</param>
/// <param name="ContainingMethod">The enclosing method, constructor, accessor, or local-function name.</param>
/// <param name="ContainingType">The declaring type that owns the enclosing method, when available.</param>
/// <param name="FilePath">Absolute path to the source file declaring the local.</param>
/// <param name="Line">1-based declaration line.</param>
/// <param name="Column">1-based declaration column.</param>
/// <param name="ProjectName">Roslyn project containing the local.</param>
public sealed record DeadLocalDto(
    string SymbolName,
    string ContainingMethod,
    string? ContainingType,
    string FilePath,
    int Line,
    int Column,
    string ProjectName);
