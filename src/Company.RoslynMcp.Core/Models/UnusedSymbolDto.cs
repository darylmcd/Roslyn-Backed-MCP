namespace Company.RoslynMcp.Core.Models;

/// <summary>
/// Represents a symbol that appears to be unused.
/// </summary>
public sealed record UnusedSymbolDto(
    string SymbolName,
    string SymbolKind,
    string FilePath,
    int Line,
    int Column,
    string? ContainingType,
    string? SymbolHandle);
