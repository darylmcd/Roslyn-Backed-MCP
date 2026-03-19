namespace Company.RoslynMcp.Core.Models;

/// <summary>
/// Represents code complexity metrics calculated for a symbol.
/// </summary>
public sealed record ComplexityMetricsDto(
    string SymbolName,
    string SymbolKind,
    string FilePath,
    int Line,
    int CyclomaticComplexity,
    int LinesOfCode,
    int MaxNestingDepth,
    int ParameterCount,
    string? ContainingType);
