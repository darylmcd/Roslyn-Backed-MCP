namespace Company.RoslynMcp.Core.Models;

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
