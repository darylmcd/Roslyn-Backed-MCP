namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents the result of collecting test coverage data.
/// </summary>
public sealed record TestCoverageResultDto(
    bool Success,
    string? Error,
    double? LineCoveragePercent,
    double? BranchCoveragePercent,
    IReadOnlyList<ModuleCoverageDto> Modules);

/// <summary>
/// Represents coverage information for a single module.
/// </summary>
public sealed record ModuleCoverageDto(
    string ModuleName,
    double LineCoveragePercent,
    int LinesCovered,
    int LinesTotal,
    IReadOnlyList<ClassCoverageDto>? Classes);

/// <summary>
/// Represents coverage information for a single class.
/// </summary>
public sealed record ClassCoverageDto(
    string ClassName,
    double LineCoveragePercent,
    int LinesCovered,
    int LinesTotal);
