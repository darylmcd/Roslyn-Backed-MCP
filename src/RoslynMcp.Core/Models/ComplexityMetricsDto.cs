namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents code complexity metrics calculated for a symbol.
/// </summary>
/// <param name="MaintainabilityIndex">Approximate maintainability index 0–100 (higher is easier to maintain); uses CC and LOC with a Halstead-volume heuristic.</param>
public sealed record ComplexityMetricsDto(
    string SymbolName,
    string SymbolKind,
    string FilePath,
    int Line,
    int CyclomaticComplexity,
    int LinesOfCode,
    int MaxNestingDepth,
    int ParameterCount,
    string? ContainingType,
    double MaintainabilityIndex);
