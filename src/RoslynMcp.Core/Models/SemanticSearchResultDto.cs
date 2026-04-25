namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents a semantic search match.
/// </summary>
/// <param name="SymbolName">Short symbol name (e.g. <c>"JobProcessor"</c>).</param>
/// <param name="SymbolKind">The Roslyn <see cref="Microsoft.CodeAnalysis.SymbolKind"/> name (e.g. <c>"NamedType"</c>, <c>"Method"</c>).</param>
/// <param name="FilePath">Absolute path to the source file containing the declaration.</param>
/// <param name="Line">1-based line number of the declaration.</param>
/// <param name="Signature">Fully qualified display string of the symbol.</param>
/// <param name="ContainingType">Containing type's short name, when applicable.</param>
/// <param name="Namespace">Containing namespace's display string, when applicable.</param>
/// <param name="SymbolHandle">Opaque handle for symbol resolution by other MCP tools. Also serves as the dedupe key for semantic search results.</param>
/// <param name="MatchKind">
/// Which matching strategy produced this result. One of:
/// <c>"structured"</c> (matched a parsed predicate),
/// <c>"name-substring"</c> (whole-query name substring fallback),
/// <c>"token-or-match"</c> (verbose-query stopword-stripped token-OR fallback).
/// Documented under <c>semantic-search-duplicate-results-and-fallback-signal</c>.
/// </param>
public sealed record SemanticSearchResultDto(
    string SymbolName,
    string SymbolKind,
    string FilePath,
    int Line,
    string Signature,
    string? ContainingType,
    string? Namespace,
    string? SymbolHandle,
    string MatchKind);
