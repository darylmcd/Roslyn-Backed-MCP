namespace Company.RoslynMcp.Core.Models;

public sealed record UnusedSymbolDto(
    string SymbolName,
    string SymbolKind,
    string FilePath,
    int Line,
    int Column,
    string? ContainingType,
    string? SymbolHandle);
