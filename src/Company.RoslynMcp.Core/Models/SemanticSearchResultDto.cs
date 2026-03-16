namespace Company.RoslynMcp.Core.Models;

public sealed record SemanticSearchResultDto(
    string SymbolName,
    string SymbolKind,
    string FilePath,
    int Line,
    string Signature,
    string? ContainingType,
    string? Namespace,
    string? SymbolHandle);
