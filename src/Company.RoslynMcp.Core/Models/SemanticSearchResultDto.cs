namespace Company.RoslynMcp.Core.Models;

/// <summary>
/// Represents a semantic search match.
/// </summary>
public sealed record SemanticSearchResultDto(
    string SymbolName,
    string SymbolKind,
    string FilePath,
    int Line,
    string Signature,
    string? ContainingType,
    string? Namespace,
    string? SymbolHandle);
