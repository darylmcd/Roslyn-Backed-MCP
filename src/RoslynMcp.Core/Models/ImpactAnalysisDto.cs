namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents the direct and indirect impact of changing a target symbol.
/// </summary>
/// <remarks>
/// FLAG-3D: The reference and declaration arrays are paginated server-side. Use
/// <see cref="ImpactAnalysisPaging"/> on the request to control how many entries
/// are returned per call. The <c>Total*</c> counters always reflect the unpaged
/// totals so callers can detect when more data is available.
/// </remarks>
public sealed record ImpactAnalysisDto(
    SymbolDto TargetSymbol,
    IReadOnlyList<LocationDto> DirectReferences,
    IReadOnlyList<SymbolDto> AffectedDeclarations,
    IReadOnlyList<string> AffectedProjects,
    string Summary,
    int TotalDirectReferences,
    int TotalAffectedDeclarations,
    bool HasMoreReferences,
    bool HasMoreDeclarations,
    int ReferencesOffset,
    int ReferencesLimit);

/// <summary>
/// Pagination/limit parameters for <c>impact_analysis</c>. Defaults are tuned to keep
/// the JSON response under typical MCP client output budgets even on large solutions.
/// </summary>
public sealed record ImpactAnalysisPaging(
    int ReferencesOffset = 0,
    int ReferencesLimit = 100,
    int DeclarationsLimit = 100);
