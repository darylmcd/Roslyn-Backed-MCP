namespace Company.RoslynMcp.Core.Models;

public sealed record TestCoverageResultDto(
    bool Success,
    string? Error,
    double? LineCoveragePercent,
    double? BranchCoveragePercent,
    IReadOnlyList<ModuleCoverageDto> Modules);

public sealed record ModuleCoverageDto(
    string ModuleName,
    double LineCoveragePercent,
    int LinesCovered,
    int LinesTotal,
    IReadOnlyList<ClassCoverageDto>? Classes);

public sealed record ClassCoverageDto(
    string ClassName,
    double LineCoveragePercent,
    int LinesCovered,
    int LinesTotal);
