namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents a symbol that appears to be unused.
/// <see cref="Confidence"/> is a heuristic: high (non-public), medium (public API), low (enum members, record/serialization-shaped properties, interface members).
/// </summary>
public sealed record UnusedSymbolDto(
    string SymbolName,
    string SymbolKind,
    string FilePath,
    int Line,
    int Column,
    string? ContainingType,
    string? SymbolHandle,
    string Confidence);
